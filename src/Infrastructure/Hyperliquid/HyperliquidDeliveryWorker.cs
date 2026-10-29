using Application.Billing;
using Domain.Common;
using System.Text.Json;
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

/// <summary>Durable owner-specific outbox with bounded concurrent chats and generation checks immediately before sending.</summary>
public sealed class HyperliquidDeliveryWorker(IServiceScopeFactory scopes, IOptions<HyperliquidOptions> options,
    IHyperliquidNotificationSender sender, ILogger<HyperliquidDeliveryWorker> logger) : BackgroundService
{
    private long _lastRecipient = long.MinValue;
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return;
        var healthTick = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entitlement = scope.ServiceProvider.GetService<IEntitlementPolicy>();
        var recipientAccess = scope.ServiceProvider.GetService<IRecipientAccess>();
                var now = DateTimeOffset.UtcNow;
                if (++healthTick % 240 == 0)
                    logger.LogInformation(HyperliquidDeliveryWorkerDiagnostics.HyperliquidOutboxBacklogFailures,
                        await db.Set<HyperliquidDelivery>().CountAsync(x => x.State == RuntimeStates.Delivery.Pending, ct),
                        await db.Set<HyperliquidDelivery>().CountAsync(x => x.State == RuntimeStates.Delivery.Failed, ct));
                var dueUsers = db.Set<HyperliquidDelivery>().Where(x => x.State == RuntimeStates.Delivery.Pending && x.DueAt <= now)
                    .Select(x => x.UserId).Distinct();
                // A bounded cursor rotates across recipients even when earlier chats never drain.
                var users = await dueUsers.Where(id => id > _lastRecipient).OrderBy(id => id).Take(4).ToListAsync(ct);
                if (users.Count == 0) users = await dueUsers.OrderBy(id => id).Take(4).ToListAsync(ct);
                if (users.Count > 0) _lastRecipient = users[^1];
                await Task.WhenAll(users.Select(user => SendUserAsync(user, ct)));
                await Task.Delay(250, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, HyperliquidDeliveryWorkerDiagnostics.HyperliquidDurableDeliverySchedulingFailed); await Task.Delay(2000, ct); }
        }
    }

    private async Task SendUserAsync(long userId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entitlement = scope.ServiceProvider.GetService<IEntitlementPolicy>();
        var recipientAccess = scope.ServiceProvider.GetService<IRecipientAccess>();
        await db.Database.OpenConnectionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        // Separate namespace from ingestion/admission. Shared by delivery hosts, per recipient.
        await using var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(7421, @key)", connection);
        cmd.Parameters.AddWithValue("key", unchecked((int)userId));
        if (!(bool)(await cmd.ExecuteScalarAsync(ct))!) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var pending = await db.Set<HyperliquidDelivery>().Where(x => x.State == RuntimeStates.Delivery.Pending && x.UserId == userId && x.DueAt <= now)
                .OrderBy(x => x.DueAt).Take(100).ToListAsync(ct);
            if (pending.Count == 0) return;
            var first = pending[0];
            var setting = await db.Set<HyperliquidSetting>().AsNoTracking().SingleOrDefaultAsync(x => x.WalletId == first.WalletId, ct);
            var wallet = await db.TrackedWallets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == first.WalletId && x.UserId == userId, ct);
            if (setting?.Enabled != true || setting.BillingPaused || entitlement?.Allows(first.WalletId, TrackingMode.Hyperliquid) == false || setting.Generation != first.Generation || wallet is null)
            { first.State = RuntimeStates.Delivery.Cancelled; await db.SaveChangesAsync(ct); return; }
            var events = await db.Set<HyperliquidEvent>().Where(x => pending.Select(d => d.EventId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            var activity = JsonSerializer.Deserialize<HyperliquidActivity>(events[first.EventId].Payload)!;
            var selected = new List<HyperliquidDelivery> { first };
            var activities = new List<HyperliquidActivity> { activity };
            if (activity.Stream == HyperliquidTokens.Streams.Fills)
            {
                var bucket = activity.Time / options.Value.AggregationMilliseconds;
                foreach (var d in pending.Skip(1).Where(x => x.WalletId == first.WalletId && x.Generation == first.Generation))
                {
                    var a = JsonSerializer.Deserialize<HyperliquidActivity>(events[d.EventId].Payload)!;
                    if (a.Stream == activity.Stream && a.Market == activity.Market && a.OrderId == activity.OrderId && a.Action == activity.Action && a.PositionSide == activity.PositionSide
                        && a.Time / options.Value.AggregationMilliseconds == bucket) { selected.Add(d); activities.Add(a); }
                }
            }
            // Preferences can change after durable fan-out; each original fill still needs to qualify.
            var eligible = activities.Where(a => a.Time >= setting.ActivatedAt.ToUnixTimeMilliseconds() && !a.LiquidatedVictim
                && (a.Usd is null || a.Usd >= wallet.MinimumUsdValue)).ToList();
            if (eligible.Count == 0) { foreach (var d in selected) d.State = RuntimeStates.Delivery.Cancelled; await db.SaveChangesAsync(ct); return; }
            var fresh = await db.Set<HyperliquidSetting>().AsNoTracking().SingleOrDefaultAsync(x => x.WalletId == wallet.Id, ct);
            if (fresh?.Enabled != true || fresh.BillingPaused || fresh.Generation != first.Generation) return;
            var success = false;
            try { success = await sender.SendAsync(userId, new HyperliquidNotification(wallet.Id, wallet.Alias ?? wallet.Address.Value, wallet.Address.Value, eligible), async token =>
                {
                    var current = await db.Set<HyperliquidSetting>().AsNoTracking().SingleOrDefaultAsync(x => x.WalletId == wallet.Id, token);
                    var currentWallet = await db.TrackedWallets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == wallet.Id && x.UserId == userId, token);
                    if (recipientAccess is not null && !await recipientAccess.AllowsAsync(userId, wallet.Id, TrackingMode.Hyperliquid, null, null, token)) return false;
                    return entitlement?.Allows(wallet.Id, TrackingMode.Hyperliquid) != false && current?.Enabled == true && !current.BillingPaused && current.Generation == first.Generation
                        && currentWallet is not null && eligible.All(a => a.Usd is null || a.Usd >= currentWallet.MinimumUsdValue);
                }, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, HyperliquidDeliveryWorkerDiagnostics.CoreDeliveryTransportFailedForRecipient, userId); }
            var afterAttempt = await db.Set<HyperliquidSetting>().AsNoTracking().SingleOrDefaultAsync(x => x.WalletId == wallet.Id, ct);
            var optedOut = afterAttempt?.Enabled != true || afterAttempt.Generation != first.Generation;
            foreach (var d in selected)
            {
                if (optedOut) { d.State = RuntimeStates.Delivery.Cancelled; continue; }
                d.Attempts++;
                d.State = success ? RuntimeStates.Delivery.Sent : d.Attempts >= 5 ? RuntimeStates.Delivery.Failed : RuntimeStates.Delivery.Pending;
                d.DueAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, d.Attempts) * 5));
            }
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            if (connection.State == System.Data.ConnectionState.Open)
            {
                await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(7421, @key)", connection);
                unlock.Parameters.AddWithValue("key", unchecked((int)userId));
                await unlock.ExecuteScalarAsync(CancellationToken.None);
            }
        }
    }

}
