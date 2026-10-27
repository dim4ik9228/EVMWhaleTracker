namespace Infrastructure.Workers;

/// <summary>Named diagnostic messages and structured log templates for TokenTransferLogSubscriberWorker.</summary>
internal static class TokenTransferLogSubscriberWorkerDiagnostics
{
    public const string SubscriberDisabled = "Token transfer log subscriber for {Chain} is disabled because WebSocketRpcUrl is not configured.";
    public const string SubscriptionDropped = "Token transfer log websocket subscription dropped for {Chain}. Reconnecting after delay.";
    public const string SubscribedToERCTransferLogsOnForTracked = "Subscribed to ERC-20 transfer logs on {Chain} for {TrackedAddressCount} tracked wallets using {FilterCount} websocket filters.";
    public const string SubscriptionsRefreshing = "Refreshing ERC-20 transfer log subscriptions for {Chain} after tracked address cache changed.";
    public const string UnsubscribeFailed = "Ignoring token transfer log unsubscribe failure for {Chain}.";
    public const string WebSocketStopFailed = "Ignoring token transfer log websocket stop failure for {Chain}.";
    public const string ClientConnectionError = "Client connection error";
}
