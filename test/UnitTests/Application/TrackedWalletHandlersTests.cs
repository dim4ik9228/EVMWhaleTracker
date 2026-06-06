using Domain.Common;
using Application.Abstractions;
using Application.TrackedWallets;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure.Caching;

namespace UnitTests.Application;

public sealed class TrackedWalletHandlersTests
{
    private const string LowercaseAddress = "0x52908400098527886e0f7030069857d2e4169ee7";

    [Fact]
    public async Task AddTrackedWalletCommandHandler_NormalizesAddress_AndUpdatesCache()
    {
        using var cache = new InMemoryCacheService();
        var repository = new FakeTrackedWalletRepository();
        var handler = new AddTrackedWalletCommandHandler(repository, cache);

        var result = await handler.Handle(
            new AddTrackedWalletCommand(
                TelegramUserId: 7777,
                RawAddress: LowercaseAddress,
                MinimumTransactionValue: 2m,
                TrackIncomingTransfers: true,
                TrackOutgoingTransfers: false,
                TrackTokenTransfers: true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Normalize(LowercaseAddress), result.Value.Address);
        Assert.Equal(7777, result.Value.UserId);
        Assert.Equal(2m, result.Value.MinimumTransactionValue);
        Assert.Single(repository.Wallets);

        var normalizedAddress = NormalizeAddress(LowercaseAddress);
        Assert.True(cache.Contains(normalizedAddress));
        Assert.True(cache.TryGetTelegramUserIds(normalizedAddress, out var telegramUserIds));
        Assert.Equal([7777], telegramUserIds);
    }

    [Fact]
    public async Task AddTrackedWalletCommandHandler_RejectsDuplicateWalletForSameUser()
    {
        using var cache = new InMemoryCacheService();
        var repository = new FakeTrackedWalletRepository();
        repository.Seed(CreateTrackedWallet(7777));

        var handler = new AddTrackedWalletCommandHandler(repository, cache);

        var result = await handler.Handle(
            new AddTrackedWalletCommand(7777, LowercaseAddress),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(Errors.TrackedWallet.AlreadyTracked, result.Error);
        Assert.Single(repository.Wallets);
        Assert.False(cache.Contains(NormalizeAddress(LowercaseAddress)));
    }

    [Fact]
    public async Task ListTrackedWalletsQueryHandler_ReturnsOnlyCurrentUsersWallets()
    {
        var repository = new FakeTrackedWalletRepository();
        repository.Seed(CreateTrackedWallet(1111));
        repository.Seed(CreateTrackedWallet(2222));
        repository.Seed(CreateTrackedWallet(1111, "0x1111111111111111111111111111111111111111"));

        var handler = new ListTrackedWalletsQueryHandler(repository);

        var result = await handler.Handle(new ListTrackedWalletsQuery(1111), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.All(result.Value, item => Assert.Equal(1111, item.UserId));
    }

    [Fact]
    public async Task RemoveTrackedWalletCommandHandler_RemovesWalletFromRepositoryAndCache()
    {
        using var cache = new InMemoryCacheService();
        var repository = new FakeTrackedWalletRepository();
        var trackedWallet = CreateTrackedWallet(7777);
        repository.Seed(trackedWallet);
        cache.Add(trackedWallet);

        var handler = new RemoveTrackedWalletCommandHandler(repository, cache);

        var result = await handler.Handle(
            new RemoveTrackedWalletCommand(7777, LowercaseAddress),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(repository.Wallets);
        Assert.False(cache.Contains(trackedWallet.Address));
    }

    [Fact]
    public async Task UpdateTrackedWalletSettingsCommandHandler_UpdatesWalletConfiguration()
    {
        var repository = new FakeTrackedWalletRepository();
        var trackedWallet = CreateTrackedWallet(7777);
        repository.Seed(trackedWallet);

        var handler = new UpdateTrackedWalletSettingsCommandHandler(repository);

        var result = await handler.Handle(
            new UpdateTrackedWalletSettingsCommand(
                TelegramUserId: 7777,
                RawAddress: LowercaseAddress,
                MinimumTransactionValue: 5m,
                TrackIncomingTransfers: false,
                TrackOutgoingTransfers: true,
                TrackTokenTransfers: false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5m, result.Value.MinimumTransactionValue);
        Assert.False(result.Value.TrackIncomingTransfers);
        Assert.True(result.Value.TrackOutgoingTransfers);
        Assert.False(result.Value.TrackTokenTransfers);
        Assert.NotNull(trackedWallet.UpdatedAtUtc);
        Assert.Equal(5m, trackedWallet.MinimumTransactionValue);
    }

    private static string Normalize(string address)
        => NormalizeAddress(address).Value;

    private static Address NormalizeAddress(string address)
    {
        var result = Address.Normalize(address);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static TrackedWallet CreateTrackedWallet(long userId, string? address = null)
    {
        var normalizedAddress = NormalizeAddress(address ?? LowercaseAddress);
        var result = TrackedWallet.Create(normalizedAddress, userId, minimumTransactionValue: 1m);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private sealed class FakeTrackedWalletRepository : ITrackedWalletRepository
    {
        private readonly List<TrackedWallet> _wallets = [];

        public IReadOnlyList<TrackedWallet> Wallets => _wallets;

        public void Seed(TrackedWallet trackedWallet) => _wallets.Add(trackedWallet);

        public Task<IReadOnlyList<TrackedWallet>> ListAllAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TrackedWallet>>(_wallets.ToArray());

        public Task<IReadOnlyList<TrackedWallet>> ListByUserIdAsync(long userId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TrackedWallet>>(_wallets.Where(wallet => wallet.UserId == userId).ToArray());

        public Task<TrackedWallet?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(_wallets.SingleOrDefault(wallet => wallet.Id == id));

        public Task<TrackedWallet?> GetByUserIdAndAddressAsync(long userId, Address address, CancellationToken cancellationToken)
            => Task.FromResult(_wallets.SingleOrDefault(wallet => wallet.UserId == userId && wallet.Address.Equals(address)));

        public Task AddAsync(TrackedWallet trackedWallet, CancellationToken cancellationToken)
        {
            _wallets.Add(trackedWallet);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(TrackedWallet trackedWallet, CancellationToken cancellationToken)
        {
            _wallets.Remove(trackedWallet);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
            => Task.FromResult(1);
    }
}
