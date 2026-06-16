using Application.Abstractions;
using Domain.Common;
using Domain.ValueObjects;
using MediatR;

namespace Application.TrackedWallets;

/// <summary>
/// Applies tracked-wallet setting changes and refreshes the cached entry.
/// </summary>
public sealed class UpdateTrackedWalletSettingsCommandHandler(
    ITrackedWalletRepository trackedWalletRepository,
    ITrackedWalletCacheService trackedWalletCache)
    : IRequestHandler<UpdateTrackedWalletSettingsCommand, Result<TrackedWalletDetails>>
{
    public async Task<Result<TrackedWalletDetails>> Handle(
        UpdateTrackedWalletSettingsCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var addressResult = Address.Normalize(request.RawAddress);
        if (addressResult.IsFailure)
        {
            return Result<TrackedWalletDetails>.Failure(addressResult.Error);
        }

        var trackedWallet = await trackedWalletRepository
            .GetByUserIdAndAddressAsync(request.TelegramUserId, addressResult.Value, cancellationToken);

        if (trackedWallet is null)
        {
            return Result<TrackedWalletDetails>.Failure(Errors.TrackedWallet.NotFound);
        }

        var minimumUsdValueResult = trackedWallet.UpdateMinimumUsdValue(request.MinimumUsdValue);
        if (minimumUsdValueResult.IsFailure)
        {
            return Result<TrackedWalletDetails>.Failure(minimumUsdValueResult.Error);
        }

        var transferTrackingResult = trackedWallet.UpdateTransferTracking(
            request.TrackIncomingTransfers,
            request.TrackOutgoingTransfers,
            request.TrackTokenTransfers);
        if (transferTrackingResult.IsFailure)
        {
            return Result<TrackedWalletDetails>.Failure(transferTrackingResult.Error);
        }

        if (request.Alias is not null)
        {
            var aliasResult = trackedWallet.UpdateAlias(request.Alias);
            if (aliasResult.IsFailure)
            {
                return Result<TrackedWalletDetails>.Failure(aliasResult.Error);
            }
        }

        if (request.EnabledChains is not null)
        {
            var enabledChainsResult = trackedWallet.UpdateEnabledChains(request.EnabledChains);
            if (enabledChainsResult.IsFailure)
            {
                return Result<TrackedWalletDetails>.Failure(enabledChainsResult.Error);
            }
        }

        await trackedWalletRepository.SaveChangesAsync(cancellationToken);
        trackedWalletCache.Add(trackedWallet);
        return Result<TrackedWalletDetails>.Success(TrackedWalletDetails.FromEntity(trackedWallet));
    }
}
