using Domain.Common;

namespace Application.Hyperliquid;

/// <summary>Named failures for Hyperliquid operations; codes remain compatible.</summary>
public static class HyperliquidErrors
{
    public static readonly Error WalletNotFound = new("Hyperliquid.WalletNotFound", "Wallet not found.");
    public static readonly Error StaleMenu = new("Hyperliquid.StaleMenu", "This setting changed. Refresh the Hyperliquid menu.");
    public static readonly Error Capacity = new("Hyperliquid.Capacity", "Hyperliquid tracking is at capacity. Try again later.");
}
