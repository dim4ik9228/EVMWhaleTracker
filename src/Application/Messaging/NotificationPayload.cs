namespace Application.Messaging;

/// <summary>
/// Structured wallet activity notification ready for presentation-specific rendering.
/// </summary>
public sealed record NotificationPayload(
    long TelegramUserId,
    Domain.Common.SupportedChain Chain,
    NotificationOperation Operation,
    string TrackedWalletAddress,
    string? TrackedWalletAlias,
    string? CounterpartyAddress,
    string? CounterpartyAlias,
    string TransactionHash,
    string AssetSymbol,
    string? AssetName,
    string? TokenContractAddress,
    decimal Amount,
    decimal? UsdValue,
    ulong BlockNumber,
    DateTimeOffset BlockTimestampUtc)
{
    /// <summary>
    /// Backward-compatible alias for the tracked wallet address.
    /// </summary>
    public string WalletAddress => TrackedWalletAddress;
}

/// <summary>
/// Direction of wallet activity from the tracked wallet's point of view.
/// </summary>
public enum NotificationOperation
{
    Received,
    Sent,
    SelfTransfer
}
