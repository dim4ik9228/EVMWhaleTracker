using System.Text.Json;
using Application.Polymarket;
using Domain.Common;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Polymarket;

/// <summary>Explicit owner-safe source binding and default-off preferences. Tracking stays fail-closed until provider proof.</summary>
public sealed class PolymarketSettings(AppDbContext db, IPolymarketProfileResolver profiles, IOptions<PolymarketOptions> options,
    IOptions<PolymarketPolygonOptions> polygon, IPolymarketPolygonRpc rpc, PolymarketPolygonHealth health) : IPolymarketSettings
{
    public const string ProviderGate = "Needs attention — activity provider verification is blocked; tracking and delivery are unavailable.";
    private const long AdmissionLock = 74220001;
    public async Task<Result<PolymarketWalletStatus>> GetAsync(long userId, Guid walletId, CancellationToken ct)
    {
        var wallet = await db.TrackedWallets.AsNoTracking().SingleOrDefaultAsync(w => w.Id == walletId && w.UserId == userId, ct);
        if (wallet is null) return Failure("Wallet not found.");
        var previous = await db.Set<PolymarketSetting>().AsNoTracking().SingleOrDefaultAsync(x => x.WalletId == walletId, ct);
        var previousProfile = previous?.ProfileJson is null ? null : JsonSerializer.Deserialize<PolymarketProfile>(previous.ProfileJson);
        var lookup = await profiles.ResolveAsync(previousProfile?.ResolvedAddress ?? wallet.Address.Value, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({AdmissionLock})", ct);
        if (!await db.TrackedWallets.AnyAsync(w => w.Id == walletId && w.UserId == userId, ct)) return Failure("Wallet not found.");
        var s = await db.Set<PolymarketSetting>().SingleOrDefaultAsync(x => x.WalletId == walletId, ct);
        if (s is not null) await db.Entry(s).ReloadAsync(ct);
        // Creation or rebind during this lookup makes its identity snapshot obsolete.
        if (s is not null && (previous is null || previous.Generation != s.Generation))
            return Result<PolymarketWalletStatus>.Success(Status(wallet, s));
        if (s is null) { s = new() { WalletId = walletId, Generation = Guid.NewGuid() }; db.Add(s); }
        if (!lookup.Stale && lookup.State is "Found" or "Missing")
        {
            var proposed = lookup.Profile?.TradingAddress;
            if (s.ProposedAddress != proposed) s.Generation = Guid.NewGuid();
            s.ProfileJson = lookup.Profile is null ? null : JsonSerializer.Serialize(lookup.Profile);
            s.ProposedAddress = proposed;
            if (!s.NeedsAttention && s.SourceAddress is not null && s.SourceAddress != proposed)
            {
                s.NeedsAttention = true;
                s.Enabled = false;
                s.Generation = Guid.NewGuid();
                await CancelAsync(walletId, ct);
            }
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        var status = Status(wallet, s, lookup.State);
        if (s.Enabled && s.SourceAddress is not null)
        {
            var checkpoint = await db.Set<PolymarketCheckpoint>().AsNoTracking().SingleOrDefaultAsync(x => x.SourceAddress == s.SourceAddress, ct);
            if (checkpoint?.Gap is not null) status = status with { Status = "Degraded — " + checkpoint.Gap };
            else if (checkpoint?.Status == "Recovering") status = status with { Status = "Recovering — Polygon history is being reconciled." };
        }
        return Result<PolymarketWalletStatus>.Success(status);
    }
    public async Task<Result<PolymarketWalletStatus>> BindAsync(long userId, Guid walletId, string address, Guid expectedGeneration, CancellationToken ct)
    {
        if (!await db.TrackedWallets.AnyAsync(w => w.Id == walletId && w.UserId == userId, ct)) return Failure("Wallet not found.");
        var normalized = Address.Normalize(address);
        if (normalized.IsFailure) return Failure("Supply a valid EVM trading-wallet address.");
        var source = normalized.Value.Value.ToLowerInvariant();
        var verified = await profiles.ResolveAsync(source, ct);
        if (verified.State != "Found" || verified.Stale || verified.Profile?.TradingAddress != source)
            return Failure("Trading-wallet mapping could not be verified. Try again after the public profile lookup recovers.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({AdmissionLock})", ct);
        var wallet = await db.TrackedWallets.SingleOrDefaultAsync(w => w.Id == walletId && w.UserId == userId, ct);
        if (wallet is null) return Failure("Wallet not found.");
        var s = await db.Set<PolymarketSetting>().SingleOrDefaultAsync(x => x.WalletId == walletId, ct);
        if (s is not null) await db.Entry(s).ReloadAsync(ct);
        if (s is null || s.Generation != expectedGeneration) return Failure("This setting changed. Refresh the Polymarket menu.");
        if (await (from setting in db.Set<PolymarketSetting>() join w in db.TrackedWallets on setting.WalletId equals w.Id
            where w.UserId == userId && w.Id != walletId && setting.SourceAddress == source select setting).AnyAsync(ct))
            return Failure("Another wallet in your watchlist is already bound to this Polymarket account.");
        // Suggested bindings retain the original profile identity so provider remapping is observable.
        // A separately supplied verified account becomes the identity refreshed on subsequent visits.
        if (source != s.ProposedAddress || s.ProfileJson is null)
            s.ProfileJson = JsonSerializer.Serialize(verified.Profile);
        s.SourceAddress = source; s.ProposedAddress = source; s.NeedsAttention = false; s.Enabled = false;
        s.Generation = Guid.NewGuid(); s.ActivatedAt = DateTimeOffset.UtcNow;
        await CancelAsync(walletId, ct); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Result<PolymarketWalletStatus>.Success(Status(wallet, s));
    }
    public async Task<Result<PolymarketWalletStatus>> SetAsync(long userId, Guid walletId, bool enabled, Guid expectedGeneration, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({AdmissionLock})", ct);
        var wallet = await db.TrackedWallets.SingleOrDefaultAsync(w => w.Id == walletId && w.UserId == userId, ct);
        if (wallet is null) return Failure("Wallet not found.");
        var s = await db.Set<PolymarketSetting>().SingleOrDefaultAsync(x => x.WalletId == walletId, ct);
        if (s is not null) await db.Entry(s).ReloadAsync(ct);
        if (s is null || s.Generation != expectedGeneration) return Failure("This setting changed. Refresh the Polymarket menu.");
        if (enabled)
        {
            if (!options.Value.Enabled || !polygon.Value.Enabled) return Failure("Polygon tracking is paused; profile lookup remains available.");
            if (s.SourceAddress is null || s.NeedsAttention) return Failure("Confirm the proposed trading account before enabling tracking.");
            var count = await db.Set<PolymarketSetting>().Where(x => x.Enabled && x.SourceAddress != null).Select(x => x.SourceAddress).Distinct().CountAsync(ct);
            if (count >= polygon.Value.MaxUniqueAddresses && !await db.Set<PolymarketSetting>().AnyAsync(x => x.Enabled && x.SourceAddress == s.SourceAddress, ct))
                return Failure("Polygon tracking capacity is full. Existing accounts remain tracked.");
            try
            {
                await rpc.VerifyChainAsync(ct);
                // A sealed latest block is the precise enrollment boundary: all later blocks are future activity,
                // including logs mined in the next block while another follower already watches the account.
                s.ActivationBlock = await rpc.HeadAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is InvalidDataException or HttpRequestException or IOException or OperationCanceledException)
            { return Failure("Polygon provider unavailable or access denied. Tracking was not enabled; try again when the source recovers."); }
            s.ActivatedAt = DateTimeOffset.UtcNow;
        }
        s.Enabled = enabled; s.Generation = Guid.NewGuid();
        await CancelAsync(walletId, ct); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Result<PolymarketWalletStatus>.Success(Status(wallet, s));
    }
    public async Task<Result<PolymarketWalletStatus>> MinimumAsync(long userId, Guid walletId, decimal minimum, Guid expectedGeneration, CancellationToken ct)
    {
        if (minimum < 0 || minimum > 1000000000000m) return Failure("Send a non-negative Polymarket minimum up to 1,000,000,000,000 collateral units.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({AdmissionLock})", ct);
        var wallet = await db.TrackedWallets.SingleOrDefaultAsync(w => w.Id == walletId && w.UserId == userId, ct);
        if (wallet is null) return Failure("Wallet not found.");
        var s = await db.Set<PolymarketSetting>().SingleOrDefaultAsync(x => x.WalletId == walletId, ct);
        if (s is not null) await db.Entry(s).ReloadAsync(ct);
        if (s is null || s.Generation != expectedGeneration) return Failure("This setting changed. Refresh the Polymarket menu.");
        s.MinimumTradeValue = minimum;
        s.Generation = Guid.NewGuid();
        await CancelAsync(walletId, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Result<PolymarketWalletStatus>.Success(Status(wallet, s));
    }
    private Task<int> CancelAsync(Guid walletId, CancellationToken ct) => db.Set<PolymarketDelivery>()
        .Where(x => x.WalletId == walletId && x.State == "Pending").ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "Cancelled"), ct);
    private PolymarketWalletStatus Status(TrackedWallet w, PolymarketSetting s, string? lookup = null) => new(s.Enabled, s.Generation,
        w.Address.Value, s.SourceAddress, s.ProposedAddress, s.MinimumTradeValue,
        !options.Value.Enabled ? "Off — service paused" : s.NeedsAttention ? "Needs attention — profile mapping changed. Confirm a rebind."
        : lookup == "Unavailable" ? "Resolving — profile temporarily unavailable; existing binding preserved."
        : lookup == "Missing" ? "Resolving — no public Polymarket profile found."
        : s.ProposedAddress is null ? "Resolving — trading account unresolved."
        : !polygon.Value.Enabled ? "Off — Polygon tracking paused; profile available."
        : !s.Enabled ? "Off — enable to follow future finalized settlements."
        : health.Status.StartsWith("Off — no enabled accounts", StringComparison.Ordinal)
            ? "Starting — connecting the confirmed trading account."
            : health.Status,
        s.ProfileJson is null ? null : JsonSerializer.Deserialize<PolymarketProfile>(s.ProfileJson));
    private static Result<PolymarketWalletStatus> Failure(string text) => Result<PolymarketWalletStatus>.Failure(new Error("Polymarket.Settings", text));
}
