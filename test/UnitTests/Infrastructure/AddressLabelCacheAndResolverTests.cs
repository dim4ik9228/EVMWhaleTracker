using Application.Abstractions;
using Domain.Common;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure.Caching;

namespace UnitTests.Infrastructure;

public sealed class AddressLabelCacheAndResolverTests
{
    private const string AddressValue = "0x52908400098527886E0F7030069857D2E4169EE7";

    [Fact]
    public void TryGet_ReturnsChainSpecificLabelBeforeGlobalLabel()
    {
        using var cache = new InMemoryAddressLabelCacheService();
        var address = Address.Create(AddressValue).Value;
        cache.ReplaceAll(
        [
            CreateEntry(address, null, "Global Entity"),
            CreateEntry(address, SupportedChain.Bsc, "BSC Entity")
        ]);

        var found = cache.TryGet(SupportedChain.Bsc, address, out var label);

        Assert.True(found);
        Assert.Equal("BSC Entity", label.Label);
    }

    [Fact]
    public void TryGet_FallsBackToGlobalLabelAcrossChains()
    {
        using var cache = new InMemoryAddressLabelCacheService();
        var address = Address.Create(AddressValue).Value;
        cache.ReplaceAll([CreateEntry(address, null, "Global Entity")]);

        var found = cache.TryGet(SupportedChain.Base, address, out var label);

        Assert.True(found);
        Assert.Equal("Global Entity", label.Label);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsShortAddressWhenNoLabelExists()
    {
        using var cache = new InMemoryAddressLabelCacheService();
        var resolver = new AddressLabelResolver(cache, new FakeEnsReverseLookupProvider());
        var address = Address.Create(AddressValue).Value;

        var resolution = await resolver.ResolveAsync(SupportedChain.Bsc, address, CancellationToken.None);

        Assert.False(resolution.IsLabeled);
        Assert.Equal("0x5290...9EE7", resolution.DisplayText);
        Assert.Equal(AddressLabelKind.Unknown, resolution.Kind);
    }

    [Fact]
    public async Task ResolveAsync_UsesEnsFallbackForEthereumCacheMiss()
    {
        using var cache = new InMemoryAddressLabelCacheService();
        var ensProvider = new FakeEnsReverseLookupProvider("vitalik.eth");
        var resolver = new AddressLabelResolver(cache, ensProvider);
        var address = Address.Create(AddressValue).Value;

        var resolution = await resolver.ResolveAsync(SupportedChain.Ethereum, address, CancellationToken.None);

        Assert.True(resolution.IsLabeled);
        Assert.Equal("vitalik.eth", resolution.DisplayText);
        Assert.Equal(AddressLabelKind.Ens, resolution.Kind);
        Assert.Equal(AddressLabelSource.EnsReverse, resolution.Source);
        Assert.Equal(1, ensProvider.Calls);
    }

    [Fact]
    public async Task ResolveAsync_DoesNotUseEnsForNonEthereumCacheMiss()
    {
        using var cache = new InMemoryAddressLabelCacheService();
        var ensProvider = new FakeEnsReverseLookupProvider("vitalik.eth");
        var resolver = new AddressLabelResolver(cache, ensProvider);
        var address = Address.Create(AddressValue).Value;

        var resolution = await resolver.ResolveAsync(SupportedChain.Base, address, CancellationToken.None);

        Assert.False(resolution.IsLabeled);
        Assert.Equal(0, ensProvider.Calls);
    }

    [Fact]
    public async Task ResolveAsync_CacheLabelBeatsEnsFallback()
    {
        using var cache = new InMemoryAddressLabelCacheService();
        var address = Address.Create(AddressValue).Value;
        cache.ReplaceAll([CreateEntry(address, SupportedChain.Ethereum, "Binance")]);
        var ensProvider = new FakeEnsReverseLookupProvider("vitalik.eth");
        var resolver = new AddressLabelResolver(cache, ensProvider);

        var resolution = await resolver.ResolveAsync(SupportedChain.Ethereum, address, CancellationToken.None);

        Assert.True(resolution.IsLabeled);
        Assert.Equal("Binance", resolution.DisplayText);
        Assert.Equal(0, ensProvider.Calls);
    }

    private static AddressLabelCacheEntry CreateEntry(Address address, SupportedChain? chain, string label)
        => new(
            address,
            chain,
            label,
            AddressLabelKind.Exchange,
            AddressLabelSource.SeedFile,
            Priority: 100);

    private sealed class FakeEnsReverseLookupProvider(string? name = null) : IEnsReverseLookupProvider
    {
        public int Calls { get; private set; }

        public Task<string?> ResolveNameAsync(Address address, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(name);
        }
    }
}
