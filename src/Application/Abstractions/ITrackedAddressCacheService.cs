using Domain.ValueObjects;

namespace Application.Abstractions;

public interface ITrackedAddressCacheService
{
    void ReplaceAll(IEnumerable<Address> addresses);

    bool Add(Address address);

    bool Remove(Address address);

    bool Contains(Address address);

    IReadOnlyCollection<Address> Snapshot();
}
