using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Application.Abstractions;
using Application.Billing;
using Application.Messaging;
using Application.Swaps;
using Domain.Common;
using Domain.Entities;
using Infrastructure.Blockchain;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Swaps;

/// <summary>Fixed-count workers resolve durable jobs; no trace work runs in Telegram dispatch.</summary>
public sealed class SwapEnrichmentWorker(SwapDecisionStore store, SwapEvidenceCache evidence, UniswapV3Classifier classifier,
    IOptions<SwapEnrichmentOptions> options, IServiceScopeFactory scopes, ITokenMetadataProvider metadata,
    IAssetPriceProvider prices, IRecipientAccess access, ILogger<SwapEnrichmentWorker> logger) : BackgroundService
{
    private readonly SwapMetadataRequests _metadataRequests = new(metadata, options.Value.Concurrency);
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(Enumerable.Range(0, options.Value.Concurrency).Select(_ => RunAsync(stoppingToken)));
    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var job = await store.ClaimAsync(ct);
                if (job is null) { await Task.Delay(100, ct); continue; }
                await ProcessAsync(job, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning(SwapDiagnostics.StoreUnavailable); await Task.Delay(500, ct); }
        }
    }
    private async Task ProcessAsync(SwapDecision job, CancellationToken shutdown)
    {
        var chain = (SupportedChain)job.Chain;
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var wallet = await db.TrackedWallets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == job.WalletId && x.UserId == job.UserId, shutdown);
        if (wallet is null || (wallet.UpdatedAtUtc ?? wallet.CreatedAtUtc) != job.WalletRevision)
        { await store.DecideAsync(job, null, SwapStates.Policy, shutdown); return; }
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        var remaining = job.Deadline - DateTimeOffset.UtcNow;
        var preparationDeadline = remaining > TimeSpan.Zero ? job.Deadline : DateTimeOffset.UtcNow + options.Value.DecisionDeadline;
        // Restart or queue-expired jobs are irrevocably fallback-only. Give baseline display
        // dependencies a bounded preparation window without restarting execution enrichment.
        budget.CancelAfter(remaining > TimeSpan.Zero ? remaining : options.Value.DecisionDeadline);
        var lines = new List<NotificationTransferLine>();
        TransactionExecutionEvidence? execution = null; VerifiedSwap? swap = null;
        Result<DateTimeOffset>? fallbackTime = null;
        var reason = !options.Value.IsEnabled(chain) ? SwapStates.Disabled : job.Reason;
        var timestamp = job.FirstObservedAt;
        // Baseline transfer interpretation starts before enrichment, within the same deadline.
        var baseline = BuildFallbackAsync(job, wallet, budget.Token);
        try
        {
            if (reason == "" && remaining > TimeSpan.Zero)
            {
                var result = await evidence.GetAsync(new(chain, job.TransactionHash, job.BlockHash), job.Deadline, shutdown).WaitAsync(budget.Token);
                if (result.IsSuccess)
                {
                    execution = result.Value; timestamp = execution.BlockTime;
                    var observed = JsonSerializer.Deserialize<List<RawLogData>>(job.ObservationsJson)!;
                    var triggerMatches = observed.All(log => execution.Logs.Any(x => x.LogIndex == log.LogIndex && x.ContractAddress.Equals(log.ContractAddress, StringComparison.OrdinalIgnoreCase)
                        && x.Data.Equals(log.Data, StringComparison.OrdinalIgnoreCase) && x.Topics.SequenceEqual(log.Topics, StringComparer.OrdinalIgnoreCase)));
                    var classified = triggerMatches ? classifier.Classify(execution, job.WalletAddress) : Result<RawSwap>.Failure(SwapErrors.Inconsistent);
                    if (classified.IsSuccess && wallet.TrackIncomingTransfers && wallet.TrackOutgoingTransfers && wallet.TrackTokenTransfers)
                    {
                        var input = await AssetAsync(chain, classified.Value.InputAsset, classified.Value.InputAmount, budget.Token);
                        var output = await AssetAsync(chain, classified.Value.OutputAsset, classified.Value.OutputAmount, budget.Token);
                        if (input is not null && output is not null)
                        {
                            var price = await prices.GetUsdPriceAsync(chain, input.ContractAddress, input.Symbol, budget.Token).WaitAsync(budget.Token);
                            decimal? usd = price.IsSuccess ? checked(input.Amount * price.Value.PriceUsd) : null;
                            if ((usd.HasValue && usd.Value >= wallet.MinimumUsdValue) || wallet.MinimumUsdValue == 0)
                                swap = new(input, output, usd);
                            else { await ObserveBaselineAsync(baseline); await store.DecideAsync(job, null, SwapStates.Policy, shutdown); return; }
                        }
                    }
                    reason = classified.IsSuccess ? SwapStates.Policy : classified.Error.Code;
                }
                else
                {
                    reason = result.Error.Code;
                    if (result.Error == SwapErrors.NonCanonical)
                    { await ObserveBaselineAsync(baseline); await store.DecideAsync(job, null, reason, shutdown); return; }
                }
            }
            if (execution is null)
                fallbackTime = await evidence.GetBlockTimeAsync(new(chain, job.TransactionHash, job.BlockHash), preparationDeadline, shutdown).WaitAsync(budget.Token);
            if (swap is null && DateTimeOffset.UtcNow < job.Deadline)
                await Task.Delay(job.Deadline - DateTimeOffset.UtcNow, budget.Token);
        }
        catch (OperationCanceledException) when (!shutdown.IsCancellationRequested) { reason = SwapStates.Deadline; }
        catch (OverflowException) { reason = SwapErrors.Inconsistent.Code; }
        catch (Exception) when (!shutdown.IsCancellationRequested) { reason = SwapErrors.Unavailable.Code; }
        var baselineLines = await ObserveBaselineAsync(baseline);
        if (swap is null)
        {
            // Capture logs admitted while evidence was in flight without moving the deadline.
            var latest = await store.ReloadAsync(job, shutdown);
            if (latest is null) return;
            if (latest.ObservationsJson != job.ObservationsJson)
            {
                // Only cached/completed metadata may finish at the boundary; no new RPC budget.
                using var expired = new CancellationTokenSource(); expired.Cancel();
                var original = JsonSerializer.Deserialize<List<RawLogData>>(job.ObservationsJson)!;
                var additional = JsonSerializer.Deserialize<List<RawLogData>>(latest.ObservationsJson)!
                    .Where(x => original.All(previous => previous.LogIndex != x.LogIndex)).ToList();
                latest.ObservationsJson = JsonSerializer.Serialize(additional);
                var extra = await BuildFallbackAsync(latest, wallet, expired.Token);
                baselineLines.AddRange(extra);
            }
            lines.AddRange(baselineLines);
        }
        if (execution is null)
        {
            // A websocket observation is not block time. If canonical time cannot be
            // established within the budget, suppress rather than pass a false activation check.
            Result<DateTimeOffset> blockTime;
            try { blockTime = fallbackTime ?? await evidence.GetBlockTimeAsync(new(chain, job.TransactionHash, job.BlockHash), preparationDeadline, shutdown).WaitAsync(budget.Token); }
            catch (OperationCanceledException) when (!shutdown.IsCancellationRequested)
            { blockTime = Result<DateTimeOffset>.Failure(SwapErrors.Unavailable); }
            if (blockTime.IsFailure)
            { await store.DecideAsync(job, null, blockTime.Error.Code, shutdown); return; }
            timestamp = blockTime.Value;
        }
        if (timestamp < wallet.CreatedAtUtc || !await access.AllowsAsync(job.UserId, job.WalletId, TrackingMode.Evm, timestamp, job.EntitlementRevision, shutdown))
        { await store.DecideAsync(job, null, SwapStates.Policy, shutdown); return; }
        var payload = swap is null && lines.Count == 0 ? null : new AggregatedNotificationPayload(job.UserId, job.WalletId, chain,
            job.WalletAddress, wallet.Alias, job.TransactionHash, job.BlockNumber, timestamp, lines,
            job.EntitlementRevision, job.EntitlementValidUntil, swap, job.Id);
        await store.DecideAsync(job, payload, swap is not null ? SwapStates.Verified : reason == "" ? SwapStates.Fallback : reason, shutdown);
    }
    private async Task<List<NotificationTransferLine>> BuildFallbackAsync(SwapDecision job, TrackedWallet wallet, CancellationToken ct)
    {
        var lines = new List<NotificationTransferLine>(); var parser = new Erc20TransferLogParser();
        foreach (var log in JsonSerializer.Deserialize<List<RawLogData>>(job.ObservationsJson)!)
        {
            if (!parser.TryParse(log, (SupportedChain)job.Chain, out var transfer)) continue;
            var self = transfer.FromAddress.Equals(transfer.ToAddress);
            var incoming = transfer.ToAddress.Value == wallet.Address.Value;
            if (!wallet.TrackTokenTransfers || (wallet.EnabledChains.Count > 0 && !wallet.EnabledChains.Contains((SupportedChain)job.Chain))
                || (self ? !wallet.TrackIncomingTransfers && !wallet.TrackOutgoingTransfers : incoming ? !wallet.TrackIncomingTransfers : !wallet.TrackOutgoingTransfers)) continue;
            try
            {
                var asset = await AssetAsync((SupportedChain)job.Chain, transfer.TokenContractAddress, transfer.RawAmount, ct);
                if (asset is null) continue;
                var price = await prices.GetUsdPriceAsync((SupportedChain)job.Chain, asset.ContractAddress, asset.Symbol, ct).WaitAsync(ct);
                decimal? usd = price.IsSuccess ? checked(asset.Amount * price.Value.PriceUsd) : null;
                if (usd is null ? wallet.MinimumUsdValue > 0 : usd < wallet.MinimumUsdValue) continue;
                lines.Add(new(self ? NotificationOperation.SelfTransfer : incoming ? NotificationOperation.Received : NotificationOperation.Sent,
                    incoming ? transfer.FromAddress.Value : transfer.ToAddress.Value, null, asset.Symbol, null, asset.ContractAddress, asset.Amount, usd));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (OverflowException) { }
        }
        return lines;
    }
    private async Task<SwapAsset?> AssetAsync(SupportedChain chain, string address, BigInteger raw, CancellationToken ct)
    {
        if (address == UniswapV3Classifier.Native) return new(null, "ETH", ConvertAmount(raw, 18));
        var result = await _metadataRequests.GetAsync(chain, address, ct);
        return result.IsSuccess ? new(address, result.Value.Symbol, ConvertAmount(raw, result.Value.Decimals)) : null;
    }
    /// <summary>Checked display conversion keeps integer accounting separate from decimal rendering.</summary>
    public static decimal ConvertAmount(BigInteger raw, int decimals)
    {
        if (decimals is < 0 or > 77 || raw < 0) throw new OverflowException();
        var divisor = BigInteger.Pow(10, decimals); var whole = BigInteger.DivRem(raw, divisor, out var remainder);
        // Decimal has 28 fractional digits; truncate only at this display boundary.
        var fraction = (remainder * BigInteger.Pow(10, 28)) / divisor;
        var amount = checked((decimal)whole + (decimal)fraction / 10000000000000000000000000000m);
        if (raw > 0 && amount == 0) throw new OverflowException();
        return amount;
    }
    private static async Task<List<NotificationTransferLine>> ObserveBaselineAsync(Task<List<NotificationTransferLine>> task)
    { try { return await task; } catch (Exception) { return []; } }
}

/// <summary>Paced outbox drain uses TryWrite so delivery-channel pressure cannot spawn waiting tasks.</summary>
public sealed class SwapOutboxWorker(SwapDecisionStore store, OnChainPipelineChannels channels) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pruneAt = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow >= pruneAt) { await store.PruneAsync(stoppingToken); pruneAt = DateTimeOffset.UtcNow.AddHours(1); }
                var payload = await store.ClaimDeliveryAsync(stoppingToken);
                var queued = payload is not null && channels.AggregatedNotificationPayloadChannel.Writer.TryWrite(payload);
                if (payload is not null && !queued)
                    await store.ReleaseDeliveryAsync(payload, stoppingToken);
                await Task.Delay(queued ? 25 : 100, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { await Task.Delay(500, stoppingToken); }
        }
    }
}
