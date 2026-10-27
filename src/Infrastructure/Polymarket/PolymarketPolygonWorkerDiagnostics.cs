namespace Infrastructure.Polymarket;

/// <summary>Named diagnostic messages and structured log templates for PolymarketPolygonWorker.</summary>
internal static class PolymarketPolygonWorkerDiagnostics
{
    public const string OffPolygonTrackingPaused = "Off — Polygon tracking paused";
    public const string StartingAnotherIngestionOwner = "Starting — another ingestion owner";
    public const string ConnectionUnavailable = "Degraded — Polygon connection/recovery unavailable; retrying";
    public const string PolygonSourceReconnectRequired = "Polygon source reconnect required ({FailureType}): {SafeReason}";
    public const string OffNoEnabledAccounts = "Off — no enabled accounts";
    public const string PolygonSourceCapacityExceeded = "Polygon source capacity exceeded.";
    public const string PolygonClosedSubscription = "Polygon closed subscription.";
    public const string PolygonFrameLimit = "Polygon frame limit.";
    public const string PolygonSocketChainMismatch = "Polygon socket chain mismatch.";
    public const string PolygonSubscriptionRejected = "Polygon subscription rejected.";
    public const string UnexpectedPolygonAcknowledgment = "Unexpected Polygon acknowledgment.";
    public const string UnacknowledgedPolygonSubscription = "Unacknowledged Polygon subscription.";
    public const string PolygonCanonicalRewindRequiresRecovery = "Polygon canonical rewind requires recovery.";
    public const string PolygonSubscriptionsAcknowledged = "Polygon subscriptions acknowledged {Acknowledged}/{Desired}";
    public const string RecoveringBoundedPolygonOverlap = "Recovering — bounded Polygon overlap";
    public const string DegradedCoverage = "Degraded — live fills active; retained history/filter coverage gaps";
    public const string ActiveCoverage = "Active — verified fills; routed/combo coverage limited";
    public const string PolygonHeadStreamStalled = "Polygon head stream stalled.";
    public const string HealthSummary = "Polygon health: chain {ChainVerified}, subscriptions {Acknowledged}/{Desired}, head {Head}, canonical boundary {CanonicalBoundary}, covered {CoveredBlock}, coverage lag {CoverageLagBlocks}, gaps {CoverageGaps}, malformed {Malformed}, unsupported {Unsupported}, duplicates {Duplicates}, buffered {Buffered}, normalization pending {NormalizationPending}, enrichment pending {EnrichmentPending}, delivery pending {DeliveryPending}, delivery failed {DeliveryFailed}, backlog count limit {BacklogCountLimit}";
    public const string SafeFailurePrefix = "Polygon ";
}
