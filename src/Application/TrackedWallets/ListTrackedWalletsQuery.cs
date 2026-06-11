using Domain.Common;
using MediatR;

namespace Application.TrackedWallets;

/// <summary>
/// Lists all wallets tracked by a Telegram user.
/// </summary>
public sealed record ListTrackedWalletsQuery(long TelegramUserId)
    : IRequest<Result<IReadOnlyList<TrackedWalletDetails>>>;
