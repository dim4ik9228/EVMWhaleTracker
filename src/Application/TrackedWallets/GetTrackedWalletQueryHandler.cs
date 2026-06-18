using Application.Abstractions;
using Domain.Common;
using MediatR;

namespace Application.TrackedWallets;

/// <summary>
/// Handles wallet-id lookups used by inline Telegram settings menus and deep links.
/// </summary>
public sealed class GetTrackedWalletQueryHandler(ITrackedWalletRepository trackedWalletRepository)
    : IRequestHandler<GetTrackedWalletQuery, Result<TrackedWalletDetails>>
{
    /// <summary>
    /// Returns wallet details only when the requested wallet belongs to the requesting Telegram user.
    /// </summary>
    public async Task<Result<TrackedWalletDetails>> Handle(GetTrackedWalletQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trackedWallet = await trackedWalletRepository.GetByIdAsync(request.WalletId, cancellationToken);
        if (trackedWallet is null || trackedWallet.UserId != request.TelegramUserId)
        {
            return Result<TrackedWalletDetails>.Failure(Errors.TrackedWallet.NotFound);
        }

        return Result<TrackedWalletDetails>.Success(TrackedWalletDetails.FromEntity(trackedWallet));
    }
}
