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
    public void Resolve_ReturnsShortAddressWhenNoLabelExists()
    {
        using var cache = new InMemoryAddressLabelCacheService();
        var resolver = new AddressLabelResolver(cache);
        var address = Address.Create(AddressValue).Value;

        var resolution = resolver.Resolve(SupportedChain.Bsc, address);

        Assert.False(resolution.IsLabeled);
        Assert.Equal("0x5290...9EE7", resolution.DisplayText);
        Assert.Equal(AddressLabelKind.Unknown, resolution.Kind);
    }

    private static AddressLabelCacheEntry CreateEntry(Address address, SupportedChain? chain, string label)
        => new(
            address,
            chain,
            label,
            AddressLabelKind.Exchange,
            AddressLabelSource.SeedFile,
            Priority: 100);
}
