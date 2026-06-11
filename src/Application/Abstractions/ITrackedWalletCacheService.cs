using Domain.Entities;
using Domain.Common;
using Domain.ValueObjects;

namespace Application.Abstractions;

/// <summary>
/// Hot-path read model for a tracked wallet, including user-specific notification settings.
/// </summary>
public sealed record TrackedWalletCacheEntry(
    Guid Id,
    long TelegramUserId,
    Address Address,
    decimal MinimumUsdValue,
    bool TrackIncomingTransfers,
    bool TrackOutgoingTransfers,
    bool TrackTokenTransfers,
    IReadOnlyCollection<SupportedChain> EnabledChains,
    string? Alias)
{
    /// <summary>
    /// Projects the domain aggregate into the in-memory matching model.
    /// </summary>
    public static TrackedWalletCacheEntry FromEntity(TrackedWallet trackedWallet)
        => new(
            trackedWallet.Id,
            trackedWallet.UserId,
            trackedWallet.Address,
            trackedWallet.MinimumUsdValue,
            trackedWallet.TrackIncomingTransfers,
            trackedWallet.TrackOutgoingTransfers,
            trackedWallet.TrackTokenTransfers,
            trackedWallet.EnabledChains.ToArray(),
            trackedWallet.Alias);
}

/// <summary>
/// In-memory tracked-wallet lookup used by blockchain workers to avoid database calls on the hot path.
/// </summary>
public interface ITrackedWalletCacheService : ITrackedAddressCacheService
{
    void ReplaceAll(IEnumerable<TrackedWallet> trackedWallets);

    bool Add(TrackedWallet trackedWallet);

    bool Remove(TrackedWallet trackedWallet);

    bool TryGetTelegramUserIds(Address address, out IReadOnlyCollection<long> telegramUserIds);

    bool TryGetEntries(Address address, out IReadOnlyCollection<TrackedWalletCacheEntry> entries);
}
