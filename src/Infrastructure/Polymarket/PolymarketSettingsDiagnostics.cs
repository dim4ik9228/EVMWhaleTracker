namespace Infrastructure.Polymarket;

/// <summary>Named diagnostic messages and structured log templates for PolymarketSettings.</summary>
internal static class PolymarketSettingsDiagnostics
{
    public const string HistoryRecovering = "Recovering — Polygon history is being reconciled.";
    public const string OffServicePaused = "Off — service paused";
    public const string MappingChanged = "Needs attention — profile mapping changed. Confirm a rebind.";
    public const string ProfileUnavailable = "Resolving — profile temporarily unavailable; existing binding preserved.";
    public const string ProfileMissing = "Resolving — no public Polymarket profile found.";
    public const string AccountUnresolved = "Resolving — trading account unresolved.";
    public const string OffPolygonTrackingPausedProfileAvailable = "Off — Polygon tracking paused; profile available.";
    public const string TrackingOff = "Off — enable to follow future finalized settlements.";
    public const string OffNoEnabledAccounts = PolymarketPolygonWorkerDiagnostics.OffNoEnabledAccounts;
    public const string AccountStarting = "Starting — connecting the confirmed trading account.";
    public static string DegradedHistory(string gap) => $"Degraded — {gap}";
}
