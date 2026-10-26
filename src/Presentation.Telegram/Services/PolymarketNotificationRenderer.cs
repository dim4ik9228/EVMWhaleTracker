using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Application.Polymarket;

namespace Presentation.Telegram.Services;

/// <summary>Safe compact individual activity rendering; no arbitrary provider URLs are accepted.</summary>
public sealed partial class PolymarketNotificationRenderer
{
    public string Render(PolymarketNotification notification, string? botUsername)
    {
        var a = notification.Activity;
        string E(string? value) => WebUtility.HtmlEncode(Clean(value));
        string N(decimal value) => value.ToString("0.########", CultureInfo.InvariantCulture);
        var account = Address().IsMatch(a.SourceAddress) ? $"<a href=\"https://polymarket.com/profile/{a.SourceAddress}\">Polymarket</a>" : "Polymarket";
        var header = $"<b>{E(notification.Alias)}</b> | {account}";
        if (!string.IsNullOrWhiteSpace(botUsername) && BotName().IsMatch(botUsername.TrimStart('@')))
            header += $" | <a href=\"https://t.me/{Uri.EscapeDataString(botUsername.TrimStart('@'))}?start=edit_wallet_{notification.WalletId:N}\">✏️</a>";
        var action = a.Action switch { "BUY" => "🟢 Bought", "SELL" => "🔴 Sold", "REDEEM" => "🏁 Redeemed", "CORRECTION" => "⚠️ Settlement correction", _ => "Activity" };
        var details = action + " " + E(a.Outcome ?? a.MarketId);
        if (a.Shares is { } shares) details += $" · {N(shares)} shares";
        var value = a.CashValue is { } cash ? $"{N(cash)} {E(a.QuoteCurrency)}" : "Value unavailable";
        if (a.Price is { } price && a.Action != "REDEEM") value = $"{N(price)} {E(a.QuoteCurrency)}/share · {value}";
        if (a.Action is "BUY" or "SELL") value += " gross";
        if (a.CollateralFee is > 0) value += $" · {N(a.CollateralFee.Value)} {E(a.QuoteCurrency)} fee";
        if (a.Action == "REDEEM") value += " payout";
        var market = a.MarketSlug is { } slug && Slug().IsMatch(slug)
            ? $"\n<a href=\"https://polymarket.com/market/{Uri.EscapeDataString(slug)}\">Market</a>" : "";
        var transaction = a.TransactionHash is { } hash && Hash().IsMatch(hash)
            ? $"\\n<a href=\"https://polygonscan.com/tx/{hash}\">Transaction</a>" : "";
        if (a.Action == "CORRECTION") return $"{header}\n{action}\n{E(a.Title)}{transaction.Replace("\\n", "\n")}";
        return $"{header}\n{details}\n{E(a.Title ?? ("Position " + a.MarketId))}\n{value}{market}{transaction.Replace("\\n", "\n")}\n{a.Time:yyyy-MM-dd HH:mm:ss} UTC";
    }
    private static string Clean(string? value) => new((value ?? "").Where(c => !char.IsControl(c) && char.GetUnicodeCategory(c) != UnicodeCategory.Format).Take(300).ToArray());
    [GeneratedRegex("^0x[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)] private static partial Regex Hash();
    [GeneratedRegex("^[A-Za-z0-9_]{5,32}$", RegexOptions.CultureInvariant)] private static partial Regex BotName();
    [GeneratedRegex("^0x[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)] private static partial Regex Address();
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,199}$", RegexOptions.CultureInvariant)] private static partial Regex Slug();
}
