using Domain.Entities;
using Domain.ValueObjects;

namespace Application.Abstractions;

public interface ITrackedWalletRepository
{
    Task<IReadOnlyList<TrackedWallet>> ListAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<TrackedWallet>> ListByUserIdAsync(long userId, CancellationToken cancellationToken);

    Task<TrackedWallet?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<TrackedWallet?> GetByUserIdAndAddressAsync(long userId, Address address, CancellationToken cancellationToken);

    Task AddAsync(TrackedWallet trackedWallet, CancellationToken cancellationToken);

    Task RemoveAsync(TrackedWallet trackedWallet, CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
