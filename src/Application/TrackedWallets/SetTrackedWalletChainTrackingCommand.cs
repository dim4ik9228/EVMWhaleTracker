using Domain.Common;
using MediatR;

namespace Application.TrackedWallets;

/// <summary>
/// Enables or disables one chain for a tracked wallet selected by id.
/// </summary>
public sealed record SetTrackedWalletChainTrackingCommand(
    long TelegramUserId,
    Guid WalletId,
    SupportedChain Chain,
    bool Enabled)
    : IRequest<Result<TrackedWalletDetails>>;
