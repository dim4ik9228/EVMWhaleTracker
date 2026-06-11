using Domain.Common;
using MediatR;

namespace Application.TrackedWallets;

/// <summary>
/// Removes a tracked wallet for a Telegram user.
/// </summary>
public sealed record RemoveTrackedWalletCommand(
    long TelegramUserId,
    string RawAddress) : IRequest<Result>;
