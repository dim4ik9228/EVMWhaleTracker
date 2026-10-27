namespace Infrastructure.Polymarket;

/// <summary>Named diagnostic messages and structured log templates for PolymarketPolygonStore.</summary>
internal static class PolymarketPolygonStoreDiagnostics
{
    public const string FilterCoverageChanged = "Contract/filter scope changed; bounded reconciliation does not establish older filter coverage.";
    public const string RecoveryHorizonExceeded = "Downtime exceeds bounded recovery horizon; older interval remains uncovered.";
    public const string ReorgHistoryUnavailable = "Reorg exceeds retained ancestry; full older canonical coverage unverified.";
    public const string AggregateConditionRedemption = "Aggregate condition redemption";
    public const string AggregateAutomaticRedemption = "Aggregate automatic redemption";
    public const string SettlementRemoved = "Previously notified settlement was removed from the canonical Polygon chain.";
    public const string SettlementRestored = "Previously removed settlement is canonical again on Polygon.";
}
