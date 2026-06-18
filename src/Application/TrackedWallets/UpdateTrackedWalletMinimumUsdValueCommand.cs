using Domain.Common;
using MediatR;

namespace Application.TrackedWallets;

/// <summary>
/// Updates the minimum USD threshold for a tracked wallet selected by id.
/// </summary>
public sealed record UpdateTrackedWalletMinimumUsdValueCommand(
    long TelegramUserId,
    Guid WalletId,
    decimal MinimumUsdValue)
    : IRequest<Result<TrackedWalletDetails>>;
