using System.Collections.ObjectModel;
using Application.Abstractions;
using Domain.ValueObjects;

namespace Infrastructure.Caching;

public sealed class InMemoryCacheService : ITrackedAddressCacheService, IDisposable
{
    private readonly HashSet<Address> _trackedAddresses = [];
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    public void ReplaceAll(IEnumerable<Address> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        _lock.EnterWriteLock();
        try
        {
            _trackedAddresses.Clear();
            foreach (var address in addresses)
            {
                _trackedAddresses.Add(address);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool Add(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        _lock.EnterWriteLock();
        try
        {
            return _trackedAddresses.Add(address);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool Remove(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        _lock.EnterWriteLock();
        try
        {
            return _trackedAddresses.Remove(address);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool Contains(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        _lock.EnterReadLock();
        try
        {
            return _trackedAddresses.Contains(address);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public IReadOnlyCollection<Address> Snapshot()
    {
        _lock.EnterReadLock();
        try
        {
            return new ReadOnlyCollection<Address>(_trackedAddresses.ToArray());
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
