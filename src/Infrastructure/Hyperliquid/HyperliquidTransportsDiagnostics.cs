namespace Infrastructure.Hyperliquid;

/// <summary>Named diagnostic messages and structured log templates for HyperliquidTransports.</summary>
internal static class HyperliquidTransportsDiagnostics
{
    public const string HyperliquidSocketClosedOrSentANonText = "Hyperliquid socket closed or sent a non-text frame.";
    public const string HyperliquidFrameExceedsConfiguredLimit = "Hyperliquid frame exceeds configured limit.";
    public const string HyperliquidHTTPThrottledRecovery = "Hyperliquid HTTP throttled recovery.";
    public const string HyperliquidHTTPResponseTooLarge = "Hyperliquid HTTP response too large.";
}
