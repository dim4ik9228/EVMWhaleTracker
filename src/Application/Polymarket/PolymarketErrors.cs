using Domain.Common;

namespace Application.Polymarket;

/// <summary>Named failures for Polymarket operations; codes remain compatible.</summary>
public static class PolymarketErrors
{
    public static readonly Error WalletNotFound = new("Polymarket.Settings", "Wallet not found.");
    public static readonly Error InvalidTradingAddress = new("Polymarket.Settings", "Supply a valid EVM trading-wallet address.");
    public static readonly Error MappingUnverified = new("Polymarket.Settings", "Trading-wallet mapping could not be verified. Try again after the public profile lookup recovers.");
    public static readonly Error StaleMenu = new("Polymarket.Settings", "This setting changed. Refresh the Polymarket menu.");
    public static readonly Error AccountAlreadyBound = new("Polymarket.Settings", "Another wallet in your watchlist is already bound to this Polymarket account.");
    public static readonly Error TrackingPaused = new("Polymarket.Settings", "Polygon tracking is paused; profile lookup remains available.");
    public static readonly Error ConfirmationRequired = new("Polymarket.Settings", "Confirm the proposed trading account before enabling tracking.");
    public static readonly Error Capacity = new("Polymarket.Settings", "Polygon tracking capacity is full. Existing accounts remain tracked.");
    public static readonly Error ProviderUnavailable = new("Polymarket.Settings", "Polygon provider unavailable or access denied. Tracking was not enabled; try again when the source recovers.");
    public static readonly Error InvalidMinimum = new("Polymarket.Settings", "Send a non-negative Polymarket minimum up to 1,000,000,000,000 collateral units.");
}
