using Domain.Entities;
using Domain.ValueObjects;

namespace Application.Abstractions;

public interface ITrackedWalletCacheService : ITrackedAddressCacheService
{
    void ReplaceAll(IEnumerable<TrackedWallet> trackedWallets);

    bool Add(TrackedWallet trackedWallet);

    bool Remove(TrackedWallet trackedWallet);

    bool TryGetTelegramUserIds(Address address, out IReadOnlyCollection<long> telegramUserIds);
}
