using System.Text.Json;
using System.Threading.Channels;
using Application.Hyperliquid;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Infrastructure.Hyperliquid;

/// <summary>One PostgreSQL advisory-lock owner ingests public Core feeds. Lease failure aborts its socket and recovery work.</summary>
public sealed class HyperliquidWorker(IServiceScopeFactory scopes, IHyperliquidSocketFactory sockets,
    IHyperliquidInfoClient info, IOptions<HyperliquidOptions> options, HyperliquidChangeSignal signal,
    ILogger<HyperliquidWorker> logger) : BackgroundService
{
    internal const long OwnerLock = 74210002;
    private static readonly string[] Streams = ["userFills", "userNonFundingLedgerUpdates"];
    private sealed record SocketFrame(string? Message, TaskCompletionSource? Barrier = null);
    private HyperliquidOptions Options => options.Value;
    private DateTimeOffset _nextConnection;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Options.Enabled) return;
        if (!Options.IsValid()) { signal.Status = "Degraded — invalid configuration"; logger.LogError("Invalid Hyperliquid bounds; source disabled"); return; }
        var attempts = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var leaseScope = scopes.CreateScope();
                var db = leaseScope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.OpenConnectionAsync(stoppingToken);
                var connection = (NpgsqlConnection)db.Database.GetDbConnection();
                await using var command = new NpgsqlCommand($"SELECT pg_try_advisory_lock({OwnerLock})", connection);
                if ((bool)(await command.ExecuteScalarAsync(stoppingToken))! != true)
                { await Task.Delay(2000, stoppingToken); continue; }
                using var owner = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var monitor = MonitorLeaseAsync(connection, owner);
                try { await RunOwnerAsync(owner.Token); attempts = 0; }
                finally
                {
                    owner.Cancel();
                    try { await monitor; } catch (OperationCanceledException) { }
                    // Do not return a pooled physical connection while holding a session lock.
                    if (connection.State == System.Data.ConnectionState.Open)
                    {
                        await using var unlock = new NpgsqlCommand($"SELECT pg_advisory_unlock({OwnerLock})", connection);
                        await unlock.ExecuteScalarAsync(CancellationToken.None);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { signal.Status = "Degraded — reconnecting"; logger.LogWarning(ex, "Hyperliquid source reconnect required"); }
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(30000, 1000 * Math.Pow(2, Math.Min(attempts++, 5))) + Random.Shared.Next(250, 1000)), stoppingToken);
        }
    }

    private static async Task MonitorLeaseAsync(NpgsqlConnection connection, CancellationTokenSource owner)
    {
        try
        {
            while (true)
            {
                await Task.Delay(1000, owner.Token);
                await using var cmd = new NpgsqlCommand("SELECT 1", connection) { CommandTimeout = 2 };
                await cmd.ExecuteScalarAsync(owner.Token);
            }
        }
        catch { owner.Cancel(); }
    }

    private async Task RunOwnerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<HyperliquidStore>();
            await store.PruneAsync(Options.RetentionDays, ct);
            await store.ReleaseUnusedReservationsAsync(ct);
            var addresses = await store.AddressesAsync(ct);
            if (addresses.Count > Options.MaxUniqueWallets)
            { signal.Status = "Degraded — capacity configuration"; logger.LogError("Core persisted enrollment exceeds configured capacity"); await Task.Delay(5000, ct); continue; }
            if (addresses.Count == 0) { signal.Status = "Connecting"; await Task.Delay(1000, ct); continue; }
            using var epoch = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await using var socket = sockets.Create();
            signal.Status = "Connecting";
            if (_nextConnection > DateTimeOffset.UtcNow) await Task.Delay(_nextConnection - DateTimeOffset.UtcNow, epoch.Token);
            _nextConnection = DateTimeOffset.UtcNow.AddMilliseconds(2200);
            await socket.ConnectAsync(new Uri(Options.WebSocketUrl), epoch.Token);
            logger.LogInformation("Hyperliquid connected with {UniqueAddresses}/{Capacity} admitted addresses", addresses.Count, Options.MaxUniqueWallets);
            var queue = Channel.CreateBounded<SocketFrame>(new BoundedChannelOptions(Options.QueueCapacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
            var acknowledged = new HashSet<string>();
            var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var lastPong = DateTimeOffset.UtcNow;
            var connectedAt = DateTimeOffset.UtcNow;
            var reader = ReadAsync();
            var heartbeat = HeartbeatAsync();
            var version = signal.Version;
            var reconcile = ReconcileAsync();
            var processor = ProcessAsync();
            Task? recovery = null;
            try
            {
                foreach (var address in addresses)
                    foreach (var stream in Streams)
                        await socket.SendAsync(new { method = "subscribe", subscription = Subscription(stream, address) }, epoch.Token);
                // Reader and persistence run before recovery; overlaps are deduplicated durably.
                recovery = RecoverSubscribedAsync();
                async Task RecoverSubscribedAsync()
                {
                await subscribed.Task.WaitAsync(epoch.Token);
                await RecoverAsync(addresses, async token =>
                {
                    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    await queue.Writer.WriteAsync(new SocketFrame(null, completion), token);
                    await completion.Task.WaitAsync(token);
                }, epoch.Token);
                }
                await Task.WhenAny(reader, heartbeat, reconcile, processor, recovery);
                if (recovery.IsCompletedSuccessfully)
                {
                    await Task.WhenAny(reader, heartbeat, reconcile, processor);
                }
                foreach (var task in new[] { reader, heartbeat, reconcile, processor, recovery })
                    if (task.IsFaulted) await task;
            }
            finally
            {
                epoch.Cancel();
                // Abort before releasing reservations; an uncertain unsubscribe always closes the entire old socket.
                await socket.DisposeAsync();
                try { await Task.WhenAll(new[] { reader, heartbeat, reconcile, processor, recovery ?? Task.CompletedTask }); }
                catch (Exception) when (epoch.IsCancellationRequested) { }
            }
            await store.PruneAsync(Options.RetentionDays, ct);

            async Task ReadAsync()
            {
                while (true)
                {
                    var message = await socket.ReceiveAsync(Options.MaximumFrameBytes, epoch.Token);
                    using var json = JsonDocument.Parse(message);
                    var root = json.RootElement;
                    var channel = root.GetProperty("channel").GetString();
                    if (channel == "pong") { lastPong = DateTimeOffset.UtcNow; continue; }
                    if (channel == "subscriptionResponse")
                    {
                        var sub = root.GetProperty("data").GetProperty("subscription");
                        var user = sub.GetProperty("user").GetString()!.ToLowerInvariant();
                        var type = sub.GetProperty("type").GetString()!;
                        if (addresses.Contains(user) && Streams.Contains(type))
                            lock (acknowledged)
                            {
                                acknowledged.Add(user + "|" + type);
                                if (acknowledged.Count == addresses.Count * 2) subscribed.TrySetResult();
                            }
                        continue;
                    }
                    if (channel == "error") throw new IOException("Hyperliquid rejected subscription.");
                    if (channel is not ("userFills" or "userNonFundingLedgerUpdates")) continue;
                    if (!queue.Writer.TryWrite(new SocketFrame(message))) throw new IOException("Hyperliquid durable consumer overloaded; reconnect recovery required.");
                }
            }
            async Task HeartbeatAsync()
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Options.PingSeconds), epoch.Token);
                    if (DateTimeOffset.UtcNow - lastPong > TimeSpan.FromSeconds(Options.PongTimeoutSeconds)) throw new IOException("Hyperliquid pong deadline exceeded.");
                    lock (acknowledged)
                        if (acknowledged.Count != addresses.Count * 2 && DateTimeOffset.UtcNow - connectedAt >= TimeSpan.FromSeconds(Options.PingSeconds))
                            throw new IOException("Hyperliquid subscriptions not acknowledged.");
                    await socket.SendAsync(new { method = "ping" }, epoch.Token);
                    lock (acknowledged)
                        logger.LogInformation("Hyperliquid health: desired {DesiredSubscriptions}, acknowledged {AcknowledgedSubscriptions}, pong age {PongAgeSeconds}, accepted {AcceptedEvents}, duplicates {DuplicateEvents}, status {Status}",
                            addresses.Count * 2, acknowledged.Count, (DateTimeOffset.UtcNow - lastPong).TotalSeconds, signal.Accepted, signal.Duplicates, signal.Status);
                }
            }
            async Task ReconcileAsync()
            {
                var maintenance = 0;
                while (true)
                {
                    await Task.Delay(1000, epoch.Token);
                    using var checkScope = scopes.CreateScope();
                    var current = await checkScope.ServiceProvider.GetRequiredService<HyperliquidStore>().AddressesAsync(epoch.Token);
                    if (++maintenance % 60 == 0)
                        await checkScope.ServiceProvider.GetRequiredService<HyperliquidStore>().PruneAsync(Options.RetentionDays, epoch.Token);
                    // Checking persisted state also catches deletion and changes made by standby hosts.
                    if (signal.Version != version || !current.SequenceEqual(addresses)) return;
                }
            }
            async Task ProcessAsync()
            {
                var markets = await MarketsAsync(epoch.Token);
                await foreach (var frame in queue.Reader.ReadAllAsync(epoch.Token))
                {
                    if (frame.Barrier is { } barrier) { barrier.TrySetResult(); continue; }
                    using var json = JsonDocument.Parse(frame.Message!);
                    var root = json.RootElement;
                    var stream = root.GetProperty("channel").GetString()!;
                    var data = root.GetProperty("data");
                    var address = data.GetProperty("user").GetString()!.ToLowerInvariant();
                    if (!addresses.Contains(address)) continue;
                    var rows = data.GetProperty(stream == "userFills" ? "fills" : "nonFundingLedgerUpdates");
                    var snapshot = data.TryGetProperty("isSnapshot", out var snapshotFlag) && snapshotFlag.ValueKind == JsonValueKind.True;
                    foreach (var row in rows.EnumerateArray())
                        await AcceptRowAsync(address, stream, row, markets, epoch.Token, snapshot);
                }
            }
        }
    }

    private static object Subscription(string stream, string address) => stream == "userFills"
        ? new { type = stream, user = address, aggregateByTime = false } : (object)new { type = stream, user = address };

    private HashSet<string>? _markets;
    private async Task<HashSet<string>> MarketsAsync(CancellationToken ct)
    {
        if (_markets is not null) return _markets;
        using var json = JsonDocument.Parse(await info.QueryAsync(new { type = "meta" }, 20, ct));
        // Native meta defaults to the original USDC perp dex; explicitly reject a different collateral index.
        if (!json.RootElement.TryGetProperty("collateralToken", out var collateral) || collateral.GetInt32() != 0)
            throw new IOException("Unsupported Hyperliquid collateral metadata.");
        return _markets = json.RootElement.GetProperty("universe").EnumerateArray().Select(x => x.GetProperty("name").GetString()!).Where(x => !x.Contains(':') && !x.StartsWith('@')).ToHashSet(StringComparer.Ordinal);
    }

    private async Task AcceptRowAsync(string address, string stream, JsonElement row, HashSet<string> markets, CancellationToken ct, bool snapshot = false)
    {
        var activity = HyperliquidProtocol.Parse(address, stream, row, markets);
        if (activity is null) return;
        using var scope = scopes.CreateScope();
        // Snapshots can include ancient, quiet-account activity whose durable identity has expired.
        // Seed those identities without replaying alerts outside the supported recovery horizon.
        var baseline = snapshot && activity.Time < DateTimeOffset.UtcNow.AddHours(-Options.RecoveryHours).ToUnixTimeMilliseconds();
        await scope.ServiceProvider.GetRequiredService<HyperliquidStore>().AcceptAsync(activity, baseline, ct);
    }

    private async Task RecoverAsync(List<string> addresses, Func<CancellationToken, Task> drainAcceptedLive, CancellationToken ct)
    {
        signal.Status = "Recovering";
        var markets = await MarketsAsync(ct);
        var end = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var degraded = false;
        foreach (var address in addresses)
        foreach (var stream in Streams)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<HyperliquidStore>();
            var checkpoint = await db.Set<HyperliquidCheckpoint>().AsNoTracking().SingleOrDefaultAsync(x => x.Key == address + "|" + stream, ct);
            var earliest = await (from s in db.Set<HyperliquidSetting>() join w in db.TrackedWallets on s.WalletId equals w.Id where s.Enabled select new { s.ActivatedAt, w.Address }).ToListAsync(ct);
            var activation = earliest.Where(x => x.Address.Value.Equals(address, StringComparison.OrdinalIgnoreCase)).Min(x => x.ActivatedAt.ToUnixTimeMilliseconds());
            var requested = checkpoint?.Time > 0 ? checkpoint.Time : activation;
            var lower = DateTimeOffset.UtcNow.AddHours(-Options.RecoveryHours).ToUnixTimeMilliseconds();
            var start = Math.Max(requested, lower);
            logger.LogInformation("Hyperliquid recovery stream {Stream}, interval {Start}..{End}", stream, start, end);
            if (requested < lower) { degraded = true; await store.MarkGapAsync(address, stream, $"Recovery age exceeded: {requested}..{lower}", ct); }
            var complete = false;
            var retentionExhausted = false;
            var recoveredFillIdentities = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                for (var page = 0; page < Options.MaximumRecoveryPages; page++)
                {
                    var kind = stream == "userFills" ? "userFillsByTime" : "userNonFundingLedgerUpdates";
                    object request = stream == "userFills" ? new { type = kind, user = address, startTime = start, endTime = end, aggregateByTime = false }
                        : new { type = kind, user = address, startTime = start, endTime = end };
                    using var json = JsonDocument.Parse(await info.QueryAsync(request, 120, ct));
                    var rows = json.RootElement.EnumerateArray().ToArray();
                    foreach (var row in rows) await AcceptRowAsync(address, stream, row, markets, ct);
                    if (stream == "userFills")
                    {
                        // Includes unsupported markets: they also consume the provider's 10,000-fill retention.
                        foreach (var row in rows) recoveredFillIdentities.Add(HyperliquidProtocol.Canonical(row));
                        if (recoveredFillIdentities.Count >= 10000)
                        {
                            retentionExhausted = true;
                            break;
                        }
                    }
                    var limit = stream == "userFills" ? 2000 : 500;
                    if (rows.Length < limit) { complete = true; break; }
                    var next = rows.Max(x => x.GetProperty("time").GetInt64());
                    if (next <= start) break; // Inclusive boundary cannot be skipped; explicitly report a gap.
                    start = next;
                }
                if (!complete) { degraded = true; await store.MarkGapAsync(address, stream,
                    retentionExhausted ? $"Provider fill retention exhausted: {requested}..{end}" : $"History page/budget boundary: {start}..{end}", ct); }
                else
                {
                    await drainAcceptedLive(ct);
                    await store.CompleteAsync(address, stream, end, ct);
                }
                if (stream != "userFills")
                { degraded = true; await store.MarkGapAsync(address, stream, $"Ledger retention completeness is not guaranteed: {requested}..{end}", ct); }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { degraded = true; logger.LogWarning(ex, "Hyperliquid recovery incomplete for stream {Stream}", stream); await store.MarkGapAsync(address, stream, $"Recovery failed: {start}..{end}", ct); }
        }
        signal.Status = degraded ? "Degraded — live alerts; history may be incomplete" : "Live";
    }
}
