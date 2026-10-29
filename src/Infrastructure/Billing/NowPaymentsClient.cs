using Jint;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Application.Billing;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Infrastructure.Billing;

/// <summary>NOWPayments canonical IPN signing, matching the provider's recursive sort and ECMAScript JSON.stringify numbers.</summary>
public static class NowPaymentsSignature
{
    public static string Canonicalize(string json)
    {
        // Reject duplicate names before JSON.parse would discard evidence. Keep date strings as strings.
        using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(json)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None, FloatParseHandling = Newtonsoft.Json.FloatParseHandling.Double, MaxDepth = 64 };
        _ = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        // Execute the provider algorithm in an ECMAScript engine: JSON.stringify's IEEE754 formatting,
        // Unicode escaping and property ordering must not be approximated by a .NET serializer.
        var engine = new Jint.Engine(o => o.TimeoutInterval(TimeSpan.FromSeconds(1)).MaxStatements(200000).LimitRecursion(128));
        engine.SetValue("ipnJson", json);
        return engine.Evaluate("""
            function sortObject(value) {
                if (Array.isArray(value)) return value.map(sortObject);
                if (value !== null && typeof value === 'object') {
                    return Object.keys(value).sort().reduce(function(result, key) { result[key] = sortObject(value[key]); return result; }, {});
                }
                return value;
            }
            JSON.stringify(sortObject(JSON.parse(ipnJson)));
            """).AsString();
    }
    public static bool Verify(string body, string signature, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || signature.Length != 128) return false;
        try
        {
            var expected = HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret.Trim()), Encoding.UTF8.GetBytes(Canonicalize(body)));
            return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(signature));
        }
        catch (Exception ex) when (ex is FormatException or Newtonsoft.Json.JsonException or InvalidDataException or Jint.Runtime.JavaScriptException or Jint.Runtime.StatementsCountOverflowException or TimeoutException) { return false; }
    }
}

/// <summary>Named billing diagnostics; no raw payloads or credentials are logged.</summary>
public static class BillingDiagnostics
{
    public const string InvalidJson = "Invalid provider JSON.";
    public const string WorkerFailure = "Billing reconciliation failed; durable work will retry.";
    public const string InvalidOptions = "Billing sales require support, HTTPS terms/callback, merchant verification and credentials.";
    public const string ImportedEvidence = "Operator imported payment evidence";
    public const string Attention = "Provider payment requires operator review.";
    public const string AuthenticatedStatus = "Verified authenticated payment status";
    public const string ProRequired = "Paused — Pro required. Open /subscription.";
    public const string FreeSelectionPaused = "Paused — Free selection. Open /subscription.";
    public const string CapacityPaused = "Paused — source capacity unavailable. Retry enabling tracking.";
    public const string VerificationPaused = "Paused — source verification unavailable. Retry enabling tracking.";
}

/// <summary>Provider retry boundary without retaining response bodies or credentials.</summary>
public sealed class BillingProviderRetryException(TimeSpan delay) : HttpRequestException(BillingDiagnostics.Attention)
{
    public TimeSpan Delay { get; } = delay;
}

