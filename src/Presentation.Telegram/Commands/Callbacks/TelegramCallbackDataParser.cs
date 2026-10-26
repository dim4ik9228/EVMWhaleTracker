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
        if (parts.Length < 4
            || !string.Equals(parts[0], "tw", StringComparison.Ordinal)
            || !string.Equals(parts[1], "v1", StringComparison.Ordinal))
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
            case "o" when parts.Length == 4:
                callbackData = new TelegramCallbackData(TelegramCallbackAction.Overview, walletId);
                return true;
            case "n" when parts.Length == 4:
                callbackData = new TelegramCallbackData(TelegramCallbackAction.Networks, walletId);
                return true;
            case "r" when parts.Length == 4:
                callbackData = new TelegramCallbackData(TelegramCallbackAction.Rename, walletId);
                return true;
            case "m" when parts.Length == 4:
                callbackData = new TelegramCallbackData(TelegramCallbackAction.MinimumUsd, walletId);
                return true;
            case "d" when parts.Length == 4:
                callbackData = new TelegramCallbackData(TelegramCallbackAction.DeletePrompt, walletId);
                return true;
            case "dc" when parts.Length == 4:
                callbackData = new TelegramCallbackData(TelegramCallbackAction.DeleteConfirm, walletId);
                return true;
            case "b" when parts.Length == 4:
                callbackData = new TelegramCallbackData(TelegramCallbackAction.Close, walletId);
                return true;
            case "na" when parts.Length == 4:
                callbackData = new TelegramCallbackData(TelegramCallbackAction.DeselectAllNetworks, walletId);
                return true;
            case "nt" when parts.Length == 6
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
        if (value == "1")
        {
            enabled = true;
            return true;
        }

        return value == "0";
    }
}

/// <summary>
/// Decoded tracked-wallet callback data, including optional chain toggle details.
/// </summary>
public readonly record struct TelegramCallbackData(
    TelegramCallbackAction Action,
    Guid WalletId,
    SupportedChain? Chain = null,
    bool? Enabled = null);

/// <summary>
/// Supported actions in the tracked-wallet inline settings menu callback protocol.
/// </summary>
public enum TelegramCallbackAction
{
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
