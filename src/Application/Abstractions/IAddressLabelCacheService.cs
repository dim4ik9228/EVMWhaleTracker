using Domain.Common;
using Domain.Entities;
using Domain.ValueObjects;

namespace Application.Abstractions;

public sealed record AddressLabelCacheEntry(
    Address Address,
    SupportedChain? Chain,
    string Label,
    AddressLabelKind Kind,
    AddressLabelSource Source,
    int Priority)
{
    public static AddressLabelCacheEntry FromEntity(AddressLabel label)
        => new(
            label.Address,
            label.Chain,
            label.Label,
            label.Kind,
            label.Source,
            label.Priority);
}

/// <summary>
/// Hot-path address label lookup with chain-specific labels preferred over global labels.
/// </summary>
public interface IAddressLabelCacheService
{
    void ReplaceAll(IEnumerable<AddressLabelCacheEntry> labels);

    void ReplaceAll(IEnumerable<AddressLabel> labels);

    bool TryGet(SupportedChain chain, Address address, out AddressLabelCacheEntry label);
}
