using Application.Abstractions;
using Application.Messaging;
using Domain.Common;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure.Blockchain;
using Infrastructure.Caching;
using Presentation.Telegram.Services;
using Telegram.Bot.Types.Enums;

namespace UnitTests.Presentation;

public sealed class TelegramNotificationRendererTests
{
    [Fact]
    public void Render_EscapesMarkdownV2_AndUsesCompactLinkedLayout()
    {
        var renderer = CreateRenderer();
        var payload = new NotificationPayload(
            TelegramUserId: 7777,
            Chain: SupportedChain.Bsc,
            Operation: NotificationOperation.Received,
            TrackedWalletAddress: "0x52908400098527886E0F7030069857D2E4169EE7",
            TrackedWalletAlias: "debabki.eth",
            CounterpartyAddress: "0x1111111111111111111111111111111111111111",
            CounterpartyAlias: "from_wallet[1]",
            TransactionHash: "0xtxhash",
            AssetSymbol: "USDT",
            AssetName: "Tether USD",
            TokenContractAddress: "0x55d398326f99059fF775485246999027B3197955",
            Amount: 252.39m,
            UsdValue: 252m,
            BlockNumber: 100,
            BlockTimestampUtc: DateTimeOffset.UtcNow);

        var rendered = renderer.Render(payload);

        Assert.Equal(ParseMode.MarkdownV2, rendered.ParseMode);
        Assert.Contains("[debabki\\.eth](https://bscscan.com/address/0x52908400098527886E0F7030069857D2E4169EE7) \\| BNB", rendered.Text);
        Assert.Contains("Received: 252\\.39 [USDT](https://bscscan.com/token/0x55d398326f99059fF775485246999027B3197955) \\(\\~$252\\) From: [from\\_wallet\\[1\\]](https://bscscan.com/address/0x1111111111111111111111111111111111111111)", rendered.Text);
        Assert.Contains("[Tx hash](https://bscscan.com/tx/0xtxhash)", rendered.Text);
    }

    [Fact]
    public void Render_KeepsNativeAssetSymbolUnlinked()
    {
        var renderer = CreateRenderer();
        var payload = new NotificationPayload(
            TelegramUserId: 7777,
            Chain: SupportedChain.Bsc,
            Operation: NotificationOperation.Received,
            TrackedWalletAddress: "0x52908400098527886E0F7030069857D2E4169EE7",
            TrackedWalletAlias: null,
            CounterpartyAddress: "0x1111111111111111111111111111111111111111",
            CounterpartyAlias: null,
            TransactionHash: "0xtxhash",
            AssetSymbol: "BNB",
            AssetName: "BNB",
            TokenContractAddress: null,
            Amount: 1.25m,
            UsdValue: 750m,
            BlockNumber: 100,
            BlockTimestampUtc: DateTimeOffset.UtcNow);

        var rendered = renderer.Render(payload);

        Assert.Contains("1\\.25 BNB", rendered.Text);
        Assert.DoesNotContain("/token/", rendered.Text);
    }

    [Fact]
    public void Render_RendersMultipleTransferLinesForAggregatedNotification()
    {
        var renderer = CreateRenderer();
        var payload = new AggregatedNotificationPayload(
            TelegramUserId: 7777,
            Chain: SupportedChain.Bsc,
            TrackedWalletAddress: "0x52908400098527886E0F7030069857D2E4169EE7",
            TrackedWalletAlias: "debabki.eth",
            TransactionHash: "0xswap",
            BlockNumber: 100,
            BlockTimestampUtc: DateTimeOffset.UtcNow,
            Lines:
            [
                new NotificationTransferLine(
                    Operation: NotificationOperation.Received,
                    CounterpartyAddress: "0x1111111111111111111111111111111111111111",
                    CounterpartyAlias: null,
                    AssetSymbol: "ESPORTS",
                    AssetName: "Esports",
                    TokenContractAddress: "0x2222222222222222222222222222222222222222",
                    Amount: 62877m,
                    UsdValue: 2876m),
                new NotificationTransferLine(
                    Operation: NotificationOperation.Sent,
                    CounterpartyAddress: "0x3333333333333333333333333333333333333333",
                    CounterpartyAlias: "Dex Router (OKX)",
                    AssetSymbol: "USDT",
                    AssetName: "Tether USD",
                    TokenContractAddress: "0x55d398326f99059fF775485246999027B3197955",
                    Amount: 2758m,
                    UsdValue: 2754m)
            ]);

        var rendered = renderer.Render(payload);

        Assert.Contains("Received: 62877 [ESPORTS](https://bscscan.com/token/0x2222222222222222222222222222222222222222) \\(\\~$2876\\) From: [0x1111\\.\\.\\.1111](https://bscscan.com/address/0x1111111111111111111111111111111111111111)", rendered.Text);
        Assert.Contains("Sent: 2758 [USDT](https://bscscan.com/token/0x55d398326f99059fF775485246999027B3197955) \\(\\~$2754\\) To: [Dex Router \\(OKX\\)](https://bscscan.com/address/0x3333333333333333333333333333333333333333)", rendered.Text);
        Assert.Contains("[Tx hash](https://bscscan.com/tx/0xswap)", rendered.Text);
    }

    [Fact]
    public void Render_UsesAddressLabelsForTrackedWalletFallbackAndCounterparties()
    {
        using var cache = new InMemoryAddressLabelCacheService();
        cache.ReplaceAll(
        [
            CreateLabel("0x52908400098527886E0F7030069857D2E4169EE7", null, "vitalik.eth"),
            CreateLabel("0x1111111111111111111111111111111111111111", SupportedChain.Bsc, "MEXC")
        ]);

        var renderer = CreateRenderer(cache);
        var payload = new NotificationPayload(
            TelegramUserId: 7777,
            Chain: SupportedChain.Bsc,
            Operation: NotificationOperation.Received,
            TrackedWalletAddress: "0x52908400098527886E0F7030069857D2E4169EE7",
            TrackedWalletAlias: null,
            CounterpartyAddress: "0x1111111111111111111111111111111111111111",
            CounterpartyAlias: "raw alias",
            TransactionHash: "0xtxhash",
            AssetSymbol: "BNB",
            AssetName: "BNB",
            TokenContractAddress: null,
            Amount: 1.25m,
            UsdValue: 750m,
            BlockNumber: 100,
            BlockTimestampUtc: DateTimeOffset.UtcNow);

        var rendered = renderer.Render(payload);

        Assert.Contains("[vitalik\\.eth](https://bscscan.com/address/0x52908400098527886E0F7030069857D2E4169EE7) \\| BNB", rendered.Text);
        Assert.Contains("From: [MEXC](https://bscscan.com/address/0x1111111111111111111111111111111111111111)", rendered.Text);
        Assert.DoesNotContain("raw alias", rendered.Text);
    }

    private static TelegramNotificationRenderer CreateRenderer(InMemoryAddressLabelCacheService? cache = null)
    {
        cache ??= new InMemoryAddressLabelCacheService();
        return new TelegramNotificationRenderer(new StaticChainMetadataProvider(), new AddressLabelResolver(cache));
    }

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
}
