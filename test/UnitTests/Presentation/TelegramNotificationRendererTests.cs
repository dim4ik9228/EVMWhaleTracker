using Application.Messaging;
using Domain.Common;
using Infrastructure.Blockchain;
using Presentation.Telegram.Services;
using Telegram.Bot.Types.Enums;

namespace UnitTests.Presentation;

public sealed class TelegramNotificationRendererTests
{
    [Fact]
    public void Render_EscapesMarkdownV2_AndUsesCompactLinkedLayout()
    {
        var renderer = CreateRenderer();
        var notification = new EnrichedTelegramNotification(
            TelegramUserId: 7777,
            TrackedWalletId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Chain: SupportedChain.Bsc,
            TrackedWalletAddress: "0x52908400098527886E0F7030069857D2E4169EE7",
            TrackedWalletDisplayText: "debabki.eth",
            TransactionHash: "0xtxhash",
            BlockNumber: 100,
            BlockTimestampUtc: DateTimeOffset.UtcNow,
            Lines:
            [
                new EnrichedTelegramTransferLine(
                    Operation: NotificationOperation.Received,
                    CounterpartyAddress: "0x1111111111111111111111111111111111111111",
                    CounterpartyDisplayText: "from_wallet[1]",
                    AssetSymbol: "USDT",
                    AssetName: "Tether USD",
                    TokenContractAddress: "0x55d398326f99059fF775485246999027B3197955",
                    Amount: 252.39m,
                    UsdValue: 252m)
            ]);

        var rendered = renderer.Render(notification, "EvmWhaleTrackerBot");

        Assert.Equal(ParseMode.MarkdownV2, rendered.ParseMode);
        Assert.Contains("[debabki\\.eth](https://bscscan.com/address/0x52908400098527886E0F7030069857D2E4169EE7) \\| BNB", rendered.Text);
        Assert.Contains("[✏️](https://t.me/EvmWhaleTrackerBot?start=edit_wallet_11111111111111111111111111111111)", rendered.Text);
        Assert.Contains("Received: 252\\.39 [USDT](https://bscscan.com/token/0x55d398326f99059fF775485246999027B3197955) \\(\\~$252\\) From: [from\\_wallet\\[1\\]](https://bscscan.com/address/0x1111111111111111111111111111111111111111)", rendered.Text);
        Assert.Contains("[Tx hash](https://bscscan.com/tx/0xtxhash)", rendered.Text);
    }

    [Fact]
    public void Render_KeepsNativeAssetSymbolUnlinked()
    {
        var renderer = CreateRenderer();
        var notification = CreateSingleLineNotification(
            assetSymbol: "BNB",
            tokenContractAddress: null,
            counterpartyDisplayText: "0x1111...1111");

        var rendered = renderer.Render(notification);

        Assert.Contains("1\\.25 BNB", rendered.Text);
        Assert.DoesNotContain("/token/", rendered.Text);
    }

    [Fact]
    public void Render_RendersMultipleTransferLinesForAggregatedNotification()
    {
        var renderer = CreateRenderer();
        var notification = new EnrichedTelegramNotification(
            TelegramUserId: 7777,
            TrackedWalletId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Chain: SupportedChain.Bsc,
            TrackedWalletAddress: "0x52908400098527886E0F7030069857D2E4169EE7",
            TrackedWalletDisplayText: "debabki.eth",
            TransactionHash: "0xswap",
            BlockNumber: 100,
            BlockTimestampUtc: DateTimeOffset.UtcNow,
            Lines:
            [
                new EnrichedTelegramTransferLine(
                    Operation: NotificationOperation.Received,
                    CounterpartyAddress: "0x1111111111111111111111111111111111111111",
                    CounterpartyDisplayText: "0x1111...1111",
                    AssetSymbol: "ESPORTS",
                    AssetName: "Esports",
                    TokenContractAddress: "0x2222222222222222222222222222222222222222",
                    Amount: 62877m,
                    UsdValue: 2876m),
                new EnrichedTelegramTransferLine(
                    Operation: NotificationOperation.Sent,
                    CounterpartyAddress: "0x3333333333333333333333333333333333333333",
                    CounterpartyDisplayText: "Dex Router (OKX)",
                    AssetSymbol: "USDT",
                    AssetName: "Tether USD",
                    TokenContractAddress: "0x55d398326f99059fF775485246999027B3197955",
                    Amount: 2758m,
                    UsdValue: 2754m)
            ]);

        var rendered = renderer.Render(notification);

        Assert.Contains("Received: 62877 [ESPORTS](https://bscscan.com/token/0x2222222222222222222222222222222222222222) \\(\\~$2876\\) From: [0x1111\\.\\.\\.1111](https://bscscan.com/address/0x1111111111111111111111111111111111111111)", rendered.Text);
        Assert.Contains("Sent: 2758 [USDT](https://bscscan.com/token/0x55d398326f99059fF775485246999027B3197955) \\(\\~$2754\\) To: [Dex Router \\(OKX\\)](https://bscscan.com/address/0x3333333333333333333333333333333333333333)", rendered.Text);
        Assert.Contains("[Tx hash](https://bscscan.com/tx/0xswap)", rendered.Text);
    }

    [Fact]
    public void Render_UsesEnrichedTrackedWalletAndCounterpartyText()
    {
        var renderer = CreateRenderer();
        var notification = CreateSingleLineNotification(
            trackedWalletDisplayText: "vitalik.eth",
            counterpartyDisplayText: "MEXC");

        var rendered = renderer.Render(notification);

        Assert.Contains("[vitalik\\.eth](https://bscscan.com/address/0x52908400098527886E0F7030069857D2E4169EE7) \\| BNB", rendered.Text);
        Assert.Contains("From: [MEXC](https://bscscan.com/address/0x1111111111111111111111111111111111111111)", rendered.Text);
    }

    private static TelegramNotificationRenderer CreateRenderer()
        => new(new StaticChainMetadataProvider());

    private static EnrichedTelegramNotification CreateSingleLineNotification(
        string trackedWalletDisplayText = "debabki.eth",
        string counterpartyDisplayText = "from_wallet",
        string assetSymbol = "BNB",
        string? tokenContractAddress = null)
        => new(
            TelegramUserId: 7777,
            TrackedWalletId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Chain: SupportedChain.Bsc,
            TrackedWalletAddress: "0x52908400098527886E0F7030069857D2E4169EE7",
            TrackedWalletDisplayText: trackedWalletDisplayText,
            TransactionHash: "0xtxhash",
            BlockNumber: 100,
            BlockTimestampUtc: DateTimeOffset.UtcNow,
            Lines:
            [
                new EnrichedTelegramTransferLine(
                    Operation: NotificationOperation.Received,
                    CounterpartyAddress: "0x1111111111111111111111111111111111111111",
                    CounterpartyDisplayText: counterpartyDisplayText,
                    AssetSymbol: assetSymbol,
                    AssetName: assetSymbol,
                    TokenContractAddress: tokenContractAddress,
                    Amount: 1.25m,
                    UsdValue: 750m)
            ]);
}
