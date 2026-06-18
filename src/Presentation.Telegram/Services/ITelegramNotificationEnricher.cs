using Application.Messaging;
using Domain.Common;

namespace Presentation.Telegram.Services;

/// <summary>
/// Resolves display names for notification payloads before Telegram-specific rendering.
/// </summary>
public interface ITelegramNotificationEnricher
{
    Task<EnrichedTelegramNotification> EnrichAsync(NotificationPayload payload, CancellationToken cancellationToken);

    Task<EnrichedTelegramNotification> EnrichAsync(AggregatedNotificationPayload payload, CancellationToken cancellationToken);
}

/// <summary>
/// Notification payload with wallet and counterparty display text resolved.
/// </summary>
public sealed record EnrichedTelegramNotification(
    long TelegramUserId,
    Guid TrackedWalletId,
    SupportedChain Chain,
    string TrackedWalletAddress,
    string TrackedWalletDisplayText,
    string TransactionHash,
    ulong BlockNumber,
    DateTimeOffset BlockTimestampUtc,
    IReadOnlyList<EnrichedTelegramTransferLine> Lines);

/// <summary>
/// One transfer line with counterparty display text resolved.
/// </summary>
public sealed record EnrichedTelegramTransferLine(
    NotificationOperation Operation,
    string? CounterpartyAddress,
    string CounterpartyDisplayText,
    string AssetSymbol,
    string? AssetName,
    string? TokenContractAddress,
    decimal Amount,
    decimal? UsdValue);
