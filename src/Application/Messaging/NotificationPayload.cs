using Domain.Common;

namespace Application.Messaging;

/// <summary>
/// Structured wallet activity notification ready for presentation-specific rendering.
/// </summary>
public sealed record NotificationPayload(
    long TelegramUserId,
    Guid TrackedWalletId,
    SupportedChain Chain,
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
    DateTimeOffset BlockTimestampUtc, long? EntitlementRevision = null, DateTimeOffset? EntitlementValidUntil = null);

/// <summary>
/// A transaction-level notification containing one or more wallet activity lines.
/// </summary>
public sealed record AggregatedNotificationPayload(
    long TelegramUserId,
    Guid TrackedWalletId,
    SupportedChain Chain,
    string TrackedWalletAddress,
    string? TrackedWalletAlias,
    string TransactionHash,
    ulong BlockNumber,
    DateTimeOffset BlockTimestampUtc,
    IReadOnlyList<NotificationTransferLine> Lines, long? EntitlementRevision = null, DateTimeOffset? EntitlementValidUntil = null,
    Application.Swaps.VerifiedSwap? Swap = null, Guid? SwapDecisionId = null, Guid? DeliveryLease = null)
{
    public static AggregatedNotificationPayload FromSingle(NotificationPayload payload)
        => new(
            payload.TelegramUserId,
            payload.TrackedWalletId,
            payload.Chain,
            payload.TrackedWalletAddress,
            payload.TrackedWalletAlias,
            payload.TransactionHash,
            payload.BlockNumber,
            payload.BlockTimestampUtc,
            [NotificationTransferLine.FromPayload(payload)], payload.EntitlementRevision, payload.EntitlementValidUntil);
}

/// <summary>
/// One transfer line inside a transaction-level notification.
/// </summary>
public sealed record NotificationTransferLine(
    NotificationOperation Operation,
    string? CounterpartyAddress,
    string? CounterpartyAlias,
    string AssetSymbol,
    string? AssetName,
    string? TokenContractAddress,
    decimal Amount,
    decimal? UsdValue)
{
    public static NotificationTransferLine FromPayload(NotificationPayload payload)
        => new(
            payload.Operation,
            payload.CounterpartyAddress,
            payload.CounterpartyAlias,
            payload.AssetSymbol,
            payload.AssetName,
            payload.TokenContractAddress,
            payload.Amount,
            payload.UsdValue);
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
