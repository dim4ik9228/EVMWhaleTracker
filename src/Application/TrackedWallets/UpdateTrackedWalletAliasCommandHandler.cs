using Application.Abstractions;
using Domain.Common;
using MediatR;

namespace Application.TrackedWallets;

/// <summary>
/// Applies tracked-wallet alias changes and refreshes the hot-path cache entry.
/// </summary>
public sealed class UpdateTrackedWalletAliasCommandHandler(
    ITrackedWalletRepository trackedWalletRepository,
    ITrackedWalletCacheService trackedWalletCache)
    : IRequestHandler<UpdateTrackedWalletAliasCommand, Result<TrackedWalletDetails>>
{
    /// <summary>
    /// Verifies wallet ownership, updates the alias aggregate field, persists, and refreshes cache state.
    /// </summary>
    public async Task<Result<TrackedWalletDetails>> Handle(
        UpdateTrackedWalletAliasCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trackedWallet = await trackedWalletRepository.GetByIdAsync(request.WalletId, cancellationToken);
        if (trackedWallet is null || trackedWallet.UserId != request.TelegramUserId)
        {
            return Result<TrackedWalletDetails>.Failure(Errors.TrackedWallet.NotFound);
        }

        var aliasResult = trackedWallet.UpdateAlias(request.Alias);
        if (aliasResult.IsFailure)
        {
            return Result<TrackedWalletDetails>.Failure(aliasResult.Error);
        }

        await trackedWalletRepository.SaveChangesAsync(cancellationToken);
        trackedWalletCache.Add(trackedWallet);
        return Result<TrackedWalletDetails>.Success(TrackedWalletDetails.FromEntity(trackedWallet));
    }
}
