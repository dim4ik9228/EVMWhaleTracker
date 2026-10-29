using Application.Hyperliquid;
using Domain.Common;
using Application.Billing;
using Infrastructure.Billing;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Hyperliquid;

/// <summary>PostgreSQL transaction lock serializes global distinct-address admission across processes.</summary>
public sealed class HyperliquidSettings(AppDbContext db, IOptions<HyperliquidOptions> options, HyperliquidChangeSignal signal, IBillingService? billing = null) : IHyperliquidSettings
{
    internal const long AdmissionLock = 74210001;
    public async Task<Result<HyperliquidWalletStatus>> GetAsync(long userId, Guid walletId, CancellationToken ct)
    {
        if (!await db.TrackedWallets.AnyAsync(x => x.Id == walletId && x.UserId == userId, ct)) return Result<HyperliquidWalletStatus>.Failure(HyperliquidErrors.WalletNotFound);
        var setting = await db.Set<HyperliquidSetting>().AsNoTracking().SingleOrDefaultAsync(x => x.WalletId == walletId, ct);
        return Result<HyperliquidWalletStatus>.Success(Status(setting));
    }
    public async Task<Result<HyperliquidWalletStatus>> SetAsync(long userId, Guid walletId, bool enabled, CancellationToken ct, Guid? expectedGeneration = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await BillingService.LockOwnerAsync(db, userId, ct);
        await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({AdmissionLock})", ct);
        // A paused host can release stale slots only while excluding every active ingestion owner.
        // Keep this transaction lock until admission/settings have committed, so a new owner cannot
        // open a socket using an address set captured before cleanup.
        var canReleasePaused = !options.Value.Enabled && await db.Database
            .SqlQueryRaw<bool>($"SELECT pg_try_advisory_xact_lock({HyperliquidWorker.OwnerLock}) AS \"Value\"").SingleAsync(ct);
        if (canReleasePaused) await ReleasePausedReservationsAsync(ct);
        var wallet = await db.TrackedWallets.SingleOrDefaultAsync(x => x.Id == walletId && x.UserId == userId, ct);
        if (wallet is null) return Result<HyperliquidWalletStatus>.Failure(HyperliquidErrors.WalletNotFound);
        var setting = await db.Set<HyperliquidSetting>().SingleOrDefaultAsync(x => x.WalletId == walletId, ct);
        if (enabled && billing is not null) { var access = await billing.CanEnableAsync(userId, walletId, TrackingMode.Hyperliquid, ct); if (access.IsFailure) return Result<HyperliquidWalletStatus>.Failure(access.Error); }
        if (setting?.Enabled == enabled && !setting.BillingPaused) return Result<HyperliquidWalletStatus>.Success(Status(setting));
        if (expectedGeneration is { } expected && expected != (setting?.Generation ?? Guid.Empty))
            return Result<HyperliquidWalletStatus>.Failure(HyperliquidErrors.StaleMenu);
        if (enabled)
        {
            var address = wallet.Address.Value.ToLowerInvariant();
            if (!await db.Set<HyperliquidReservation>().AnyAsync(x => x.Address == address, ct))
            {
                // Disabled configuration does not undergo startup validation, but can never exceed the public-IP cap.
                if (await db.Set<HyperliquidReservation>().CountAsync(ct) >= Math.Clamp(options.Value.MaxUniqueWallets, 1, 10))
                    return Result<HyperliquidWalletStatus>.Failure(HyperliquidErrors.Capacity);
                db.Add(new HyperliquidReservation { Address = address });
            }
        }
        if (setting is null) { setting = new HyperliquidSetting { WalletId = walletId }; db.Add(setting); }
        setting.Enabled = enabled; setting.BillingPaused = false; setting.BillingPauseReason = null;
        setting.Generation = Guid.NewGuid();
        setting.ActivatedAt = DateTimeOffset.UtcNow;
        await db.Set<HyperliquidDelivery>().Where(x => x.WalletId == walletId && x.State == RuntimeStates.Delivery.Pending).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, RuntimeStates.Delivery.Cancelled), ct);
        await db.SaveChangesAsync(ct);
        if (canReleasePaused) await ReleasePausedReservationsAsync(ct);
        await tx.CommitAsync(ct);
        signal.Changed();
        if (billing is not null) await billing.RefreshAsync(userId, ct);
        return Result<HyperliquidWalletStatus>.Success(Status(setting));
    }
    private HyperliquidWalletStatus Status(HyperliquidSetting? s) => new(s?.Enabled ?? false, s?.Generation ?? Guid.Empty,
        s?.Enabled != true ? RuntimeStates.Profile.Off : s.BillingPaused ? s.BillingPauseReason ?? BillingDiagnostics.ProRequired : !options.Value.Enabled ? HyperliquidSettingsDiagnostics.DegradedServicePaused : signal.Status);

    private async Task ReleasePausedReservationsAsync(CancellationToken ct)
    {
        var addresses = (await (from s in db.Set<HyperliquidSetting>() join w in db.TrackedWallets on s.WalletId equals w.Id
            where s.Enabled && !s.BillingPaused select w.Address).ToListAsync(ct)).Select(a => a.Value.ToLowerInvariant()).Distinct().ToList();
        var stale = await db.Set<HyperliquidReservation>().Where(x => !addresses.Contains(x.Address)).ToListAsync(ct);
        db.RemoveRange(stale);
        await db.SaveChangesAsync(ct);
    }
}
