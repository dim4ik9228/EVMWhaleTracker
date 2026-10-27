using Presentation.Telegram.Commands;
namespace Presentation.Telegram.Services;

/// <summary>Named presentation text and formatting templates for PolymarketNotificationRenderer.</summary>
internal static class PolymarketNotificationRendererText
{
    public static string ProfileLink(string address) => $"<a href=\"https://polymarket.com/profile/{address}\">Polymarket</a>";
    public static string Header(string alias, string account) => $"<b>{alias}</b> | {account}";
    public static string EditWalletLink(string username, Guid walletId) => $" | <a href=\"https://t.me/{username}?start={TelegramInteractionTokens.EditWalletPrefix}{walletId:N}\">✏️</a>";
    public const string SettlementCorrection = "⚠️ Settlement correction";
    public const string ValueUnavailable = "Value unavailable";
    public static string MarketLink(string slug) => $"\n<a href=\"https://polymarket.com/market/{slug}\">Market</a>";
    public static string TransactionLink(string hash) => $"\\n<a href=\"https://polygonscan.com/tx/{hash}\">Transaction</a>";
    public static string CorrectionMessage(string header, string action, string title, string transaction) => $"{header}\n{action}\n{title}{transaction}";
    public static string SettlementMessage(string header, string details, string title, string value, string market, string transaction, DateTimeOffset time) => $"{header}\n{details}\n{title}\n{value}{market}{transaction}\n{time:yyyy-MM-dd HH:mm:ss} UTC";

    public const string Activity = "Activity";
    public const string Polymarket = "Polymarket";
    public const string PositionPrefix = "Position ";

    public static string Shares(string shares) => $" · {shares} shares";
    public static string UnitPrice(string price, string currency, string value) => $"{price} {currency}/share · {value}";
    public static string Fee(string amount, string currency) => $" · {amount} {currency} fee";

    public const string Bought = "🟢 Bought";
    public const string Sold = "🔴 Sold";
    public const string Redeemed = "🏁 Redeemed";
    public const string GrossSuffix = " gross";
    public const string PayoutSuffix = " payout";
}
