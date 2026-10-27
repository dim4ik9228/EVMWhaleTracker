namespace Application.Polymarket;

/// <summary>Stable PolymarketTokens values shared by normalization, storage and presentation.</summary>
public static class PolymarketTokens
{
    /// <summary>Actions compatibility tokens.</summary>
    public static class Actions
    {
        public const string Buy = "BUY";
        public const string Sell = "SELL";
        public const string Redeem = "REDEEM";
        public const string Correction = "CORRECTION";
    }
    /// <summary>Currencies compatibility tokens.</summary>
    public static class Currencies
    {
        public const string Pusd = "pUSD";
        public const string Usdc = "USDC";
        public const string BridgedUsdc = "USDC.e";
    }
}
