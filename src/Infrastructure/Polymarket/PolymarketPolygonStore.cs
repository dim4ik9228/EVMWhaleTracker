using Domain.Common;
using System.Text.Json;
using Application.Polymarket;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Polymarket;

/// <summary>Transactional raw inbox, canonical normalization, and recipient outbox. Coverage advances only after all filters persist.</summary>
public sealed class PolymarketPolygonStore(AppDbContext db, IPolymarketPolygonRpc rpc,
    IPolymarketMarketResolver markets, IOptions<PolymarketPolygonOptions> options, PolymarketPolygonHealth health)
{
    public async Task<string[]> SourcesAsync(CancellationToken ct) => await (from s in db.Set<PolymarketSetting>()
        join w in db.TrackedWallets on s.WalletId equals w.Id where s.Enabled && !s.NeedsAttention && s.SourceAddress != null
        select s.SourceAddress!).Distinct().OrderBy(x => x).ToArrayAsync(ct);

    public async Task PersistAsync(IEnumerable<PolymarketChainLog> logs, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74220003)", ct);
        foreach (var log in logs)
        {
            var existing = await db.Set<PolymarketRawLog>().FindAsync([log.Id], ct);
            if (existing is not null)
            {
                health.Duplicates++;
                if (log.Removed && !existing.Removed) { existing.Removed = true; existing.DecodeState = RuntimeStates.Normalization.Orphaned; await OrphanAsync(log.BlockNumber, log.BlockHash, ct); }
                else if (!log.Removed && existing.Removed && (await rpc.BlockAsync(log.BlockNumber, ct)).Hash == log.BlockHash)
                {
                    // A branch may become canonical again. A replay alone is not proof; check its block hash.
                    existing.Removed = false;
                    existing.DecodeState = RuntimeStates.Normalization.Pending;
                }
                continue;
            }
            db.Add(new PolymarketRawLog { Id = log.Id, Contract = log.Contract, BlockNumber = log.BlockNumber,
                BlockHash = log.BlockHash, TransactionHash = log.TransactionHash, TransactionIndex = log.TransactionIndex,
                LogIndex = log.LogIndex, TopicsJson = JsonSerializer.Serialize(log.Topics), Data = log.Data,
                Removed = log.Removed, StoredAt = DateTimeOffset.UtcNow, DecodeState = log.Removed ? RuntimeStates.Normalization.Orphaned : RuntimeStates.Normalization.Pending });
            if (log.Removed) await OrphanAsync(log.BlockNumber, log.BlockHash, ct);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
    }

    public async Task InitializeAsync(string[] sources, long head, CancellationToken ct)
    {
        var scopeVersion = PolymarketContractRegistry.Version + ":" + string.Join(",", options.Value.Families.Distinct().OrderBy(x => x, StringComparer.Ordinal));
        foreach (var source in sources)
        {
            var checkpoint = await db.Set<PolymarketCheckpoint>().FindAsync([source], ct);
            var start = await db.Set<PolymarketSetting>().Where(x => x.Enabled && x.SourceAddress == source)
                .MinAsync(x => x.ActivationBlock, ct) ?? head;
            if (checkpoint is null)
            {
                checkpoint = new() { SourceAddress = source, CoveredBlock = start, ScanNextBlock = start + 1,
                    RegistryVersion = scopeVersion };
                var block = await rpc.BlockAsync(start, ct); checkpoint.CoveredBlockHash = block.Hash;
                db.Add(checkpoint);
            }
            else if (checkpoint.CoveredBlock is null)
            {
                // Profile-era timestamp cursors never proved Polygon block coverage.
                checkpoint.CoveredBlock = start;
                checkpoint.CoveredBlockHash = (await rpc.BlockAsync(start, ct)).Hash;
            }
            if (checkpoint.RegistryVersion != scopeVersion)
            {
                var restart = Math.Max(start, Math.Max(0, head - options.Value.RecoveryBlocks));
                checkpoint.CoveredBlock = restart;
                checkpoint.CoveredBlockHash = (await rpc.BlockAsync(restart, ct)).Hash;
                checkpoint.Gap = PolymarketPolygonStoreDiagnostics.FilterCoverageChanged;
                checkpoint.RegistryVersion = scopeVersion;
                // Revisit previously unsupported wire records under the newly selected decoder scope.
                await db.Set<PolymarketRawLog>().Where(x => x.BlockNumber > restart && !x.Removed && x.DecodeState.StartsWith(RuntimeStates.Normalization.Unsupported))
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.DecodeState, RuntimeStates.Normalization.Pending), ct);
            }
            checkpoint.Status = RuntimeStates.Checkpoint.Recovering;
            checkpoint.ScanTargetBlock = head;
            var floor = Math.Max(0, head - options.Value.RecoveryBlocks);
            if ((checkpoint.CoveredBlock ?? start) < floor)
            { checkpoint.Gap = PolymarketPolygonStoreDiagnostics.RecoveryHorizonExceeded; checkpoint.ScanNextBlock = floor; }
            else checkpoint.ScanNextBlock = checkpoint.CoveredBlock + 1;
        }
        await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
    }

    public async Task<long> NextAsync(string[] sources, CancellationToken ct) => await db.Set<PolymarketCheckpoint>()
        .Where(x => sources.Contains(x.SourceAddress)).MinAsync(x => x.ScanNextBlock, ct) ?? 0;

    public async Task CoveredAsync(string[] sources, PolymarketChainBlock block, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var checkpoint in await db.Set<PolymarketCheckpoint>().Where(x => sources.Contains(x.SourceAddress)).ToListAsync(ct))
        {
            if (checkpoint.CoveredBlock > block.Number) continue;
            checkpoint.CoveredBlock = block.Number; checkpoint.CoveredBlockHash = block.Hash;
            checkpoint.ScanNextBlock = block.Number + 1; checkpoint.LastSuccessfulScan = DateTimeOffset.UtcNow;
            checkpoint.Status = checkpoint.Gap is null ? RuntimeStates.Checkpoint.Active : RuntimeStates.Checkpoint.Degraded;
        }
        await RememberBlockAsync(block, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); db.ChangeTracker.Clear();
    }

    public async Task<bool> ValidateAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74220003)", ct);
        var changed = false;
        var checkpoints = await db.Set<PolymarketCheckpoint>().Where(x => x.CoveredBlock != null && x.CoveredBlockHash != null).ToListAsync(ct);
        foreach (var checkpoint in checkpoints)
        {
            var actual = await rpc.BlockAsync(checkpoint.CoveredBlock!.Value, ct);
            if (actual.Hash == checkpoint.CoveredBlockHash) continue;
            changed = true;
            var ancestors = await db.Set<PolymarketBlock>().AsNoTracking().Where(x => x.Number < actual.Number)
                .OrderByDescending(x => x.Number).Take(options.Value.RetainedAncestry).ToListAsync(ct);
            long? common = null;
            foreach (var candidate in ancestors)
                if ((await rpc.BlockAsync(candidate.Number, ct)).Hash == candidate.Hash) { common = candidate.Number; break; }
            var rewind = common ?? Math.Max(0, actual.Number - options.Value.RecoveryBlocks);
            await OrphanAsync(rewind + 1, null, ct);
            checkpoint.CoveredBlock = rewind;
            checkpoint.CoveredBlockHash = (await rpc.BlockAsync(rewind, ct)).Hash;
            checkpoint.ScanNextBlock = rewind + 1;
            checkpoint.Gap = common is null ? PolymarketPolygonStoreDiagnostics.ReorgHistoryUnavailable : checkpoint.Gap;
            checkpoint.Status = RuntimeStates.Checkpoint.Recovering;
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); db.ChangeTracker.Clear(); return changed;
    }

    public async Task FinalizeAsync(long final, CancellationToken ct)
    {
        var sources = (await SourcesAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var query = db.Set<PolymarketRawLog>().Where(x => (x.DecodeState == RuntimeStates.Normalization.Pending || x.DecodeState == RuntimeStates.Normalization.Enriching) && !x.Removed && x.BlockNumber <= final);
        var fair = new Dictionary<string, PolymarketRawLog>();
        foreach (var source in sources)
        {
            var padded = PolymarketContractRegistry.WalletTopic(source);
            foreach (var item in await query.Where(x => x.TopicsJson.Contains(padded)).OrderBy(x => x.BlockNumber).ThenBy(x => x.LogIndex).Take(10).ToListAsync(ct))
                fair.TryAdd(item.Id, item);
        }
        foreach (var item in await query.OrderBy(x => x.BlockNumber).ThenBy(x => x.LogIndex).Take(20).ToListAsync(ct)) fair.TryAdd(item.Id, item);
        var pending = fair.Values.OrderBy(x => x.BlockNumber).ThenBy(x => x.LogIndex).ToArray();
        var enrichmentBudget = System.Diagnostics.Stopwatch.StartNew();
        foreach (var raw in pending)
        {
            var block = await rpc.BlockAsync(raw.BlockNumber, ct);
            if (raw.BlockHash != block.Hash)
            {
                await using var orphanTx = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74220003)", ct);
                await db.Entry(raw).ReloadAsync(ct);
                if ((await rpc.BlockAsync(raw.BlockNumber, ct)).Hash == raw.BlockHash)
                { await orphanTx.RollbackAsync(ct); continue; }
                raw.Removed = true; raw.DecodeState = RuntimeStates.Normalization.Orphaned;
                await OrphanAsync(raw.BlockNumber, raw.BlockHash, ct);
                await db.SaveChangesAsync(ct); await orphanTx.CommitAsync(ct);
                continue;
            }
            var log = new PolymarketChainLog(raw.Contract, raw.BlockNumber, raw.BlockHash, raw.TransactionHash,
                raw.TransactionIndex, raw.LogIndex, JsonSerializer.Deserialize<string[]>(raw.TopicsJson)!, raw.Data, false);
            var (activity, state) = PolymarketContractRegistry.Decode(log, block.Time, sources, options.Value.Families);
            if (activity is null && log.Contract == PolymarketContractRegistry.Positions && log.Topics.FirstOrDefault() == PolymarketContractRegistry.Redemption)
                (activity, state) = DecodeDirectRedemption(log, block.Time, sources);
            if (activity is null && log.Contract == PolymarketContractRegistry.AutoRedeemer)
                (activity, state) = DecodeAutoRedemption(log, block.Time, sources);
            if (activity is not null && activity.Action is PolymarketTokens.Actions.Buy or PolymarketTokens.Actions.Sell)
            {
                var exchanges = PolymarketContractRegistry.Exchanges.Select(x => x.Address).ToArray();
                var transactionLogs = await db.Set<PolymarketRawLog>().AsNoTracking().Where(x => x.TransactionHash == raw.TransactionHash
                    && x.BlockHash == raw.BlockHash && !x.Removed && exchanges.Contains(x.Contract))
                    .Select(x => x.TopicsJson).Take(1001).ToArrayAsync(ct);
                var own = PolymarketContractRegistry.WalletTopic(activity.SourceAddress);
                if (transactionLogs.Length > 1000 || transactionLogs.Select(x => JsonSerializer.Deserialize<string[]>(x)!)
                    .Any(topics => topics.Length == 4 && (topics[0] == PolymarketContractRegistry.CurrentFill
                        || topics[0] == PolymarketContractRegistry.LegacyFill) && topics[2] == own && topics[3] == own))
                { activity = null; state = RuntimeStates.Normalization.SelfTradeSuppressed; }
            }
            raw.DecodeState = state;
            if (state == RuntimeStates.Normalization.Malformed) health.Malformed++;
            if (state.StartsWith(RuntimeStates.Normalization.Unsupported, StringComparison.Ordinal)) health.Unsupported++;
            if (activity is null) continue;
            var family = PolymarketContractRegistry.Exchanges.SingleOrDefault(e => e.Address == raw.Contract)?.Family ?? PolymarketWireTokens.CtfRedemption;
            PolymarketMarket? metadata = null;
            if (activity.Action != PolymarketTokens.Actions.Redeem && enrichmentBudget.Elapsed < TimeSpan.FromSeconds(2))
            {
                using var lookup = CancellationTokenSource.CreateLinkedTokenSource(ct);
                lookup.CancelAfter(TimeSpan.FromSeconds(1));
                try { metadata = await markets.ResolveAsync(family, activity.MarketId, lookup.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            }
            if (metadata is null && activity.Action != PolymarketTokens.Actions.Redeem && DateTimeOffset.UtcNow - raw.StoredAt < TimeSpan.FromSeconds(options.Value.EnrichmentWaitSeconds))
            { raw.DecodeState = RuntimeStates.Normalization.Enriching; continue; }
            if (metadata is not null) activity = activity with { Title = metadata.Question, Outcome = metadata.Outcome, MarketSlug = metadata.Slug };
            // After the bounded wait the token ID plus verified quantities is safe; no guessed market/outcome text.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74220003)", ct);
            // Live removals and recovery normalization share one commit lock. Re-read after enrichment waits.
            await db.Entry(raw).ReloadAsync(ct);
            if (raw.Removed) { await tx.RollbackAsync(ct); continue; }
            var canonicalBlock = await rpc.BlockAsync(raw.BlockNumber, ct);
            if (raw.BlockHash != canonicalBlock.Hash)
            {
                raw.Removed = true; raw.DecodeState = RuntimeStates.Normalization.Orphaned;
                await OrphanAsync(raw.BlockNumber, raw.BlockHash, ct);
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                continue;
            }
            raw.DecodeState = state;
            var previousEvent = await db.Set<PolymarketEvent>().SingleOrDefaultAsync(x => x.Id == activity.Id, ct);
            if (previousEvent is not null && !previousEvent.Canonical)
                await RestoreAsync(previousEvent, ct);
            if (previousEvent is null)
            {
                db.Add(new PolymarketEvent { Id = activity.Id, SourceAddress = activity.SourceAddress, Time = activity.Time,
                    Payload = JsonSerializer.Serialize(activity), StoredAt = DateTimeOffset.UtcNow, BlockNumber = raw.BlockNumber,
                    BlockHash = raw.BlockHash, RawLogId = raw.Id, DecoderVersion = PolymarketContractRegistry.Version });
                var recipients = await (from s in db.Set<PolymarketSetting>() join w in db.TrackedWallets on s.WalletId equals w.Id
                    where s.Enabled && !s.NeedsAttention && s.SourceAddress == activity.SourceAddress && s.ActivationBlock < raw.BlockNumber
                    select new { Setting = s, w.UserId }).ToListAsync(ct);
                foreach (var recipient in recipients)
                    if (activity.Action == PolymarketTokens.Actions.Redeem || activity.CashValue is { } cash && cash >= recipient.Setting.MinimumTradeValue)
                        db.Add(new PolymarketDelivery { Id = Guid.NewGuid(), EventId = activity.Id, WalletId = recipient.Setting.WalletId,
                            Generation = recipient.Setting.Generation, UserId = recipient.UserId, DueAt = DateTimeOffset.UtcNow });
                var checkpoint = await db.Set<PolymarketCheckpoint>().FindAsync([activity.SourceAddress], ct);
                if (checkpoint is not null) checkpoint.LatestActivity = activity.Time;
            }
            await RememberBlockAsync(block, ct);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
    }

    private static (PolymarketActivity?, string) DecodeDirectRedemption(PolymarketChainLog log, DateTimeOffset time, HashSet<string> sources)
    {
        // Only direct account redemption is holder-proven. Router recipient/initiator alone never proves ownership.
        if (log.Topics.Length != 4 || !sources.Contains(PolymarketContractRegistry.AddressTopic(log.Topics[1]))) return (null, RuntimeStates.Normalization.UnsupportedRoutedRedemption);
        var collateral = PolymarketContractRegistry.AddressTopic(log.Topics[2]);
        if (collateral != PolymarketContractRegistry.Usdc && collateral != PolymarketContractRegistry.Pusd && collateral != PolymarketContractRegistry.NativeUsdc)
            return (null, RuntimeStates.Normalization.UnsupportedCollateral);
        if (log.Topics[3] != "0x" + new string('0', 64)) return (null, RuntimeStates.Normalization.UnsupportedNestedRedemption);
        try
        {
            var source = PolymarketContractRegistry.AddressTopic(log.Topics[1]);
            var payout = PolymarketContractRegistry.Units(PolymarketContractRegistry.Word(log.Data, 2));
            return (new(log.Id + ":" + source, source, time, PolymarketTokens.Actions.Redeem, "0x" + log.Data.Substring(2, 64),
                PolymarketPolygonStoreDiagnostics.AggregateConditionRedemption, null, null, null, payout,
                collateral == PolymarketContractRegistry.Pusd ? PolymarketTokens.Currencies.Pusd : collateral == PolymarketContractRegistry.Usdc ? PolymarketTokens.Currencies.BridgedUsdc : PolymarketTokens.Currencies.Usdc, null, log.TransactionHash), RuntimeStates.Normalization.Decoded);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) { return (null, RuntimeStates.Normalization.Malformed); }
    }

    private static (PolymarketActivity?, string) DecodeAutoRedemption(PolymarketChainLog log, DateTimeOffset time, HashSet<string> sources)
    {
        if (log.Topics.Length != 3 || log.Data.Length != 66) return (null, RuntimeStates.Normalization.Malformed);
        var source = PolymarketContractRegistry.AddressTopic(log.Topics[1]);
        if (!sources.Contains(source)) return (null, RuntimeStates.Normalization.Context);
        if (log.Topics[0] != PolymarketContractRegistry.AutoRedemption && log.Topics[0] != PolymarketContractRegistry.AutoBinaryRedemption
            && log.Topics[0] != PolymarketContractRegistry.AutoNegativeRedemption) return (null, RuntimeStates.Normalization.Unsupported);
        try
        {
            var id = log.Topics[0] == PolymarketContractRegistry.AutoRedemption
                ? PolymarketContractRegistry.Word(log.Topics[2], 0).ToString(System.Globalization.CultureInfo.InvariantCulture) : log.Topics[2];
            return (new(log.Id + ":" + source, source, time, PolymarketTokens.Actions.Redeem, id, PolymarketPolygonStoreDiagnostics.AggregateAutomaticRedemption, null, null, null,
                PolymarketContractRegistry.Units(PolymarketContractRegistry.Word(log.Data, 0)), PolymarketTokens.Currencies.Pusd, null, log.TransactionHash), RuntimeStates.Normalization.Decoded);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) { return (null, RuntimeStates.Normalization.Malformed); }
    }

    private async Task RememberBlockAsync(PolymarketChainBlock block, CancellationToken ct)
    {
        var previous = await db.Set<PolymarketBlock>().FindAsync([block.Number], ct);
        if (previous is null) db.Add(new PolymarketBlock { Number = block.Number, Hash = block.Hash, ParentHash = block.ParentHash, Time = block.Time });
        else { previous.Hash = block.Hash; previous.ParentHash = block.ParentHash; previous.Time = block.Time; }
    }
    private async Task OrphanAsync(long from, string? hash, CancellationToken ct)
    {
        if (hash is null)
        {
            // Recovery overlaps the live stream: replacement-branch logs may already be durable.
            // Only invalidate hashes proven noncanonical, never every log above the ancestor.
            var candidates = await db.Set<PolymarketRawLog>().AsNoTracking().Where(x => x.BlockNumber >= from && !x.Removed)
                .Select(x => new { x.BlockNumber, x.BlockHash }).Distinct().ToListAsync(ct);
            var normalized = await db.Set<PolymarketEvent>().AsNoTracking().Where(x => x.BlockNumber >= from && x.Canonical && !x.DecoderVersion.EndsWith(PolymarketWireTokens.CorrectionSuffix))
                .Select(x => new { x.BlockNumber, x.BlockHash }).Distinct().ToListAsync(ct);
            foreach (var block in candidates.Concat(normalized).Distinct().GroupBy(x => x.BlockNumber))
            {
                var canonical = await rpc.BlockAsync(block.Key, ct);
                foreach (var orphan in block.Where(x => x.BlockHash != canonical.Hash))
                    await OrphanAsync(orphan.BlockNumber, orphan.BlockHash, ct);
            }
            return;
        }
        var events = await db.Set<PolymarketEvent>().Where(x => x.Canonical && !x.DecoderVersion.EndsWith(PolymarketWireTokens.CorrectionSuffix) && x.BlockNumber >= from && (hash == null || x.BlockHash == hash)).ToListAsync(ct);
        foreach (var e in events)
        {
            e.Canonical = false;
            var deliveries = await db.Set<PolymarketDelivery>().Where(x => x.EventId == e.Id).ToListAsync(ct);
            foreach (var d in deliveries.Where(x => x.State == RuntimeStates.Delivery.Pending)) d.State = RuntimeStates.Delivery.Cancelled;
            var correctionId = e.Id + PolymarketWireTokens.CorrectionIdentitySuffix + Guid.NewGuid().ToString("N");
            if (deliveries.Any(x => x.State == RuntimeStates.Delivery.Sent))
            {
                var original = JsonSerializer.Deserialize<PolymarketActivity>(e.Payload)!;
                db.Add(new PolymarketEvent { Id = correctionId, SourceAddress = e.SourceAddress, Time = DateTimeOffset.UtcNow,
                    StoredAt = DateTimeOffset.UtcNow, Payload = JsonSerializer.Serialize(original with { Id = correctionId, Action = PolymarketTokens.Actions.Correction, Title = PolymarketPolygonStoreDiagnostics.SettlementRemoved }),
                    RawLogId = e.RawLogId, BlockNumber = e.BlockNumber, BlockHash = e.BlockHash, DecoderVersion = e.DecoderVersion + PolymarketWireTokens.CorrectionSuffix });
                foreach (var d in deliveries.Where(x => x.State == RuntimeStates.Delivery.Sent))
                    db.Add(new PolymarketDelivery { Id = Guid.NewGuid(), EventId = correctionId, WalletId = d.WalletId,
                        Generation = d.Generation, UserId = d.UserId, DueAt = DateTimeOffset.UtcNow });
            }
        }
        await db.Set<PolymarketRawLog>().Where(x => x.BlockNumber >= from && (hash == null || x.BlockHash == hash))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Removed, true).SetProperty(x => x.DecodeState, RuntimeStates.Normalization.Orphaned), ct);
    }

    private async Task RestoreAsync(PolymarketEvent original, CancellationToken ct)
    {
        original.Canonical = true;
        var corrections = await db.Set<PolymarketEvent>().Where(x => x.RawLogId == original.RawLogId
            && x.SourceAddress == original.SourceAddress && x.Canonical && x.DecoderVersion.EndsWith(PolymarketWireTokens.CorrectionSuffix)).ToListAsync(ct);
        var notices = new List<PolymarketDelivery>();
        foreach (var correction in corrections)
        {
            correction.Canonical = false;
            var deliveries = await db.Set<PolymarketDelivery>().Where(x => x.EventId == correction.Id).ToListAsync(ct);
            foreach (var d in deliveries.Where(x => x.State == RuntimeStates.Delivery.Pending)) d.State = RuntimeStates.Delivery.Cancelled;
            notices.AddRange(deliveries.Where(x => x.State == RuntimeStates.Delivery.Sent));
        }
        // Never repeat an already sent execution. Correct only recipients who received an orphan notice.
        if (notices.Count != 0)
        {
            var activity = JsonSerializer.Deserialize<PolymarketActivity>(original.Payload)!;
            var id = original.Id + PolymarketWireTokens.CorrectionIdentitySuffix + Guid.NewGuid().ToString("N");
            db.Add(new PolymarketEvent { Id = id, SourceAddress = original.SourceAddress, Time = DateTimeOffset.UtcNow,
                StoredAt = DateTimeOffset.UtcNow, Payload = JsonSerializer.Serialize(activity with { Id = id, Action = PolymarketTokens.Actions.Correction,
                    Title = PolymarketPolygonStoreDiagnostics.SettlementRestored }), RawLogId = original.RawLogId,
                BlockNumber = original.BlockNumber, BlockHash = original.BlockHash, DecoderVersion = original.DecoderVersion + PolymarketWireTokens.CorrectionSuffix });
            foreach (var recipient in notices.DistinctBy(x => new { x.WalletId, x.Generation }))
                db.Add(new PolymarketDelivery { Id = Guid.NewGuid(), EventId = id, WalletId = recipient.WalletId,
                    Generation = recipient.Generation, UserId = recipient.UserId, DueAt = DateTimeOffset.UtcNow });
        }
        var decoded = JsonSerializer.Deserialize<PolymarketActivity>(original.Payload)!;
        var pending = await db.Set<PolymarketDelivery>().Where(x => x.EventId == original.Id && x.State == RuntimeStates.Delivery.Cancelled).ToListAsync(ct);
        foreach (var d in pending)
        {
            var s = await db.Set<PolymarketSetting>().AsNoTracking().SingleOrDefaultAsync(x => x.WalletId == d.WalletId, ct);
            if (s?.Enabled == true && !s.NeedsAttention && s.Generation == d.Generation && s.SourceAddress == original.SourceAddress
                && s.ActivationBlock < original.BlockNumber && (decoded.Action == PolymarketTokens.Actions.Redeem || decoded.CashValue >= s.MinimumTradeValue))
            { d.State = RuntimeStates.Delivery.Pending; d.Attempts = 0; d.DueAt = DateTimeOffset.UtcNow; }
        }
    }
    public async Task PruneAsync(long head, CancellationToken ct)
    {
        var before = DateTimeOffset.UtcNow.AddDays(-options.Value.RetentionDays);
        await db.Set<PolymarketRawLog>().Where(x => x.StoredAt < before && x.DecodeState != RuntimeStates.Normalization.Pending && x.DecodeState != RuntimeStates.Normalization.Enriching).ExecuteDeleteAsync(ct);
        await db.Set<PolymarketBlock>().Where(x => x.Number < head - options.Value.RetainedAncestry).ExecuteDeleteAsync(ct);
        // Terminal work expires only beyond the longer deduplication horizon; pending deliveries retain their events.
        var expired = db.Set<PolymarketEvent>().Where(x => x.StoredAt < before).Select(x => x.Id);
        await db.Set<PolymarketDelivery>().Where(x => x.State != RuntimeStates.Delivery.Pending && expired.Contains(x.EventId)).ExecuteDeleteAsync(ct);
        await db.Set<PolymarketEvent>().Where(x => x.StoredAt < before
            && !db.Set<PolymarketDelivery>().Any(d => d.EventId == x.Id)).ExecuteDeleteAsync(ct);
    }

    /// <summary>Samples bounded aggregate counters without exposing wallet identities or provider payloads.</summary>
    public async Task RefreshHealthAsync(string[] sources, CancellationToken ct)
    {
        var checkpoints = await db.Set<PolymarketCheckpoint>().AsNoTracking().Where(x => sources.Contains(x.SourceAddress))
            .Select(x => new { x.CoveredBlock, x.Gap }).ToListAsync(ct);
        health.CoveredBlock = checkpoints.Count == sources.Length && checkpoints.All(x => x.CoveredBlock.HasValue)
            ? checkpoints.Min(x => x.CoveredBlock) : null;
        health.CoverageLagBlocks = health.CoveredBlock is { } covered ? Math.Max(0, health.Head - covered) : null;
        health.CoverageGaps = checkpoints.Count(x => x.Gap != null);
        var cap = PolymarketPolygonHealth.BacklogCountLimit + 1;
        health.NormalizationPending = await db.Set<PolymarketRawLog>().Where(x => x.DecodeState == RuntimeStates.Normalization.Pending && !x.Removed)
            .Take(cap).CountAsync(ct);
        health.EnrichmentPending = await db.Set<PolymarketRawLog>().Where(x => x.DecodeState == RuntimeStates.Normalization.Enriching && !x.Removed)
            .Take(cap).CountAsync(ct);
        health.DeliveryPending = await db.Set<PolymarketDelivery>().Where(x => x.State == RuntimeStates.Delivery.Pending).Take(cap).CountAsync(ct);
        health.DeliveryFailed = await db.Set<PolymarketDelivery>().Where(x => x.State == RuntimeStates.Delivery.Failed).Take(cap).CountAsync(ct);
    }
}
