using Domain.Common;
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
