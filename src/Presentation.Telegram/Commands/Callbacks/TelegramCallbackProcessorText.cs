using Presentation.Telegram.Commands;
namespace Presentation.Telegram.Commands.Callbacks;

/// <summary>Named presentation text and formatting templates for TelegramCallbackProcessor.</summary>
internal static class TelegramCallbackProcessorText
{
    public const string PrivateChatRequired = "Please open this bot in a private chat to manage tracked wallets.";
    public const string WalletNotFound = "Wallet not found.";
    public const string LastNetworkRequired = "At least one network must remain enabled.";
    public const string MinimumUsdPrompt = "Send the minimum transaction USD value.\nA positive minimum skips transfers without a USD price. Use 0 to notify on every matched transaction, including unpriced transfers.";
    public const string PolymarketMenuStale = "This setting changed. Refresh the Polymarket menu.";
    public const string VerifiedAccountMissing = "No verified trading account is available.";
    public const string PolymarketMinimumPrompt = "Send the Polymarket minimum gross trade value in collateral units (pUSD / USDC.e). Default: 0. Redemptions bypass this minimum.";
    public const string PolymarketSourcePrompt = "Send the profile's trading-wallet address. It must be verified by the public profile API. This creates a new binding and leaves tracking off.";
    public const string PublicNameMissing = "No public name is available.";
    public const string ConfirmAccountRebind = "Confirm account / Rebind";
    public const string UsePublicName = "Use public name";
    public static string PolymarketDetails(string alias, string profileName, string originalAddress, string sourceAddress, string proposedAddress, string minimum, string status) => $"<b>Polymarket</b>\nYour alias: {alias}\nPolymarket profile: {profileName}\nEntered: <code>{originalAddress}</code>\nBound account: <code>{sourceAddress}</code>\nProposed account: <code>{proposedAddress}</code>\nPolymarket follows the confirmed trading account above.\nTrade minimum: {minimum} collateral units (gross)\n{status}";
    public const string HyperliquidIsUnavailable = "Hyperliquid is unavailable.";
    public static string HyperliquidDetails(string status) => $"<b>Hyperliquid</b>\n{status}\nNative USDC perp trades, liquidations, deposits and withdrawals.\nHistory may be delayed or incomplete during recovery.";
    public const string NetworkNotConfigured = "That network is not configured for tracking.";
    public const string TrackedWalletDeleted = "Tracked wallet deleted.";
    public const string WaitingForYourReply = "Waiting for your reply.";
}
