using Application.Abstractions;
using Domain.Common;
using Domain.Entities;
using Domain.ValueObjects;

namespace Infrastructure.Caching;

/// <summary>
/// Resolves display labels from the hot in-memory cache, ENS fallback, and a safe short-address fallback.
/// </summary>
public sealed class AddressLabelResolver(
    IAddressLabelCacheService cache,
    IEnsReverseLookupProvider ensReverseLookupProvider)
    : IAddressLabelResolver
{
    public async Task<AddressLabelResolution> ResolveAsync(
        SupportedChain chain,
        Address address,
        CancellationToken cancellationToken)
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

        if (chain == SupportedChain.Ethereum)
        {
            var ensName = await ensReverseLookupProvider.ResolveNameAsync(address, cancellationToken);
            if (!string.IsNullOrWhiteSpace(ensName))
            {
                return new AddressLabelResolution(
                    address,
                    chain,
                    ensName,
                    IsLabeled: true,
                    AddressLabelKind.Ens,
                    AddressLabelSource.EnsReverse);
            }
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
