using Application.Abstractions;
using Domain.Common;
using MediatR;

namespace Application.TrackedWallets;

/// <summary>
/// Deletes tracked wallets from inline Telegram settings flows and evicts them from cache.
/// </summary>
public sealed class RemoveTrackedWalletByIdCommandHandler(
    ITrackedWalletRepository trackedWalletRepository,
    ITrackedWalletCacheService trackedWalletCache)
    : IRequestHandler<RemoveTrackedWalletByIdCommand, Result>
{
    /// <summary>
    /// Verifies wallet ownership, deletes the aggregate, persists, and removes the cache entry.
    /// </summary>
    public async Task<Result> Handle(RemoveTrackedWalletByIdCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trackedWallet = await trackedWalletRepository.GetByIdAsync(request.WalletId, cancellationToken);
        if (trackedWallet is null || trackedWallet.UserId != request.TelegramUserId)
        {
            return Result.Failure(Errors.TrackedWallet.NotFound);
        }

        await trackedWalletRepository.RemoveAsync(trackedWallet, cancellationToken);
        await trackedWalletRepository.SaveChangesAsync(cancellationToken);
        trackedWalletCache.Remove(trackedWallet);
        return Result.Success();
    }
}
