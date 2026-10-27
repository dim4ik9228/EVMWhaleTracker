using Presentation.Telegram.Commands;
using Domain.Common;

namespace Presentation.Telegram.Commands.Callbacks;

/// <summary>
/// Parses compact tracked-wallet callback data emitted by inline Telegram settings menus.
/// </summary>
public static class TelegramCallbackDataParser
{
    /// <summary>
    /// Attempts to decode a versioned callback payload into a strongly typed menu action.
    /// </summary>
    public static bool TryParse(string? data, out TelegramCallbackData callbackData)
    {
        callbackData = default;

        if (string.IsNullOrWhiteSpace(data))
        {
            return false;
        }

        var parts = data.Split(':');
        if (parts.Length == 4 && parts[0] == TelegramInteractionTokens.Callbacks.Polymarket && parts[1].Length == 1 && Guid.TryParseExact(parts[2], "N", out var pmWallet) && parts[3].Length == 22)
        {
            var action = parts[1] switch { TelegramInteractionTokens.Callbacks.Toggle => TelegramCallbackAction.PolymarketToggle, TelegramInteractionTokens.Callbacks.Bind => TelegramCallbackAction.PolymarketBind,
                TelegramInteractionTokens.Callbacks.Minimum => TelegramCallbackAction.PolymarketMinimum, TelegramInteractionTokens.Callbacks.Alias => TelegramCallbackAction.PolymarketAlias,
                TelegramInteractionTokens.Callbacks.Source => TelegramCallbackAction.PolymarketSource, _ => (TelegramCallbackAction?)null };
            if (action is null) return false;
            try
            {
                var bytes = Convert.FromBase64String(parts[3].Replace('-', '+').Replace('_', '/') + "==");
                if (bytes.Length != 16) return false;
                callbackData = new TelegramCallbackData(action.Value, pmWallet, Generation: new Guid(bytes));
                return true;
            }
            catch (FormatException) { return false; }
        }
        if (parts.Length == 4 && parts[0] == TelegramInteractionTokens.Callbacks.HyperliquidToggle && Guid.TryParseExact(parts[1], "N", out var coreWallet)
            && TryParseToggle(parts[2], out var coreToggle) && parts[3].Length == 22)
        {
            try
            {
                var bytes = Convert.FromBase64String(parts[3].Replace('-', '+').Replace('_', '/') + "==");
                if (bytes.Length != 16) return false;
                var generation = new Guid(bytes);
                callbackData = new TelegramCallbackData(TelegramCallbackAction.HyperliquidToggle, coreWallet, Enabled: coreToggle, Generation: generation);
                return true;
            }
            catch (FormatException) { return false; }
        }
        if (parts.Length < 4
            || !string.Equals(parts[0], TelegramInteractionTokens.Callbacks.Wallet, StringComparison.Ordinal)
            || !string.Equals(parts[1], TelegramInteractionTokens.Callbacks.Version, StringComparison.Ordinal))
        {
            return false;
        }

        var actionToken = parts[2];
        if (!Guid.TryParseExact(parts[3], "N", out var walletId))
        {
            return false;
        }

        switch (actionToken)
        {
            case TelegramInteractionTokens.Callbacks.Predictions when parts.Length == 4:
                callbackData = new TelegramCallbackData(TelegramCallbackAction.Polymarket, walletId);
                return true;
            case TelegramInteractionTokens.Callbacks.Hyperliquid when parts.Length == 4:
                callbackData = new TelegramCallbackData(TelegramCallbackAction.Hyperliquid, walletId);
                return true;
            case TelegramInteractionTokens.Callbacks.Overview when parts.Length == 4:
                callbackData = new TelegramCallbackData(TelegramCallbackAction.Overview, walletId);
                return true;
            case TelegramInteractionTokens.Callbacks.Networks when parts.Length == 4:
                callbackData = new TelegramCallbackData(TelegramCallbackAction.Networks, walletId);
                return true;
            case TelegramInteractionTokens.Callbacks.Rename when parts.Length == 4:
                callbackData = new TelegramCallbackData(TelegramCallbackAction.Rename, walletId);
                return true;
            case TelegramInteractionTokens.Callbacks.Minimum when parts.Length == 4:
                callbackData = new TelegramCallbackData(TelegramCallbackAction.MinimumUsd, walletId);
                return true;
            case TelegramInteractionTokens.Callbacks.Delete when parts.Length == 4:
                callbackData = new TelegramCallbackData(TelegramCallbackAction.DeletePrompt, walletId);
                return true;
            case TelegramInteractionTokens.Callbacks.DeleteConfirm when parts.Length == 4:
                callbackData = new TelegramCallbackData(TelegramCallbackAction.DeleteConfirm, walletId);
                return true;
            case TelegramInteractionTokens.Callbacks.Close when parts.Length == 4:
                callbackData = new TelegramCallbackData(TelegramCallbackAction.Close, walletId);
                return true;
            case TelegramInteractionTokens.Callbacks.DeselectNetworks when parts.Length == 4:
                callbackData = new TelegramCallbackData(TelegramCallbackAction.DeselectAllNetworks, walletId);
                return true;
            case TelegramInteractionTokens.Callbacks.NetworkToggle when parts.Length == 6
                && int.TryParse(parts[4], out var chainId)
                && Enum.IsDefined(typeof(SupportedChain), chainId)
                && chainId != (int)SupportedChain.Arc
                && TryParseToggle(parts[5], out var enabled):
                callbackData = new TelegramCallbackData(
                    TelegramCallbackAction.NetworkToggle,
                    walletId,
                    (SupportedChain)chainId,
                    enabled);
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseToggle(string value, out bool enabled)
    {
        enabled = false;
        if (value == TelegramInteractionTokens.Callbacks.Enabled)
        {
            enabled = true;
            return true;
        }

        return value == TelegramInteractionTokens.Callbacks.Disabled;
    }
}

/// <summary>
/// Decoded tracked-wallet callback data, including optional chain toggle details.
/// </summary>
public readonly record struct TelegramCallbackData(
    TelegramCallbackAction Action,
    Guid WalletId,
    SupportedChain? Chain = null,
    bool? Enabled = null,
    Guid? Generation = null);

/// <summary>
/// Supported actions in the tracked-wallet inline settings menu callback protocol.
/// </summary>
public enum TelegramCallbackAction
{
    /// <summary>Independent Polymarket details.</summary>
    Polymarket,
    /// <summary>Explicit source preference.</summary>
    PolymarketToggle,
    /// <summary>Confirm the currently displayed mapped account.</summary>
    PolymarketBind,
    /// <summary>Independent minimum prompt.</summary>
    PolymarketMinimum,
    /// <summary>Use current public profile name.</summary>
    PolymarketAlias,
    /// <summary>Supply a verified trading address.</summary>
    PolymarketSource,
    /// <summary>Separate HyperCore status menu.</summary>
    Hyperliquid,
    /// <summary>Explicit idempotent HyperCore preference.</summary>
    HyperliquidToggle,
    /// <summary>
    /// Render the tracked-wallet overview menu.
    /// </summary>
    Overview,

    /// <summary>
    /// Render the configured-network checklist.
    /// </summary>
    Networks,

    /// <summary>
    /// Enable or disable a single chain.
    /// </summary>
    NetworkToggle,

    /// <summary>
    /// Reject a request that would disable every configured network.
    /// </summary>
    DeselectAllNetworks,

    /// <summary>
    /// Prompt the user for a replacement wallet alias.
    /// </summary>
    Rename,

    /// <summary>
    /// Prompt the user for a replacement minimum USD threshold.
    /// </summary>
    MinimumUsd,

    /// <summary>
    /// Render delete confirmation.
    /// </summary>
    DeletePrompt,

    /// <summary>
    /// Delete the tracked wallet after confirmation.
    /// </summary>
    DeleteConfirm,

    /// <summary>
    /// Close the inline settings menu message.
    /// </summary>
    Close
}
