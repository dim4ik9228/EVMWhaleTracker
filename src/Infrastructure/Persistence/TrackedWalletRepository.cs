using Application.Abstractions;
using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class TrackedWalletRepository : ITrackedWalletRepository
{
    private readonly AppDbContext _dbContext;

    public TrackedWalletRepository(AppDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<TrackedWallet>> ListAllAsync(CancellationToken cancellationToken)
    {
        var wallets = await _dbContext.TrackedWallets
            .AsNoTracking()
            .OrderBy(wallet => wallet.UserId)
            .ThenBy(wallet => wallet.Address)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return wallets;
    }

    public async Task<IReadOnlyList<TrackedWallet>> ListByUserIdAsync(long userId, CancellationToken cancellationToken)
    {
        var wallets = await _dbContext.TrackedWallets
            .AsNoTracking()
            .Where(wallet => wallet.UserId == userId)
            .OrderBy(wallet => wallet.Address)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return wallets;
    }

    public Task<TrackedWallet?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => _dbContext.TrackedWallets
            .AsNoTracking()
            .FirstOrDefaultAsync(wallet => wallet.Id == id, cancellationToken);

    public Task<TrackedWallet?> GetByUserIdAndAddressAsync(long userId, Address address, CancellationToken cancellationToken)
        => _dbContext.TrackedWallets
            .AsNoTracking()
            .FirstOrDefaultAsync(wallet => wallet.UserId == userId && wallet.Address == address, cancellationToken);

    public Task AddAsync(TrackedWallet trackedWallet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trackedWallet);

        _dbContext.TrackedWallets.Add(trackedWallet);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(TrackedWallet trackedWallet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trackedWallet);

        _dbContext.TrackedWallets.Remove(trackedWallet);
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _dbContext.SaveChangesAsync(cancellationToken);
}
