using Presentation.Telegram.Commands;
using System.Globalization;
using System.Net;
using Application.Abstractions;
using Application.TrackedWallets;
using Domain.Common;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Presentation.Telegram.Services;

/// <summary>
/// Builds Telegram HTML menus for editing tracked-wallet settings.
/// </summary>
public sealed class TrackedWalletMenuRenderer : ITrackedWalletMenuRenderer
{
    /// <summary>
    /// Builds the wallet overview menu with actions for thresholds, networks, rename, and deletion.
    /// </summary>
    public TelegramMenuMessage RenderOverview(
        TrackedWalletDetails wallet,
        IReadOnlyList<ChainMetadata> configuredChains)
    {
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(configuredChains);

        var displayName = string.IsNullOrWhiteSpace(wallet.Alias) ? TrackedWalletMenuRendererText.Main : wallet.Alias;
        var text =
            TrackedWalletMenuRendererText.Overview(HtmlEncode(displayName), HtmlEncode(FormatEnabledChains(wallet, configuredChains)), HtmlEncode(wallet.Address), wallet.MinimumUsdValue.ToString("0.##", CultureInfo.InvariantCulture));

        var walletId = wallet.Id.ToString("N");
        var keyboard = new InlineKeyboardMarkup(
        [
            [
                InlineKeyboardButton.WithCallbackData(TrackedWalletMenuRendererText.MinUSD, $"{TelegramInteractionTokens.Callbacks.Wallet}:{TelegramInteractionTokens.Callbacks.Version}:{TelegramInteractionTokens.Callbacks.Minimum}:{walletId}"),
                InlineKeyboardButton.WithCallbackData(TrackedWalletMenuRendererText.Networks, $"{TelegramInteractionTokens.Callbacks.Wallet}:{TelegramInteractionTokens.Callbacks.Version}:{TelegramInteractionTokens.Callbacks.Networks}:{walletId}")
            ],
            [InlineKeyboardButton.WithCallbackData(TrackedWalletMenuRendererText.Hyperliquid, $"{TelegramInteractionTokens.Callbacks.Wallet}:{TelegramInteractionTokens.Callbacks.Version}:{TelegramInteractionTokens.Callbacks.Hyperliquid}:{walletId}"), InlineKeyboardButton.WithCallbackData(TrackedWalletMenuRendererText.Polymarket, $"{TelegramInteractionTokens.Callbacks.Wallet}:{TelegramInteractionTokens.Callbacks.Version}:{TelegramInteractionTokens.Callbacks.Predictions}:{walletId}")],
            [InlineKeyboardButton.WithCallbackData(TrackedWalletMenuRendererText.Rename, $"{TelegramInteractionTokens.Callbacks.Wallet}:{TelegramInteractionTokens.Callbacks.Version}:{TelegramInteractionTokens.Callbacks.Rename}:{walletId}")],
            [InlineKeyboardButton.WithCallbackData(TrackedWalletMenuRendererText.Delete, $"{TelegramInteractionTokens.Callbacks.Wallet}:{TelegramInteractionTokens.Callbacks.Version}:{TelegramInteractionTokens.Callbacks.Delete}:{walletId}")],
            [InlineKeyboardButton.WithCallbackData(TrackedWalletMenuRendererText.Back, $"{TelegramInteractionTokens.Callbacks.Wallet}:{TelegramInteractionTokens.Callbacks.Version}:{TelegramInteractionTokens.Callbacks.Close}:{walletId}")]
        ]);

        return new TelegramMenuMessage(text, ParseMode.Html, keyboard);
    }

