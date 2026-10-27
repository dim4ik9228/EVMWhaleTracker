namespace Application.Hyperliquid;

/// <summary>Stable HyperliquidTokens values shared by normalization, storage and presentation.</summary>
public static class HyperliquidTokens
{
    /// <summary>Streams compatibility tokens.</summary>
    public static class Streams
    {
        public const string Fills = "userFills";
        public const string Ledger = "userNonFundingLedgerUpdates";
    }
    /// <summary>Actions compatibility tokens.</summary>
    public static class Actions
    {
        public const string Deposit = "deposit";
        public const string Withdraw = "withdraw";
        public const string Liquidation = "liquidation";
        public const string OpenLong = "Open long";
        public const string OpenShort = "Open short";
        public const string ClosePosition = "Close position";
        public const string FlipPosition = "Flip position";
        public const string IncreasePosition = "Increase position";
        public const string ReducePosition = "Reduce position";
    }
    /// <summary>Positions compatibility tokens.</summary>
    public static class Positions
    {
        public const string Long = "Long";
        public const string Short = "Short";
    }
    /// <summary>Currencies compatibility tokens.</summary>
    public static class Currencies
    {
        public const string Usdc = "USDC";
    }
}
