namespace Infrastructure.Workers;

/// <summary>Named diagnostic messages and structured log templates for NotificationAggregationWorker.</summary>
internal static class NotificationAggregationWorkerDiagnostics
{
    public const string DebounceIntervalMustBePositive = "Debounce interval must be positive.";
    public const string AggregationFailed = "Failed to aggregate notification for transaction {TransactionHash}.";
    public const string NotificationAggregated = "Aggregated {LineCount} notification lines for chat {ChatId} and transaction {TransactionHash}.";
}
