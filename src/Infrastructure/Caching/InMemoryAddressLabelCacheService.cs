using Application.Abstractions;
using Domain.Common;
using Domain.Entities;
using Domain.ValueObjects;

namespace Infrastructure.Caching;

/// <summary>
/// Thread-safe in-memory address-label lookup used by notification rendering.
/// </summary>
public sealed class InMemoryAddressLabelCacheService : IAddressLabelCacheService, IDisposable
{
    private readonly Dictionary<(SupportedChain Chain, Address Address), AddressLabelCacheEntry> _chainScopedLabels = new();
    private readonly Dictionary<Address, AddressLabelCacheEntry> _globalLabels = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    public void ReplaceAll(IEnumerable<AddressLabelCacheEntry> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        _lock.EnterWriteLock();
        try
        {
            _chainScopedLabels.Clear();
            _globalLabels.Clear();

            foreach (var label in labels)
            {
                if (label.Chain.HasValue)
                {
                    _chainScopedLabels[(label.Chain.Value, label.Address)] = label;
                }
                else
                {
                    _globalLabels[label.Address] = label;
                }
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void ReplaceAll(IEnumerable<AddressLabel> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        ReplaceAll(labels.Select(AddressLabelCacheEntry.FromEntity));
    }

    public bool TryGet(SupportedChain chain, Address address, out AddressLabelCacheEntry label)
    {
        ArgumentNullException.ThrowIfNull(address);

        _lock.EnterReadLock();
        try
        {
            if (_chainScopedLabels.TryGetValue((chain, address), out label!))
            {
                return true;
            }

            return _globalLabels.TryGetValue(address, out label!);
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
