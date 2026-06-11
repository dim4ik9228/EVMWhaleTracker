using Domain.Entities;
using Domain.Common;
using Domain.ValueObjects;

namespace Application.Abstractions;

public sealed record TrackedWalletCacheEntry(
    Guid Id,
    long TelegramUserId,
    Address Address,
    decimal MinimumTransactionValue,
    bool TrackIncomingTransfers,
    bool TrackOutgoingTransfers,
    bool TrackTokenTransfers,
    IReadOnlyCollection<SupportedChain> EnabledChains,
    string? Alias)
{
    public static TrackedWalletCacheEntry FromEntity(TrackedWallet trackedWallet)
        => new(
            trackedWallet.Id,
            trackedWallet.UserId,
            trackedWallet.Address,
            trackedWallet.MinimumTransactionValue,
            trackedWallet.TrackIncomingTransfers,
            trackedWallet.TrackOutgoingTransfers,
            trackedWallet.TrackTokenTransfers,
            trackedWallet.EnabledChains.ToArray(),
            trackedWallet.Alias);
}

public interface ITrackedWalletCacheService : ITrackedAddressCacheService
{
    void ReplaceAll(IEnumerable<TrackedWallet> trackedWallets);

    bool Add(TrackedWallet trackedWallet);

    bool Remove(TrackedWallet trackedWallet);

    bool TryGetTelegramUserIds(Address address, out IReadOnlyCollection<long> telegramUserIds);

    bool TryGetEntries(Address address, out IReadOnlyCollection<TrackedWalletCacheEntry> entries);
}
