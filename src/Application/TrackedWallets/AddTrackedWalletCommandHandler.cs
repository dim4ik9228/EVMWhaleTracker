using Application.Abstractions;
using Domain.Common;
using Domain.Entities;
using Domain.ValueObjects;
using MediatR;

namespace Application.TrackedWallets;

public sealed class AddTrackedWalletCommandHandler(
    ITrackedWalletRepository trackedWalletRepository,
    ITrackedWalletCacheService trackedWalletCache)
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
            request.MinimumTransactionValue,
            request.TrackIncomingTransfers,
            request.TrackOutgoingTransfers,
            request.TrackTokenTransfers);

        if (createResult.IsFailure)
        {
            return Result<TrackedWalletDetails>.Failure(createResult.Error);
        }

        await trackedWalletRepository.AddAsync(createResult.Value, cancellationToken);
        await trackedWalletRepository.SaveChangesAsync(cancellationToken);

        trackedWalletCache.Add(createResult.Value);
        return Result<TrackedWalletDetails>.Success(TrackedWalletDetails.FromEntity(createResult.Value));
    }
}
