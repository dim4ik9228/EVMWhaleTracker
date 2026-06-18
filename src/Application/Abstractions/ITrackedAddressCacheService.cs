using Domain.ValueObjects;

namespace Application.Abstractions;

/// <summary>
/// Address-only snapshot used by blockchain subscribers to construct wallet-specific filters.
/// </summary>
public interface ITrackedAddressCacheService
{
    /// <summary>
    /// Monotonically increasing version of the tracked address set.
    /// </summary>
    long Version { get; }

    void ReplaceAll(IEnumerable<Address> addresses);

    bool Add(Address address);

    bool Remove(Address address);

    bool Contains(Address address);

    IReadOnlyCollection<Address> Snapshot();

    /// <summary>
    /// Completes when the tracked address set changes after the observed version.
    /// </summary>
    Task WaitForChangeAsync(long observedVersion, CancellationToken cancellationToken);
}
