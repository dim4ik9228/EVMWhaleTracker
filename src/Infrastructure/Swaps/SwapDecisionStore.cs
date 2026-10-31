using System.Data.Common;
using System.Text.Json;
using Application.Abstractions;
using Application.Messaging;
using Application.Swaps;
using Domain.Entities;
using Domain.Common;
using Infrastructure.Blockchain;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Swaps;

/// <summary>PostgreSQL source of truth for intake, fenced decisions and their delivery outbox.</summary>
public sealed class SwapDecisionStore(IServiceScopeFactory scopes, IOptions<SwapEnrichmentOptions> options,
    ITrackedWalletCacheService cache, ILogger<SwapDecisionStore> logger) : ISwapCandidateIntake, ISwapDeliveryGuard
{
    private readonly SwapEnrichmentOptions _options = options.Value;
    private readonly Erc20TransferLogParser _parser = new();
    public bool IsEnabled(SupportedChain chain) => _options.IsEnabled(chain);
    public async Task ObserveAsync(RawLogData log, RawBlockData block, CancellationToken cancellationToken)
    {
        if (block.Chain != SupportedChain.Ethereum || !ValidHash(log.TransactionHash) || !ValidHash(block.BlockHash)
            || !string.Equals(log.BlockHash, block.BlockHash, StringComparison.OrdinalIgnoreCase) || log.LogIndex is null) return;
        log = log with { TransactionHash = log.TransactionHash.ToLowerInvariant(), BlockHash = block.BlockHash.ToLowerInvariant() };
        block = block with { BlockHash = block.BlockHash.ToLowerInvariant() };
        if (!_parser.TryParse(log, block.Chain, out var transfer)) return;
        var recipients = new List<TrackedWalletCacheEntry>();
        if (cache.TryGetEntries(transfer.FromAddress, out var from)) recipients.AddRange(from);
        if (cache.TryGetEntries(transfer.ToAddress, out var to)) recipients.AddRange(to);
        recipients = recipients.DistinctBy(x => x.Id).ToList();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (await TryObserveAsync(log, block, recipients, cancellationToken)) return;
            }
            catch (Exception ex) when (ex is DbException or DbUpdateException)
            { logger.LogWarning(SwapDiagnostics.StoreUnavailable); }
            await Task.Delay(250, cancellationToken);
        }
    }
    private async Task<bool> TryObserveAsync(RawLogData log, RawBlockData block, List<TrackedWalletCacheEntry> entries, CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(db, ct);
        var hash = log.TransactionHash.ToLowerInvariant(); var chain = (int)block.Chain; var now = DateTimeOffset.UtcNow;
        var rows = await db.Set<SwapDecision>().Where(x => x.Chain == chain && x.TransactionHash == hash).ToListAsync(ct);
        if (log.Removed)
        {
            foreach (var row in rows.Where(x => x.BlockHash == block.BlockHash))
            { row.State = row.SentAt is null ? SwapStates.Invalidated : SwapStates.SentInvalidated; row.PayloadJson = null; row.LeaseOwner = null; row.LeaseUntil = null; row.Reason = SwapStates.Removed; }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true;
        }
        var ids = entries.Select(x => x.Id).ToArray();
        var wallets = await db.TrackedWallets.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        var eligible = wallets.Where(x => x.TrackTokenTransfers && (x.EnabledChains.Count == 0 || x.EnabledChains.Contains(block.Chain))).ToArray();
        var newCount = eligible.Count(x => !rows.Any(row => row.WalletId == x.Id) || rows.Any(row => row.WalletId == x.Id && row.State == SwapStates.Invalidated));
        var pending = await db.Set<SwapDecision>().CountAsync(x => x.Chain == chain && (x.State == SwapStates.Candidate || x.State == SwapStates.Outbox), ct);
        if (pending + newCount > _options.PendingCapacity) return false;
        var admitted = await db.Set<SwapDecision>().Where(x => x.Chain == chain && x.State == SwapStates.Candidate && x.Reason == "")
            .Select(x => new { x.TransactionHash, x.BlockHash }).Distinct().CountAsync(ct);
        foreach (var wallet in eligible)
        {
            var row = rows.SingleOrDefault(x => x.WalletId == wallet.Id);
            if (row is not null && row.SentAt is not null) continue;
            if (row is not null && row.State != SwapStates.Candidate && row.State != SwapStates.Invalidated) continue;
            var entry = entries.Single(x => x.Id == wallet.Id);
            if (row is null)
            {
                var firstObserved = log.ObservedAtUtc is { } observed && observed < now ? observed : now;
                row = new SwapDecision { Id = Guid.NewGuid(), Chain = chain, TransactionHash = hash, WalletId = wallet.Id,
                    UserId = wallet.UserId, WalletAddress = wallet.Address.Value, FirstObservedAt = firstObserved,
                    Deadline = firstObserved + _options.DecisionDeadline, ExpiresAt = now + _options.Retention,
                    WalletRevision = wallet.UpdatedAtUtc ?? wallet.CreatedAtUtc,
                    EntitlementRevision = entry.EntitlementRevision, EntitlementValidUntil = entry.EntitlementValidUntil,
                    Reason = admitted >= _options.AdmissionCapacity && !rows.Any(x => x.State == SwapStates.Candidate && x.Reason == "") ? SwapStates.AdmissionFull : "" };
                db.Add(row); rows.Add(row);
            }
            if (row.BlockHash != block.BlockHash)
            {
                row.ObservationsJson = "[]"; row.BlockHash = block.BlockHash; row.BlockNumber = block.BlockNumber;
                row.State = SwapStates.Candidate; row.PayloadJson = null; row.LeaseOwner = null; row.LeaseUntil = null;
                // Keep the original absolute deadline on replay and reinclusion.
            }
            var logs = JsonSerializer.Deserialize<List<RawLogData>>(row.ObservationsJson)!;
            if (logs.All(x => x.LogIndex != log.LogIndex) && logs.Count < 256)
            { logs.Add(log); row.ObservationsJson = JsonSerializer.Serialize(logs); }
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true;
    }
    public async Task<SwapDecision?> ClaimAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAsync(db, ct);
        var now = DateTimeOffset.UtcNow;
        var row = await db.Set<SwapDecision>().Where(x => x.State == SwapStates.Candidate && (x.LeaseUntil == null || x.LeaseUntil < now))
            .OrderBy(x => x.FirstObservedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (row is null) return null;
        row.LeaseOwner = Guid.NewGuid(); row.LeaseUntil = now + TimeSpan.FromSeconds(15);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return row;
    }
    public async Task<bool> DecideAsync(SwapDecision claimed, AggregatedNotificationPayload? payload, string reason, CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAsync(db, ct);
        var row = await db.Set<SwapDecision>().SingleAsync(x => x.Id == claimed.Id, ct);
        if (row.State != SwapStates.Candidate || row.LeaseOwner != claimed.LeaseOwner || row.LeaseUntil <= DateTimeOffset.UtcNow || row.BlockHash != claimed.BlockHash) return false;
        row.State = reason == SwapErrors.NonCanonical.Code ? SwapStates.Invalidated : payload is null ? SwapStates.Suppressed : SwapStates.Outbox;
        row.PayloadJson = payload is null ? null : JsonSerializer.Serialize(payload with { SwapDecisionId = row.Id });
        row.Reason = reason; row.LeaseOwner = null; row.LeaseUntil = null;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        logger.LogDebug(SwapDiagnostics.Decision, row.Id, reason, (DateTimeOffset.UtcNow - row.FirstObservedAt).TotalMilliseconds);
        return true;
    }
    public async Task<AggregatedNotificationPayload?> ClaimDeliveryAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockAsync(db, ct);
        var now = DateTimeOffset.UtcNow;
        var row = await db.Set<SwapDecision>().Where(x => x.State == SwapStates.Outbox && (x.LeaseUntil == null || x.LeaseUntil < now))
            .OrderBy(x => x.FirstObservedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (row is null) return null;
        row.LeaseOwner = Guid.NewGuid(); row.LeaseUntil = now + TimeSpan.FromMinutes(2);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return JsonSerializer.Deserialize<AggregatedNotificationPayload>(row.PayloadJson!)! with { DeliveryLease = row.LeaseOwner };
    }
    public async Task ReleaseDeliveryAsync(AggregatedNotificationPayload payload, CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Set<SwapDecision>().Where(x => x.Id == payload.SwapDecisionId && x.LeaseOwner == payload.DeliveryLease)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseOwner, (Guid?)null).SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null), ct);
    }
    public async Task<bool> AllowsAsync(AggregatedNotificationPayload payload, CancellationToken cancellationToken)
    {
        if (payload.Chain != SupportedChain.Ethereum) return payload.SwapDecisionId is null;
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Rollback keeps durable identities authoritative. An ordinary replay must not race
        // the fallback outbox or repeat a terminal decision after enrichment is disabled.
        if (payload.SwapDecisionId is null)
        {
            var hash = payload.TransactionHash.ToLowerInvariant();
            return !await db.Set<SwapDecision>().AnyAsync(x => x.Chain == (int)payload.Chain && x.TransactionHash == hash
                && x.UserId == payload.TelegramUserId && x.WalletId == payload.TrackedWalletId, cancellationToken);
        }
        var row = await db.Set<SwapDecision>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == payload.SwapDecisionId, cancellationToken);
        if (row is null || row.State != SwapStates.Outbox || row.UserId != payload.TelegramUserId || row.WalletId != payload.TrackedWalletId
            || row.Chain != (int)payload.Chain || row.TransactionHash != payload.TransactionHash || row.WalletAddress != payload.TrackedWalletAddress
            || row.LeaseOwner != payload.DeliveryLease || row.LeaseUntil <= DateTimeOffset.UtcNow) return false;
        var wallet = await db.TrackedWallets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == row.WalletId && x.UserId == row.UserId, cancellationToken);
        return wallet is not null && wallet.Address.Value == row.WalletAddress && (wallet.UpdatedAtUtc ?? wallet.CreatedAtUtc) == row.WalletRevision
            && wallet.TrackTokenTransfers && (wallet.EnabledChains.Count == 0 || wallet.EnabledChains.Contains(payload.Chain));
    }
    public async Task CompleteAsync(AggregatedNotificationPayload payload, bool sent, CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        // An expired queued lease is a retryable ownership failure, not a policy decision.
        // A successful send may still be acknowledged while this same owner holds the row.
        await db.Set<SwapDecision>().Where(x => x.Id == payload.SwapDecisionId && x.LeaseOwner == payload.DeliveryLease && x.State == SwapStates.Outbox
                && (sent || x.LeaseUntil > now))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, sent ? SwapStates.Sent : SwapStates.Suppressed)
                .SetProperty(x => x.SentAt, sent ? DateTimeOffset.UtcNow : (DateTimeOffset?)null)
                .SetProperty(x => x.LeaseOwner, (Guid?)null).SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null), ct);
    }
    public async Task<SwapDecision?> ReloadAsync(SwapDecision claimed, CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<SwapDecision>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == claimed.Id
            && x.State == SwapStates.Candidate && x.LeaseOwner == claimed.LeaseOwner && x.BlockHash == claimed.BlockHash, ct);
    }
    public async Task PruneAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        await db.Set<SwapDecision>().Where(x => x.ExpiresAt < now && x.State != SwapStates.Candidate && x.State != SwapStates.Outbox).ExecuteDeleteAsync(ct);
    }
    private static Task LockAsync(AppDbContext db, CancellationToken ct) => db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74240001)", ct);
    private static bool ValidHash(string value) => value.Length == 66 && value.StartsWith("0x") && value[2..].All(Uri.IsHexDigit);
}
/// <summary>Persisted swap lifecycle and reason tokens.</summary>
public static class SwapStates
{
    public const string Candidate = "candidate", Outbox = "outbox", Suppressed = "suppressed", Sent = "sent",
        Invalidated = "invalidated", SentInvalidated = "sent-invalidated", Removed = "removed", AdmissionFull = "admission-full",
        Deadline = "deadline", Disabled = "disabled", Verified = "verified", Policy = "policy", Fallback = "fallback";
}
