using Application.Billing;
using Domain.Common;
using System.Text.Json;
using Application.Polymarket;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Infrastructure.Polymarket;

/// <summary>Individual canonical activities share Telegram budgets and recheck recipient generation after limiter waits.</summary>
public sealed class PolymarketDeliveryWorker(IServiceScopeFactory scopes, IOptions<PolymarketOptions> options,
    IOptions<PolymarketPolygonOptions> polygon, IPolymarketNotificationSender sender,
    ILogger<PolymarketDeliveryWorker> logger) : BackgroundService
{
    private long _lastUser = long.MinValue;
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled || !polygon.Value.Enabled) return;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entitlement = scope.ServiceProvider.GetService<IEntitlementPolicy>();
        var recipientAccess = scope.ServiceProvider.GetService<IRecipientAccess>();
                var now = DateTimeOffset.UtcNow;
                var due = db.Set<PolymarketDelivery>().Where(x => x.State == RuntimeStates.Delivery.Pending && x.DueAt <= now).Select(x => x.UserId).Distinct();
                var users = await due.Where(x => x > _lastUser).OrderBy(x => x).Take(4).ToArrayAsync(ct);
                if (users.Length == 0) users = await due.OrderBy(x => x).Take(4).ToArrayAsync(ct);
                if (users.Length != 0) _lastUser = users[^1];
                await Task.WhenAll(users.Select(user => SendAsync(user, ct)));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, PolymarketDeliveryWorkerDiagnostics.PolymarketOutboxSchedulingFailed); }
            await Task.Delay(500, ct);
        }
    }
    private async Task SendAsync(long user, CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entitlement = scope.ServiceProvider.GetService<IEntitlementPolicy>();
        var recipientAccess = scope.ServiceProvider.GetService<IRecipientAccess>();
        await db.Database.OpenConnectionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var acquire = new NpgsqlCommand("SELECT pg_try_advisory_lock(7422, @key)", connection);
        acquire.Parameters.AddWithValue("key", unchecked((int)user));
        if (!(bool)(await acquire.ExecuteScalarAsync(ct))!) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var delivery = await db.Set<PolymarketDelivery>().Where(x => x.UserId == user && x.State == RuntimeStates.Delivery.Pending && x.DueAt <= now)
                .OrderBy(x => x.DueAt).FirstOrDefaultAsync(ct);
            if (delivery is null) return;
            var source = await db.Set<PolymarketEvent>().AsNoTracking().SingleAsync(x => x.Id == delivery.EventId, ct);
            var activity = JsonSerializer.Deserialize<PolymarketActivity>(source.Payload)!;
            async Task<bool> Eligible(CancellationToken token)
            {
                if (!options.Value.Enabled || !polygon.Value.Enabled) return false;
                if (recipientAccess is not null && !await recipientAccess.AllowsAsync(user, delivery.WalletId, TrackingMode.Polymarket, null, null, token)) return false;
                var setting = await db.Set<PolymarketSetting>().AsNoTracking().SingleOrDefaultAsync(x => x.WalletId == delivery.WalletId, token);
                var wallet = await db.TrackedWallets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == delivery.WalletId && x.UserId == user, token);
                var canonical = await db.Set<PolymarketEvent>().AsNoTracking().AnyAsync(x => x.Id == source.Id && x.Canonical, token);
                return entitlement?.Allows(delivery.WalletId, TrackingMode.Polymarket) != false && wallet is not null && canonical && setting?.Enabled == true && !setting.BillingPaused && !setting.NeedsAttention
                    && setting.Generation == delivery.Generation && setting.SourceAddress == source.SourceAddress
                    && (activity.Action == PolymarketTokens.Actions.Correction || setting.ActivationBlock < source.BlockNumber)
                    && (activity.Action is PolymarketTokens.Actions.Redeem or PolymarketTokens.Actions.Correction || activity.CashValue is { } cash && cash >= setting.MinimumTradeValue);
            }
            if (!await Eligible(ct)) { delivery.State = RuntimeStates.Delivery.Cancelled; await db.SaveChangesAsync(ct); return; }
            var wallet = await db.TrackedWallets.AsNoTracking().SingleAsync(x => x.Id == delivery.WalletId && x.UserId == user, ct);
            var sent = false;
            try { sent = await sender.SendAsync(user, new(wallet.Id, wallet.Alias ?? wallet.Address.Value, activity), Eligible, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogWarning(PolymarketDeliveryWorkerDiagnostics.PolymarketRecipientAttemptFailed, ex.GetType().Name); }
            delivery.Attempts++;
            delivery.State = sent ? RuntimeStates.Delivery.Sent : !await Eligible(ct) ? RuntimeStates.Delivery.Cancelled : delivery.Attempts >= 5 ? RuntimeStates.Delivery.Failed : RuntimeStates.Delivery.Pending;
            delivery.DueAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, delivery.Attempts) * 5));
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            if (connection.State == System.Data.ConnectionState.Open)
            {
                await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(7422, @key)", connection);
                unlock.Parameters.AddWithValue("key", unchecked((int)user)); await unlock.ExecuteScalarAsync(CancellationToken.None);
            }
        }
    }
}
