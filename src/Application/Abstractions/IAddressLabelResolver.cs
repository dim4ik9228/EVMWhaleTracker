using Domain.Common;
using Domain.Entities;
using Domain.ValueObjects;

namespace Application.Abstractions;

public sealed record AddressLabelResolution(
    Address Address,
    SupportedChain Chain,
    string DisplayText,
    bool IsLabeled,
    AddressLabelKind Kind,
    AddressLabelSource Source);

public interface IAddressLabelResolver
{
    Task<AddressLabelResolution> ResolveAsync(
        SupportedChain chain,
        Address address,
        CancellationToken cancellationToken);
}
