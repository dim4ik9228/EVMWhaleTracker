using Domain.Common;
using MediatR;

namespace Application.TrackedWallets;

/// <summary>
/// Loads one tracked wallet by id after verifying that it belongs to the Telegram user.
/// </summary>
public sealed record GetTrackedWalletQuery(
    long TelegramUserId,
    Guid WalletId)
    : IRequest<Result<TrackedWalletDetails>>;
