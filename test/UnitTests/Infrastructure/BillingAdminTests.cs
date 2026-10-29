using Application.Billing;
using Domain.Entities;
using Infrastructure.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace UnitTests.Infrastructure;

public sealed partial class BillingTests
{
    private static BillingAdministration Admin(global::Infrastructure.Persistence.AppDbContext db, Clock clock, IBillingService? billing = null) =>
        new(db, Options.Create(new BillingOptions { OperatorUserIds = [99, 98] }), clock, billing ?? Service(db, clock));

    [BillingPostgresFact]
    public async Task AdminAuthorizationKnownTargetAndValidationDoNotCreateUnknownAccounts()
    {
        await Reset(); await using var db = Db(); var clock = new Clock(); var admin = Admin(db, clock);
        Assert.Equal(BillingAdminErrors.Unauthorized, (await admin.AccountAsync(2, 1, 0, default)).Error);
        Assert.Equal(BillingAdminErrors.Unauthorized, (await admin.PreviewAsync(2, 1, 24, null, "reason", default)).Error);
        Assert.Equal(BillingAdminErrors.Unknown, (await admin.AccountAsync(99, 777, 0, default)).Error);
        Assert.Equal(BillingAdminErrors.Unknown, (await admin.PreviewAsync(99, 777, 24, null, "reason", default)).Error);
        Assert.Empty(await db.Set<BillingAccount>().ToListAsync());
        db.Add(new BillingAccount { UserId = 1 }); await db.SaveChangesAsync();
        Assert.Equal(BillingAdminErrors.Invalid, (await admin.PreviewAsync(99, 1, 0, null, "reason", default)).Error);
        Assert.Equal(BillingAdminErrors.Invalid, (await admin.PreviewAsync(99, 1, 8761, null, "reason", default)).Error);
        Assert.Equal(BillingAdminErrors.Invalid, (await admin.PreviewAsync(99, 1, 24, null, new string('x', 301), default)).Error);
        Assert.Equal(BillingAdminErrors.Invalid, (await admin.PreviewAsync(99, 1, 24, null, " ", default)).Error);
        Assert.True((await admin.PreviewAsync(99, 1, 2, null, "legacy hourly", default)).IsSuccess);
        Assert.Empty(await db.Set<AccessGrant>().ToListAsync());
    }
    [BillingPostgresFact]
    public async Task AdminExtendsContiguousAccessAndConcurrentReplayCommitsOneAudit()
    {
        await Reset(); var clock = new Clock(); AdminPreview p;
        await using (var db = Db())
        {
            db.Add(new BillingAccount { UserId = 1 });
            foreach (var (start, end) in new[] { (0.0, 1.0), (0.5, 2.0), (2.0, 3.0), (10.0, 11.0) })
                db.Add(new AccessGrant { Id = Guid.NewGuid(), UserId = 1, StartsAt = clock.Now.AddDays(start), EndsAt = clock.Now.AddDays(end) });
            await db.SaveChangesAsync();
            p = (await Admin(db, clock).PreviewAsync(99, 1, 24, null, "test extension", default)).Value;
            Assert.Equal(clock.Now.AddDays(3), p.StartsAt); Assert.Equal(clock.Now.AddDays(4), p.EndsAt);
            var view = (await Admin(db, clock).AccountAsync(99, 1, 0, default)).Value;
            Assert.Equal(clock.Now.AddDays(3), view.Until);
            Assert.Equal(BillingAdminErrors.Unauthorized, (await Admin(db, clock).ConfirmAsync(2, p, default)).Error);
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ => { await using var db = Db(); return await Admin(db, clock).ConfirmAsync(99, p, default); }));
        Assert.All(results, x => Assert.True(x.IsSuccess));
        await using var check = Db(); Assert.Single(await check.Set<BillingAudit>().ToListAsync());
        var manual = await check.Set<AccessGrant>().SingleAsync(x => x.OperatorId != null); Assert.Equal(p.OperationId, manual.Id);
        Assert.Equal(5, await check.Set<AccessGrant>().CountAsync()); Assert.Empty(await check.Set<PaymentReceipt>().ToListAsync());
        Assert.Equal(clock.Now.AddDays(11), (await check.Set<AccessGrant>().OrderByDescending(x => x.EndsAt).FirstAsync()).EndsAt);
    }
    [BillingPostgresFact]
    public async Task AdminRejectsStaleExpiredAndPurchasedRevokePreservesOtherPeriods()
    {
        await Reset(); var clock = new Clock(); await using var db = Db();
        db.Add(new BillingAccount { UserId = 1 });
        var order = new PurchaseOrder { Id = Guid.NewGuid(), UserId = 1, CreatedAt = clock.Now, CheckoutExpiresAt = clock.Now.AddHours(1) };
        var paid = new AccessGrant { Id = Guid.NewGuid(), UserId = 1, OperatorId = 99, OrderId = order.Id, StartsAt = clock.Now, EndsAt = clock.Now.AddDays(30) };
        db.Add(order); db.Add(paid); await db.SaveChangesAsync();
        var admin = Admin(db, clock);
        Assert.Equal(BillingAdminErrors.ManualOnly, (await admin.PreviewAsync(99, 1, 0, paid.Id, "no", default)).Error);
        var first = (await admin.PreviewAsync(99, 1, 1, null, "first", default)).Value;
        var stale = (await admin.PreviewAsync(98, 1, 24, null, "second", default)).Value;
        Assert.True((await admin.ConfirmAsync(99, first, default)).IsSuccess);
        Assert.Equal(BillingAdminErrors.Stale, (await admin.ConfirmAsync(98, stale, default)).Error);
        var expired = (await admin.PreviewAsync(99, 1, 24, null, "expires", default)).Value;
        clock.Now = clock.Now.AddMinutes(10);
        Assert.Equal(BillingAdminErrors.Expired, (await admin.ConfirmAsync(99, expired, default)).Error);
        var revoke = (await admin.PreviewAsync(99, 1, 0, first.OperationId, "remove test", default)).Value;
        Assert.True((await admin.ConfirmAsync(99, revoke, default)).IsSuccess);
        Assert.True((await admin.ConfirmAsync(99, revoke, default)).IsSuccess);
        await db.Entry(paid).ReloadAsync(); Assert.Null(paid.ReversedAt);
        Assert.NotNull((await db.Set<AccessGrant>().SingleAsync(x => x.Id == first.OperationId)).ReversedAt);
        Assert.Equal(2, await db.Set<BillingAudit>().CountAsync());
    }
    [BillingPostgresFact]
    public async Task AdminRequiresFreshPreviewAfterAccessStartsOrExpiresWithoutProjectionRefresh()
    {
        foreach (var startsInFuture in new[] { false, true })
        {
            await Reset(); var clock = new Clock(); await using var db = Db();
            db.Add(new BillingAccount { UserId = 1 });
            var existing = new AccessGrant { Id = Guid.NewGuid(), UserId = 1,
                StartsAt = startsInFuture ? clock.Now.AddMinutes(1) : clock.Now.AddDays(-1),
                EndsAt = startsInFuture ? clock.Now.AddDays(30) : clock.Now.AddMinutes(1) };
            db.Add(existing); await db.SaveChangesAsync();
            var admin = Admin(db, clock);
            var preview = (await admin.PreviewAsync(99, 1, 24, null, "boundary preview", default)).Value;
            Assert.Equal(clock.Now, preview.ObservedAt);
            // No refresh or other mutation advances the persisted account revision.
            clock.Now = clock.Now.AddMinutes(1);
            Assert.True(clock.Now < preview.ExpiresAt);
            Assert.Equal(BillingAdminErrors.Stale, (await admin.ConfirmAsync(99, preview, default)).Error);
            Assert.Equal(0, (await db.Set<BillingAccount>().SingleAsync()).Revision);
            Assert.Single(await db.Set<AccessGrant>().ToListAsync());
            Assert.Empty(await db.Set<BillingAudit>().ToListAsync());
            Assert.Null(existing.ReversedAt);
        }
    }
    [BillingPostgresFact]
    public async Task AdminNoAccessPreviewCanConfirmAfterDelayWithoutMovingItsDates()
    {
        await Reset(); var clock = new Clock(); await using var db = Db();
        db.Add(new BillingAccount { UserId = 1 }); await db.SaveChangesAsync();
        var admin = Admin(db, clock);
        var preview = (await admin.PreviewAsync(99, 1, 24, null, "delayed confirmation", default)).Value;
        clock.Now = clock.Now.AddMinutes(2);
        Assert.True((await admin.ConfirmAsync(99, preview, default)).IsSuccess);
        var grant = await db.Set<AccessGrant>().SingleAsync();
        Assert.Equal(preview.StartsAt, grant.StartsAt); Assert.Equal(preview.EndsAt, grant.EndsAt);
        Assert.Single(await db.Set<BillingAudit>().ToListAsync());
    }
    [BillingPostgresFact]
    public async Task AdminCommittedOperationCanRecoverAfterProjectionFailure()
    {
        await Reset(); var clock = new Clock(); AdminPreview p;
        await using (var db = Db())
        {
            db.Add(new BillingAccount { UserId = 1 }); await db.SaveChangesAsync();
            var failing = DispatchProxy.Create<IBillingService, FailingProjection>();
            var admin = Admin(db, clock, failing); p = (await admin.PreviewAsync(99, 1, 24, null, "recover", default)).Value;
            await Assert.ThrowsAsync<InvalidOperationException>(() => admin.ConfirmAsync(99, p, default));
        }
        await using var recovery = Db();
        Assert.Single(await recovery.Set<AccessGrant>().ToListAsync()); Assert.Single(await recovery.Set<BillingAudit>().ToListAsync());
        clock.Now = clock.Now.AddMinutes(11);
        Assert.True((await Admin(recovery, clock).ConfirmAsync(99, p, default)).IsSuccess);
        Assert.Single(await recovery.Set<AccessGrant>().ToListAsync()); Assert.True((await recovery.Set<BillingAccount>().SingleAsync()).WasPro);
    }
    [BillingPostgresFact]
    public async Task AdminDifferentConcurrentPreviewsCannotOverwriteEachOther()
    {
        await Reset(); var clock = new Clock(); AdminPreview first, second;
        await using (var db = Db())
        {
            db.Add(new BillingAccount { UserId = 1 }); await db.SaveChangesAsync();
            first = (await Admin(db, clock).PreviewAsync(99, 1, 24, null, "one", default)).Value;
            second = (await Admin(db, clock).PreviewAsync(98, 1, 48, null, "two", default)).Value;
        }
        var results = await Task.WhenAll(new[] { first, second }.Select(async p => { await using var db = Db(); return await Admin(db, clock).ConfirmAsync(p.Actor, p, default); }));
        Assert.Single(results, x => x.IsSuccess); Assert.Single(results, x => x.IsFailure && x.Error == BillingAdminErrors.Stale);
        await using var check = Db(); Assert.Single(await check.Set<AccessGrant>().ToListAsync()); Assert.Single(await check.Set<BillingAudit>().ToListAsync());
    }
    [BillingPostgresFact]
    public async Task AdminBrowsingAndPreviewDoNotPersistAccountsForTrackingOnlyUsers()
    {
        await Reset(); var clock = new Clock(); await using var db = Db();
        db.Add(Wallet(1)); await db.SaveChangesAsync();
        Assert.True((await Admin(db, clock).AccountAsync(99, 1, 0, default)).IsSuccess);
        var preview = (await Admin(db, clock).PreviewAsync(99, 1, 24, null, "preview only", default)).Value;
        Assert.Empty(await db.Set<BillingAccount>().ToListAsync());
        Assert.Empty(await db.Set<BillingAudit>().ToListAsync()); Assert.Empty(await db.Set<AccessGrant>().ToListAsync());
        Assert.True((await Admin(db, clock).ConfirmAsync(99, preview, default)).IsSuccess);
        Assert.Single(await db.Set<BillingAccount>().ToListAsync());
    }
    [BillingPostgresFact]
    public async Task ExpertRevokeRetainsPaidGrantCompatibilityWithoutRefundOrOtherPeriodChanges()
    {
        await Reset(); var clock = new Clock(); await using var db = Db();
        db.Add(new BillingAccount { UserId = 1 });
        var order = new PurchaseOrder { Id = Guid.NewGuid(), UserId = 1, CreatedAt = clock.Now, CheckoutExpiresAt = clock.Now.AddHours(1) };
        var paid = new AccessGrant { Id = Guid.NewGuid(), UserId = 1, OrderId = order.Id, StartsAt = clock.Now, EndsAt = clock.Now.AddDays(30) };
        var later = new AccessGrant { Id = Guid.NewGuid(), UserId = 1, StartsAt = clock.Now.AddDays(30), EndsAt = clock.Now.AddDays(60) };
        db.Add(order); db.Add(paid); db.Add(later); await db.SaveChangesAsync(); var admin = Admin(db, clock);
        Assert.Equal(BillingAdminErrors.ManualOnly, (await admin.PreviewAsync(99, 1, 0, paid.Id, "panel disallowed", default)).Error);
        Assert.Equal(BillingAdminErrors.Unauthorized, (await admin.RevokeExpertAsync(2, 1, paid.Id, "unauthorized", default)).Error);
        Assert.Equal(BillingAdminErrors.Invalid, (await admin.RevokeExpertAsync(99, 1, paid.Id, " ", default)).Error);
        Assert.True((await admin.RevokeExpertAsync(99, 1, paid.Id, "explicit expert correction", default)).IsSuccess);
        Assert.True((await admin.RevokeExpertAsync(99, 1, paid.Id, "retry", default)).IsSuccess);
        Assert.NotNull(paid.ReversedAt); Assert.Null(later.ReversedAt); Assert.Equal(clock.Now.AddDays(30), later.StartsAt);
        Assert.Single(await db.Set<BillingAudit>().ToListAsync()); Assert.Empty(await db.Set<PaymentReceipt>().ToListAsync());
    }
    public class FailingProjection : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new InvalidOperationException("Synthetic projection failure");
    }
}
