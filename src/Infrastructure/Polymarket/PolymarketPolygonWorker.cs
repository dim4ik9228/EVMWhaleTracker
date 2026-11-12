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
    private DateOnly _socketDay;
    private int _socketMessages;
    private const long OwnerLock = 74220002;
    private sealed record Filter(string[] Address, object?[] Topics);
    private PolymarketPolygonOptions Options => options.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!Options.Enabled || !profileOptions.Value.Enabled) { health.Status = PolymarketPolygonWorkerDiagnostics.OffPolygonTrackingPaused; return; }
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            var retry = TimeSpan.FromSeconds(5);
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
                retry = ex is PolymarketProviderQuotaException quota ? quota.RetryAfter
                    : TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(++failures, 6))));
                if (ex is PolymarketProviderQuotaException)
                    retry = Options.ExtendCooldown(retry);
                if (Options.CooldownRemaining > retry) retry = Options.CooldownRemaining;
                health.Status = ex is PolymarketProviderQuotaException
                    ? PolymarketPolygonWorkerDiagnostics.QuotaPaused : PolymarketPolygonWorkerDiagnostics.ConnectionUnavailable;
                logger.LogInformation(PolymarketPolygonWorkerDiagnostics.RetrySummary,
                    (long)Math.Ceiling(retry.TotalSeconds), DateTimeOffset.UtcNow.Add(retry),
                    rpc is PolymarketPolygonRpc counted ? counted.RequestCount : 0);
                var reason = ex is InvalidDataException && ex.Message.StartsWith(PolymarketPolygonWorkerDiagnostics.SafeFailurePrefix, StringComparison.Ordinal)
                    && !ex.Message.Contains("://", StringComparison.Ordinal) ? ex.Message : ex.GetType().Name;
                logger.LogWarning(PolymarketPolygonWorkerDiagnostics.PolygonSourceReconnectRequired, ex.GetType().Name, reason);
                if (rpc is PolymarketPolygonRpc adapter)
                    logger.LogInformation(PolymarketPolygonWorkerDiagnostics.RequestSummary,
                        adapter.RequestCount, adapter.LogRequestCount, _socketMessages, Options.DailyHttpRequestLimit, Options.DailySocketMessageLimit);
            }
            if (Options.CooldownRemaining > retry) retry = Options.CooldownRemaining;
            await Task.Delay(retry, ct);
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
            await StoreAsync(async s => { await s.ValidateAsync(sources, ct); return true; });
            using var epoch = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            // .NET transport PING/PONG detects a dead quiet connection without RPC subscriptions.
            socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(20);
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(epoch.Token))
            { timeout.CancelAfter(TimeSpan.FromSeconds(Options.TimeoutSeconds)); await socket.ConnectAsync(new Uri(Options.WebSocketEndpoint), timeout.Token); }
            var filters = Filters(sources);
            health.DesiredSubscriptions = filters.Length; health.AcknowledgedSubscriptions = 0;
            var queue = Channel.CreateBounded<PolymarketPolygonFrame>(new BoundedChannelOptions(Options.QueueCapacity)
            { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
            var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var recovered = false;
            var socketChainVerified = false;

            var reader = ReadAsync();
            var processor = ProcessAsync();
            var reconcile = ReconcileAsync();
            var sourceChanges = WatchSourcesAsync();
            Task? recovery = null;
            try
            {
                await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id = 0, method = PolymarketWireTokens.ChainId, @params = System.Array.Empty<object>() }), WebSocketMessageType.Text, true, epoch.Token);
                for (var i = 0; i < filters.Length; i++)
                    await SendAsync(i + 1, new object[] { PolymarketWireTokens.LogSubscription, new { address = filters[i].Address, topics = filters[i].Topics } });
                recovery = RecoverAsync();
                var first = await Task.WhenAny(reader, processor, reconcile, sourceChanges, recovery);
                await first;
                if (first == recovery) await await Task.WhenAny(reader, processor, reconcile, sourceChanges);
            }
            finally
            {
                epoch.Cancel(); socket.Abort(); queue.Writer.TryComplete();
                foreach (var task in new[] { reader, processor, reconcile, sourceChanges, recovery }.Where(t => t is not null))
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
                    var day = DateOnly.FromDateTime(DateTime.UtcNow);
                    if (_socketDay != day) { _socketDay = day; _socketMessages = 0; }
                    if (++_socketMessages > Options.DailySocketMessageLimit)
                    {
                        var reset = PolymarketProviderQuotaException.UntilReset(DateTimeOffset.UtcNow);
                        throw new PolymarketProviderQuotaException(Options.ExtendCooldown(reset));
                    }
                    using var document = JsonDocument.Parse(message.ToArray()); var root = document.RootElement;
                    try { PolymarketProviderQuotaException.ThrowIfQuota(root, Options.QuotaBackoffSeconds); }
                    catch (PolymarketProviderQuotaException quota)
                    {
                        throw new PolymarketProviderQuotaException(Options.ExtendCooldown(quota.RetryAfter));
                    }
                    ThrowIfSocketRpcError(root);
                    if (root.TryGetProperty("id", out var id))
                    {
                        if (id.GetInt32() == 0)
                        {
                            if (!root.TryGetProperty("result", out var chain) || chain.GetString() != PolymarketWireTokens.PolygonChainQuantity) throw new InvalidDataException(PolymarketPolygonWorkerDiagnostics.PolygonSocketChainMismatch);
                            socketChainVerified = true;
                            if (acks.Count == filters.Length) acknowledged.TrySetResult();
                            continue;
                        }
                        if (root.TryGetProperty("error", out _) || !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.String)
                            throw new InvalidDataException(PolymarketPolygonWorkerDiagnostics.PolygonSubscriptionRejected);
                        if (id.GetInt32() < 1 || id.GetInt32() > filters.Length) throw new InvalidDataException(PolymarketPolygonWorkerDiagnostics.UnexpectedPolygonAcknowledgment);
                        acks.Add(id.GetInt32()); subscriptions.Add(result.GetString()!); health.AcknowledgedSubscriptions = acks.Count;
                        if (acks.Count == filters.Length && socketChainVerified) acknowledged.TrySetResult();
                    }
                    else if (root.TryGetProperty("params", out var parameters))
                    {
                        if (!subscriptions.Contains(parameters.GetProperty("subscription").GetString()!)) throw new InvalidDataException(PolymarketPolygonWorkerDiagnostics.UnacknowledgedPolygonSubscription);
                        var result = parameters.GetProperty("result");
                        if (result.TryGetProperty("logIndex", out _)) await queue.Writer.WriteAsync(new(PolymarketPolygonRpc.ParseLog(result)), epoch.Token);
                        else throw new InvalidDataException(PolymarketPolygonWorkerDiagnostics.UnexpectedPolygonAcknowledgment);
                    }
                }
            }
            async Task ProcessAsync()
            {
                // This reader never runs finality, maintenance, or recovery RPC work.
                await PolymarketPolygonFrameProcessor.RunAsync(queue.Reader, Options.QueueCapacity,
                    async (log, token) => await StoreAsync(async s => { await s.PersistAsync([log], token); return true; }),
                    (_, _) => Task.CompletedTask, epoch.Token);
            }
            async Task BarrierAsync()
            {
                var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await queue.Writer.WriteAsync(new(Barrier: barrier), epoch.Token);
                await barrier.Task.WaitAsync(epoch.Token);
            }
            async Task RecoverAsync()
            {
                await acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(Options.TimeoutSeconds), epoch.Token);
                logger.LogInformation(PolymarketPolygonWorkerDiagnostics.PolygonSubscriptionsAcknowledged, health.AcknowledgedSubscriptions, health.DesiredSubscriptions);
                var target = await rpc.HeadAsync(epoch.Token); health.Head = target; health.Status = PolymarketPolygonWorkerDiagnostics.RecoveringBoundedPolygonOverlap;
                await StoreAsync(async s => { await s.InitializeAsync(sources, target, epoch.Token); return true; });
                await CatchupAsync(target);
                var final = await FinalBoundaryAsync(target, epoch.Token);
                await StoreAsync(async s => { await s.FinalizeAsync(final, epoch.Token); await s.PruneAsync(target, epoch.Token); return true; });
                recovered = true;
                await StoreAsync(async s => { await s.RefreshHealthAsync(sources, epoch.Token); return true; });
                health.Status = health.CoverageGaps > 0
                    ? PolymarketPolygonWorkerDiagnostics.DegradedCoverage
                    : PolymarketPolygonWorkerDiagnostics.ActiveCoverage;
            }
            async Task CatchupAsync(long target)
            {
                if (await StoreAsync(s => s.ValidateAsync(sources, epoch.Token)))
                    throw new InvalidDataException(PolymarketPolygonWorkerDiagnostics.PolygonCanonicalRewindRequiresRecovery);
                var from = await StoreAsync(s => s.NextAsync(sources, epoch.Token));
                await PolymarketPolygonCatchup.RunAsync(from, target, Options,
                    filters.Select(filter => new Func<long, long, object>((first, last) => new
                    {
                        address = filter.Address, topics = filter.Topics,
                        fromBlock = PolymarketPolygonRpc.Quantity(first), toBlock = PolymarketPolygonRpc.Quantity(last)
                    })).ToArray(), rpc,
                    async logs => { await StoreAsync(async s => { await s.PersistAsync(logs, epoch.Token); return true; }); },
                    BarrierAsync, block => StoreAsync(s => s.CoveredAsync(sources, block, epoch.Token)), epoch.Token);
            }
            async Task WatchSourcesAsync()
            {
                while (true)
                {
                    await Task.Delay(1000, epoch.Token);
                    if (!(await StoreAsync(s => s.SourcesAsync(epoch.Token))).SequenceEqual(sources)) return;
                }
            }
            async Task ReconcileAsync()
            {
                var ticks = 0;
                var scheduler = new PolymarketPolygonWorkScheduler(Options, TimeProvider.System);
                while (true)
                {
                    await Task.Delay(1000, epoch.Token);
                    if (recovered)
                    {
                        await scheduler.TickAsync(
                            () => StoreAsync(s => s.HasPendingAsync(sources, epoch.Token)),
                            async () =>
                            {
                                // Drain preceding log persistence before canonical work; this alone cannot prove coverage.
                                var head = await rpc.HeadAsync(epoch.Token);
                                await BarrierAsync();
                                health.Head = head;
                                var final = await FinalBoundaryAsync(head, epoch.Token);
                                await StoreAsync(async s =>
                                {
                                    if (await s.ValidateAsync(sources, epoch.Token))
                                        throw new InvalidDataException(PolymarketPolygonWorkerDiagnostics.PolygonCanonicalRewindRequiresRecovery);
                                    await s.FinalizeAsync(final, epoch.Token);
                                    return true;
                                });
                                // Timer/head reads do not prove filtered stream coverage. Only the audit below
                                // or reconnect recovery may advance the durable cursor.
                            },
                            async () => { await StoreAsync(async s => { await s.PruneAsync(health.Head, epoch.Token); return true; }); },
                            async () =>
                            {
                                var target = await FinalBoundaryAsync(await rpc.HeadAsync(epoch.Token), epoch.Token);
                                await CatchupAsync(target);
                            });
                    }
                    if (++ticks % 60 == 0)
                    {
                        await StoreAsync(async s => { await s.RefreshHealthAsync(sources, epoch.Token); return true; });
                        if (rpc is PolymarketPolygonRpc adapter)
                            logger.LogInformation(PolymarketPolygonWorkerDiagnostics.RequestSummary,
                                adapter.RequestCount, adapter.LogRequestCount, _socketMessages, Options.DailyHttpRequestLimit, Options.DailySocketMessageLimit);
                        logger.LogInformation(PolymarketPolygonWorkerDiagnostics.HealthSummary,
                            health.ChainVerified, health.AcknowledgedSubscriptions, health.DesiredSubscriptions, health.Head, health.CanonicalBoundary,
                            health.CoveredBlock, health.CoverageLagBlocks, health.CoverageGaps, health.Malformed, health.Unsupported,
                            health.Duplicates, queue.Reader.Count, health.NormalizationPending, health.EnrichmentPending,
                            health.DeliveryPending, health.DeliveryFailed, PolymarketPolygonHealth.BacklogCountLimit);
                    }
                    // Sparse audits prove coverage; log subscriptions remain the immediate detection path.
                }
            }
        }
    }
    internal static void ThrowIfSocketRpcError(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error) || error.ValueKind == JsonValueKind.Null) return;
        var code = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
        throw new InvalidDataException(PolymarketPolygonWorkerDiagnostics.PolygonSocketRpcRejected(code));
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
}
