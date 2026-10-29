using Application.Abstractions;
using Application.Billing;
using Domain.Common;
using Domain.Entities;
using Domain.ValueObjects;
using MediatR;

namespace Application.TrackedWallets;

/// <summary>
/// Creates a tracked wallet, persists it, and immediately updates the hot-path cache.
/// </summary>
public sealed class AddTrackedWalletCommandHandler(
    ITrackedWalletRepository trackedWalletRepository,
    ITrackedWalletCacheService trackedWalletCache, IBillingService? billing = null)
    : IRequestHandler<AddTrackedWalletCommand, Result<TrackedWalletDetails>>
{
    public async Task<Result<TrackedWalletDetails>> Handle(AddTrackedWalletCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var addressResult = Address.Normalize(request.RawAddress);
        if (addressResult.IsFailure)
        {
            return Result<TrackedWalletDetails>.Failure(addressResult.Error);
        }

        var existingWallet = await trackedWalletRepository.GetByUserIdAndAddressAsync(
            request.TelegramUserId,
            addressResult.Value,
            cancellationToken);

        if (existingWallet is not null)
        {
            return Result<TrackedWalletDetails>.Failure(Errors.TrackedWallet.AlreadyTracked);
        }

        var createResult = TrackedWallet.Create(
            addressResult.Value,
            request.TelegramUserId,
            request.MinimumUsdValue,
            request.TrackIncomingTransfers,
            request.TrackOutgoingTransfers,
            request.TrackTokenTransfers,
            request.Alias,
            request.EnabledChains);

        if (createResult.IsFailure)
        {
            return Result<TrackedWalletDetails>.Failure(createResult.Error);
        }

        if (billing is not null)
        {
            var admission = await billing.AddWalletAsync(createResult.Value, cancellationToken);
            return admission.IsFailure ? Result<TrackedWalletDetails>.Failure(admission.Error) : Result<TrackedWalletDetails>.Success(TrackedWalletDetails.FromEntity(createResult.Value));
        }

        await trackedWalletRepository.AddAsync(createResult.Value, cancellationToken);
        await trackedWalletRepository.SaveChangesAsync(cancellationToken);

        trackedWalletCache.Add(createResult.Value);
        return Result<TrackedWalletDetails>.Success(TrackedWalletDetails.FromEntity(createResult.Value));
    }
}
