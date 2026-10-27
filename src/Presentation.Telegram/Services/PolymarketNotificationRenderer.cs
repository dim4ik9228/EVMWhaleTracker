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
        var account = Address().IsMatch(a.SourceAddress) ? PolymarketNotificationRendererText.ProfileLink(a.SourceAddress) : PolymarketNotificationRendererText.Polymarket;
        var header = PolymarketNotificationRendererText.Header(E(notification.Alias), account);
        if (!string.IsNullOrWhiteSpace(botUsername) && BotName().IsMatch(botUsername.TrimStart('@')))
            header += PolymarketNotificationRendererText.EditWalletLink(Uri.EscapeDataString(botUsername.TrimStart('@')), notification.WalletId);
        var action = a.Action switch { PolymarketTokens.Actions.Buy => PolymarketNotificationRendererText.Bought, PolymarketTokens.Actions.Sell => PolymarketNotificationRendererText.Sold, PolymarketTokens.Actions.Redeem => PolymarketNotificationRendererText.Redeemed, PolymarketTokens.Actions.Correction => PolymarketNotificationRendererText.SettlementCorrection, _ => PolymarketNotificationRendererText.Activity };
        var details = action + " " + E(a.Outcome ?? a.MarketId);
        if (a.Shares is { } shares) details += PolymarketNotificationRendererText.Shares(N(shares));
        var value = a.CashValue is { } cash ? $"{N(cash)} {E(a.QuoteCurrency)}" : PolymarketNotificationRendererText.ValueUnavailable;
        if (a.Price is { } price && a.Action != PolymarketTokens.Actions.Redeem) value = PolymarketNotificationRendererText.UnitPrice(N(price), E(a.QuoteCurrency), value);
        if (a.Action is PolymarketTokens.Actions.Buy or PolymarketTokens.Actions.Sell) value += PolymarketNotificationRendererText.GrossSuffix;
        if (a.CollateralFee is > 0) value += PolymarketNotificationRendererText.Fee(N(a.CollateralFee.Value), E(a.QuoteCurrency));
        if (a.Action == PolymarketTokens.Actions.Redeem) value += PolymarketNotificationRendererText.PayoutSuffix;
        var market = a.MarketSlug is { } slug && Slug().IsMatch(slug)
            ? PolymarketNotificationRendererText.MarketLink(Uri.EscapeDataString(slug)) : "";
        var transaction = a.TransactionHash is { } hash && Hash().IsMatch(hash)
            ? PolymarketNotificationRendererText.TransactionLink(hash) : "";
        if (a.Action == PolymarketTokens.Actions.Correction) return PolymarketNotificationRendererText.CorrectionMessage(header, action, E(a.Title), transaction.Replace("\\n", "\n"));
        return PolymarketNotificationRendererText.SettlementMessage(header, details, E(a.Title ?? (PolymarketNotificationRendererText.PositionPrefix + a.MarketId)), value, market, transaction.Replace("\\n", "\n"), a.Time);
    }
    private static string Clean(string? value) => new((value ?? "").Where(c => !char.IsControl(c) && char.GetUnicodeCategory(c) != UnicodeCategory.Format).Take(300).ToArray());
    [GeneratedRegex("^0x[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)] private static partial Regex Hash();
    [GeneratedRegex("^[A-Za-z0-9_]{5,32}$", RegexOptions.CultureInvariant)] private static partial Regex BotName();
    [GeneratedRegex("^0x[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)] private static partial Regex Address();
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,199}$", RegexOptions.CultureInvariant)] private static partial Regex Slug();
}
