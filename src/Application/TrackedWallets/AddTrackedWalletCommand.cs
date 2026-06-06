using Domain.Common;
using MediatR;

namespace Application.TrackedWallets;

public sealed record AddTrackedWalletCommand(
    long TelegramUserId,
    string RawAddress,
    decimal MinimumTransactionValue = 0m,
    bool TrackIncomingTransfers = true,
    bool TrackOutgoingTransfers = true,
    bool TrackTokenTransfers = true) : IRequest<Result<TrackedWalletDetails>>;
