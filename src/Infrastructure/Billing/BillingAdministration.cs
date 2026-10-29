using Application.Billing;
using Domain.Common;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Billing;

/// <summary>Manual ledger operations share the purchase owner lock and durable audit identity.</summary>
public sealed class BillingAdministration(AppDbContext db, IOptions<BillingOptions> options, TimeProvider clock,
    IBillingService billing) : IBillingAdministration
{
    public bool IsOperator(long actor) => actor > 0 && options.Value.IsOperator(actor);
    private async Task<bool> Known(long target, CancellationToken ct) => target > 0 &&
        (await db.Set<BillingAccount>().AnyAsync(x => x.UserId == target, ct) || await db.TrackedWallets.AnyAsync(x => x.UserId == target, ct));
    private static bool Manual(AccessGrant g) => g.OperatorId != null && g.OrderId == null && g.ReceiptId == null;
    private async Task<BillingAccount> Account(long target, CancellationToken ct)
    {
        var a = await db.Set<BillingAccount>().SingleOrDefaultAsync(x => x.UserId == target, ct);
        if (a == null) { a = new() { UserId = target }; db.Add(a); }
        else await db.Entry(a).ReloadAsync(ct);
        return a;
    }
    public async Task<Result<AdminAccount>> AccountAsync(long actor, long target, int page, CancellationToken ct)
    {
        if (!IsOperator(actor)) return Result<AdminAccount>.Failure(BillingAdminErrors.Unauthorized);
        if (!await Known(target, ct)) return Result<AdminAccount>.Failure(BillingAdminErrors.Unknown);
        page = Math.Clamp(page, 0, 1000);
        var now = clock.GetUtcNow();
        var revision = await db.Set<BillingAccount>().Where(x => x.UserId == target).Select(x => x.Revision).SingleOrDefaultAsync(ct);
        var periods = await db.Set<AccessGrant>().AsNoTracking().Where(x => x.UserId == target && x.ReversedAt == null && x.EndsAt > now)
            .OrderBy(x => x.StartsAt).Take(1001).ToListAsync(ct);
        if (periods.Count > 1000) return Result<AdminAccount>.Failure(BillingAdminErrors.Stale);
        DateTimeOffset? active = null;
        foreach (var period in periods)
            if (period.StartsAt <= (active ?? now) && period.EndsAt > (active ?? now)) active = period.EndsAt;
        var grants = await db.Set<AccessGrant>().AsNoTracking().Where(x => x.UserId == target && x.OperatorId != null && x.OrderId == null && x.ReceiptId == null)
            .OrderByDescending(x => x.StartsAt).ThenBy(x => x.Id).Skip(page * 5).Take(6).ToListAsync(ct);
        return Result<AdminAccount>.Success(new(target, revision, active != null, active, grants.Take(5).ToList(), page, grants.Count > 5));
    }
    public async Task<Result<AdminPreview>> PreviewAsync(long actor, long target, int hours, Guid? revoke, string reason, CancellationToken ct)
    {
        if (!IsOperator(actor)) return Result<AdminPreview>.Failure(BillingAdminErrors.Unauthorized);
        if (!await Known(target, ct)) return Result<AdminPreview>.Failure(BillingAdminErrors.Unknown);
        reason = reason.Trim();
        if (reason.Length is < 1 or > 300 || revoke == null && (hours < 1 || hours > 365 * 24))
            return Result<AdminPreview>.Failure(BillingAdminErrors.Invalid);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await BillingService.LockOwnerAsync(db, target, ct);
        var revision = await db.Set<BillingAccount>().Where(x => x.UserId == target).Select(x => x.Revision).SingleOrDefaultAsync(ct);
        var now = clock.GetUtcNow(); var start = now; var end = now;
        if (revoke is { } id)
        {
            var g = await db.Set<AccessGrant>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == target, ct);
            if (g == null || !Manual(g) || g.ReversedAt != null) return Result<AdminPreview>.Failure(BillingAdminErrors.ManualOnly);
            start = g.StartsAt; end = g.EndsAt;
        }
        else
        {
            var grants = await db.Set<AccessGrant>().AsNoTracking().Where(x => x.UserId == target && x.ReversedAt == null && x.EndsAt > now)
                .OrderBy(x => x.StartsAt).Take(1001).ToListAsync(ct);
            if (grants.Count > 1000) return Result<AdminPreview>.Failure(BillingAdminErrors.Stale);
            foreach (var g in grants) if (g.StartsAt <= start && g.EndsAt > start) start = g.EndsAt;
            end = start.AddHours(hours);
        }
        await tx.CommitAsync(ct);
        return Result<AdminPreview>.Success(new(Guid.NewGuid(), actor, target, revision, hours, revoke, reason, start, end, now.AddMinutes(10), now));
    }
    public async Task<Result> ConfirmAsync(long actor, AdminPreview p, CancellationToken ct)
    {
        if (!IsOperator(actor) || actor != p.Actor) return Result.Failure(BillingAdminErrors.Unauthorized);
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await BillingService.LockOwnerAsync(db, p.Target, ct);
            var audit = await db.Set<BillingAudit>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == p.OperationId, ct);
            var action = p.Revoke == null ? BillingStates.Grant : BillingStates.Revoke;
            var reference = p.Revoke?.ToString("N") ?? p.OperationId.ToString("N");
            if (audit != null)
            {
                if (audit.ActorId != actor || audit.UserId != p.Target || audit.Action != action || audit.Reference != reference || audit.Reason != p.Reason)
                    return Result.Failure(BillingAdminErrors.Stale);
            }
            else
            {
                var now = clock.GetUtcNow();
                if (p.OperationId == Guid.Empty || p.ExpiresAt <= now) return Result.Failure(BillingAdminErrors.Expired);
                if (!await Known(p.Target, ct)) return Result.Failure(BillingAdminErrors.Unknown);
                var a = await Account(p.Target, ct);
                if (a.Revision != p.Revision) return Result.Failure(BillingAdminErrors.Stale);
                // Time alone can change access before the projection worker advances the account revision.
                if (p.ObservedAt > now || await db.Set<AccessGrant>().AsNoTracking().AnyAsync(g => g.UserId == p.Target && g.ReversedAt == null &&
                    (g.StartsAt > p.ObservedAt && g.StartsAt <= now || g.EndsAt > p.ObservedAt && g.EndsAt <= now), ct))
                    return Result.Failure(BillingAdminErrors.Stale);
                if (string.IsNullOrWhiteSpace(p.Reason) || p.Reason.Length > 300) return Result.Failure(BillingAdminErrors.Invalid);
                if (p.Revoke is { } id)
                {
                    var g = await db.Set<AccessGrant>().SingleOrDefaultAsync(x => x.Id == id && x.UserId == p.Target, ct);
                    if (g == null || !Manual(g) || g.ReversedAt != null) return Result.Failure(BillingAdminErrors.ManualOnly);
                    g.ReversedAt = clock.GetUtcNow();
                }
                else
                {
                    if ((p.Hours < 1 || p.Hours > 365 * 24) || p.EndsAt != p.StartsAt.AddHours(p.Hours)) return Result.Failure(BillingAdminErrors.Invalid);
                    db.Add(new AccessGrant { Id = p.OperationId, UserId = p.Target, OperatorId = actor, StartsAt = p.StartsAt, EndsAt = p.EndsAt, Reason = p.Reason });
                }
                db.Add(new BillingAudit { Id = p.OperationId, ActorId = actor, UserId = p.Target, Action = action, Reference = reference, Reason = p.Reason, At = clock.GetUtcNow() });
                a.Revision++; await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        // Replays also retry projection work after a previous post-commit failure. The periodic projection worker is the recovery backstop.
        await billing.RefreshAsync(p.Target, ct);
        return Result.Success();
    }

    public async Task<Result> RevokeExpertAsync(long actor, long target, Guid grant, string reason, CancellationToken ct)
    {
        if (!IsOperator(actor)) return Result.Failure(BillingAdminErrors.Unauthorized);
        if (!await Known(target, ct)) return Result.Failure(BillingAdminErrors.Unknown);
        reason = reason.Trim();
        if (reason.Length is < 1 or > 300) return Result.Failure(BillingAdminErrors.Invalid);
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await BillingService.LockOwnerAsync(db, target, ct);
            var g = await db.Set<AccessGrant>().SingleOrDefaultAsync(x => x.Id == grant && x.UserId == target, ct);
            if (g == null) return Result.Failure(BillingAdminErrors.Invalid);
            await db.Entry(g).ReloadAsync(ct);
            if (g.ReversedAt == null)
            {
                var account = await Account(target, ct);
                g.ReversedAt = clock.GetUtcNow(); account.Revision++;
                db.Add(new BillingAudit { Id = Guid.NewGuid(), ActorId = actor, UserId = target, Action = BillingStates.Revoke,
                    Reference = grant.ToString("N"), Reason = reason, At = clock.GetUtcNow() });
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        await billing.RefreshAsync(target, ct);
        return Result.Success();
    }
}
