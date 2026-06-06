using Domain.Common;
using MediatR;

namespace Application.TrackedWallets;

public sealed record ListTrackedWalletsQuery(long TelegramUserId)
    : IRequest<Result<IReadOnlyList<TrackedWalletDetails>>>;
