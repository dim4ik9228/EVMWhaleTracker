using Application.Billing;
using Domain.Common;
using System.Text.Json;
using Application.Hyperliquid;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Hyperliquid;

/// <summary>Atomic source deduplication, fan-out and checkpoint persistence under the single ingestion lease.</summary>
public sealed class HyperliquidStore(AppDbContext db, HyperliquidChangeSignal? signal = null, IOptions<HyperliquidOptions>? options = null, IEntitlementPolicy? entitlement = null)
{
    private static readonly SemaphoreSlim AcceptGate = new(1);
    public async Task<List<string>> AddressesAsync(CancellationToken ct) => (await (from s in db.Set<HyperliquidSetting>()
        join w in db.TrackedWallets on s.WalletId equals w.Id where s.Enabled && !s.BillingPaused select w).ToListAsync(ct))
        .Where(w => entitlement?.Allows(w.Id, TrackingMode.Hyperliquid) != false).Select(w => w.Address)
        .Select(a => a.Value.ToLowerInvariant()).Distinct().Order().ToList();

    public async Task AcceptAsync(HyperliquidActivity activity, bool baseline, CancellationToken ct)
    {
        await AcceptGate.WaitAsync(ct);
        try
        {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (await db.Set<HyperliquidEvent>().AnyAsync(x => x.Id == activity.Id, ct)) { signal?.DuplicateEvent(); return; }
        db.Add(new HyperliquidEvent { Id = activity.Id, Address = activity.Address, Stream = activity.Stream,
            Time = activity.Time, Payload = JsonSerializer.Serialize(activity), StoredAt = DateTimeOffset.UtcNow });
        var owners = await (from s in db.Set<HyperliquidSetting>() join w in db.TrackedWallets on s.WalletId equals w.Id
            where s.Enabled && !s.BillingPaused select new { Setting = s, Wallet = w }).ToListAsync(ct);
        foreach (var owner in owners.Where(o => o.Wallet.Address.Value.Equals(activity.Address, StringComparison.OrdinalIgnoreCase)))
        {
            if (entitlement?.Allows(owner.Wallet.Id, TrackingMode.Hyperliquid) == false || baseline || activity.LiquidatedVictim || activity.Time < owner.Setting.ActivatedAt.ToUnixTimeMilliseconds()
                || activity.Usd is { } usd && usd < owner.Wallet.MinimumUsdValue) continue;
            db.Add(new HyperliquidDelivery { Id = Guid.NewGuid(), EventId = activity.Id, WalletId = owner.Wallet.Id,
                UserId = owner.Wallet.UserId, Generation = owner.Setting.Generation,
                DueAt = DateTimeOffset.FromUnixTimeMilliseconds(
                    (activity.Time / (options?.Value.AggregationMilliseconds ?? 1000) + 1)
                    * (options?.Value.AggregationMilliseconds ?? 1000)) });
        }
        // Completeness is advanced by recovery only after every bounded page is committed.
        // A live high timestamp or a partly committed page must never skip older unsaved work.
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        signal?.AcceptedEvent();
        }
        finally { AcceptGate.Release(); }
    }

    public async Task CompleteAsync(string address, string stream, long boundary, CancellationToken ct)
    {
        var key = address + "|" + stream;
        var c = await db.Set<HyperliquidCheckpoint>().FindAsync([key], ct);
        if (c is null) { c = new HyperliquidCheckpoint { Key = key }; db.Add(c); }
        c.Time = Math.Max(c.Time, boundary);
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkGapAsync(string address, string stream, string reason, CancellationToken ct)
    {
        var key = address + "|" + stream;
        var c = await db.Set<HyperliquidCheckpoint>().FindAsync([key], ct);
        if (c is null) { c = new HyperliquidCheckpoint { Key = key }; db.Add(c); }
        c.Gap = reason;
        await db.SaveChangesAsync(ct);
    }

    public async Task ReleaseUnusedReservationsAsync(CancellationToken ct)
    {
        // Only call after the previous socket has been disposed. Admission uses this same lock.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({HyperliquidSettings.AdmissionLock})", ct);
        var addresses = await AddressesAsync(ct);
        var stale = await db.Set<HyperliquidReservation>().Where(x => !addresses.Contains(x.Address)).ToListAsync(ct);
        db.RemoveRange(stale);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task PruneAsync(int retentionDays, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        await db.Set<HyperliquidDelivery>().Where(x => x.State != RuntimeStates.Delivery.Pending && x.DueAt < cutoff).ExecuteDeleteAsync(ct);
        await db.Set<HyperliquidEvent>().Where(x => x.StoredAt < cutoff && !db.Set<HyperliquidDelivery>().Any(d => d.EventId == x.Id)).ExecuteDeleteAsync(ct);
    }
}
