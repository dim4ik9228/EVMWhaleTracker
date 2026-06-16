using Domain.Common;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace UnitTests.Infrastructure.Persistence;

public sealed class AddressLabelRepositoryTests
{
    private const string AddressValue = "0x52908400098527886e0f7030069857d2e4169ee7";

    [Fact]
    public async Task UpsertAsync_InsertsAndUpdatesLabel()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var label = CreateLabel("MEXC", SupportedChain.Bsc);

        await fixture.Repository.UpsertAsync(label, CancellationToken.None);
        await fixture.Repository.SaveChangesAsync(CancellationToken.None);

        var updatedLabel = CreateLabel("MEXC Hot Wallet", SupportedChain.Bsc);
        await fixture.Repository.UpsertAsync(updatedLabel, CancellationToken.None);
        await fixture.Repository.SaveChangesAsync(CancellationToken.None);

        var labels = await fixture.Repository.GetEnabledAsync(CancellationToken.None);

        Assert.Single(labels);
        Assert.Equal("MEXC Hot Wallet", labels[0].Label);
        Assert.Equal(SupportedChain.Bsc, labels[0].Chain);
    }

    [Fact]
    public async Task GetEnabledAsync_ExcludesDisabledLabels()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var enabled = CreateLabel("MEXC", SupportedChain.Bsc);
        var disabled = AddressLabel.Create(
            "0x1111111111111111111111111111111111111111",
            SupportedChain.Bsc,
            "Disabled",
            AddressLabelKind.Exchange,
            AddressLabelSource.SeedFile,
            isEnabled: false).Value;

        await fixture.Repository.UpsertAsync(enabled, CancellationToken.None);
        await fixture.Repository.UpsertAsync(disabled, CancellationToken.None);
        await fixture.Repository.SaveChangesAsync(CancellationToken.None);

        var labels = await fixture.Repository.GetEnabledAsync(CancellationToken.None);

        Assert.Single(labels);
        Assert.Equal("MEXC", labels[0].Label);
    }

    private static AddressLabel CreateLabel(string label, SupportedChain? chain)
        => AddressLabel.Create(
            AddressValue,
            chain,
            label,
            AddressLabelKind.Exchange,
            AddressLabelSource.SeedFile).Value;

    private sealed class PersistenceFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private PersistenceFixture(SqliteConnection connection, AppDbContext dbContext, AddressLabelRepository repository)
        {
            _connection = connection;
            DbContext = dbContext;
            Repository = repository;
        }

        public AppDbContext DbContext { get; }

        public AddressLabelRepository Repository { get; }

        public static async Task<PersistenceFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var repository = new AddressLabelRepository(dbContext);
            return new PersistenceFixture(connection, dbContext, repository);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
