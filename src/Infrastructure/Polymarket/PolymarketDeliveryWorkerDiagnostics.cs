namespace Infrastructure.Polymarket;

/// <summary>Named diagnostic messages and structured log templates for PolymarketDeliveryWorker.</summary>
internal static class PolymarketDeliveryWorkerDiagnostics
{
    public const string PolymarketOutboxSchedulingFailed = "Polymarket outbox scheduling failed";
    public const string PolymarketRecipientAttemptFailed = "Polymarket recipient attempt failed ({FailureType})";
}
