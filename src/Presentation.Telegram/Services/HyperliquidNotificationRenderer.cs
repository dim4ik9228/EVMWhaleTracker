using Presentation.Telegram.Commands;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Application.Hyperliquid;

namespace Presentation.Telegram.Services;

/// <summary>Renders native perpetual activity as compact explorer-linked Telegram HTML.</summary>
public sealed class HyperliquidNotificationRenderer
{
    /// <summary>Formats an owner-specific alert; old durable payloads retain honest generic effect labels.</summary>
    public string Render(HyperliquidNotification notification, string? botUsername = null)
    {
        var events = notification.Activities;
        var first = events[0];
        var walletUrl = "https://hypurrscan.io/address/" + Uri.EscapeDataString(notification.Address);
        var header = HyperliquidNotificationRendererText.Header(Link(notification.DisplayText, walletUrl));
        if (!string.IsNullOrWhiteSpace(botUsername))
            header += " | " + Link(HyperliquidNotificationRendererText.EditIcon, $"https://t.me/{Uri.EscapeDataString(botUsername.Trim().TrimStart('@'))}?start={TelegramInteractionTokens.EditWalletPrefix}{notification.WalletId:N}");
        var lines = new List<string> { header };
        if (first.Stream == HyperliquidTokens.Streams.Fills)
        {
            var size = events.Sum(x => x.Size);
            var price = size == 0 ? 0 : events.Sum(x => x.Size * x.Price) / size;
            var action = first.Action switch
            {
                HyperliquidTokens.Actions.OpenLong => HyperliquidNotificationRendererText.Longed,
                HyperliquidTokens.Actions.OpenShort => HyperliquidNotificationRendererText.Shorted,
                HyperliquidTokens.Actions.IncreasePosition when first.PositionSide == HyperliquidTokens.Positions.Long => HyperliquidNotificationRendererText.IncreasedLong,
                HyperliquidTokens.Actions.IncreasePosition when first.PositionSide == HyperliquidTokens.Positions.Short => HyperliquidNotificationRendererText.IncreasedShort,
                HyperliquidTokens.Actions.ReducePosition when first.PositionSide is HyperliquidTokens.Positions.Long or HyperliquidTokens.Positions.Short => HyperliquidNotificationRendererText.ReducedPrefix + first.PositionSide,
                HyperliquidTokens.Actions.ClosePosition when first.PositionSide is HyperliquidTokens.Positions.Long or HyperliquidTokens.Positions.Short => HyperliquidNotificationRendererText.ClosedPrefix + first.PositionSide,
                HyperliquidTokens.Actions.FlipPosition when first.PositionSide is HyperliquidTokens.Positions.Long or HyperliquidTokens.Positions.Short => HyperliquidNotificationRendererText.FlippedTo + first.PositionSide,
                _ => first.Action
            };
            var notional = events.All(x => x.Usd.HasValue) ? HyperliquidNotificationRendererText.NotionalPrefix + Usd(events.Sum(x => x.Usd!.Value)) : "";
            lines.Add(HyperliquidNotificationRendererText.PerpetualFill(E(action), Amount(size), E(first.Market), notional, ExecutionPrice(price), E(first.Market)));
            if (events.All(x => x.ClosedPnl.HasValue))
            {
                var pnl = events.Sum(x => x.ClosedPnl!.Value);
                if (pnl != 0)
                    lines.Add(HyperliquidNotificationRendererText.RealizedPnl(RealizedPnl(pnl)));
            }
        }
        else if (first.Action is HyperliquidTokens.Actions.Deposit or HyperliquidTokens.Actions.Withdraw)
            lines.Add(HyperliquidNotificationRendererText.LedgerTransfer((first.Action == HyperliquidTokens.Actions.Deposit ? HyperliquidNotificationRendererText.Deposit : HyperliquidNotificationRendererText.Withdrawal), Amount(first.Size)));
        else
        {
            lines.Add(HyperliquidNotificationRendererText.LiquidationHyperliquidLedger);
            AddLiquidationFacts(lines, first.LedgerFacts);
        }
        var hashes = events.Select(x => x.TransactionHash).Where(IsHash).Select(x => x!.ToLowerInvariant()).Distinct().ToList();
        if (hashes.Count == 1) lines.Add(Link(HyperliquidNotificationRendererText.TxHash, "https://hypurrscan.io/tx/" + hashes[0]));
        else if (hashes.Count > 1)
        {
            lines.Add(HyperliquidNotificationRendererText.TransactionLinksPrefix + string.Join(" · ", hashes.Take(5).Select((hash, index) => Link((index + 1).ToString(CultureInfo.InvariantCulture), "https://hypurrscan.io/tx/" + hash))));
            if (hashes.Count > 5) lines.Add(Link(HyperliquidNotificationRendererText.AdditionalTransactions(hashes.Count - 5), walletUrl));
        }
        if (hashes.Count == 0 || events.Any(x => !IsHash(x.TransactionHash)))
            lines.Add(Link(HyperliquidNotificationRendererText.WalletActivity, walletUrl));
        return string.Join('\n', lines);
    }

    private static void AddLiquidationFacts(List<string> lines, string? facts)
    {
        if (string.IsNullOrWhiteSpace(facts)) return;
        try
        {
            using var json = JsonDocument.Parse(facts);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty("accountValue", out var value) && decimal.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var account))
                lines.Add(HyperliquidNotificationRendererText.ReportedAccountValue + Usd(account));
            if (root.TryGetProperty("liquidatedPositions", out var positions) && positions.ValueKind == JsonValueKind.Array)
            {
                foreach (var position in positions.EnumerateArray().Take(5))
                {
                    if (position.ValueKind != JsonValueKind.Object) continue;
                    if (!position.TryGetProperty("coin", out var coin) || coin.ValueKind != JsonValueKind.String) continue;
                    var size = position.TryGetProperty("szi", out var szi) && decimal.TryParse(szi.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)
                        ? Amount(Math.Abs(amount)) + " " : "";
                    lines.Add(HyperliquidNotificationRendererText.LiquidatedPosition + size + E(coin.GetString()!));
                }
                if (positions.GetArrayLength() > 5) lines.Add(HyperliquidNotificationRendererText.AdditionalPositions(positions.GetArrayLength() - 5));
            }
        }
        catch (JsonException) { /* Old or incomplete ledger facts remain a truthful generic ledger alert. */ }
    }

    private static bool IsHash(string? hash) => hash is { Length: 66 } && hash.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        && hash.Skip(2).All(Uri.IsHexDigit) && hash.Skip(2).Any(x => x != '0');
    private static string E(string value) => WebUtility.HtmlEncode(value);
    private static string Link(string text, string url) => HyperliquidNotificationRendererText.HtmlLink(E(url), E(text));
    private static string Amount(decimal value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    private static string Usd(decimal value) => (value < 0 ? "-$" : "$")
        + Math.Abs(value).ToString(Math.Abs(value) < 1 ? "0.00" : "0.##", CultureInfo.InvariantCulture);
    private static string ExecutionPrice(decimal value) => "$" + value.ToString("0.############################", CultureInfo.InvariantCulture);
    private static string RealizedPnl(decimal value) => (value < 0 ? "-$" : "+$")
        + Math.Abs(value).ToString(Math.Abs(value) < 0.01m ? "0.############################" : "0.00", CultureInfo.InvariantCulture);
}
