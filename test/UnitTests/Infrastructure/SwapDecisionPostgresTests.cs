using Application.Abstractions;
using Application.Messaging;
using Domain.Common;
using Domain.Entities;
using Infrastructure.Caching;
using Infrastructure.Persistence;
using Infrastructure.Swaps;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Application.Billing;
using Application.Swaps;
using System.Text.Json;
using Presentation.Telegram.Services;
using Presentation.Telegram.Workers;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace UnitTests.Infrastructure;

// Uses the existing serialized BillingTests class and strictly guarded disposable database.
public sealed partial class BillingTests
{
    private static async Task<(ServiceProvider Services, SwapDecisionStore Store, RawBlockData Block)> SwapStore(int capacity = 10000, int admission = 256)
    {
        await Reset(); await using var db = Db(); await db.Set<SwapDecision>().ExecuteDeleteAsync();
        var address = global::Domain.ValueObjects.Address.Normalize(SwapEnrichmentTests.Wallet).Value;
        var wallet = TrackedWallet.Create(address, 900001).Value;
        var second = TrackedWallet.Create(address, 900002, minimumUsdValue: 500).Value;
        db.AddRange(wallet, second); await db.SaveChangesAsync();
        var cache = new InMemoryCacheService(); cache.Add(wallet); cache.Add(second);
        var services = new ServiceCollection(); services.AddScoped(_ => Db());
        var provider = services.BuildServiceProvider();
        var store = new SwapDecisionStore(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new SwapEnrichmentOptions
        { Enabled = true, ProviderQualified = true, PendingCapacity = capacity, AdmissionCapacity = admission }), cache, NullLogger<SwapDecisionStore>.Instance);
        var e = SwapEnrichmentTests.Reference(); var log = e.Logs.Single(x => x.ContractAddress == SwapEnrichmentTests.Ondo && x.Topics[0] == global::Infrastructure.Blockchain.Erc20TransferLogParser.TransferEventTopic);
        return (provider, store, new(SupportedChain.Ethereum, e.BlockNumber, e.Identity.BlockHash, DateTimeOffset.UtcNow, [], [log]));
    }
    [BillingPostgresFact]
    public async Task SwapDurableIntakeDeduplicatesRecipientsAndNeverSlidesDeadline()
    {
        var fixture = await SwapStore(); using var provider = fixture.Services;
        var (store, block) = (fixture.Store, fixture.Block);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.ObserveAsync(block.Logs[0], block, default)));
        await using var db = Db(); var rows = await db.Set<SwapDecision>().OrderBy(x => x.UserId).ToListAsync();
        Assert.Equal(2, rows.Count); Assert.NotEqual(rows[0].UserId, rows[1].UserId);
        var deadline = rows[0].Deadline;
        await store.ObserveAsync(block.Logs[0] with { LogIndex = 999 }, block, default);
        db.ChangeTracker.Clear(); Assert.Equal(deadline, (await db.Set<SwapDecision>().SingleAsync(x => x.Id == rows[0].Id)).Deadline);
        Assert.Equal(2, System.Text.Json.JsonSerializer.Deserialize<List<RawLogData>>((await db.Set<SwapDecision>().FirstAsync()).ObservationsJson)!.Count);
    }
    [BillingPostgresFact]
    public async Task SwapExpiredLeaseIsFencedAndDecisionOutboxSurvivesNewStore()
    {
        var fixture = await SwapStore(); using var provider = fixture.Services; var store = fixture.Store; var block = fixture.Block;
        await store.ObserveAsync(block.Logs[0], block, default);
        var old = (await store.ClaimAsync(default))!;
        await using var db = Db();
        await db.Set<SwapDecision>().Where(x => x.Id == old.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntil, DateTimeOffset.UtcNow.AddSeconds(-1)));
        var fresh = (await store.ClaimAsync(default))!; Assert.Equal(old.Id, fresh.Id); Assert.NotEqual(old.LeaseOwner, fresh.LeaseOwner);
        var payload = new AggregatedNotificationPayload(fresh.UserId, fresh.WalletId, SupportedChain.Ethereum, fresh.WalletAddress, "private alias", fresh.TransactionHash,
            fresh.BlockNumber, DateTimeOffset.UtcNow, [new(NotificationOperation.Sent, null, null, "ONDO", null, SwapEnrichmentTests.Ondo, 1, null)]);
        Assert.False(await store.DecideAsync(old, payload, SwapStates.Fallback, default));
        Assert.True(await store.DecideAsync(fresh, payload, SwapStates.Fallback, default));
        Assert.False(await store.DecideAsync(fresh, payload, SwapStates.Verified, default));
        var restarted = new SwapDecisionStore(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new SwapEnrichmentOptions()), new InMemoryCacheService(), NullLogger<SwapDecisionStore>.Instance);
        var delivery = (await restarted.ClaimDeliveryAsync(default))!;
        Assert.Equal(fresh.Id, delivery.SwapDecisionId); Assert.True(await restarted.AllowsAsync(delivery, default));
        Assert.False(await restarted.AllowsAsync(delivery with { TelegramUserId = 42 }, default));
        await restarted.CompleteAsync(delivery, true, default);
        await store.ObserveAsync(block.Logs[0], block, default);
        Assert.Null(await restarted.ClaimDeliveryAsync(default));
    }
    [BillingPostgresFact]
    public async Task SwapRemovedAndReincludedVersionCannotDeliverStaleLeaseOrDuplicateSentIdentity()
    {
        var fixture = await SwapStore(); using var provider = fixture.Services; var store = fixture.Store; var block = fixture.Block;
        await store.ObserveAsync(block.Logs[0], block, default);
        var claim = (await store.ClaimAsync(default))!;
        await store.ObserveAsync(block.Logs[0] with { Removed = true }, block, default);
        Assert.False(await store.DecideAsync(claim, null, SwapStates.Fallback, default));
        var hash = "0x" + new string('a', 64); var newBlock = block with { BlockHash = hash, Logs = [block.Logs[0] with { BlockHash = hash }] };
        await store.ObserveAsync(newBlock.Logs[0], newBlock, default);
        var fresh = (await store.ClaimAsync(default))!; Assert.Equal(hash, fresh.BlockHash); Assert.Equal(claim.Deadline, fresh.Deadline);
        var payload = new AggregatedNotificationPayload(fresh.UserId, fresh.WalletId, SupportedChain.Ethereum, fresh.WalletAddress, null, fresh.TransactionHash,
            fresh.BlockNumber, DateTimeOffset.UtcNow, []);
        await store.DecideAsync(fresh, payload, SwapStates.Fallback, default);
        var delivery = (await store.ClaimDeliveryAsync(default))!; await store.CompleteAsync(delivery, true, default);
        await store.ObserveAsync(newBlock.Logs[0] with { Removed = true }, newBlock, default);
        await store.ObserveAsync(block.Logs[0], block, default);
        await using var db = Db(); var sent = await db.Set<SwapDecision>().SingleAsync(x => x.Id == fresh.Id);
        Assert.Equal(SwapStates.SentInvalidated, sent.State); Assert.NotNull(sent.SentAt); Assert.Null(await store.ClaimDeliveryAsync(default));
    }
    [BillingPostgresFact]
    public async Task SwapDurableQuotaBackpressuresAndCancellationStopsWaiting()
    {
        var fixture = await SwapStore(capacity: 1); using var provider = fixture.Services;
        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.ObserveAsync(fixture.Block.Logs[0], fixture.Block, budget.Token));
        await using var db = Db(); Assert.Empty(await db.Set<SwapDecision>().ToListAsync());
    }
    [BillingPostgresFact]
    public async Task SwapSettingsRevisionDeletionAndRetentionAreFailClosed()
    {
        var fixture = await SwapStore(); using var provider = fixture.Services; var store = fixture.Store; var block = fixture.Block;
        await store.ObserveAsync(block.Logs[0], block, default); var row = (await store.ClaimAsync(default))!;
        var payload = new AggregatedNotificationPayload(row.UserId, row.WalletId, SupportedChain.Ethereum, row.WalletAddress, null, row.TransactionHash,
            row.BlockNumber, DateTimeOffset.UtcNow, []);
        await store.DecideAsync(row, payload, SwapStates.Fallback, default); var delivery = (await store.ClaimDeliveryAsync(default))!;
        await using var db = Db(); var wallet = await db.TrackedWallets.SingleAsync(x => x.Id == row.WalletId);
        wallet.UpdateMinimumUsdValue(999); await db.SaveChangesAsync(); Assert.False(await store.AllowsAsync(delivery, default));
        await db.Set<SwapDecision>().ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-1)));
        await store.PruneAsync(default); Assert.Equal(2, await db.Set<SwapDecision>().CountAsync());
        await store.CompleteAsync(delivery, false, default); await store.PruneAsync(default); Assert.Single(await db.Set<SwapDecision>().ToListAsync());
    }

    [BillingPostgresFact]
    public async Task SwapDisabledReplayRetainsRecipientAndGenerationIsolation()
    {
        var fixture = await SwapStore(); using var provider = fixture.Services; var store = fixture.Store; var block = fixture.Block;
        await store.ObserveAsync(block.Logs[0], block, default); var job = (await store.ClaimAsync(default))!;
        var ordinary = new AggregatedNotificationPayload(job.UserId, job.WalletId, SupportedChain.Ethereum, job.WalletAddress, null,
            job.TransactionHash, job.BlockNumber, DateTimeOffset.UtcNow, []);
        var disabled = new SwapDecisionStore(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new SwapEnrichmentOptions()), new InMemoryCacheService(), NullLogger<SwapDecisionStore>.Instance);
        Assert.False(await disabled.AllowsAsync(ordinary, default));
        Assert.True(await disabled.AllowsAsync(ordinary with { TrackedWalletId = Guid.NewGuid() }, default));
        Assert.True(await disabled.AllowsAsync(ordinary with { Chain = SupportedChain.Base }, default));
        await disabled.ObserveAsync(block.Logs[0] with { Removed = true }, block, default);
        Assert.False(await store.DecideAsync(job, ordinary, SwapStates.Fallback, default));
    }

    [BillingPostgresFact]
    public async Task SwapDispatcherPermanentTelegramRejectionsTerminateDurableWork()
    {
        foreach (var status in new[] { 400, 403 })
        {
            var fixture = await SwapStore(); using var services = fixture.Services;
            var payload = await PrepareSwapDelivery(fixture.Store, fixture.Block);
            var guard = new ObservedSwapGuard(fixture.Store); var handler = new SwapDeliveryHandler(status);
            await using var limiter = new TelegramGlobalRateLimiter(); var channels = new OnChainPipelineChannels();
            using var worker = SwapDispatcher(channels, guard, handler, limiter); await worker.StartAsync(default);
            try
            {
                await channels.AggregatedNotificationPayloadChannel.Writer.WriteAsync(payload);
                Assert.False(await guard.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10)));
                await using var db = Db();
                var row = await db.Set<SwapDecision>().SingleAsync(x => x.Id == payload.SwapDecisionId);
                Assert.Equal(SwapStates.Suppressed, row.State); Assert.Null(row.LeaseOwner); Assert.Equal(1, handler.Calls);
                Assert.Null(await fixture.Store.ClaimDeliveryAsync(default));
            }
            finally { await worker.StopAsync(default); }
        }
    }

    [BillingPostgresFact]
    public async Task SwapDispatcherExpiredQueuedLeaseRemainsPendingAndCanBeReclaimed()
    {
        var fixture = await SwapStore(); using var services = fixture.Services;
        var payload = await PrepareSwapDelivery(fixture.Store, fixture.Block);
        await using (var db = Db())
            await db.Set<SwapDecision>().Where(x => x.Id == payload.SwapDecisionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntil, DateTimeOffset.UtcNow.AddSeconds(-1)));
        var guard = new ObservedSwapGuard(fixture.Store); var handler = new SwapDeliveryHandler(200);
        await using var limiter = new TelegramGlobalRateLimiter(); var channels = new OnChainPipelineChannels();
        using var worker = SwapDispatcher(channels, guard, handler, limiter); await worker.StartAsync(default);
        try
        {
            await channels.AggregatedNotificationPayloadChannel.Writer.WriteAsync(payload);
            Assert.False(await guard.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(0, handler.Calls);
            await using var db = Db();
            Assert.Equal(SwapStates.Outbox, (await db.Set<SwapDecision>().SingleAsync(x => x.Id == payload.SwapDecisionId)).State);
            var fresh = await fixture.Store.ClaimDeliveryAsync(default);
            Assert.NotNull(fresh); Assert.Equal(payload.SwapDecisionId, fresh.SwapDecisionId); Assert.NotEqual(payload.DeliveryLease, fresh.DeliveryLease);
            Assert.True(await fixture.Store.AllowsAsync(fresh, default));
            await fixture.Store.CompleteAsync(payload, false, default);
            Assert.True(await fixture.Store.AllowsAsync(fresh, default));
        }
        finally { await worker.StopAsync(default); }
    }
    private static async Task<AggregatedNotificationPayload> PrepareSwapDelivery(SwapDecisionStore store, RawBlockData block)
    {
        await store.ObserveAsync(block.Logs[0], block, default); var job = (await store.ClaimAsync(default))!;
        var payload = new AggregatedNotificationPayload(job.UserId, job.WalletId, SupportedChain.Ethereum, job.WalletAddress, null,
            job.TransactionHash, job.BlockNumber, DateTimeOffset.UtcNow, []);
        Assert.True(await store.DecideAsync(job, payload, SwapStates.Fallback, default));
        return (await store.ClaimDeliveryAsync(default))!;
    }
    private static NotificationDispatcherWorker SwapDispatcher(OnChainPipelineChannels channels, ObservedSwapGuard guard,
        SwapDeliveryHandler handler, TelegramGlobalRateLimiter limiter)
        => new(channels, new TelegramBotClient(new TelegramBotClientOptions("123456:fixture-token") { RetryCount = 0 }, new HttpClient(handler)),
            limiter, new SwapDeliveryView(), new SwapDeliveryView(), new SwapDeliveryView(), NullLogger<NotificationDispatcherWorker>.Instance, swapGuard: guard);
    private sealed class ObservedSwapGuard(SwapDecisionStore store) : ISwapDeliveryGuard
    {
        public TaskCompletionSource<bool> Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> AllowsAsync(AggregatedNotificationPayload payload, CancellationToken ct) => store.AllowsAsync(payload, ct);
        public async Task CompleteAsync(AggregatedNotificationPayload payload, bool sent, CancellationToken ct)
        { await store.CompleteAsync(payload, sent, ct); Completed.TrySetResult(sent); }
    }
    private sealed class SwapDeliveryHandler(int status) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)status)
            { Content = new StringContent($"{{\"ok\":false,\"error_code\":{status},\"description\":\"fixture\"}}", System.Text.Encoding.UTF8, "application/json") });
        }
    }
    private sealed class SwapDeliveryView : ITelegramNotificationEnricher, ITelegramNotificationRenderer, ITelegramBotUsernameProvider
    {
        public Task<EnrichedTelegramNotification> EnrichAsync(NotificationPayload payload, CancellationToken ct)
            => EnrichAsync(AggregatedNotificationPayload.FromSingle(payload), ct);
        public Task<EnrichedTelegramNotification> EnrichAsync(AggregatedNotificationPayload payload, CancellationToken ct)
            => Task.FromResult(new EnrichedTelegramNotification(payload.TelegramUserId, payload.TrackedWalletId, payload.Chain,
                payload.TrackedWalletAddress, "fixture", payload.TransactionHash, payload.BlockNumber, payload.BlockTimestampUtc, []));
        public TelegramRenderedMessage Render(EnrichedTelegramNotification payload, string? botUsername = null) => new("fixture", ParseMode.Html);
        public Task<string?> GetBotUsernameAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    }

    [BillingPostgresFact]
    public async Task SwapWorkerAppliesAggregateThresholdPreferencesAndUnpricedPolicyWithinFixedBudget()
    {
        foreach (var scenario in new[] { "aggregate", "unpriced-zero", "unpriced-positive", "outgoing-only", "disabled", "slow", "metadata-slow", "activation", "reenrollment" })
        {
            var fixture = await SwapStore(); using var services = fixture.Services; var store = fixture.Store;
            var execution = SwapEnrichmentTests.Reference() with { BlockTime = scenario == "reenrollment" ? DateTimeOffset.UtcNow.AddMinutes(-10) : DateTimeOffset.UtcNow };
            await using (var db = Db())
            {
                foreach (var wallet in await db.TrackedWallets.ToListAsync())
                {
                    wallet.UpdateMinimumUsdValue(scenario == "aggregate" ? 200000 : scenario == "unpriced-positive" ? 1 : 0);
                    wallet.UpdateTransferTracking(scenario != "outgoing-only", true, true);
                }
                await db.SaveChangesAsync();
            }
            // One observed leg is below the minimum; complete receipt evidence proves two
            // same-asset debits whose combined input exceeds it.
            var trigger = fixture.Block.Logs[0];
            if (scenario == "aggregate")
            {
                var half = SwapEnrichmentTests.Input / 2;
                string Word(System.Numerics.BigInteger x) => "0x" + x.ToString("x").PadLeft(64, '0')[^64..];
                trigger = trigger with { Data = Word(half) };
                execution = execution with { Logs = execution.Logs.Select(x => x.LogIndex == trigger.LogIndex ? trigger : x)
                    .Append(trigger with { LogIndex = 999, Data = Word(SwapEnrichmentTests.Input - half) }).ToArray() };
            }
            await store.ObserveAsync(trigger, fixture.Block, default);
            var config = Options.Create(new SwapEnrichmentOptions { Enabled = scenario != "disabled", ProviderQualified = true, Concurrency = 1, DecisionDeadline = TimeSpan.FromMilliseconds(350) });
            // Intake's persisted deadline is authoritative; shorten it for deterministic tests.
            await using (var db = Db())
                await db.Set<SwapDecision>().ExecuteUpdateAsync(s => s.SetProperty(x => x.Deadline, DateTimeOffset.UtcNow.AddMilliseconds(350)));
            var source = new WorkerEvidence(execution, scenario); var quotes = new WorkerAssets(scenario);
            var access = new WorkerAccess(scenario == "activation" ? execution.BlockTime.AddMinutes(1) : null);
            using var worker = new SwapEnrichmentWorker(store, new SwapEvidenceCache(source, config), new(), config,
                services.GetRequiredService<IServiceScopeFactory>(), quotes, quotes, access, NullLogger<SwapEnrichmentWorker>.Instance);
            await worker.StartAsync(default);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                List<SwapDecision> rows;
                do
                {
                    await Task.Delay(25, timeout.Token); await using var db = Db(); rows = await db.Set<SwapDecision>().AsNoTracking().ToListAsync(timeout.Token);
                } while (rows.Any(x => x.State == SwapStates.Candidate));
                Assert.Equal(2, rows.Count);
                var suppressed = scenario is "unpriced-positive" or "metadata-slow" or "activation" or "reenrollment";
                Assert.All(rows, x => Assert.Equal(suppressed ? SwapStates.Suppressed : SwapStates.Outbox, x.State));
                foreach (var row in rows.Where(x => x.PayloadJson is not null))
                {
                    var payload = JsonSerializer.Deserialize<AggregatedNotificationPayload>(row.PayloadJson!)!;
                    Assert.Equal(execution.BlockTime, payload.BlockTimestampUtc);
                    if (scenario is "aggregate" or "unpriced-zero") { Assert.NotNull(payload.Swap); Assert.Empty(payload.Lines); }
                    else { Assert.Null(payload.Swap); Assert.Single(payload.Lines); }
                }
                Assert.Equal(scenario == "disabled" ? 0 : 1, source.Calls);
                if (scenario == "activation") Assert.All(access.Times, x => Assert.Equal(execution.BlockTime, x));
            }
            finally { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2)); await worker.StopAsync(stop.Token); }
        }
    }
    private sealed class WorkerEvidence(TransactionExecutionEvidence evidence, string scenario) : ITransactionExecutionEvidenceProvider
    {
        public int Calls;
        public async Task<Result<TransactionExecutionEvidence>> GetAsync(ExecutionIdentity identity, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            if (scenario == "slow") await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return scenario is "activation" ? Result<TransactionExecutionEvidence>.Failure(SwapErrors.Unsupported) : Result<TransactionExecutionEvidence>.Success(evidence);
        }
        public Task<Result<DateTimeOffset>> GetBlockTimeAsync(ExecutionIdentity identity, CancellationToken ct)
            => Task.FromResult(Result<DateTimeOffset>.Success(evidence.BlockTime));
    }

    [BillingPostgresFact]
    public async Task SwapAdmissionAndFullDeliveryChannelKeepDurableWorkBoundedAndPending()
    {
        var fixture = await SwapStore(admission: 1); using var services = fixture.Services; var store = fixture.Store; var block = fixture.Block;
        await store.ObserveAsync(block.Logs[0], block, default);
        var another = block.Logs[0] with { TransactionHash = "0x" + new string('d', 64) };
        await store.ObserveAsync(another, block, default);
        await using (var db = Db())
        {
            var rows = await db.Set<SwapDecision>().ToListAsync();
            Assert.Equal(2, rows.Count(x => x.Reason == SwapStates.AdmissionFull));
        }
        for (var i = 0; i < 2; i++)
        {
            var claim = (await store.ClaimAsync(default))!;
            var payload = new AggregatedNotificationPayload(claim.UserId, claim.WalletId, SupportedChain.Ethereum, claim.WalletAddress, null,
                claim.TransactionHash, claim.BlockNumber, DateTimeOffset.UtcNow, []);
            Assert.True(await store.DecideAsync(claim, payload, SwapStates.Fallback, default));
        }
        var channels = new OnChainPipelineChannels(aggregatedNotificationBufferCapacity: 1);
        using var worker = new SwapOutboxWorker(store, channels); await worker.StartAsync(default);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await channels.AggregatedNotificationPayloadChannel.Reader.WaitToReadAsync(timeout.Token);
            await Task.Delay(300, timeout.Token);
            await using var db = Db(); Assert.Equal(2, await db.Set<SwapDecision>().CountAsync(x => x.State == SwapStates.Outbox));
            Assert.Equal(1, channels.AggregatedNotificationPayloadChannel.Reader.Count);
        }
        finally { await worker.StopAsync(default); }
    }
    private sealed class WorkerAssets(string scenario) : ITokenMetadataProvider, IAssetPriceProvider
    {
        public Task<Result<TokenMetadata>> GetAsync(SupportedChain chain, string address, CancellationToken ct)
            => scenario == "metadata-slow" ? new TaskCompletionSource<Result<TokenMetadata>>().Task
                : Task.FromResult(Result<TokenMetadata>.Success(new(chain, address, "ONDO", "Ondo", 18)));
        public Task<Result<AssetUsdPrice>> GetUsdPriceAsync(SupportedChain chain, string? address, string symbol, CancellationToken ct)
            => Task.FromResult(scenario.StartsWith("unpriced") ? Result<AssetUsdPrice>.Failure(AssetPriceErrors.Unavailable)
                : Result<AssetUsdPrice>.Success(new(2, DateTimeOffset.UtcNow, "fixture")));
    }
    private sealed class WorkerAccess(DateTimeOffset? activeSince) : IRecipientAccess
    {
        public readonly System.Collections.Concurrent.ConcurrentBag<DateTimeOffset?> Times = [];
        public Task<bool> AllowsAsync(long owner, Guid wallet, TrackingMode mode, DateTimeOffset? time, long? revision, CancellationToken ct)
        { Times.Add(time); return Task.FromResult(activeSince is null || time >= activeSince); }
    }
}
