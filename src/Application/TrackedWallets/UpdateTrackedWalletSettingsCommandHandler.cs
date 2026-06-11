using Application.Abstractions;
using Domain.Common;
using Domain.ValueObjects;
using MediatR;

namespace Application.TrackedWallets;

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

        var updateResult = trackedWallet.UpdateTrackingConfiguration(
            request.MinimumTransactionValue,
            request.TrackIncomingTransfers,
            request.TrackOutgoingTransfers,
            request.TrackTokenTransfers,
            request.Alias,
            request.EnabledChains);

        if (updateResult.IsFailure)
        {
            return Result<TrackedWalletDetails>.Failure(updateResult.Error);
        }

        await trackedWalletRepository.SaveChangesAsync(cancellationToken);
        trackedWalletCache.Add(trackedWallet);
        return Result<TrackedWalletDetails>.Success(TrackedWalletDetails.FromEntity(trackedWallet));
    }
}
