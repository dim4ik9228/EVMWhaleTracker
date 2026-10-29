using System.Text.Json;
using Application.Billing;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Infrastructure.Billing;

/// <summary>Fresh database checks ensure delayed projection workers cannot extend a paid entitlement.</summary>
public sealed class RecipientAccess(IServiceScopeFactory scopes, IOptions<BillingOptions> options, TimeProvider clock,
    IEntitlementPolicy policy) : IRecipientAccess
{
    public async Task<bool> AllowsAsync(long owner, Guid walletId, TrackingMode mode, DateTimeOffset? activityAt, long? enrollmentRevision, CancellationToken ct)
    {
        if (!options.Value.EnforcementEnabled) return true;
        if (!policy.Allows(walletId, mode, activityAt, enrollmentRevision)) return false;
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var wallet = await db.TrackedWallets.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.Id == walletId, ct);
        if (wallet is null) return false;
        var account = await db.Set<BillingAccount>().AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner, ct);
        if (account is null) return false;
        var now = clock.GetUtcNow();
        var enrollment = await db.Set<WalletAccessState>().AsNoTracking().SingleOrDefaultAsync(x => x.WalletId == walletId, ct);
        if (enrollment is null || enrollment.AccountRevision != account.Revision || !enrollment.EvmEnabled || enrollment.EvmUntil <= now
            || enrollmentRevision is { } revision && revision != enrollment.EnrollmentRevision
            || activityAt is { } timestamp && timestamp < enrollment.ActiveSince) return false;
        var pro = await db.Set<AccessGrant>().AnyAsync(x => x.UserId == owner && x.ReversedAt == null && x.StartsAt <= now && x.EndsAt > now, ct);
        var selected = (JsonSerializer.Deserialize<List<Guid>>(account.FreeWalletIds) ?? []).Contains(walletId);
        if (!pro) return selected && (mode == TrackingMode.Evm || mode == TrackingMode.Polymarket && account.FreePolymarketWalletId == walletId);
        var effective = await db.TrackedWallets.Where(x => x.UserId == owner).OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Take(BillingPlans.ProWallets).Select(x => x.Id).ToListAsync(ct);
        return effective.Contains(walletId);
    }
}
