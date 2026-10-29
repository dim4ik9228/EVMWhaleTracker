using System.Collections.ObjectModel;
using Application.Abstractions;
using Application.Billing;
using Domain.Entities;
using Domain.ValueObjects;

namespace Infrastructure.Caching;

/// <summary>
/// Thread-safe in-memory lookup table for tracked addresses and wallet-specific notification settings.
/// </summary>
public sealed class InMemoryCacheService(IEntitlementPolicy? policy = null) : ITrackedWalletCacheService, IDisposable
{
    private readonly HashSet<Address> _trackedAddresses = [];
    private readonly Dictionary<Address, Dictionary<Guid, TrackedWalletCacheEntry>> _trackedWalletEntries = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private TaskCompletionSource _changeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _version;

    public long Version
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _version;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

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

            SignalChangedCore();
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
                AddCore(trackedWallet, out _);
            }

            SignalChangedCore();
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
            var added = _trackedAddresses.Add(address);
            if (added)
            {
                SignalChangedCore();
            }

            return added;
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
            var added = AddCore(trackedWallet, out var addressAdded);
            if (addressAdded)
            {
                SignalChangedCore();
            }

            return added;
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
            var removed = _trackedAddresses.Remove(address);
            if (removed)
            {
                SignalChangedCore();
            }

            return removed;
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
                    SignalChangedCore();
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
                entries = cachedEntries.Values.Where(x => policy?.Allows(x.Id, TrackingMode.Evm) != false).Select(x => x with { EntitlementRevision = policy?.Get(x.Id)?.Revision, EntitlementValidUntil = policy?.Get(x.Id)?.ValidUntil }).ToArray();
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
            return new ReadOnlyCollection<Address>(_trackedAddresses.Where(a => policy?.EnforcementEnabled != true || _trackedWalletEntries.TryGetValue(a, out var es) && es.Values.Any(e => policy.Allows(e.Id, TrackingMode.Evm))).ToArray());
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public Task WaitForChangeAsync(long observedVersion, CancellationToken cancellationToken)
    {
        _lock.EnterReadLock();
        try
        {
            if (_version != observedVersion)
            {
                return Task.CompletedTask;
            }

            return _changeSignal.Task.WaitAsync(cancellationToken);
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

    private bool AddCore(TrackedWallet trackedWallet, out bool addressAdded)
    {
        if (policy?.Allows(trackedWallet.Id, TrackingMode.Evm) == false) { addressAdded = false; return false; }
        addressAdded = _trackedAddresses.Add(trackedWallet.Address);
        if (!_trackedWalletEntries.TryGetValue(trackedWallet.Address, out var entries))
        {
            entries = [];
            _trackedWalletEntries[trackedWallet.Address] = entries;
        }

        var wasNew = !entries.ContainsKey(trackedWallet.Id);
        entries[trackedWallet.Id] = TrackedWalletCacheEntry.FromEntity(trackedWallet);
        return wasNew;
    }

    private void SignalChangedCore()
    {
        _version++;
        var previousSignal = _changeSignal;
        _changeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        previousSignal.TrySetResult();
    }
}
