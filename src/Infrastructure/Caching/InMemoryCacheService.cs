using System.Collections.ObjectModel;
using Application.Abstractions;
using Domain.Entities;
using Domain.ValueObjects;

namespace Infrastructure.Caching;

/// <summary>
/// Thread-safe in-memory lookup table for tracked addresses and wallet-specific notification settings.
/// </summary>
public sealed class InMemoryCacheService : ITrackedWalletCacheService, IDisposable
{
    private readonly HashSet<Address> _trackedAddresses = [];
    private readonly Dictionary<Address, Dictionary<Guid, TrackedWalletCacheEntry>> _trackedWalletEntries = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    public void ReplaceAll(IEnumerable<Address> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        _lock.EnterWriteLock();
        try
        {
            _trackedAddresses.Clear();
            _trackedWalletEntries.Clear();
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
            _trackedWalletEntries.Clear();

            foreach (var trackedWallet in trackedWallets)
            {
                AddCore(trackedWallet);
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
            return AddCore(trackedWallet);
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
            _trackedWalletEntries.Remove(address);
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
            if (_trackedWalletEntries.TryGetValue(trackedWallet.Address, out var entries))
            {
                removed = entries.Remove(trackedWallet.Id);
                if (entries.Count == 0)
                {
                    _trackedWalletEntries.Remove(trackedWallet.Address);
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
            if (_trackedWalletEntries.TryGetValue(address, out var entries))
            {
                telegramUserIds = entries.Values
                    .Select(entry => entry.TelegramUserId)
                    .Distinct()
                    .ToArray();
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

    public bool TryGetEntries(Address address, out IReadOnlyCollection<TrackedWalletCacheEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(address);

        _lock.EnterReadLock();
        try
        {
            if (_trackedWalletEntries.TryGetValue(address, out var cachedEntries))
            {
                entries = cachedEntries.Values.ToArray();
                return true;
            }

            entries = Array.Empty<TrackedWalletCacheEntry>();
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

    private bool AddCore(TrackedWallet trackedWallet)
    {
        _trackedAddresses.Add(trackedWallet.Address);
        if (!_trackedWalletEntries.TryGetValue(trackedWallet.Address, out var entries))
        {
            entries = [];
            _trackedWalletEntries[trackedWallet.Address] = entries;
        }

        var wasNew = !entries.ContainsKey(trackedWallet.Id);
        entries[trackedWallet.Id] = TrackedWalletCacheEntry.FromEntity(trackedWallet);
        return wasNew;
    }
}
