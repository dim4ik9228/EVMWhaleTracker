using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure.Caching;

namespace UnitTests.Infrastructure;

public sealed class InMemoryCacheServiceTests
{
    private const string AddressValue = "0x52908400098527886E0F7030069857D2E4169EE7";

    [Fact]
    public void Add_AndContains_RecognizeTrackedAddress()
    {
        using var cache = new InMemoryCacheService();
        var address = CreateAddress();

        var added = cache.Add(address);
        var contains = cache.Contains(address);

        Assert.True(added);
        Assert.True(contains);
    }

    [Fact]
    public void ReplaceAll_WithTrackedWallets_PreservesMultipleOwnersForSameAddress()
    {
        using var cache = new InMemoryCacheService();
        var firstWallet = CreateTrackedWallet(1111);
        var secondWallet = CreateTrackedWallet(2222);

        cache.ReplaceAll([firstWallet, secondWallet]);

        Assert.True(cache.Contains(firstWallet.Address));
        Assert.True(cache.TryGetTelegramUserIds(firstWallet.Address, out var telegramUserIds));
        Assert.Equal([1111, 2222], telegramUserIds.OrderBy(value => value));
    }

    [Fact]
    public void Remove_LastTrackedWalletForAddress_RemovesAddressFromCache()
    {
        using var cache = new InMemoryCacheService();
        var firstWallet = CreateTrackedWallet(1111);
        var secondWallet = CreateTrackedWallet(2222);

        cache.Add(firstWallet);
        cache.Add(secondWallet);

        Assert.True(cache.Remove(firstWallet));
        Assert.True(cache.Contains(firstWallet.Address));

        Assert.True(cache.Remove(secondWallet));
        Assert.False(cache.Contains(secondWallet.Address));
        Assert.False(cache.TryGetTelegramUserIds(secondWallet.Address, out _));
    }

    [Fact]
    public async Task WaitForChangeAsync_CompletesWhenAddressSetChanges()
    {
        using var cache = new InMemoryCacheService();
        var observedVersion = cache.Version;

        var waitTask = cache.WaitForChangeAsync(observedVersion, CancellationToken.None);

        Assert.False(waitTask.IsCompleted);

        cache.Add(CreateAddress());

        await waitTask;
        Assert.True(cache.Version > observedVersion);
    }

    [Fact]
    public void Add_ExistingAddressForAnotherWallet_DoesNotChangeVersion()
    {
        using var cache = new InMemoryCacheService();
        var firstWallet = CreateTrackedWallet(1111);
        var secondWallet = CreateTrackedWallet(2222);

        cache.Add(firstWallet);
        var versionAfterFirstWallet = cache.Version;

        cache.Add(secondWallet);

        Assert.Equal(versionAfterFirstWallet, cache.Version);
    }

    private static Address CreateAddress()
    {
        var result = Address.Create(AddressValue);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static TrackedWallet CreateTrackedWallet(long userId)
    {
        var result = TrackedWallet.Create(AddressValue, userId, minimumUsdValue: 1m);
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
