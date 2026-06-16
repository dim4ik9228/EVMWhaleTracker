using Application.Abstractions;
using Domain.Common;
using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of address-label persistence.
/// </summary>
public sealed class AddressLabelRepository(AppDbContext dbContext) : IAddressLabelRepository
{
    public async Task<IReadOnlyList<AddressLabel>> GetEnabledAsync(CancellationToken cancellationToken)
    {
        var labels = await dbContext.AddressLabels
            .AsNoTracking()
            .Where(label => label.IsEnabled)
            .OrderByDescending(label => label.Priority)
            .ThenBy(label => label.Label)
            .ToListAsync(cancellationToken);

        return labels;
    }

    public Task<AddressLabel?> GetByAddressAndChainAsync(
        Address address,
        SupportedChain? chain,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);

        return dbContext.AddressLabels
            .AsNoTracking()
            .FirstOrDefaultAsync(
                label => label.Address == address && label.Chain == chain,
                cancellationToken);
    }

    public async Task UpsertAsync(AddressLabel label, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(label);

        var existing = await dbContext.AddressLabels
            .FirstOrDefaultAsync(
                persisted => persisted.Address == label.Address && persisted.Chain == label.Chain,
                cancellationToken);

        if (existing is null)
        {
            dbContext.AddressLabels.Add(label);
            return;
        }

        var updateResult = existing.Update(
            label.Label,
            label.Kind,
            label.Source,
            label.SourceReference,
            label.Priority,
            label.IsEnabled);

        if (updateResult.IsFailure)
        {
            throw new InvalidOperationException(updateResult.Error.Description);
        }
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => dbContext.SaveChangesAsync(cancellationToken);
}
