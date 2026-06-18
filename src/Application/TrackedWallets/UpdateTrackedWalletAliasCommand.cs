using Domain.Common;
using MediatR;

namespace Application.TrackedWallets;

/// <summary>
/// Renames a tracked wallet selected by id from an inline Telegram settings flow.
/// </summary>
public sealed record UpdateTrackedWalletAliasCommand(
    long TelegramUserId,
    Guid WalletId,
    string? Alias)
    : IRequest<Result<TrackedWalletDetails>>;
