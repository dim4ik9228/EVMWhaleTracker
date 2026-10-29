using Application.Billing;
using Domain.Entities;
using Infrastructure.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Presentation.Telegram.Billing;

namespace UnitTests.Infrastructure;

public sealed partial class BillingTests
{
    [Fact]
    public void CryptoSalesNeedOnlyApiAndIpnSecretsWithMerchantPrerequisites()
    {
        var options = new BillingOptions { CryptoSalesEnabled = true, NowPaymentsApiKey = "api", NowPaymentsIpnSecret = "ipn",
            CallbackUrl = "https://fixture.invalid/ipn", TermsUrl = "https://fixture.invalid/terms", SupportContact = "support", MerchantUsdtBscVerified = true };
        Assert.True(options.IsValid());
        options.NowPaymentsIpnSecret = ""; Assert.False(options.IsValid());
        options.CryptoSalesEnabled = false; Assert.True(options.IsValid());
    }

    [BillingPostgresFact]
    public async Task LegacyUnknownIdentitySweepsAreBoundedAndLateIdentityRecovers()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var billing = Service(db, clock);
        var order = (await billing.CreateOrderAsync(1, false, default)).Value;
        var attempt = (await billing.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
        attempt.InvoiceId = "late-invoice"; attempt.State = BillingStates.ReconciliationNeedsCredentials;
        var otherOrder = (await billing.CreateOrderAsync(2, false, default)).Value;
        var ambiguous = new PaymentAttempt { Id = Guid.NewGuid(), OrderId = otherOrder.Id, Provider = BillingPlans.CryptoProvider,
            State = BillingStates.CreationUnknown, CreatedAt = clock.Now, RetryAt = clock.Now };
        db.Add(ambiguous); await db.SaveChangesAsync();
        var calls = 0;
        var client = new NowPaymentsClient(new HttpClient(new FixtureHandler((request, ct) =>
        {
            calls++; Assert.Null(request.Headers.Authorization); Assert.EndsWith("/payment/late-payment", request.RequestUri!.AbsolutePath);
            return Task.FromResult("{\"payment_id\":\"late-payment\",\"invoice_id\":\"late-invoice\",\"order_id\":\"" + order.Id.ToString("N")
                + "\",\"pay_currency\":\"usdtbsc\",\"pay_amount\":29,\"actually_paid\":29,\"price_currency\":\"usd\",\"price_amount\":29,\"payment_status\":\"finished\"}");
        })) { BaseAddress = new("https://fixture.invalid/v1/") }, Options.Create(new BillingOptions { NowPaymentsApiKey = "api" }), db, billing, clock);
        var services = new ServiceCollection(); services.AddSingleton(db); services.AddSingleton(client);
        await using var provider = services.BuildServiceProvider();
        using var worker = new BillingWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BillingWorker>.Instance, clock);
        await worker.ReconcileCryptoAsync(provider, default);
        Assert.Equal(0, calls); Assert.Equal(BillingStates.AwaitingIdentity, attempt.State); Assert.True(attempt.RetryAt > clock.Now);
        Assert.Equal(BillingStates.CreationUnknown, ambiguous.State);
        clock.Now = clock.Now.AddDays(1); await worker.ReconcileCryptoAsync(provider, default);
        Assert.Equal(BillingStates.IdentityAttention, attempt.State); Assert.Equal(0, calls);
        var ipn = new PaymentReceipt { Id = Guid.NewGuid(), Provider = BillingPlans.CryptoProvider, EventKey = BillingKeys.Ipn("late"),
            ChargeId = "late-payment", State = BillingStates.Inbox, ReceivedAt = clock.Now };
        db.Add(ipn); await db.SaveChangesAsync(); await worker.ReconcileCryptoAsync(provider, default);
        Assert.Equal("late-payment", attempt.ExternalId); Assert.Equal(BillingStates.Settled, attempt.State);
        Assert.Single(await db.Set<AccessGrant>().ToListAsync());
        var previous = calls; await worker.ReconcileCryptoAsync(provider, default); Assert.Equal(previous, calls);
    }

    [BillingPostgresFact]
    public async Task AttachmentCommitSurvivesProcessingFailureAndWorkerRecovers()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var billing = Service(db, clock);
        var order = (await billing.CreateOrderAsync(1, false, default)).Value;
        var attempt = (await billing.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
        attempt.State = BillingStates.CreationUnknown; await db.SaveChangesAsync();
        var fail = true;
        var client = new NowPaymentsClient(new HttpClient(new FixtureHandler((request, ct) =>
        {
            Assert.Null(request.Headers.Authorization); Assert.EndsWith("/payment/attached", request.RequestUri!.AbsolutePath);
            return Task.FromResult("{\"payment_id\":\"attached\",\"invoice_id\":\"invoice\",\"order_id\":\"" + order.Id.ToString("N")
                + "\",\"pay_currency\":\"usdtbsc\",\"pay_amount\":29,\"actually_paid\":29,\"price_currency\":\"usd\",\"price_amount\":29,\"payment_status\":\"finished\"}");
        })) { BaseAddress = new("https://fixture.invalid/v1/") }, Options.Create(new BillingOptions { NowPaymentsApiKey = "api" }), db, new FailOnceBilling(billing, () => fail), clock);
        Assert.False(await client.AttachAsync(99, 2, attempt.Id, "invoice", "attached", "wrong owner", default));
        await Assert.ThrowsAsync<IOException>(() => client.AttachAsync(99, 1, attempt.Id, "invoice", "attached", "recovery", default));
        Assert.Equal("attached", attempt.ExternalId); Assert.Single(await db.Set<BillingAudit>().ToListAsync());
        fail = false; await client.PollAsync("attached", default); await client.PollAsync("attached", default);
        Assert.Single(await db.Set<AccessGrant>().ToListAsync());
    }
    [BillingPostgresFact]
    public async Task RateLimitSchedulesBothAttemptAndReceiptAndDoesNotHotLoop()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var billing = Service(db, clock);
        var order = (await billing.CreateOrderAsync(1, false, default)).Value;
        var attempt = (await billing.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
        attempt.InvoiceId = "limited-invoice"; attempt.ExternalId = "limited-payment"; await db.SaveChangesAsync();
        var handler = new LimitedHandler();
        var client = new NowPaymentsClient(new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/v1/") }, Options.Create(new BillingOptions { NowPaymentsApiKey = "api" }), db, billing, clock);
        var services = new ServiceCollection(); services.AddSingleton(db); services.AddSingleton(client);
        await using var provider = services.BuildServiceProvider();
        using var worker = new BillingWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BillingWorker>.Instance, clock);
        await worker.ReconcileCryptoAsync(provider, default);
        Assert.Equal(1, handler.Calls); Assert.True(attempt.RetryAt >= clock.Now.AddMinutes(10));
        Assert.True((await db.Set<PaymentReceipt>().AsNoTracking().SingleAsync()).RetryAt >= clock.Now.AddMinutes(10));
        await worker.ReconcileCryptoAsync(provider, default); Assert.Equal(1, handler.Calls);
        clock.Now = clock.Now.AddMinutes(11); await worker.ReconcileCryptoAsync(provider, default); Assert.Equal(2, handler.Calls);
    }

    [BillingPostgresFact]
    public async Task KnownDuplicateChildRefundPreservesWinningPrimaryAndQuote()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var billing = Service(db, clock);
        var order = (await billing.CreateOrderAsync(1, false, default)).Value;
        var attempt = (await billing.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
        attempt.InvoiceId = "invoice"; attempt.ExternalId = "primary"; attempt.ExpectedAmount = 29m;
        await db.SaveChangesAsync();
        await billing.ReceiveAsync(new(BillingPlans.CryptoProvider, BillingKeys.Settlement("primary", 29), "primary", attempt.Id, null, BillingPlans.UsdtBsc, 29), default);
        // Persisted pre-switch evidence remains eligible for accounting after a restart.
        await billing.ReceiveAsync(new(BillingPlans.CryptoProvider, BillingKeys.Settlement("child", 31), "child", attempt.Id, null, BillingPlans.UsdtBsc, 31), default);
        var client = new NowPaymentsClient(new HttpClient(new FixtureHandler((request, ct) =>
        {
            Assert.Null(request.Headers.Authorization); Assert.EndsWith("/payment/child", request.RequestUri!.AbsolutePath);
            return Task.FromResult("{\"payment_id\":\"child\",\"invoice_id\":\"invoice\",\"order_id\":\"" + order.Id.ToString("N")
                + "\",\"pay_currency\":\"usdtbsc\",\"pay_amount\":29,\"actually_paid\":0,\"price_currency\":\"usd\",\"price_amount\":29,\"payment_status\":\"refunded\"}");
        })) { BaseAddress = new("https://fixture.invalid/v1/") }, Options.Create(new BillingOptions { NowPaymentsApiKey = "api" }), db, billing, clock);
        await client.PollAsync("child", default); await client.PollAsync("child", default);
        var reversal = await db.Set<PaymentReceipt>().SingleAsync(x => x.ChargeId == "child" && x.Reversal);
        Assert.Equal(BillingStates.Reversed, reversal.State); Assert.Equal(31m, reversal.Amount);
        Assert.Null((await db.Set<AccessGrant>().SingleAsync()).ReversedAt);
        Assert.Equal(BillingStates.Settled, attempt.State); Assert.Equal("primary", attempt.ExternalId); Assert.Equal(29m, attempt.ExpectedAmount);
    }

    [BillingPostgresFact]
    public async Task ForeignOrderEvidenceIsUnassignedAndCannotLeakIntoOwnerHistory()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var billing = Service(db, clock);
        var order = (await billing.CreateOrderAsync(1, false, default)).Value;
        var attempt = (await billing.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
        attempt.InvoiceId = "invoice"; await db.SaveChangesAsync();
        var client = new NowPaymentsClient(new HttpClient(new FixtureHandler((request, ct) => Task.FromResult(
            "{\"payment_id\":\"foreign\",\"invoice_id\":\"invoice\",\"order_id\":\"foreign-order\",\"pay_currency\":\"usdtbsc\",\"actually_paid\":29}")))
            { BaseAddress = new("https://fixture.invalid/v1/") }, Options.Create(new BillingOptions { NowPaymentsApiKey = "api" }), db, billing, clock);
        await client.PollAsync("foreign", default);
        Assert.All(await db.Set<PaymentReceipt>().ToListAsync(), x => Assert.Null(x.AttemptId));
        Assert.Null(attempt.ExternalId); Assert.Empty(await db.Set<AccessGrant>().ToListAsync());
    }

    [BillingPostgresFact]
    public async Task ImportRechecksExistingEvidenceWithoutClaimingPrimaryAndLateIpnStillRecovers()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var billing = Service(db, clock);
        var order = (await billing.CreateOrderAsync(1, false, default)).Value;
        var attempt = (await billing.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
        attempt.InvoiceId = "invoice";
        db.Add(new PaymentReceipt { Id = Guid.NewGuid(), Provider = BillingPlans.CryptoProvider, EventKey = BillingKeys.Discovered("payment"),
            ChargeId = "payment", State = BillingStates.Review, ReceivedAt = clock.Now });
        await db.SaveChangesAsync();
        var client = new NowPaymentsClient(new HttpClient(new FixtureHandler((request, ct) => Task.FromResult(
            "{\"payment_id\":\"payment\",\"invoice_id\":\"invoice\",\"order_id\":\"" + order.Id.ToString("N")
            + "\",\"pay_currency\":\"usdtbsc\",\"pay_amount\":29,\"actually_paid\":29,\"price_currency\":\"usd\",\"price_amount\":29,\"payment_status\":\"finished\"}")))
            { BaseAddress = new("https://fixture.invalid/v1/") }, Options.Create(new BillingOptions { NowPaymentsApiKey = "api" }), db, billing, clock);
        Assert.True(await client.ImportAsync(99, "payment", "investigate", default));
        await client.PollAsync("payment", default);
        Assert.Null(attempt.ExternalId); Assert.Null(attempt.ExpectedAmount); Assert.Empty(await db.Set<AccessGrant>().ToListAsync());
        Assert.Equal(BillingDiagnostics.ImportedEvidence, (await db.Set<PaymentReceipt>().SingleAsync()).Evidence);
        var ipn = new PaymentReceipt { Id = Guid.NewGuid(), Provider = BillingPlans.CryptoProvider, EventKey = BillingKeys.Ipn("late"),
            ChargeId = "payment", State = BillingStates.Inbox, ReceivedAt = clock.Now };
        db.Add(ipn); await db.SaveChangesAsync(); await client.ProcessInboxAsync(ipn, default);
        Assert.Equal("payment", attempt.ExternalId); Assert.Single(await db.Set<AccessGrant>().ToListAsync());
    }

    [BillingPostgresFact]
    public async Task RecoveryListIsPrivateReadOnlyPaginatedAndIncludesActionReferences()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var billing = Service(db, clock);
        for (var n = 1; n <= 9; n++)
        {
            var order = (await billing.CreateOrderAsync(n, false, default)).Value;
            var attempt = (await billing.PrepareAttemptAsync(n, order.Id, BillingPlans.CryptoProvider, default)).Value;
            attempt.InvoiceId = "invoice-" + new string('i', 110) + n; attempt.State = BillingStates.IdentityAttention;
            db.Add(new PaymentReceipt { Id = Guid.NewGuid(), Provider = BillingPlans.CryptoProvider, EventKey = "review:" + n,
                ChargeId = "payment-" + new string('p', 110) + n, AttemptId = attempt.Id, State = BillingStates.Review, ReceivedAt = clock.Now });
        }
        await db.SaveChangesAsync();
        var messages = new List<string>();
        var bot = new Telegram.Bot.TelegramBotClient("123456:fixture-token", new HttpClient(new FixtureHandler(async (request, ct) =>
        {
            using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            messages.Add(body.RootElement.GetProperty("text").GetString()!);
            return "{\"ok\":true,\"result\":{\"message_id\":1,\"date\":1793059200,\"chat\":{\"id\":99,\"type\":\"private\"},\"text\":\"fixture\"}}";
        })));
        var adapter = new TelegramBilling(billing, db, Options.Create(new BillingOptions { OperatorUserIds = [99] }), null!, bot);
        Telegram.Bot.Types.Update Command(long actor, bool group, int page) => new() { Message = new() {
            From = new() { Id = actor, FirstName = "Fixture" }, Chat = new() { Id = actor, Type = group ? Telegram.Bot.Types.Enums.ChatType.Group : Telegram.Bot.Types.Enums.ChatType.Private },
            Text = "/billing_recovery " + page } };
        await adapter.HandleAsync(Command(2, false, 0), default); Assert.Equal(BillingText.NotAuthorized, messages.Last());
        await adapter.HandleAsync(Command(99, true, 0), default); Assert.Equal(BillingText.PrivateOnly, messages.Last());
        await adapter.HandleAsync(Command(99, false, 0), default); var first = messages.Last();
        Assert.Equal(4, first.Split("Unknown payment identity").Length - 1);
        Assert.Equal(4, first.Split("Received review evidence").Length - 1);
        Assert.Contains("Owner ", first); Assert.Contains("Payment payment-", first); Assert.True(first.Length < 4096);
        await adapter.HandleAsync(Command(99, false, 2), default);
        Assert.Equal(1, messages.Last().Split("Unknown payment identity").Length - 1);
        Assert.Empty(await db.Set<BillingAudit>().ToListAsync()); Assert.Empty(await db.Set<AccessGrant>().ToListAsync());
        Assert.Equal(9, await db.Set<PaymentReceipt>().CountAsync());
    }

    private sealed class LimitedHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Assert.Null(request.Headers.Authorization); Assert.EndsWith("/payment/limited-payment", request.RequestUri!.AbsolutePath);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromMinutes(10)); return Task.FromResult(response);
        }
    }

}
