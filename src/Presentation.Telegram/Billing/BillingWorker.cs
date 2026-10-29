using Application.Billing;
using Domain.Entities;
using Infrastructure.Billing;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Presentation.Telegram.Services;
using Telegram.Bot;
using Telegram.Bot.Types.Payments;
using Npgsql;

namespace Presentation.Telegram.Billing;

/// <summary>Bounded expiry/projection refresh and durable reconciliation. Sales flags do not stop accounting.</summary>
public sealed class BillingWorker(IServiceScopeFactory scopes, ILogger<BillingWorker> logger, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.OpenConnectionAsync(ct);
                var connection = (NpgsqlConnection)db.Database.GetDbConnection();
                await using var lease = new NpgsqlCommand("SELECT pg_try_advisory_lock(74230003)", connection);
                if (!(bool)(await lease.ExecuteScalarAsync(ct))!) { await Task.Delay(TimeSpan.FromSeconds(10), ct); continue; }
                try
                {
                var now = clock.GetUtcNow();
                await ReconcileCryptoAsync(scope.ServiceProvider, ct);
                var bot = scope.ServiceProvider.GetService<ITelegramBotClient>();
                if (bot is not null)
                {
                    await ReconcileStarsAsync(scope.ServiceProvider, bot, ct);
                    var notices = await db.Set<BillingOutbox>().Where(x => x.SentAt == null && x.RetryAt <= now).OrderBy(x => x.CreatedAt).Take(10).ToListAsync(ct);
                    foreach (var notice in notices)
                    {
                        try
                        {
                            await scope.ServiceProvider.GetRequiredService<TelegramGlobalRateLimiter>().WaitAsync(notice.UserId, ct);
                            await bot.SendMessage(notice.UserId, BillingNoticeText.For(notice.Kind), cancellationToken: ct); notice.SentAt = clock.GetUtcNow();
                        }
                        catch (Exception) when (!ct.IsCancellationRequested) { notice.Attempts++; notice.RetryAt = now.AddMinutes(Math.Min(1440, Math.Pow(2, Math.Min(notice.Attempts, 10)))); }
                        await db.SaveChangesAsync(ct);
                    }
                }
                // Retain ledger identities; raw authenticated callback bodies have a bounded retention window.
                await db.Set<PaymentReceipt>().Where(x => x.ReceivedAt < now.AddDays(-30) && x.Evidence != "" && x.Evidence != BillingDiagnostics.ImportedEvidence).ExecuteUpdateAsync(s => s.SetProperty(x => x.Evidence, ""), ct);
                await db.SaveChangesAsync(ct);
                }
                finally { if (connection.State == System.Data.ConnectionState.Open) { await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(74230003)", connection); await unlock.ExecuteScalarAsync(CancellationToken.None); } }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(BillingDiagnostics.WorkerFailure + " {FailureType}", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
    }
    internal async Task ReconcileCryptoAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>(); var client = services.GetRequiredService<NowPaymentsClient>();
        var now = clock.GetUtcNow();
        var processed = new HashSet<string>(StringComparer.Ordinal);
        // Receipt identities have a separate budget: invoice-only attempts cannot starve real evidence.
        var identities = await db.Set<PaymentReceipt>().Where(x => x.Provider == BillingPlans.CryptoProvider
            && x.ChargeId != "" && x.RetryAt <= now).GroupBy(x => x.ChargeId)
            .Select(g => new { Id = g.Key, Due = g.Min(x => x.RetryAt) }).OrderBy(x => x.Due).Take(20).ToListAsync(ct);
        foreach (var identity in identities)
        {
            processed.Add(identity.Id);
            try
            {
                var callback = await db.Set<PaymentReceipt>().Where(x => x.Provider == BillingPlans.CryptoProvider && x.ChargeId == identity.Id
                    && (x.State == BillingStates.Inbox || x.State == BillingStates.AwaitingProvider) && x.EventKey.StartsWith("ipn:"))
                    .OrderBy(x => x.ReceivedAt).FirstOrDefaultAsync(ct);
                PaymentReceipt transport;
                if (callback is not null) { await client.ProcessInboxAsync(callback, ct); transport = callback; }
                else
                {
                    await client.PollAsync(identity.Id, ct);
                    var key = BillingKeys.Discovered(identity.Id);
                    transport = await db.Set<PaymentReceipt>().SingleAsync(x => x.Provider == BillingPlans.CryptoProvider && x.EventKey == key, ct);
                }
                await db.Set<PaymentReceipt>().Where(x => x.Provider == BillingPlans.CryptoProvider && x.ChargeId == identity.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RetryAt, transport.RetryAt), ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                var receipts = await db.Set<PaymentReceipt>().Where(x => x.Provider == BillingPlans.CryptoProvider && x.ChargeId == identity.Id).ToListAsync(ct);
                foreach (var item in receipts) { item.RetryAt = FailureRetry(now, item.Retries++, ex); }
                var retryAt = receipts.Count == 0 ? FailureRetry(now, 0, ex) : receipts.Max(x => x.RetryAt);
                await db.Set<PaymentAttempt>().Where(x => x.Provider == BillingPlans.CryptoProvider && x.ExternalId == identity.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RetryAt, retryAt), ct);
                await db.SaveChangesAsync(ct);
            }
        }
        var attempts = await db.Set<PaymentAttempt>().Where(x => x.Provider == BillingPlans.CryptoProvider && x.ExternalId != null && x.RetryAt <= now)
            .OrderBy(x => x.RetryAt).ThenBy(x => x.CreatedAt).Take(20).ToListAsync(ct);
        foreach (var attempt in attempts)
        {
            try { if (processed.Add(attempt.ExternalId!)) await client.PollAsync(attempt.ExternalId!, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                attempt.RetryAt = FailureRetry(now, attempt.Retries++, ex);
                await db.Set<PaymentReceipt>().Where(x => x.Provider == BillingPlans.CryptoProvider && x.ChargeId == attempt.ExternalId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RetryAt, attempt.RetryAt), ct);
            }
            if (attempt.RetryAt <= now) attempt.RetryAt = now.AddMinutes(attempt.State is BillingStates.Settled or BillingStates.Reversed ? 15 : 1);
            await db.SaveChangesAsync(ct);
        }
        // Bounded compatibility transition; legacy credentials states are data, never an authentication requirement.
        var unknown = await db.Set<PaymentAttempt>().Where(x => x.Provider == BillingPlans.CryptoProvider && x.ExternalId == null && x.RetryAt <= now)
            .OrderBy(x => x.RetryAt).ThenBy(x => x.CreatedAt).Take(20).ToListAsync(ct);
        foreach (var attempt in unknown)
        {
            if (attempt.State is not (BillingStates.Settled or BillingStates.Reversed))
            {
                var expiry = await db.Set<PurchaseOrder>().Where(x => x.Id == attempt.OrderId).Select(x => x.CheckoutExpiresAt).SingleAsync(ct);
                if (attempt.InvoiceId is null) { if (attempt.State != BillingStates.Creating) attempt.State = BillingStates.CreationUnknown; }
                else attempt.State = expiry <= now ? BillingStates.IdentityAttention : BillingStates.AwaitingIdentity;
            }
            attempt.RetryAt = now.AddMinutes(15);
        }
        await db.SaveChangesAsync(ct);
    }

    private static DateTimeOffset FailureRetry(DateTimeOffset now, int retries, Exception failure) =>
        failure is BillingProviderRetryException retry ? now.Add(retry.Delay).AddSeconds(Random.Shared.Next(15)) : now.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, Math.Min(retries, 7))) + Random.Shared.Next(30));

    internal async Task ReconcileStarsAsync(IServiceProvider services, ITelegramBotClient bot, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>(); var billing = services.GetRequiredService<IBillingService>();
        var checkpoint = await db.Set<BillingCheckpoint>().SingleOrDefaultAsync(x => x.Key == BillingStates.StarsHistory, ct);
        if (checkpoint is null) { checkpoint = new() { Key = BillingStates.StarsHistory }; db.Add(checkpoint); await db.SaveChangesAsync(ct); }
        if (checkpoint.RetryAt > clock.GetUtcNow()) return;
        try
        {
            // Persisted page progress and overlap handle insertions at the beginning; each complete sweep returns to zero.
            var transactions = await bot.GetStarTransactions(checked((int)checkpoint.Offset), 100, ct);
            foreach (var t in transactions.Transactions)
            {
                var reversal = t.Receiver is TransactionPartnerUser;
                var partner = (reversal ? t.Receiver : t.Source) as TransactionPartnerUser;
                if (partner is null || partner.TransactionType != TransactionPartnerUserTransactionType.InvoicePayment) continue;
                var confident = Guid.TryParseExact(partner.InvoicePayload, "N", out var attempt) && (t.NanostarAmount ?? 0) == 0;
                if (reversal && !confident && (t.NanostarAmount ?? 0) == 0)
                {
                    var original = await db.Set<PaymentReceipt>().AsNoTracking().SingleOrDefaultAsync(x => x.Provider == BillingPlans.StarsProvider
                        && x.ChargeId == t.Id && x.UserId == partner.User.Id && (x.State == BillingStates.Applied || x.State == BillingStates.DuplicatePayment) && !x.Reversal, ct);
                    if (original?.AttemptId is { } known && original.Amount == Math.Abs(t.Amount)) { attempt = known; confident = true; }
                }
                await billing.ReceiveAsync(new(BillingPlans.StarsProvider, (reversal ? BillingKeys.Refund(t.Id) : BillingKeys.Paid(t.Id)), t.Id,
                    confident ? attempt : null, partner.User.Id, BillingPlans.Xtr, reversal ? Math.Abs(t.Amount) : t.Amount, reversal, Verified: confident), ct);
            }
            checkpoint.Offset = transactions.Transactions.Length < 100 ? 0 : checkpoint.Offset + 90;
            checkpoint.RetryAt = clock.GetUtcNow().AddSeconds(checkpoint.Offset == 0 ? 120 : 5);
            checkpoint.Attention = null;
        }
        catch (Exception) when (!ct.IsCancellationRequested) { checkpoint.RetryAt = clock.GetUtcNow().AddMinutes(2); checkpoint.Attention = BillingDiagnostics.Attention; }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Every host refreshes its own entitlement cache independently of payment-provider latency and financial ownership.</summary>
public sealed class BillingProjectionWorker(IServiceScopeFactory scopes, ILogger<BillingProjectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                long after = 0;
                while (!ct.IsCancellationRequested)
                {
                    using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var owners = await db.TrackedWallets.Select(x => x.UserId).Union(db.Set<BillingAccount>().Select(x => x.UserId))
                        .Where(x => x > after).Distinct().OrderBy(x => x).Take(50).ToArrayAsync(ct);
                    if (owners.Length == 0) break;
                    foreach (var owner in owners)
                    {
                        try { using var ownerScope = scopes.CreateScope(); await ownerScope.ServiceProvider.GetRequiredService<IBillingService>().RefreshAsync(owner, ct); }
                        catch (Exception) when (!ct.IsCancellationRequested) { logger.LogWarning(BillingDiagnostics.WorkerFailure); }
                    }
                    after = owners[^1];
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning(BillingDiagnostics.WorkerFailure); }
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
    }
}

/// <summary>Stable notice copy; external send failure never undoes ledger settlement.</summary>
public static class BillingNoticeText
{
    public static string For(string kind) => kind switch
    {
        BillingStates.Activated => "Pro purchase received. Your 30-day paid period is recorded. Open /subscription for your receipt and dates.",
        BillingStates.Expired => "Pro has expired. Your saved preferences are preserved; your Free selection remains active. /subscription",
        BillingStates.Reversed => "Your refund was recorded. Paid periods from other purchases or payment methods are preserved. See /subscription and /paysupport.",
        _ => "A payment needs review (including duplicate or excess payment). No additional period was granted. Contact /paysupport."
    };
}