    /// <summary>
    /// Builds a checklist of currently configured runtime networks for the wallet.
    /// </summary>
    public TelegramMenuMessage RenderNetworks(
        TrackedWalletDetails wallet,
        IReadOnlyList<ChainMetadata> configuredChains)
    {
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(configuredChains);

        var effectiveEnabledChains = GetEffectiveEnabledChains(wallet, configuredChains);
        var walletId = wallet.Id.ToString("N");
        var rows = configuredChains
            .Chunk(2)
            .Select(chunk => chunk
                .Select(chain =>
                {
                    var isEnabled = effectiveEnabledChains.Contains(chain.Chain);
                    var nextEnabled = isEnabled ? TelegramInteractionTokens.Callbacks.Disabled : TelegramInteractionTokens.Callbacks.Enabled;
                    var label = $"{(isEnabled ? "✅" : "⬜")} {FormatChainShortName(chain)}";
                    return InlineKeyboardButton.WithCallbackData(
                        label,
                        $"{TelegramInteractionTokens.Callbacks.Wallet}:{TelegramInteractionTokens.Callbacks.Version}:{TelegramInteractionTokens.Callbacks.NetworkToggle}:{walletId}:{(int)chain.Chain}:{nextEnabled}");
                })
                .ToArray())
            .ToList();

        rows.Add([InlineKeyboardButton.WithCallbackData(TrackedWalletMenuRendererText.DeselectAllNetworks, $"{TelegramInteractionTokens.Callbacks.Wallet}:{TelegramInteractionTokens.Callbacks.Version}:{TelegramInteractionTokens.Callbacks.DeselectNetworks}:{walletId}")]);
        rows.Add(
        [
            InlineKeyboardButton.WithCallbackData(TrackedWalletMenuRendererText.Cancel, $"{TelegramInteractionTokens.Callbacks.Wallet}:{TelegramInteractionTokens.Callbacks.Version}:{TelegramInteractionTokens.Callbacks.Overview}:{walletId}"),
            InlineKeyboardButton.WithCallbackData(TrackedWalletMenuRendererText.Done, $"{TelegramInteractionTokens.Callbacks.Wallet}:{TelegramInteractionTokens.Callbacks.Version}:{TelegramInteractionTokens.Callbacks.Overview}:{walletId}")
        ]);

        return new TelegramMenuMessage(
            TrackedWalletMenuRendererText.NetworksPrompt,
            ParseMode.Html,
            new InlineKeyboardMarkup(rows));
    }

    /// <summary>
    /// Builds the destructive-action confirmation menu for wallet deletion.
    /// </summary>
    public TelegramMenuMessage RenderDeleteConfirmation(TrackedWalletDetails wallet)
    {
        ArgumentNullException.ThrowIfNull(wallet);

        var displayName = string.IsNullOrWhiteSpace(wallet.Alias) ? TrackedWalletMenuRendererText.ThisWallet : wallet.Alias;
        var text =
            TrackedWalletMenuRendererText.DeleteConfirmation(HtmlEncode(displayName), HtmlEncode(ShortAddress(wallet.Address)));

        var walletId = wallet.Id.ToString("N");
        return new TelegramMenuMessage(
            text,
            ParseMode.Html,
            new InlineKeyboardMarkup(
            [
                [
                    InlineKeyboardButton.WithCallbackData(TrackedWalletMenuRendererText.Delete, $"{TelegramInteractionTokens.Callbacks.Wallet}:{TelegramInteractionTokens.Callbacks.Version}:{TelegramInteractionTokens.Callbacks.DeleteConfirm}:{walletId}"),
                    InlineKeyboardButton.WithCallbackData(TrackedWalletMenuRendererText.Cancel, $"{TelegramInteractionTokens.Callbacks.Wallet}:{TelegramInteractionTokens.Callbacks.Version}:{TelegramInteractionTokens.Callbacks.Overview}:{walletId}")
                ]
            ]));
    }

    private static HashSet<SupportedChain> GetEffectiveEnabledChains(
        TrackedWalletDetails wallet,
        IReadOnlyList<ChainMetadata> configuredChains)
    {
        if (wallet.EnabledChains is null || wallet.EnabledChains.Count == 0)
        {
            return configuredChains.Select(chain => chain.Chain).ToHashSet();
        }

        return wallet.EnabledChains.ToHashSet();
    }

    private static string FormatEnabledChains(
        TrackedWalletDetails wallet,
        IReadOnlyList<ChainMetadata> configuredChains)
    {
        if (configuredChains.Count == 0)
        {
            return TrackedWalletMenuRendererText.NoRuntimeNetworksConfigured;
        }

        var enabledChains = GetEffectiveEnabledChains(wallet, configuredChains);
        return string.Join(
            " · ",
            configuredChains
                .Where(chain => enabledChains.Contains(chain.Chain))
                .Select(FormatChainShortName));
    }

    private static string FormatChainShortName(ChainMetadata metadata)
        => metadata.Chain switch
        {
            SupportedChain.Arbitrum => TrackedWalletMenuRendererText.ArbitrumShortName,
            SupportedChain.Bsc => TrackedWalletMenuRendererText.BnbShortName,
            SupportedChain.Ethereum => TrackedWalletMenuRendererText.EthereumShortName,
            _ => metadata.ShortName
        };

    private static string ShortAddress(string address)
        => address.Length <= 12 ? address : $"{address[..6]}...{address[^4..]}";

    private static string HtmlEncode(string value)
        => WebUtility.HtmlEncode(value);
}