/// <summary>Hosted checkout adapter. Unknown invoice creation is never blindly retried.</summary>
public sealed class NowPaymentsClient(HttpClient http, IOptions<BillingOptions> options, AppDbContext db, IBillingService billing, TimeProvider clock)
{
    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Add("x-api-key", options.Value.NowPaymentsApiKey);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - clock.GetUtcNow()) ?? TimeSpan.FromMinutes(1);
            throw new BillingProviderRetryException(TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 30, 86400)));
        }
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new BillingProviderRetryException(TimeSpan.FromMinutes(5));
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
    public async Task<PaymentAttempt> CheckoutAsync(long user, Guid order, CancellationToken ct)
    {
        var prepared = await billing.PrepareAttemptAsync(user, order, BillingPlans.CryptoProvider, ct);
        if (prepared.IsFailure) throw new InvalidOperationException(prepared.Error.Description);
        var attempt = prepared.Value;
        var purchase = await db.Set<PurchaseOrder>().AsNoTracking().SingleAsync(x => x.Id == order && x.UserId == user, ct);
        if (attempt.State != BillingStates.Creating) return attempt;
        // Claim before HTTP: a crash between claim and response has the same safe unknown outcome as timeout.
        var claimed = await db.Set<PaymentAttempt>().Where(x => x.Id == attempt.Id && x.State == BillingStates.Creating)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, BillingStates.CreationUnknown), ct);
        if (claimed == 0) { await db.Entry(attempt).ReloadAsync(ct); return attempt; }
        try
        {
            using var minimum = await SendAsync(new(HttpMethod.Get, "min-amount?currency_from=usdtbsc&currency_to=usdtbsc"), ct);
            using var quote = await SendAsync(new(HttpMethod.Get, "estimate?amount=" + purchase.UsdPrice.ToString(CultureInfo.InvariantCulture) + "&currency_from=usd&currency_to=usdtbsc"), ct);
            var estimated = quote.RootElement.GetProperty("estimated_amount").GetDecimal();
            if (estimated < minimum.RootElement.GetProperty("min_amount").GetDecimal()) return attempt;
            using var invoice = await SendAsync(new(HttpMethod.Post, "invoice") { Content = JsonContent.Create(new
            { price_amount = purchase.UsdPrice, price_currency = "usd", pay_currency = BillingPlans.UsdtBsc, order_id = order.ToString("N"), ipn_callback_url = options.Value.CallbackUrl, is_fee_paid_by_user = false }) }, ct);
            await db.Entry(attempt).ReloadAsync(ct);
            attempt.InvoiceId = invoice.RootElement.GetProperty("id").ToString();
            attempt.CheckoutUrl = invoice.RootElement.GetProperty("invoice_url").GetString(); attempt.State = BillingStates.Pending;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested) { await db.Entry(attempt).ReloadAsync(ct); }
        return attempt;
    }

    public async Task ProcessInboxAsync(PaymentReceipt inbox, CancellationToken ct)
    {
        // Serialize primary binding with attachments across hosts. Financial ingestion takes its own transaction locks.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(74230006)", ct);
            try
            {
                await db.Entry(inbox).ReloadAsync(ct); await ProcessInboxCoreAsync(inbox, ct);
                await db.Set<PaymentReceipt>().Where(x => x.Provider == BillingPlans.CryptoProvider && x.ChargeId == inbox.ChargeId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RetryAt, inbox.RetryAt).SetProperty(x => x.Retries, 0), ct);
            }
            finally { await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(74230006)", CancellationToken.None); }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private async Task ProcessInboxCoreAsync(PaymentReceipt inbox, CancellationToken ct)
    {
        // The durable identity is sufficient for authenticated recovery after raw-body retention expires.
        var id = inbox.ChargeId;
        using var status = await SendAsync(new(HttpMethod.Get, "payment/" + Uri.EscapeDataString(id)), ct);
        var p = status.RootElement;
        if (p.GetProperty("payment_id").ToString() != id) { inbox.State = BillingStates.Review; inbox.RetryAt = clock.GetUtcNow().AddMinutes(15); await db.SaveChangesAsync(ct); return; }
        var invoice = p.TryGetProperty("invoice_id", out var inv) ? inv.ToString() : null;
        var orderId = p.GetProperty("order_id").GetString();
        var attempt = await db.Set<PaymentAttempt>().SingleOrDefaultAsync(x => x.Provider == BillingPlans.CryptoProvider && x.InvoiceId == invoice && x.InvoiceId != null, ct);
        if (attempt is not null) await db.Entry(attempt).ReloadAsync(ct);
        if (attempt is null)
        {
            // An IPN may win the race against saving the invoice response. Keep retrying durable
            // evidence rather than permanently stranding a genuine payment after its first callback.
            inbox.State = inbox.Evidence == BillingDiagnostics.ImportedEvidence ? BillingStates.Review : inbox.ReceivedAt.AddHours(24) > clock.GetUtcNow() ? BillingStates.AwaitingProvider : BillingStates.Review;
            inbox.RetryAt = clock.GetUtcNow().AddMinutes(inbox.State == BillingStates.Review ? 15 : 1); await db.SaveChangesAsync(ct); return;
        }
        // Never show foreign-order evidence in this invoice owner's receipt history.
        inbox.AttemptId = orderId == attempt.OrderId.ToString("N") ? attempt.Id : null;
        if (inbox.Evidence == BillingDiagnostics.ImportedEvidence && attempt.ExternalId is null)
        {
            inbox.State = BillingStates.Review; inbox.RetryAt = clock.GetUtcNow().AddMinutes(15); await db.SaveChangesAsync(ct); return;
        }
        if (orderId != attempt.OrderId.ToString("N") || attempt.ExternalId is not null && attempt.ExternalId != id)
        {
            var observedAmount = p.TryGetProperty("actually_paid", out var observedPaid) && observedPaid.ValueKind != JsonValueKind.Null ? Amount(observedPaid) : 0m;
            if (orderId == attempt.OrderId.ToString("N") && p.GetProperty("payment_status").GetString() == "refunded")
            {
                // Older ledgers can contain accepted duplicate child charges. Reconcile their
                // individual reversal without replacing the primary identity or its frozen quote.
                var original = await db.Set<PaymentReceipt>().AsNoTracking().Where(x => x.Provider == BillingPlans.CryptoProvider
                    && x.ChargeId == id && x.AttemptId == attempt.Id && !x.Reversal
                    && (x.State == BillingStates.Applied || x.State == BillingStates.DuplicatePayment))
                    .OrderByDescending(x => x.State == BillingStates.Applied).ThenBy(x => x.ReceivedAt).FirstOrDefaultAsync(ct);
                var purchase = await db.Set<PurchaseOrder>().AsNoTracking().SingleAsync(x => x.Id == attempt.OrderId, ct);
                var quote = Amount(p.GetProperty("pay_amount"));
                var validRefund = original is not null && p.GetProperty("pay_currency").GetString() == BillingPlans.UsdtBsc
                    && quote > 0 && attempt.ExpectedAmount == quote && p.GetProperty("price_currency").GetString() == "usd"
                    && Amount(p.GetProperty("price_amount")) == purchase.UsdPrice
                    && (!p.TryGetProperty("network", out var childNetwork) || childNetwork.ValueKind == JsonValueKind.Null
                        || string.Equals(childNetwork.GetString(), "bsc", StringComparison.OrdinalIgnoreCase));
                await billing.ReceiveAsync(new(BillingPlans.CryptoProvider, BillingKeys.Refund(id), id, attempt.Id, null,
                    p.GetProperty("pay_currency").GetString()!, original?.Amount ?? observedAmount, Reversal: true,
                    Evidence: BillingDiagnostics.AuthenticatedStatus, Verified: validRefund), ct);
                inbox.State = validRefund ? BillingStates.Processed : BillingStates.Review;
                inbox.RetryAt = clock.GetUtcNow().AddMinutes(15); await db.SaveChangesAsync(ct); return;
            }
            await billing.ReceiveAsync(new(BillingPlans.CryptoProvider, BillingKeys.Settlement(id, observedAmount) + ":review", id,
                orderId == attempt.OrderId.ToString("N") ? attempt.Id : null, null,
                p.GetProperty("pay_currency").GetString()!, observedAmount, Evidence: BillingDiagnostics.AuthenticatedStatus, Verified: false), ct);
            inbox.State = BillingStates.Review; inbox.RetryAt = clock.GetUtcNow().AddMinutes(15); await db.SaveChangesAsync(ct); return;
        }
        attempt.ExternalId = id;
        if (attempt.State is BillingStates.AwaitingIdentity or BillingStates.IdentityAttention or BillingStates.ReconciliationNeedsCredentials or BillingStates.CreationUnknown)
            attempt.State = BillingStates.Pending;
        var currency = p.GetProperty("pay_currency").GetString()!;
        var order = await db.Set<PurchaseOrder>().AsNoTracking().SingleAsync(x => x.Id == attempt.OrderId, ct);
        var expected = Amount(p.GetProperty("pay_amount"));
        var actual = p.TryGetProperty("actually_paid", out var paid) && paid.ValueKind != JsonValueKind.Null ? Amount(paid) : 0m;
        // Freeze the first authenticated customer quote; later incompatible quotes require review.
        var networkValid = !p.TryGetProperty("network", out var network) || network.ValueKind == JsonValueKind.Null || network.GetString()?.Equals("bsc", StringComparison.OrdinalIgnoreCase) == true;
        var valid = currency == BillingPlans.UsdtBsc && networkValid && expected > 0 && (attempt.ExpectedAmount is null || attempt.ExpectedAmount == expected)
            && p.GetProperty("price_currency").GetString() == "usd" && Amount(p.GetProperty("price_amount")) == order.UsdPrice;
        attempt.ExpectedAmount ??= expected;
        var state = p.GetProperty("payment_status").GetString();
        await db.SaveChangesAsync(ct);
        if (state == "finished")
        {
            await billing.ReceiveAsync(new(BillingPlans.CryptoProvider, BillingKeys.Settlement(id, actual), id, attempt.Id, null, currency, actual,
                Evidence: BillingDiagnostics.AuthenticatedStatus, Verified: valid && actual >= expected), ct);
            inbox.State = BillingStates.Processed;
        }
        else if (state == "refunded")
        {
            var original = await db.Set<PaymentReceipt>().AsNoTracking().Where(x => x.Provider == BillingPlans.CryptoProvider && x.ChargeId == id
                && (x.State == BillingStates.Applied || x.State == BillingStates.DuplicatePayment)).OrderByDescending(x => x.State == BillingStates.Applied).ThenBy(x => x.ReceivedAt).FirstOrDefaultAsync(ct);
            if (original is not null) await billing.ReceiveAsync(new(BillingPlans.CryptoProvider, BillingKeys.Refund(id), id, attempt.Id, null,
                currency, original.Amount, Reversal: true, Evidence: BillingDiagnostics.AuthenticatedStatus, Verified: valid), ct);
            inbox.State = original is null ? BillingStates.Review : BillingStates.Processed;
        }
        else if (state is "partially_paid" or "failed" or "expired" || !valid) inbox.State = BillingStates.Review;
        else inbox.State = BillingStates.AwaitingProvider;
        attempt.Retries = 0;
        attempt.RetryAt = clock.GetUtcNow().AddMinutes(attempt.State is BillingStates.Settled or BillingStates.Reversed ? 15 : 1);
        inbox.RetryAt = attempt.RetryAt;
        await db.SaveChangesAsync(ct);
    }
    private static decimal Amount(JsonElement value) => value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture);

    /// <summary>Poll one durable payment identity, retaining legacy receipt keys.</summary>
    public async Task PollAsync(string payment, CancellationToken ct)
    {
        var inbox = await EnsureInboxAsync(payment, ct);
        await ProcessInboxAsync(inbox, ct);
    }

    private async Task<PaymentReceipt> EnsureInboxAsync(string payment, CancellationToken ct)
    {
        // Ingestion and manual recovery share the ledger's receipt identity lock.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74230001)", ct);
        var key = BillingKeys.Discovered(payment);
        var inbox = await db.Set<PaymentReceipt>().SingleOrDefaultAsync(x => x.Provider == BillingPlans.CryptoProvider && x.EventKey == key, ct);
        if (inbox is null)
        {
            inbox = new() { Id = Guid.NewGuid(), Provider = BillingPlans.CryptoProvider, EventKey = key, ChargeId = payment,
                State = BillingStates.Inbox, ReceivedAt = clock.GetUtcNow(), RetryAt = clock.GetUtcNow() };
            db.Add(inbox); await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct); return inbox;
    }

    /// <summary>Import authenticated child evidence without replacing the primary or issuing another period.</summary>
    public async Task<bool> ImportAsync(long actor, string payment, string reason, CancellationToken ct)
    {
        using var status = await SendAsync(new(HttpMethod.Get, "payment/" + Uri.EscapeDataString(payment)), ct);
        var p = status.RootElement;
        if (p.GetProperty("payment_id").ToString() != payment) return false;
        var invoice = p.TryGetProperty("invoice_id", out var i) ? i.ToString() : null;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74230001)", ct);
            var owner = await (from a in db.Set<PaymentAttempt>() join o in db.Set<PurchaseOrder>() on a.OrderId equals o.Id
                where a.Provider == BillingPlans.CryptoProvider && a.InvoiceId != null && a.InvoiceId == invoice select (long?)o.UserId).SingleOrDefaultAsync(ct);
            db.Add(new BillingAudit { Id = Guid.NewGuid(), ActorId = actor, UserId = owner ?? 0, Action = BillingStates.ImportPayment,
                Reference = payment, Reason = reason, At = clock.GetUtcNow() });
            var key = BillingKeys.Discovered(payment);
            var existing = await db.Set<PaymentReceipt>().SingleOrDefaultAsync(x => x.Provider == BillingPlans.CryptoProvider && x.EventKey == key, ct);
            if (existing is null)
                db.Add(new PaymentReceipt { Id = Guid.NewGuid(), Provider = BillingPlans.CryptoProvider, EventKey = key, ChargeId = payment,
                    State = BillingStates.Inbox, Evidence = BillingDiagnostics.ImportedEvidence, ReceivedAt = clock.GetUtcNow(), RetryAt = clock.GetUtcNow() });
            else existing.Evidence = BillingDiagnostics.ImportedEvidence;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        await PollAsync(payment, ct); return true;
    }

    /// <summary>Audited support recovery for unknown invoice creation. Both dashboard invoice identity and authenticated payment data must agree.</summary>
    public async Task<bool> AttachAsync(long actor, long owner, Guid attemptId, string invoice, string payment, string reason, CancellationToken ct)
    {
        using var verified = await SendAsync(new(HttpMethod.Get, "payment/" + Uri.EscapeDataString(payment)), ct);
        var p = verified.RootElement;
        if (p.GetProperty("payment_id").ToString() != payment || p.GetProperty("invoice_id").ToString() != invoice) return false;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74230006)", ct);
            await BillingService.LockOwnerAsync(db, owner, ct);
            var attempt = await (from a in db.Set<PaymentAttempt>() join o in db.Set<PurchaseOrder>() on a.OrderId equals o.Id
                where a.Id == attemptId && o.UserId == owner && a.Provider == BillingPlans.CryptoProvider select a).SingleOrDefaultAsync(ct);
            if (attempt is not null) await db.Entry(attempt).ReloadAsync(ct);
            if (attempt is null || p.GetProperty("order_id").GetString() != attempt.OrderId.ToString("N")
                || attempt.InvoiceId is not null && attempt.InvoiceId != invoice || attempt.ExternalId is not null && attempt.ExternalId != payment) return false;
            var purchase = await db.Set<PurchaseOrder>().AsNoTracking().SingleAsync(x => x.Id == attempt.OrderId, ct);
            var expected = Amount(p.GetProperty("pay_amount"));
            if (p.GetProperty("pay_currency").GetString() != BillingPlans.UsdtBsc || expected <= 0
                || attempt.ExpectedAmount is { } frozen && frozen != expected
                || p.GetProperty("price_currency").GetString() != "usd" || Amount(p.GetProperty("price_amount")) != purchase.UsdPrice
                || p.TryGetProperty("network", out var network) && network.ValueKind != JsonValueKind.Null && !string.Equals(network.GetString(), "bsc", StringComparison.OrdinalIgnoreCase)) return false;
            attempt.InvoiceId = invoice; attempt.ExternalId = payment;
            attempt.ExpectedAmount ??= expected;
            db.Add(new BillingAudit { Id = Guid.NewGuid(), ActorId = actor, UserId = owner, Action = BillingStates.AttachPayment,
                Reference = attemptId.ToString("N"), Reason = reason, At = clock.GetUtcNow() });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        await PollAsync(payment, ct); return true;
    }
}
