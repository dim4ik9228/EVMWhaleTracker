using Domain.Common;
using MediatR;

namespace Application.TrackedWallets;

public sealed record RemoveTrackedWalletCommand(
    long TelegramUserId,
    string RawAddress) : IRequest<Result>;
