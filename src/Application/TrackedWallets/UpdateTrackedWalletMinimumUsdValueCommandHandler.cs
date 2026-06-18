using Application.Abstractions;
using Domain.Common;
using MediatR;

namespace Application.TrackedWallets;

/// <summary>
/// Applies minimum USD threshold changes and refreshes the hot-path cache entry.
/// </summary>
public sealed class UpdateTrackedWalletMinimumUsdValueCommandHandler(
    ITrackedWalletRepository trackedWalletRepository,
    ITrackedWalletCacheService trackedWalletCache)
    : IRequestHandler<UpdateTrackedWalletMinimumUsdValueCommand, Result<TrackedWalletDetails>>
{
    /// <summary>
    /// Verifies wallet ownership, updates the threshold aggregate field, persists, and refreshes cache state.
    /// </summary>
    public async Task<Result<TrackedWalletDetails>> Handle(
        UpdateTrackedWalletMinimumUsdValueCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trackedWallet = await trackedWalletRepository.GetByIdAsync(request.WalletId, cancellationToken);
        if (trackedWallet is null || trackedWallet.UserId != request.TelegramUserId)
        {
            return Result<TrackedWalletDetails>.Failure(Errors.TrackedWallet.NotFound);
        }

        var minimumResult = trackedWallet.UpdateMinimumUsdValue(request.MinimumUsdValue);
        if (minimumResult.IsFailure)
        {
            return Result<TrackedWalletDetails>.Failure(minimumResult.Error);
        }

        await trackedWalletRepository.SaveChangesAsync(cancellationToken);
        trackedWalletCache.Add(trackedWallet);
        return Result<TrackedWalletDetails>.Success(TrackedWalletDetails.FromEntity(trackedWallet));
    }
}
