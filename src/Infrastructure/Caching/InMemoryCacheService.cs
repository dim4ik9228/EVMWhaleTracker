using System.Collections.ObjectModel;
using Application.Abstractions;
using Domain.Entities;
using Domain.ValueObjects;

namespace Infrastructure.Caching;

public sealed class InMemoryCacheService : ITrackedWalletCacheService, IDisposable
{
    private readonly HashSet<Address> _trackedAddresses = [];
    private readonly Dictionary<Address, HashSet<long>> _trackedWalletOwners = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    public void ReplaceAll(IEnumerable<Address> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        _lock.EnterWriteLock();
        try
        {
            _trackedAddresses.Clear();
            _trackedWalletOwners.Clear();
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

    public void ReplaceAll(IEnumerable<TrackedWallet> trackedWallets)
    {
        ArgumentNullException.ThrowIfNull(trackedWallets);

        _lock.EnterWriteLock();
        try
        {
            _trackedAddresses.Clear();
            _trackedWalletOwners.Clear();

            foreach (var trackedWallet in trackedWallets)
            {
                _trackedAddresses.Add(trackedWallet.Address);

                if (!_trackedWalletOwners.TryGetValue(trackedWallet.Address, out var owners))
                {
                    owners = [];
                    _trackedWalletOwners[trackedWallet.Address] = owners;
                }

                owners.Add(trackedWallet.UserId);
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

    public bool Add(TrackedWallet trackedWallet)
    {
        ArgumentNullException.ThrowIfNull(trackedWallet);

        _lock.EnterWriteLock();
        try
        {
            _trackedAddresses.Add(trackedWallet.Address);
            if (!_trackedWalletOwners.TryGetValue(trackedWallet.Address, out var owners))
            {
                owners = [];
                _trackedWalletOwners[trackedWallet.Address] = owners;
            }

            return owners.Add(trackedWallet.UserId);
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

    public bool Remove(TrackedWallet trackedWallet)
    {
        ArgumentNullException.ThrowIfNull(trackedWallet);

        _lock.EnterWriteLock();
        try
        {
            var removed = false;
            if (_trackedWalletOwners.TryGetValue(trackedWallet.Address, out var owners))
            {
                removed = owners.Remove(trackedWallet.UserId);
                if (owners.Count == 0)
                {
                    _trackedWalletOwners.Remove(trackedWallet.Address);
                    _trackedAddresses.Remove(trackedWallet.Address);
                }
            }

            return removed;
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

    public bool TryGetTelegramUserIds(Address address, out IReadOnlyCollection<long> telegramUserIds)
    {
        ArgumentNullException.ThrowIfNull(address);

        _lock.EnterReadLock();
        try
        {
            if (_trackedWalletOwners.TryGetValue(address, out var userIds))
            {
                telegramUserIds = userIds.ToArray();
                return true;
            }

            telegramUserIds = Array.Empty<long>();
            return false;
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
