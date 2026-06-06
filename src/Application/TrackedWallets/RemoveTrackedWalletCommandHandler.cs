using Application.Abstractions;
using Domain.Common;
using Domain.ValueObjects;
using MediatR;

namespace Application.TrackedWallets;

public sealed class RemoveTrackedWalletCommandHandler(
    ITrackedWalletRepository trackedWalletRepository,
    ITrackedWalletCacheService trackedWalletCache)
    : IRequestHandler<RemoveTrackedWalletCommand, Result>
{
    public async Task<Result> Handle(RemoveTrackedWalletCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var addressResult = Address.Normalize(request.RawAddress);
        if (addressResult.IsFailure)
        {
            return Result.Failure(addressResult.Error);
        }

        var trackedWallet = await trackedWalletRepository.GetByUserIdAndAddressAsync(
            request.TelegramUserId,
            addressResult.Value,
            cancellationToken);

        if (trackedWallet is null)
        {
            return Result.Failure(Errors.TrackedWallet.NotFound);
        }

        await trackedWalletRepository.RemoveAsync(trackedWallet, cancellationToken);
        await trackedWalletRepository.SaveChangesAsync(cancellationToken);
        trackedWalletCache.Remove(trackedWallet);

        return Result.Success();
    }
}
