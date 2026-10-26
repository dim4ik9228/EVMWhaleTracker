using Domain.Entities;
using Domain.Common;
using Domain.ValueObjects;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace UnitTests.Infrastructure.Persistence;

public sealed class TrackedWalletRepositoryTests
{
    private const string LowercaseAddress = "0x52908400098527886e0f7030069857d2e4169ee7";

    [Fact]
    public async Task NewChainSettings_RoundTripWithoutChangingExistingExplicitLists()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var wallet = CreateTrackedWallet(12345);
        Assert.True(wallet.UpdateEnabledChains([SupportedChain.Robinhood, SupportedChain.Arc]).IsSuccess);
        await fixture.Repository.AddAsync(wallet, CancellationToken.None);
        await fixture.Repository.SaveChangesAsync(CancellationToken.None);
        Assert.Equal("[4663,5042]", fixture.DbContext.Entry(wallet).Property<string>("_enabledChainsJson").CurrentValue);
        fixture.DbContext.ChangeTracker.Clear();
        var fetched = await fixture.Repository.GetByUserIdAndAddressAsync(12345, wallet.Address, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal([SupportedChain.Robinhood, SupportedChain.Arc], fetched.EnabledChains);
        Assert.True(fetched.UpdateEnabledChains([SupportedChain.Bsc, SupportedChain.Base]).IsSuccess);
        await fixture.Repository.SaveChangesAsync(CancellationToken.None);
        fixture.DbContext.ChangeTracker.Clear();
        fetched = await fixture.Repository.GetByUserIdAndAddressAsync(12345, wallet.Address, CancellationToken.None);
        Assert.Equal([SupportedChain.Bsc, SupportedChain.Base], fetched!.EnabledChains);
    }

    [Fact]
    public async Task ExistingAllButOneNumericChainList_RemainsExplicitAfterNetworkExpansion()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var wallet = CreateTrackedWallet(12345);
        await fixture.Repository.AddAsync(wallet, CancellationToken.None);
        // Stored before Robinhood and Arc were added: all original chains except Base.
        const string savedJson = "[1,10,30,56,100,137,999,42161,42220]";
        fixture.DbContext.Entry(wallet).Property<string>("_enabledChainsJson").CurrentValue = savedJson;
        await fixture.Repository.SaveChangesAsync(CancellationToken.None);
        fixture.DbContext.ChangeTracker.Clear();

        var fetched = await fixture.Repository.GetByUserIdAndAddressAsync(12345, wallet.Address, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal(savedJson, fixture.DbContext.Entry(fetched).Property<string>("_enabledChainsJson").CurrentValue);
        Assert.DoesNotContain(SupportedChain.Robinhood, fetched.EnabledChains);
        Assert.DoesNotContain(SupportedChain.Arc, fetched.EnabledChains);

        Assert.True(fetched.SetChainTracking(SupportedChain.Base, enabled: true).IsSuccess);
        await fixture.Repository.SaveChangesAsync(CancellationToken.None);
        fixture.DbContext.ChangeTracker.Clear();
        fetched = await fixture.Repository.GetByUserIdAndAddressAsync(12345, wallet.Address, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Equal(10, fetched.EnabledChains.Count);
        Assert.DoesNotContain(SupportedChain.Robinhood, fetched.EnabledChains);
        Assert.DoesNotContain(SupportedChain.Arc, fetched.EnabledChains);
    }

    [Fact]
    public async Task Add_SaveAndQueryByUserIdAndAddress_Works()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var wallet = CreateTrackedWallet(12345);

        await fixture.Repository.AddAsync(wallet, CancellationToken.None);
        await fixture.Repository.SaveChangesAsync(CancellationToken.None);

        var fetched = await fixture.Repository.GetByUserIdAndAddressAsync(12345, wallet.Address, CancellationToken.None);
        var allWallets = await fixture.Repository.ListAllAsync(CancellationToken.None);
        var userWallets = await fixture.Repository.ListByUserIdAsync(12345, CancellationToken.None);

        Assert.NotNull(fetched);
        Assert.Equal(wallet.Id, fetched.Id);
        Assert.Equal(wallet.Alias, fetched.Alias);
        Assert.Equal(wallet.EnabledChains, fetched.EnabledChains);
        Assert.Single(allWallets);
        Assert.Single(userWallets);
    }

    [Fact]
    public async Task RemoveAsync_DeletesWalletFromDatabase()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var wallet = CreateTrackedWallet(12345);

        await fixture.Repository.AddAsync(wallet, CancellationToken.None);
        await fixture.Repository.SaveChangesAsync(CancellationToken.None);

        await fixture.Repository.RemoveAsync(wallet, CancellationToken.None);
        await fixture.Repository.SaveChangesAsync(CancellationToken.None);

        var allWallets = await fixture.Repository.ListAllAsync(CancellationToken.None);
        var fetched = await fixture.Repository.GetByUserIdAndAddressAsync(12345, wallet.Address, CancellationToken.None);

        Assert.Empty(allWallets);
        Assert.Null(fetched);
    }

    [Fact]
    public async Task DuplicateUserAndAddressCombination_IsRejectedByUniqueIndex()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var firstWallet = CreateTrackedWallet(12345);
        var secondWallet = CreateTrackedWallet(12345);

        await fixture.Repository.AddAsync(firstWallet, CancellationToken.None);
        await fixture.Repository.AddAsync(secondWallet, CancellationToken.None);

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Repository.SaveChangesAsync(CancellationToken.None));
    }

    private static TrackedWallet CreateTrackedWallet(long userId)
    {
        var normalizedAddressResult = Address.Normalize(LowercaseAddress);
        Assert.True(normalizedAddressResult.IsSuccess);

        var result = TrackedWallet.Create(
            normalizedAddressResult.Value,
            userId,
            minimumUsdValue: 2m,
            alias: "MEXC Hot Wallet",
            enabledChains: [SupportedChain.Bsc, SupportedChain.Base]);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private sealed class PersistenceFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private PersistenceFixture(SqliteConnection connection, AppDbContext dbContext, TrackedWalletRepository repository)
        {
            _connection = connection;
            DbContext = dbContext;
            Repository = repository;
        }

        public AppDbContext DbContext { get; }

        public TrackedWalletRepository Repository { get; }

        public static async Task<PersistenceFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var repository = new TrackedWalletRepository(dbContext);
            return new PersistenceFixture(connection, dbContext, repository);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
