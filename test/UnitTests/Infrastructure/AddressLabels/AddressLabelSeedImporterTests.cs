using Domain.Common;
using Domain.Entities;
using Infrastructure.AddressLabels;
using Infrastructure.Caching;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace UnitTests.Infrastructure.AddressLabels;

public sealed class AddressLabelSeedImporterTests
{
    [Fact]
    public async Task ImportAsync_UpsertsRowsAndRefreshesCache()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var seedPath = Path.Combine(Path.GetTempPath(), $"address-labels-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            seedPath,
            """
            {
              "schemaVersion": 1,
              "reviewStatus": "reviewed",
              "scopePolicy": {
                "exchange": "global",
                "bridge": "chainScoped"
              },
              "entries": [
                {
                  "address": "0x1111111111111111111111111111111111111111",
                  "chain": null,
                  "nameTag": "MEXC",
                  "kind": "Exchange",
                  "sourceReference": "test",
                  "priority": 100,
                  "isEnabled": true
                },
                {
                  "address": "0x2222222222222222222222222222222222222222",
                  "chain": "Base",
                  "nameTag": "Across Bridge",
                  "kind": "Bridge",
                  "sourceReference": "test",
                  "priority": 90,
                  "isEnabled": true
                },
                {
                  "address": "bad",
                  "chain": "Bsc",
                  "nameTag": "Bad",
                  "kind": "Exchange",
                  "sourceReference": "test"
                }
              ]
            }
            """);

