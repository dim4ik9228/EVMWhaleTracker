using Domain.Common;
using MediatR;

namespace Application.TrackedWallets;

/// <summary>
/// Removes a tracked wallet selected by id after verifying Telegram user ownership.
/// </summary>
public sealed record RemoveTrackedWalletByIdCommand(
    long TelegramUserId,
    Guid WalletId)
    : IRequest<Result>;
