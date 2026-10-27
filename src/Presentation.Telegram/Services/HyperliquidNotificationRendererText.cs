using Application.Hyperliquid;
namespace Presentation.Telegram.Services;

/// <summary>Named presentation text and formatting templates for HyperliquidNotificationRenderer.</summary>
internal static class HyperliquidNotificationRendererText
{
    public const string IncreasedLong = "🟢 Increased Long";
    public const string IncreasedShort = "🔴 Increased Short";
    public const string FlippedTo = "🔄 Flipped to ";
    public static string PerpetualFill(string action, string size, string market, string notional, string price, string priceMarket) => $"{action} {size} {market}{notional} via Hyperliquid PERP · {price} per {priceMarket}";
    public static string LedgerTransfer(string action, string amount) => $"{action} · {amount} USDC · Hyperliquid ledger";
    public const string LiquidationHyperliquidLedger = "⚠️ Liquidation · Hyperliquid ledger";
    public const string TxHash = "Tx hash";
    public const string TransactionLinksPrefix = "Tx hash: ";
    public static string AdditionalTransactions(int count) => $"Wallet activity ({count} more transactions)";
    public const string WalletActivity = "Wallet activity";
    public const string ReportedAccountValue = "Reported account value: ";
    public const string LiquidatedPosition = "Liquidated position: ";
    public static string AdditionalPositions(int count) => $"…and {count} more positions";
    public static string HtmlLink(string url, string text) => $"<a href=\"{url}\">{text}</a>";

    public const string Deposit = "📥 Deposit";
    public const string Withdrawal = "📤 Withdrawal";

    public static string Header(string walletLink) => $"{walletLink} | HL";
    public static string RealizedPnl(string pnl) => $"Realized P&amp;L: {pnl}";

    public const string Longed = "🟢 Longed";
    public const string Shorted = "🔴 Shorted";
    public const string ReducedPrefix = "↘️ Reduced ";
    public const string ClosedPrefix = "❎ Close ";
    public const string NotionalPrefix = " for ~";
    public const string EditIcon = "✏️";
}