        try
        {
            var report = await fixture.Importer.ImportAsync(seedPath, dryRun: false, allowCandidate: false, CancellationToken.None);
            var enabledLabels = await fixture.Repository.GetEnabledAsync(CancellationToken.None);
            var exchangeAddress = global::Domain.ValueObjects.Address.Normalize("0x1111111111111111111111111111111111111111").Value;
            var baseAddress = global::Domain.ValueObjects.Address.Normalize("0x2222222222222222222222222222222222222222").Value;

            Assert.Equal(2, report.Inserted);
            Assert.Equal(1, report.Invalid);
            Assert.Equal(2, enabledLabels.Count);
            Assert.True(fixture.Cache.TryGet(SupportedChain.Bsc, exchangeAddress, out var bscLabel));
            Assert.Equal("MEXC", bscLabel.Label);
            Assert.Null(bscLabel.Chain);
            Assert.True(fixture.Cache.TryGet(SupportedChain.Base, exchangeAddress, out var globalExchangeLabel));
            Assert.Equal("MEXC", globalExchangeLabel.Label);
            Assert.True(fixture.Cache.TryGet(SupportedChain.Base, baseAddress, out var baseLabel));
            Assert.Equal("Across Bridge", baseLabel.Label);
        }
        finally
        {
            File.Delete(seedPath);
        }
    }

    [Fact]
    public async Task ImportAsync_DryRunDoesNotWriteRows()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var seedPath = Path.Combine(Path.GetTempPath(), $"address-labels-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(seedPath, CreateSeed(reviewStatus: "reviewed"));

        try
        {
            var report = await fixture.Importer.ImportAsync(seedPath, dryRun: true, allowCandidate: false, CancellationToken.None);
            var enabledLabels = await fixture.Repository.GetEnabledAsync(CancellationToken.None);

            Assert.Equal(1, report.Inserted);
            Assert.Empty(enabledLabels);
        }
        finally
        {
            File.Delete(seedPath);
        }
    }

    [Fact]
    public async Task ImportAsync_RejectsCandidateSeedByDefault()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var seedPath = Path.Combine(Path.GetTempPath(), $"address-labels-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(seedPath, CreateSeed(reviewStatus: "candidate"));

        try
        {
            var report = await fixture.Importer.ImportAsync(seedPath, dryRun: false, allowCandidate: false, CancellationToken.None);
            var enabledLabels = await fixture.Repository.GetEnabledAsync(CancellationToken.None);

            Assert.Equal(1, report.Invalid);
            Assert.Contains(report.Messages, message => message.Contains("--allow-candidate", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(enabledLabels);
        }
        finally
        {
            File.Delete(seedPath);
        }
    }

    [Fact]
    public async Task ImportAsync_AllowsCandidateSeedWhenFlagIsSet()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var seedPath = Path.Combine(Path.GetTempPath(), $"address-labels-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(seedPath, CreateSeed(reviewStatus: "candidate"));

        try
        {
            var report = await fixture.Importer.ImportAsync(seedPath, dryRun: false, allowCandidate: true, CancellationToken.None);
            var enabledLabels = await fixture.Repository.GetEnabledAsync(CancellationToken.None);

            Assert.Equal(1, report.Inserted);
            Assert.Equal(0, report.Invalid);
            Assert.Single(enabledLabels);
        }
        finally
        {
            File.Delete(seedPath);
        }
    }

    [Fact]
    public async Task ImportAsync_RejectsChainScopedExchange()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var seedPath = Path.Combine(Path.GetTempPath(), $"address-labels-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            seedPath,
            """
            {
              "schemaVersion": 1,
              "reviewStatus": "reviewed",
              "scopePolicy": {
                "exchange": "global",
                "bridge": "chainScoped"
              },
              "entries": [
                {
                  "address": "0x1111111111111111111111111111111111111111",
                  "chain": "Base",
                  "nameTag": "MEXC",
                  "kind": "Exchange",
                  "sourceReference": "test"
                }
              ]
            }
            """);

        try
        {
            var report = await fixture.Importer.ImportAsync(seedPath, dryRun: false, allowCandidate: false, CancellationToken.None);
            var enabledLabels = await fixture.Repository.GetEnabledAsync(CancellationToken.None);

            Assert.Equal(1, report.Invalid);
            Assert.Contains(report.Messages, message => message.Contains("global", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(enabledLabels);
        }
        finally
        {
            File.Delete(seedPath);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportAsync_DuneUpsertWinsMatchingGlobalAndPreservesOtherScopes(bool preferGlobalExchanges)
    {
        await using var fixture = await ImportFixture.CreateAsync();
        const string address = "0x1111111111111111111111111111111111111111";
        var original = AddressLabel.Create(address, null, "Old", AddressLabelKind.Exchange,
            AddressLabelSource.Manual, "original", 500).Value;
        var scoped = AddressLabel.Create(address, SupportedChain.Base, "Scoped", AddressLabelKind.Bridge,
            AddressLabelSource.SeedFile, "scoped", 90).Value;
        var unrelated = AddressLabel.Create("0x2222222222222222222222222222222222222222", null, "Unrelated",
            AddressLabelKind.Exchange, AddressLabelSource.Manual, "unrelated", 200).Value;
        await fixture.Repository.UpsertAsync(original, CancellationToken.None);
        await fixture.Repository.UpsertAsync(scoped, CancellationToken.None);
        await fixture.Repository.UpsertAsync(unrelated, CancellationToken.None);
        await fixture.Repository.SaveChangesAsync(CancellationToken.None);
        var path = Path.Combine(Path.GetTempPath(), $"dune-seed-{Guid.NewGuid():N}.json");
        var seed = CreateSeed("reviewed").Replace("\"sourceReference\": \"test\"", "\"source\": \"dune\", \"sourceReference\": \"dune-export.json; tester; 2026-01-01\"");
        await File.WriteAllTextAsync(path, seed);
        try
        {
            var dryRun = await fixture.Importer.ImportAsync(path, true, false, CancellationToken.None, preferGlobalExchanges);
            Assert.Equal(1, dryRun.Updated);
            Assert.Equal(preferGlobalExchanges ? 1 : 0, dryRun.DisabledScoped);
            Assert.True((await fixture.Repository.GetByAddressAndChainAsync(scoped.Address, scoped.Chain, CancellationToken.None))!.IsEnabled);
            Assert.Equal("Old", (await fixture.Repository.GetByAddressAndChainAsync(original.Address, null, CancellationToken.None))!.Label);
            Assert.False(fixture.Cache.TryGet(SupportedChain.Bsc, original.Address, out _));

            var imported = await fixture.Importer.ImportAsync(path, false, false, CancellationToken.None, preferGlobalExchanges);
            var repeated = await fixture.Importer.ImportAsync(path, false, false, CancellationToken.None, preferGlobalExchanges);
            Assert.Equal(1, imported.Updated);
            Assert.Equal(0, imported.Inserted);
            Assert.Equal(0, imported.Invalid);
            Assert.Equal(1, repeated.Updated);
            var rows = await fixture.Repository.GetEnabledAsync(CancellationToken.None);
            Assert.Equal(preferGlobalExchanges ? 2 : 3, rows.Count);
            Assert.Equal(preferGlobalExchanges ? 1 : 0, imported.DisabledScoped);
            Assert.Equal(0, repeated.DisabledScoped);
            var updated = rows.Single(row => row.Id == original.Id);
            Assert.Equal("MEXC", updated.Label);
            Assert.Equal(AddressLabelSource.Dune, updated.Source);
            Assert.Equal("dune-export.json; tester; 2026-01-01", updated.SourceReference);
            Assert.Equal(100, updated.Priority);
            var preservedScope = (await fixture.Repository.GetByAddressAndChainAsync(scoped.Address, scoped.Chain, CancellationToken.None))!;
            Assert.Equal(scoped.Id, preservedScope.Id);
            Assert.Equal("Scoped", preservedScope.Label);
            Assert.Equal(AddressLabelKind.Bridge, preservedScope.Kind);
            Assert.Equal(AddressLabelSource.SeedFile, preservedScope.Source);
            Assert.Equal("scoped", preservedScope.SourceReference);
            Assert.Equal(90, preservedScope.Priority);
            Assert.Equal(!preferGlobalExchanges, preservedScope.IsEnabled);
            Assert.Contains(rows, row => row.Id == unrelated.Id && row.Label == "Unrelated" && row.Source == AddressLabelSource.Manual);
            Assert.True(fixture.Cache.TryGet(SupportedChain.Bsc, original.Address, out var global));
            Assert.Equal(AddressLabelSource.Dune, global.Source);
            Assert.True(fixture.Cache.TryGet(SupportedChain.Base, original.Address, out var chain));
            Assert.Equal(preferGlobalExchanges ? "MEXC" : "Scoped", chain.Label);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ImportAsync_PreferGlobalDisablesExactEnabledRowAndPreservesDisabledHistory()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        const string address = "0x1111111111111111111111111111111111111111";
        var historical = AddressLabel.Create(address, SupportedChain.Ethereum, "Historical Bridge",
            AddressLabelKind.Bridge, AddressLabelSource.Manual, "historical metadata", 999, false).Value;
        var active = AddressLabel.Create(address, SupportedChain.Ethereum, "Active Bridge",
            AddressLabelKind.Bridge, AddressLabelSource.SeedFile, "active metadata", 90).Value;
        // Disabled history can coexist with one enabled row under the filtered unique index.
        fixture.DbContext.AddressLabels.Add(historical);
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.AddressLabels.Add(active);
        await fixture.DbContext.SaveChangesAsync();
        var historicalBefore = System.Text.Json.JsonSerializer.Serialize(historical);
        var activeBefore = (active.Id, active.Address, active.Chain, active.Label, active.Kind, active.Source,
            active.SourceReference, active.Priority, active.CreatedAtUtc);
        var activeUpdatedBefore = active.UpdatedAtUtc;
        var path = Path.Combine(Path.GetTempPath(), $"history-seed-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, CreateSeed("reviewed"));
        try
        {
            var dryRun = await fixture.Importer.ImportAsync(path, true, false, CancellationToken.None, true);
            Assert.Equal(1, dryRun.DisabledScoped);
            Assert.True(active.IsEnabled);
            Assert.Equal(activeUpdatedBefore, active.UpdatedAtUtc);
            Assert.Equal(historicalBefore, System.Text.Json.JsonSerializer.Serialize(historical));

            var imported = await fixture.Importer.ImportAsync(path, false, false, CancellationToken.None, true);
            Assert.Equal(1, imported.DisabledScoped);
            fixture.DbContext.ChangeTracker.Clear();
            var rows = await fixture.DbContext.AddressLabels.AsNoTracking().ToListAsync();
            Assert.Equal(3, rows.Count);
            var disabled = rows.Single(row => row.Id == active.Id);
            Assert.False(disabled.IsEnabled);
            Assert.Equal(activeBefore, (disabled.Id, disabled.Address, disabled.Chain, disabled.Label, disabled.Kind,
                disabled.Source, disabled.SourceReference, disabled.Priority, disabled.CreatedAtUtc));
            Assert.NotEqual(activeUpdatedBefore, disabled.UpdatedAtUtc);
            Assert.Equal(historicalBefore, System.Text.Json.JsonSerializer.Serialize(rows.Single(row => row.Id == historical.Id)));
            Assert.True(fixture.Cache.TryGet(SupportedChain.Ethereum, active.Address, out var resolved));
            Assert.Equal("MEXC", resolved.Label);
            Assert.Equal(0, (await fixture.Importer.ImportAsync(path, false, false, CancellationToken.None, true)).DisabledScoped);
            Assert.False(await fixture.Repository.DisableByIdAsync(historical.Id, CancellationToken.None));
            Assert.False(await fixture.Repository.DisableByIdAsync(Guid.NewGuid(), CancellationToken.None));
            await fixture.Repository.SaveChangesAsync(CancellationToken.None);
            Assert.Equal(historicalBefore, System.Text.Json.JsonSerializer.Serialize(
                await fixture.DbContext.AddressLabels.AsNoTracking().SingleAsync(row => row.Id == historical.Id)));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("etherscan", AddressLabelSource.SeedFile)]
    [InlineData("DUNE", AddressLabelSource.Dune)]
    [InlineData("unknown", AddressLabelSource.SeedFile)]
    public async Task ImportAsync_PreservesSourceCompatibility(string source, AddressLabelSource expected)
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var path = Path.Combine(Path.GetTempPath(), $"source-seed-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, CreateSeed("reviewed").Replace("\"sourceReference\": \"test\"", $"\"source\": \"{source}\", \"sourceReference\": \"test\""));
        try
        {
            var report = await fixture.Importer.ImportAsync(path, false, false, CancellationToken.None);
            Assert.Equal(0, report.Invalid);
            Assert.Equal(expected, Assert.Single(await fixture.Repository.GetEnabledAsync(CancellationToken.None)).Source);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportAsync_NonObjectRowsAreInvalidWithoutAbortingValidEntries(bool dryRun)
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var seed = System.Text.Json.Nodes.JsonNode.Parse(CreateSeed("reviewed"))!;
        var entries = seed["entries"]!.AsArray();
        entries.Add((System.Text.Json.Nodes.JsonNode?)null);
        entries.Add(123);
        entries.Add("untrusted text");
        var path = Path.Combine(Path.GetTempPath(), $"nonobject-seed-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, seed.ToJsonString());
        try
        {
            var report = await fixture.Importer.ImportAsync(path, dryRun, false, CancellationToken.None, true);
            Assert.Equal(1, report.Inserted);
            Assert.Equal(3, report.Invalid);
            Assert.Equal(3, report.Messages.Count);
            Assert.Equal(dryRun ? 0 : 1, (await fixture.Repository.GetEnabledAsync(CancellationToken.None)).Count);
        }
        finally { File.Delete(path); }
    }

    private static string CreateSeed(string reviewStatus)
        => $$"""
           {
             "schemaVersion": 1,
             "reviewStatus": "{{reviewStatus}}",
             "scopePolicy": {
               "exchange": "global",
               "bridge": "chainScoped"
             },
             "entries": [
               {
                 "address": "0x1111111111111111111111111111111111111111",
                 "chain": null,
                 "nameTag": "MEXC",
                 "kind": "Exchange",
                 "sourceReference": "test"
               }
             ]
           }
           """;

    private sealed class ImportFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ImportFixture(
            SqliteConnection connection,
            AppDbContext dbContext,
            AddressLabelRepository repository,
            InMemoryAddressLabelCacheService cache,
            AddressLabelSeedImporter importer)
        {
            _connection = connection;
            DbContext = dbContext;
            Repository = repository;
            Cache = cache;
            Importer = importer;
        }

        public AppDbContext DbContext { get; }

        public AddressLabelRepository Repository { get; }

        public InMemoryAddressLabelCacheService Cache { get; }

        public AddressLabelSeedImporter Importer { get; }

        public static async Task<ImportFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var repository = new AddressLabelRepository(dbContext);
            var cache = new InMemoryAddressLabelCacheService();
            var importer = new AddressLabelSeedImporter(repository, cache);

            return new ImportFixture(connection, dbContext, repository, cache, importer);
        }

        public async ValueTask DisposeAsync()
        {
            Cache.Dispose();
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
