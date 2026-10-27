using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Hyperliquid;

namespace Infrastructure.Hyperliquid;

/// <summary>Normalizes canonical fills and ledger events without relying on transaction hashes for identity.</summary>
public static class HyperliquidProtocol
{
    public static HyperliquidActivity? Parse(string address, string stream, JsonElement row, IReadOnlySet<string> markets)
    {
        address = address.ToLowerInvariant();
        var time = row.GetProperty("time").GetInt64();
        if (stream == HyperliquidTokens.Streams.Fills)
        {
            var coin = Text(row, "coin");
            if (!markets.Contains(coin) || coin.Contains(':') || coin.StartsWith('@')) return null;
            var size = Number(row, "sz");
            var price = Number(row, "px");
            if (size <= 0 || price <= 0) return null;
            var side = Text(row, "side");
            if (side is not (HyperliquidWireTokens.BuySide or HyperliquidWireTokens.SellSide)) return null;
            var start = Number(row, "startPosition");
            var end = start + (side == HyperliquidWireTokens.BuySide ? size : -size);
            var effect = start == 0 ? (end > 0 ? HyperliquidTokens.Actions.OpenLong : HyperliquidTokens.Actions.OpenShort)
                : end == 0 ? HyperliquidTokens.Actions.ClosePosition : Math.Sign(end) != Math.Sign(start) ? HyperliquidTokens.Actions.FlipPosition
                : Math.Abs(end) > Math.Abs(start) ? HyperliquidTokens.Actions.IncreasePosition : HyperliquidTokens.Actions.ReducePosition;
            var victim = row.TryGetProperty(HyperliquidTokens.Actions.Liquidation, out var liquidation) && liquidation.ValueKind == JsonValueKind.Object
                && Text(liquidation, "liquidatedUser").Equals(address, StringComparison.OrdinalIgnoreCase);
            var tid = row.GetProperty("tid").GetRawText();
            var order = row.GetProperty("oid").GetRawText();
            // The documented trade identity includes block time; tid alone can recur across blocks.
            var id = HyperliquidWireTokens.FillIdentity(address, time, coin, tid, order);
            return new(id, address, stream, time, effect, coin, order, size, price, Math.Abs(size * price),
                row.TryGetProperty("closedPnl", out _) ? Number(row, "closedPnl") : null, victim, TransactionHash: ValidTransactionHash(Text(row, "hash")),
                PositionSide: (effect == HyperliquidTokens.Actions.FlipPosition ? end : start == 0 ? end : start) > 0 ? HyperliquidTokens.Positions.Long : HyperliquidTokens.Positions.Short);
        }
        if (stream != HyperliquidTokens.Streams.Ledger) return null;
        var delta = row.GetProperty("delta");
        var kind = Text(delta, "type");
        if (kind is not (HyperliquidTokens.Actions.Deposit or HyperliquidTokens.Actions.Withdraw or HyperliquidTokens.Actions.Liquidation)) return null;
        // Canonical sorted JSON includes the complete delta (including liquidated positions), time and hash.
        var canonical = Canonical(row);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        decimal? usd = kind == HyperliquidTokens.Actions.Liquidation ? null : Math.Abs(Number(delta, "usdc"));
        return new(HyperliquidWireTokens.LedgerIdentity(address, key), address, stream, time, kind, HyperliquidTokens.Currencies.Usdc, "", usd ?? 0, 1, usd, null, LedgerFacts: kind == HyperliquidTokens.Actions.Liquidation ? Canonical(delta) : null, TransactionHash: ValidTransactionHash(Text(row, "hash")));
    }

    /// <summary>Only real full-width source hashes can become explorer transaction links.</summary>
    public static string? ValidTransactionHash(string? hash) => hash is { Length: 66 }
        && hash.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        && hash.AsSpan(2).ContainsAnyExcept("0123456789abcdefABCDEF") == false
        && hash.AsSpan(2).ContainsAnyExcept('0') ? hash.ToLowerInvariant() : null;

    public static string Canonical(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(',', e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => JsonSerializer.Serialize(p.Name) + ":" + Canonical(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(',', e.EnumerateArray().Select(Canonical)) + "]",
        _ => e.GetRawText()
    };
    private static string Text(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
    private static decimal Number(JsonElement e, string key) => decimal.Parse(e.GetProperty(key).ToString(), CultureInfo.InvariantCulture);
}
