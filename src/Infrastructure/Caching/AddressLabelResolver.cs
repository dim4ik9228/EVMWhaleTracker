using Application.Abstractions;
using Domain.Common;
using Domain.Entities;
using Domain.ValueObjects;

namespace Infrastructure.Caching;

/// <summary>
/// Resolves display labels from the hot in-memory cache with a safe short-address fallback.
/// </summary>
public sealed class AddressLabelResolver(IAddressLabelCacheService cache) : IAddressLabelResolver
{
    public AddressLabelResolution Resolve(SupportedChain chain, Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (cache.TryGet(chain, address, out var label))
        {
            return new AddressLabelResolution(
                address,
                chain,
                label.Label,
                IsLabeled: true,
                label.Kind,
                label.Source);
        }

        return new AddressLabelResolution(
            address,
            chain,
            ShortAddress(address.Value),
            IsLabeled: false,
            AddressLabelKind.Unknown,
            AddressLabelSource.Manual);
    }

    private static string ShortAddress(string address)
        => address.Length <= 12
            ? address
            : $"{address[..6]}...{address[^4..]}";
}
