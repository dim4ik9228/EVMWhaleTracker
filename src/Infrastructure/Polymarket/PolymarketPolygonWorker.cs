using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Application.Polymarket;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Infrastructure.Polymarket;

/// <summary>One dedicated database lease owns filtered Polygon push ingestion and bounded reconnect recovery.</summary>
public sealed class PolymarketPolygonWorker(IServiceScopeFactory scopes, IPolymarketPolygonRpc rpc,
    IOptions<PolymarketPolygonOptions> options, IOptions<PolymarketOptions> profileOptions,
    PolymarketPolygonHealth health, ILogger<PolymarketPolygonWorker> logger) : BackgroundService
{
    private const long OwnerLock = 74220002;
    private sealed record Frame(PolymarketChainLog? Log = null, long? Head = null, TaskCompletionSource? Barrier = null);
    private sealed record Filter(string[] Address, object?[] Topics);
    private PolymarketPolygonOptions Options => options.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!Options.Enabled || !profileOptions.Value.Enabled) { health.Status = PolymarketPolygonWorkerDiagnostics.OffPolygonTrackingPaused; return; }
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var lease = scopes.CreateScope();
                var db = lease.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.OpenConnectionAsync(ct);
                var connection = (NpgsqlConnection)db.Database.GetDbConnection();
                await using var acquire = new NpgsqlCommand($"SELECT pg_try_advisory_lock({OwnerLock})", connection);
                if (!(bool)(await acquire.ExecuteScalarAsync(ct))!) { health.Status = PolymarketPolygonWorkerDiagnostics.StartingAnotherIngestionOwner; await Task.Delay(2000, ct); continue; }
                using var owner = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var monitor = MonitorLeaseAsync(connection, owner);
                try { await RunOwnerAsync(owner.Token); }
                finally
                {
                    owner.Cancel(); await monitor;
                    if (connection.State == System.Data.ConnectionState.Open)
                    { await using var unlock = new NpgsqlCommand($"SELECT pg_advisory_unlock({OwnerLock})", connection); await unlock.ExecuteScalarAsync(CancellationToken.None); }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Exceptions may contain websocket URLs. Only the exception type is logged.
                health.Status = PolymarketPolygonWorkerDiagnostics.ConnectionUnavailable;
                var reason = ex is InvalidDataException && ex.Message.StartsWith(PolymarketPolygonWorkerDiagnostics.SafeFailurePrefix, StringComparison.Ordinal)
                    && !ex.Message.Contains("://", StringComparison.Ordinal) ? ex.Message : ex.GetType().Name;
                logger.LogWarning(PolymarketPolygonWorkerDiagnostics.PolygonSourceReconnectRequired, ex.GetType().Name, reason);
            }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
    private static async Task MonitorLeaseAsync(NpgsqlConnection connection, CancellationTokenSource owner)
    {
        try
        {
            while (!owner.IsCancellationRequested)
            { await Task.Delay(1000, owner.Token); await using var cmd = new NpgsqlCommand("SELECT 1", connection) { CommandTimeout = 2 }; await cmd.ExecuteScalarAsync(owner.Token); }
        }
        catch { owner.Cancel(); }
    }

    private async Task RunOwnerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var sources = await StoreAsync(s => s.SourcesAsync(ct));
            if (sources.Length == 0) { health.Status = PolymarketPolygonWorkerDiagnostics.OffNoEnabledAccounts; await Task.Delay(1000, ct); continue; }
            if (sources.Length > Options.MaxUniqueAddresses) throw new InvalidDataException(PolymarketPolygonWorkerDiagnostics.PolygonSourceCapacityExceeded);
            await rpc.VerifyChainAsync(ct); health.ChainVerified = true;
            await StoreAsync(async s => { await s.ValidateAsync(ct); return true; });
            using var epoch = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(epoch.Token))
            { timeout.CancelAfter(TimeSpan.FromSeconds(Options.TimeoutSeconds)); await socket.ConnectAsync(new Uri(Options.WebSocketEndpoint), timeout.Token); }
            var filters = Filters(sources);
            health.DesiredSubscriptions = filters.Length + 1; health.AcknowledgedSubscriptions = 0;
            var queue = Channel.CreateBounded<Frame>(new BoundedChannelOptions(Options.QueueCapacity)
            { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
            var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var recovered = false;
            var socketChainVerified = false;
            var lastFrame = DateTimeOffset.UtcNow;
            var reader = ReadAsync();
            var processor = ProcessAsync();
            var reconcile = ReconcileAsync();
            Task? recovery = null;
            try
            {
                await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id = 0, method = PolymarketWireTokens.ChainId, @params = System.Array.Empty<object>() }), WebSocketMessageType.Text, true, epoch.Token);
                for (var i = 0; i < filters.Length; i++)
                    await SendAsync(i + 1, new object[] { PolymarketWireTokens.LogSubscription, new { address = filters[i].Address, topics = filters[i].Topics } });
                await SendAsync(filters.Length + 1, new object[] { PolymarketWireTokens.Heads });
                recovery = RecoverAsync();
                var first = await Task.WhenAny(reader, processor, reconcile, recovery);
                await first;
                if (first == recovery) await await Task.WhenAny(reader, processor, reconcile);
            }
            finally
            {
                epoch.Cancel(); socket.Abort(); queue.Writer.TryComplete();
                foreach (var task in new[] { reader, processor, reconcile, recovery }.Where(t => t is not null))
                    try { await task!; } catch (Exception) { /* All epoch tasks are drained; the winning failure already propagates. */ }
            }

            async Task SendAsync(int id, object[] parameters)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id, method = PolymarketWireTokens.Subscribe, @params = parameters });
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, epoch.Token);
            }
            async Task ReadAsync()
            {
                var acks = new HashSet<int>(); var subscriptions = new HashSet<string>();
                var bytes = new byte[8192];
                while (!epoch.IsCancellationRequested)
                {
                    using var message = new MemoryStream(); ValueWebSocketReceiveResult receive;
                    do
                    {
                        receive = await socket.ReceiveAsync(bytes.AsMemory(), epoch.Token);
                        if (receive.MessageType == WebSocketMessageType.Close) throw new WebSocketException(PolymarketPolygonWorkerDiagnostics.PolygonClosedSubscription);
                        if (message.Length + receive.Count > Options.MaximumFrameBytes) throw new InvalidDataException(PolymarketPolygonWorkerDiagnostics.PolygonFrameLimit);
                        message.Write(bytes, 0, receive.Count);
                    } while (!receive.EndOfMessage);
                    using var document = JsonDocument.Parse(message.ToArray()); var root = document.RootElement;
                    lastFrame = DateTimeOffset.UtcNow;
                    if (root.TryGetProperty("id", out var id))
                    {
                        if (id.GetInt32() == 0)
                        {
                            if (!root.TryGetProperty("result", out var chain) || chain.GetString() != PolymarketWireTokens.PolygonChainQuantity) throw new InvalidDataException(PolymarketPolygonWorkerDiagnostics.PolygonSocketChainMismatch);
                            socketChainVerified = true;
                            if (acks.Count == filters.Length + 1) acknowledged.TrySetResult();
                            continue;
                        }
                        if (root.TryGetProperty("error", out _) || !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.String)
                            throw new InvalidDataException(PolymarketPolygonWorkerDiagnostics.PolygonSubscriptionRejected);
                        if (id.GetInt32() < 1 || id.GetInt32() > filters.Length + 1) throw new InvalidDataException(PolymarketPolygonWorkerDiagnostics.UnexpectedPolygonAcknowledgment);
                        acks.Add(id.GetInt32()); subscriptions.Add(result.GetString()!); health.AcknowledgedSubscriptions = acks.Count;
                        if (acks.Count == filters.Length + 1 && socketChainVerified) acknowledged.TrySetResult();
                    }
                    else if (root.TryGetProperty("params", out var parameters))
                    {
                        if (!subscriptions.Contains(parameters.GetProperty("subscription").GetString()!)) throw new InvalidDataException(PolymarketPolygonWorkerDiagnostics.UnacknowledgedPolygonSubscription);
                        var result = parameters.GetProperty("result");
                        if (result.TryGetProperty("logIndex", out _)) await queue.Writer.WriteAsync(new(PolymarketPolygonRpc.ParseLog(result)), epoch.Token);
                        else await queue.Writer.WriteAsync(new(Head: PolymarketPolygonRpc.Hex(result.GetProperty("number").GetString()!)), epoch.Token);
                    }
                }
            }
            async Task ProcessAsync()
            {
                var headsSincePrune = 0;
                await foreach (var frame in queue.Reader.ReadAllAsync(epoch.Token))
                {
                    if (frame.Log is not null) await StoreAsync(async s => { await s.PersistAsync([frame.Log], epoch.Token); return true; });
                    if (frame.Head is { } head)
                    {
                        health.Head = head;
                        if (recovered)
                        {
                            // Live heads advance coverage only after every filter is acknowledged and prior frames are durable.
                            // The configured canonical boundary protects against cross-subscription scheduling.
                            var final = await FinalBoundaryAsync(head, epoch.Token);
                            await StoreAsync(async s => { await s.FinalizeAsync(final, epoch.Token); return true; });
                            var covered = await rpc.BlockAsync(final, epoch.Token);
                            await StoreAsync(async s => { if (await s.ValidateAsync(epoch.Token)) throw new InvalidDataException(PolymarketPolygonWorkerDiagnostics.PolygonCanonicalRewindRequiresRecovery); await s.CoveredAsync(sources, covered, epoch.Token); return true; });
                            if (++headsSincePrune >= 64)
                            {
                                await StoreAsync(async s => { await s.PruneAsync(head, epoch.Token); return true; });
                                headsSincePrune = 0;
                            }
                        }
                    }
                    frame.Barrier?.TrySetResult();
                }
            }
            async Task RecoverAsync()
            {
                await acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(Options.TimeoutSeconds), epoch.Token);
                logger.LogInformation(PolymarketPolygonWorkerDiagnostics.PolygonSubscriptionsAcknowledged, health.AcknowledgedSubscriptions, health.DesiredSubscriptions);
                var target = await rpc.HeadAsync(epoch.Token); health.Head = target; health.Status = PolymarketPolygonWorkerDiagnostics.RecoveringBoundedPolygonOverlap;
                await StoreAsync(async s => { await s.InitializeAsync(sources, target, epoch.Token); return true; });
                var from = await StoreAsync(s => s.NextAsync(sources, epoch.Token));
                var span = Options.LogRangeBlocks; var requests = 0;
                while (from <= target)
                {
                    var to = Math.Min(target, from + span - 1);
                    try
                    {
                        foreach (var filter in filters)
                        {
                            if (++requests > Options.RecoveryRequests) throw new RecoveryBudgetException();
                            var logs = await rpc.LogsAsync(new { address = filter.Address, topics = filter.Topics,
                                fromBlock = PolymarketPolygonRpc.Quantity(from), toBlock = PolymarketPolygonRpc.Quantity(to) }, epoch.Token);
                            await StoreAsync(async s => { await s.PersistAsync(logs, epoch.Token); return true; });
                        }
                    }
                    catch (PolymarketLogRangeException) when (span > 1) { span = Math.Max(1, span / 2); continue; }
                    var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    await queue.Writer.WriteAsync(new(Barrier: barrier), epoch.Token); await barrier.Task.WaitAsync(epoch.Token);
                    var block = await rpc.BlockAsync(to, epoch.Token);
                    await StoreAsync(async s => { await s.CoveredAsync(sources, block, epoch.Token); return true; });
                    from = to + 1;
                }
                var final = await FinalBoundaryAsync(target, epoch.Token);
                await StoreAsync(async s => { await s.FinalizeAsync(final, epoch.Token); await s.PruneAsync(target, epoch.Token); return true; });
                recovered = true;
                await StoreAsync(async s => { await s.RefreshHealthAsync(sources, epoch.Token); return true; });
                health.Status = health.CoverageGaps > 0
                    ? PolymarketPolygonWorkerDiagnostics.DegradedCoverage
                    : PolymarketPolygonWorkerDiagnostics.ActiveCoverage;
            }
            async Task ReconcileAsync()
            {
                var ticks = 0;
                while (true)
                {
                    await Task.Delay(1000, epoch.Token);
                    if (DateTimeOffset.UtcNow - lastFrame > TimeSpan.FromSeconds(90)) throw new TimeoutException(PolymarketPolygonWorkerDiagnostics.PolygonHeadStreamStalled);
                    if (!(await StoreAsync(s => s.SourcesAsync(epoch.Token))).SequenceEqual(sources)) return;
                    if (++ticks % 60 == 0)
                    {
                        await StoreAsync(async s => { await s.RefreshHealthAsync(sources, epoch.Token); return true; });
                        logger.LogInformation(PolymarketPolygonWorkerDiagnostics.HealthSummary,
                            health.ChainVerified, health.AcknowledgedSubscriptions, health.DesiredSubscriptions, health.Head, health.CanonicalBoundary,
                            health.CoveredBlock, health.CoverageLagBlocks, health.CoverageGaps, health.Malformed, health.Unsupported,
                            health.Duplicates, queue.Reader.Count, health.NormalizationPending, health.EnrichmentPending,
                            health.DeliveryPending, health.DeliveryFailed, PolymarketPolygonHealth.BacklogCountLimit);
                    }
                    // No quiet-wallet eth_getLogs polling: only subscription updates/reconnect invoke recovery.
                }
            }
        }
    }
    private async Task<long> FinalBoundaryAsync(long head, CancellationToken ct)
    {
        var boundary = Options.FinalityMode == PolymarketWireTokens.FinalizedMode
            ? Math.Min(head, await rpc.FinalizedHeadAsync(ct)) : Math.Max(0, head - Options.ConfirmationDepth);
        health.CanonicalBoundary = boundary;
        return boundary;
    }
    private Filter[] Filters(string[] sources)
    {
        var wallets = sources.Select(PolymarketContractRegistry.WalletTopic).ToArray();
        var exchanges = PolymarketContractRegistry.Exchanges.Where(e => Options.Families.Contains(e.Family)).Select(e => e.Address).ToArray();
        return
        [
            new(exchanges, [new[] { PolymarketContractRegistry.CurrentFill, PolymarketContractRegistry.LegacyFill }, null, wallets]),
            new([PolymarketContractRegistry.Positions], [PolymarketContractRegistry.Redemption, wallets]),
            new([PolymarketContractRegistry.AutoRedeemer], [new[] { PolymarketContractRegistry.AutoRedemption, PolymarketContractRegistry.AutoBinaryRedemption, PolymarketContractRegistry.AutoNegativeRedemption }, wallets]),
            new([PolymarketContractRegistry.Positions, PolymarketContractRegistry.PositionManager], [new[] { PolymarketContractRegistry.TransferSingle, PolymarketContractRegistry.TransferBatch }, null, wallets]),
            new(["0x1000008dd9001b968442c1000017eae6e0da00ba", "0x200000900045e3b6259600682756002200028933"], [PolymarketContractRegistry.PositionRedemption, null, null, wallets])
        ];
    }
    private async Task<T> StoreAsync<T>(Func<PolymarketPolygonStore, Task<T>> action)
    { using var scope = scopes.CreateScope(); return await action(scope.ServiceProvider.GetRequiredService<PolymarketPolygonStore>()); }
    private sealed class RecoveryBudgetException : Exception;
}
