namespace Infrastructure.Hyperliquid;

/// <summary>Named diagnostic messages and structured log templates for HyperliquidDeliveryWorker.</summary>
internal static class HyperliquidDeliveryWorkerDiagnostics
{
    public const string HyperliquidOutboxBacklogFailures = "Hyperliquid outbox backlog {PendingDeliveries}, failures {FailedDeliveries}";
    public const string HyperliquidDurableDeliverySchedulingFailed = "Hyperliquid durable delivery scheduling failed";
    public const string CoreDeliveryTransportFailedForRecipient = "Core delivery transport failed for recipient {UserId}";
}
