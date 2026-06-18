using Application.Abstractions;
using Application.Messaging;
using Domain.Common;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure.Caching;
using Presentation.Telegram.Services;

namespace UnitTests.Presentation;

public sealed class TelegramNotificationEnricherTests
{
    [Fact]
    public async Task EnrichAsync_UsesTrackedWalletAliasBeforeAddressLabel()
    {
        using var cache = new InMemoryAddressLabelCacheService();
        cache.ReplaceAll(
        [
            CreateLabel("0x52908400098527886E0F7030069857D2E4169EE7", null, "vitalik.eth")
        ]);

        var enricher = CreateEnricher(cache);
        var payload = CreatePayload(trackedWalletAlias: "my whale");

        var enriched = await enricher.EnrichAsync(payload, CancellationToken.None);

        Assert.Equal("my whale", enriched.TrackedWalletDisplayText);
    }

    [Fact]
    public async Task EnrichAsync_UsesAddressLabelsForTrackedWalletFallbackAndCounterparties()
    {
        using var cache = new InMemoryAddressLabelCacheService();
        cache.ReplaceAll(
        [
            CreateLabel("0x52908400098527886E0F7030069857D2E4169EE7", null, "vitalik.eth"),
            CreateLabel("0x1111111111111111111111111111111111111111", SupportedChain.Bsc, "MEXC")
        ]);

        var enricher = CreateEnricher(cache);
        var payload = CreatePayload(
            trackedWalletAlias: null,
            counterpartyAlias: "raw alias");

        var enriched = await enricher.EnrichAsync(payload, CancellationToken.None);

        Assert.Equal("vitalik.eth", enriched.TrackedWalletDisplayText);
        Assert.Equal("MEXC", enriched.Lines.Single().CounterpartyDisplayText);
    }

    [Fact]
    public async Task EnrichAsync_UsesCounterpartyAliasWhenNoLabelExists()
    {
        using var cache = new InMemoryAddressLabelCacheService();
        var enricher = CreateEnricher(cache);
        var payload = CreatePayload(
            trackedWalletAlias: "tracked",
            counterpartyAlias: "raw alias");

        var enriched = await enricher.EnrichAsync(payload, CancellationToken.None);

        Assert.Equal("raw alias", enriched.Lines.Single().CounterpartyDisplayText);
    }

    [Fact]
    public async Task EnrichAsync_UsesShortAddressWhenNoCounterpartyAliasOrLabelExists()
    {
        using var cache = new InMemoryAddressLabelCacheService();
        var enricher = CreateEnricher(cache);
        var payload = CreatePayload(
            trackedWalletAlias: "tracked",
            counterpartyAlias: null);

        var enriched = await enricher.EnrichAsync(payload, CancellationToken.None);

        Assert.Equal("0x1111...1111", enriched.Lines.Single().CounterpartyDisplayText);
    }

    [Fact]
    public async Task EnrichAsync_UsesEnsResolutionAsLabelForEthereumCounterparty()
    {
        using var cache = new InMemoryAddressLabelCacheService();
        var enricher = CreateEnricher(cache, ensName: "counterparty.eth");
        var payload = CreatePayload(
            chain: SupportedChain.Ethereum,
            trackedWalletAlias: "tracked",
            counterpartyAlias: "raw alias");

        var enriched = await enricher.EnrichAsync(payload, CancellationToken.None);

        Assert.Equal("counterparty.eth", enriched.Lines.Single().CounterpartyDisplayText);
    }

    private static TelegramNotificationEnricher CreateEnricher(
        InMemoryAddressLabelCacheService cache,
        string? ensName = null)
        => new(new AddressLabelResolver(cache, new FakeEnsReverseLookupProvider(ensName)));

    private static NotificationPayload CreatePayload(
        SupportedChain chain = SupportedChain.Bsc,
        string? trackedWalletAlias = "debabki.eth",
        string? counterpartyAlias = null)
        => new(
            TelegramUserId: 7777,
            TrackedWalletId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Chain: chain,
            Operation: NotificationOperation.Received,
            TrackedWalletAddress: "0x52908400098527886E0F7030069857D2E4169EE7",
            TrackedWalletAlias: trackedWalletAlias,
            CounterpartyAddress: "0x1111111111111111111111111111111111111111",
            CounterpartyAlias: counterpartyAlias,
            TransactionHash: "0xtxhash",
            AssetSymbol: "BNB",
            AssetName: "BNB",
            TokenContractAddress: null,
            Amount: 1.25m,
            UsdValue: 750m,
            BlockNumber: 100,
            BlockTimestampUtc: DateTimeOffset.UtcNow);

    private static AddressLabelCacheEntry CreateLabel(string address, SupportedChain? chain, string label)
    {
        var addressResult = Address.Normalize(address);
        Assert.True(addressResult.IsSuccess);

        return new AddressLabelCacheEntry(
            addressResult.Value,
            chain,
            label,
            AddressLabelKind.Exchange,
            AddressLabelSource.SeedFile,
            Priority: 100);
    }

    private sealed class FakeEnsReverseLookupProvider(string? ensName) : IEnsReverseLookupProvider
    {
        public Task<string?> ResolveNameAsync(Address address, CancellationToken cancellationToken)
            => Task.FromResult(ensName);
    }
}
