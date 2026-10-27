namespace Infrastructure.Hyperliquid;

/// <summary>Provider dispatch and protocol values; bytes must remain compatible with upstream APIs.</summary>
internal static class HyperliquidWireTokens
{
    public const string FillsByTime = "userFillsByTime";
    public const string LedgerRows = "nonFundingLedgerUpdates";
    public const string Subscribe = "subscribe";
    public const string SubscriptionResponse = "subscriptionResponse";
    public const string Ping = "ping";
    public const string Pong = "pong";
    public const string Error = "error";
    public const string Metadata = "meta";
    public const string BuySide = "B";
    public const string SellSide = "A";
    public static string FillIdentity(string address, long time, string coin, string tradeId, string orderId) => $"HyperCore|{address}|fills|{time}|{coin}|{tradeId}|{orderId}";
    public static string LedgerIdentity(string address, string key) => $"HyperCore|{address}|ledger|{key}";
}
