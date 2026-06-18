using Application.Abstractions;
using Application.TrackedWallets;
using Domain.Common;
using Presentation.Telegram.Services;

namespace UnitTests.Presentation;

public sealed class TrackedWalletMenuRendererTests
{
    [Fact]
    public void RenderNetworks_TreatsEmptyEnabledChainsAsAllConfiguredNetworks()
    {
        var wallet = CreateWallet(enabledChains: []);
        var renderer = new TrackedWalletMenuRenderer();

        var menu = renderer.RenderNetworks(wallet, ConfiguredChains());

        var buttonTexts = menu.ReplyMarkup.InlineKeyboard
            .SelectMany(row => row)
            .Select(button => button.Text)
            .ToArray();

        Assert.Contains("✅ ETH", buttonTexts);
        Assert.Contains("✅ BNB", buttonTexts);
        Assert.Contains("✅ Base", buttonTexts);
        Assert.Contains("✅ ARB", buttonTexts);
    }

    [Fact]
    public void RenderOverview_UsesCompactWalletCallbackData()
    {
        var walletId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var wallet = CreateWallet(walletId);
        var renderer = new TrackedWalletMenuRenderer();

        var menu = renderer.RenderOverview(wallet, ConfiguredChains());

        var callbackData = menu.ReplyMarkup.InlineKeyboard
            .SelectMany(row => row)
            .Select(button => button.CallbackData)
            .Where(data => data is not null)
            .ToArray();

        Assert.Contains($"tw:v1:n:{walletId:N}", callbackData);
        Assert.All(callbackData, data => Assert.True(data!.Length <= 64));
    }

    private static TrackedWalletDetails CreateWallet(
        Guid? walletId = null,
        IReadOnlyCollection<SupportedChain>? enabledChains = null)
        => new(
            walletId ?? Guid.NewGuid(),
            "0x52908400098527886E0F7030069857D2E4169EE7",
            7777,
            0m,
            true,
            true,
            true,
            DateTimeOffset.UtcNow,
            null,
            "debabki.eth",
            enabledChains ?? []);

    private static IReadOnlyList<ChainMetadata> ConfiguredChains()
        =>
        [
            new(SupportedChain.Ethereum, "Ethereum", "ETH", "ETH", 18, "https://etherscan.io"),
            new(SupportedChain.Bsc, "BNB Chain", "BNB", "BNB", 18, "https://bscscan.com"),
            new(SupportedChain.Base, "Base", "Base", "ETH", 18, "https://basescan.org"),
            new(SupportedChain.Arbitrum, "Arbitrum", "Arbitrum", "ETH", 18, "https://arbiscan.io")
        ];
}
