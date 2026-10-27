namespace Infrastructure.Hyperliquid;

/// <summary>Named diagnostic messages and structured log templates for HyperliquidWorker.</summary>
internal static class HyperliquidWorkerDiagnostics
{
    public const string DegradedInvalidConfiguration = "Degraded — invalid configuration";
    public const string InvalidHyperliquidBoundsSourceDisabled = "Invalid Hyperliquid bounds; source disabled";
    public const string HyperliquidSourceReconnectRequired = "Hyperliquid source reconnect required";
    public const string DegradedCapacityConfiguration = "Degraded — capacity configuration";
    public const string EnrollmentCapacityExceeded = "Core persisted enrollment exceeds configured capacity";
    public const string HyperliquidConnectedWithAdmittedAddresses = "Hyperliquid connected with {UniqueAddresses}/{Capacity} admitted addresses";
    public const string HyperliquidRejectedSubscription = "Hyperliquid rejected subscription.";
    public const string ConsumerOverloaded = "Hyperliquid durable consumer overloaded; reconnect recovery required.";
    public const string HyperliquidPongDeadlineExceeded = "Hyperliquid pong deadline exceeded.";
    public const string HyperliquidSubscriptionsNotAcknowledged = "Hyperliquid subscriptions not acknowledged.";
    public const string HealthSummary = "Hyperliquid health: desired {DesiredSubscriptions}, acknowledged {AcknowledgedSubscriptions}, pong age {PongAgeSeconds}, accepted {AcceptedEvents}, duplicates {DuplicateEvents}, status {Status}";
    public const string UnsupportedHyperliquidCollateralMetadata = "Unsupported Hyperliquid collateral metadata.";
    public const string HyperliquidRecoveryStreamInterval = "Hyperliquid recovery stream {Stream}, interval {Start}..{End}";
    public static string RecoveryAgeExceeded(long start, long end) => $"Recovery age exceeded: {start}..{end}";
    public static string ProviderFillRetentionExhausted(long start, long end) => $"Provider fill retention exhausted: {start}..{end}";
    public static string HistoryPageBudgetBoundary(long start, long end) => $"History page/budget boundary: {start}..{end}";
    public static string LedgerHistoryIncomplete(long start, long end) => $"Ledger retention completeness is not guaranteed: {start}..{end}";
    public const string HyperliquidRecoveryIncompleteForStream = "Hyperliquid recovery incomplete for stream {Stream}";
    public static string RecoveryFailed(long start, long end) => $"Recovery failed: {start}..{end}";
    public const string DegradedLiveAlertsHistoryMayBeIncomplete = "Degraded — live alerts; history may be incomplete";
    public const string DegradedReconnecting = "Degraded — reconnecting";
}
