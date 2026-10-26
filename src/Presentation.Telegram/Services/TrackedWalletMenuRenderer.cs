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

        var displayName = string.IsNullOrWhiteSpace(wallet.Alias) ? "Main" : wallet.Alias;
        var text =
            $"""
            <b>{HtmlEncode(displayName)}</b>
            {HtmlEncode(FormatEnabledChains(wallet, configuredChains))}
            <code>{HtmlEncode(wallet.Address)}</code>
            Minimum USD: <code>${wallet.MinimumUsdValue.ToString("0.##", CultureInfo.InvariantCulture)}</code>
            """;

        var walletId = wallet.Id.ToString("N");
        var keyboard = new InlineKeyboardMarkup(
        [
            [
                InlineKeyboardButton.WithCallbackData("💵 Min USD", $"tw:v1:m:{walletId}"),
                InlineKeyboardButton.WithCallbackData("🛰 Networks", $"tw:v1:n:{walletId}")
            ],
            [InlineKeyboardButton.WithCallbackData("Hyperliquid", $"tw:v1:h:{walletId}"), InlineKeyboardButton.WithCallbackData("Polymarket", $"tw:v1:p:{walletId}")],
            [InlineKeyboardButton.WithCallbackData("✏️ Rename", $"tw:v1:r:{walletId}")],
            [InlineKeyboardButton.WithCallbackData("🗑 Delete", $"tw:v1:d:{walletId}")],
            [InlineKeyboardButton.WithCallbackData("↩️ Back", $"tw:v1:b:{walletId}")]
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
                    var nextEnabled = isEnabled ? "0" : "1";
                    var label = $"{(isEnabled ? "✅" : "⬜")} {FormatChainShortName(chain)}";
                    return InlineKeyboardButton.WithCallbackData(
                        label,
                        $"tw:v1:nt:{walletId}:{(int)chain.Chain}:{nextEnabled}");
                })
                .ToArray())
            .ToList();

        rows.Add([InlineKeyboardButton.WithCallbackData("Deselect All Networks", $"tw:v1:na:{walletId}")]);
        rows.Add(
        [
            InlineKeyboardButton.WithCallbackData("❌ Cancel", $"tw:v1:o:{walletId}"),
            InlineKeyboardButton.WithCallbackData("☑️ Done", $"tw:v1:o:{walletId}")
        ]);

        return new TelegramMenuMessage(
            "Choose ✅ Networks to add and press ☑️ Done:",
            ParseMode.Html,
            new InlineKeyboardMarkup(rows));
    }

    /// <summary>
    /// Builds the destructive-action confirmation menu for wallet deletion.
    /// </summary>
    public TelegramMenuMessage RenderDeleteConfirmation(TrackedWalletDetails wallet)
    {
        ArgumentNullException.ThrowIfNull(wallet);

        var displayName = string.IsNullOrWhiteSpace(wallet.Alias) ? "this wallet" : wallet.Alias;
        var text =
            $"""
            Delete {HtmlEncode(displayName)}?
            <code>{HtmlEncode(ShortAddress(wallet.Address))}</code>
            """;

        var walletId = wallet.Id.ToString("N");
        return new TelegramMenuMessage(
            text,
            ParseMode.Html,
            new InlineKeyboardMarkup(
            [
                [
                    InlineKeyboardButton.WithCallbackData("🗑 Delete", $"tw:v1:dc:{walletId}"),
                    InlineKeyboardButton.WithCallbackData("❌ Cancel", $"tw:v1:o:{walletId}")
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
            return "No runtime networks configured";
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
            SupportedChain.Arbitrum => "ARB",
            SupportedChain.Bsc => "BNB",
            SupportedChain.Ethereum => "ETH",
            _ => metadata.ShortName
        };

    private static string ShortAddress(string address)
        => address.Length <= 12 ? address : $"{address[..6]}...{address[^4..]}";

    private static string HtmlEncode(string value)
        => WebUtility.HtmlEncode(value);
}
