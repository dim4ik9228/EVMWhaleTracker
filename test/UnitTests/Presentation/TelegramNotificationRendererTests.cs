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
        var renderer = new TelegramNotificationRenderer(new StaticChainMetadataProvider());
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
        var renderer = new TelegramNotificationRenderer(new StaticChainMetadataProvider());
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
}
