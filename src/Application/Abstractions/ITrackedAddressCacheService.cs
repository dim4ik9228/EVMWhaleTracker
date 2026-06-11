using Domain.ValueObjects;

namespace Application.Abstractions;

/// <summary>
/// Address-only snapshot used by blockchain subscribers to construct wallet-specific filters.
/// </summary>
public interface ITrackedAddressCacheService
{
    void ReplaceAll(IEnumerable<Address> addresses);

    bool Add(Address address);

    bool Remove(Address address);

    bool Contains(Address address);

    IReadOnlyCollection<Address> Snapshot();
}
