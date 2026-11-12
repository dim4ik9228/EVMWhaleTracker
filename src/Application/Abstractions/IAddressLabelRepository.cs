using Domain.Entities;
using Domain.ValueObjects;
using Domain.Common;

namespace Application.Abstractions;

/// <summary>
/// Persistence port for durable address labels.
/// </summary>
public interface IAddressLabelRepository
{
    Task<IReadOnlyList<AddressLabel>> GetEnabledAsync(CancellationToken cancellationToken);

    Task<AddressLabel?> GetByAddressAndChainAsync(
        Address address,
        SupportedChain? chain,
        CancellationToken cancellationToken);

    Task UpsertAsync(AddressLabel label, CancellationToken cancellationToken);

    /// <summary>Disables only the identified enabled row; returns false when missing or already disabled.</summary>
    Task<bool> DisableByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
