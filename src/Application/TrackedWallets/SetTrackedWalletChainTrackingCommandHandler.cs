using Application.Abstractions;
using Domain.Common;
using MediatR;

namespace Application.TrackedWallets;

/// <summary>
/// Applies chain filter toggles while preserving the domain's empty-list-means-all convention.
/// </summary>
public sealed class SetTrackedWalletChainTrackingCommandHandler(
    ITrackedWalletRepository trackedWalletRepository,
    ITrackedWalletCacheService trackedWalletCache)
    : IRequestHandler<SetTrackedWalletChainTrackingCommand, Result<TrackedWalletDetails>>
{
    /// <summary>
    /// Verifies ownership, toggles one chain through the aggregate, persists, and refreshes cache state.
    /// </summary>
    public async Task<Result<TrackedWalletDetails>> Handle(
        SetTrackedWalletChainTrackingCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trackedWallet = await trackedWalletRepository.GetByIdAsync(request.WalletId, cancellationToken);
        if (trackedWallet is null || trackedWallet.UserId != request.TelegramUserId)
        {
            return Result<TrackedWalletDetails>.Failure(Errors.TrackedWallet.NotFound);
        }

        var trackingResult = trackedWallet.SetChainTracking(request.Chain, request.Enabled);
        if (trackingResult.IsFailure)
        {
            return Result<TrackedWalletDetails>.Failure(trackingResult.Error);
        }

        await trackedWalletRepository.SaveChangesAsync(cancellationToken);
        trackedWalletCache.Add(trackedWallet);
        return Result<TrackedWalletDetails>.Success(TrackedWalletDetails.FromEntity(trackedWallet));
    }
}
