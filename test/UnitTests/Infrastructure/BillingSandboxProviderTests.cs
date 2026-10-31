using Application.Billing;
using Domain.Entities;
using Infrastructure.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Presentation.Telegram.Configuration;
using Presentation.Telegram.Handlers;
using Presentation.Telegram.Services;
using Presentation.Telegram.Workers;
using Telegram.Bot;

namespace UnitTests.Infrastructure;

public sealed partial class BillingTests
{
    [BillingPostgresFact]
    public async Task SandboxCheckoutAcceptsStringEstimateAndNumericOrStringMinimumWithoutCreatingTwice()
    {
        foreach (var minimum in new[] { "1.25", "\"1.25\"" })
        {
            await Reset(); var clock = new Clock(); await using var db = Db();
            var config = new BillingOptions { SandboxMode = true, OperatorUserIds = [1], CryptoSalesEnabled = true, NowPaymentsApiKey = "fixture" };
            var billing = Service(db, clock, config: config);
            var order = (await billing.CreateOrderAsync(1, true, default)).Value;
            var requests = 0; var invoices = 0;
            var http = new HttpClient(new FixtureHandler((request, ct) =>
            {
                requests++;
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/min-amount")) return Task.FromResult("{\"min_amount\":" + minimum + "}");
                if (path.EndsWith("/estimate")) return Task.FromResult("{\"estimated_amount\":\"28.95365783\"}");
                Assert.Equal(HttpMethod.Post, request.Method); Assert.EndsWith("/invoice", path); invoices++;
                return Task.FromResult("{\"id\":\"fixture-invoice\",\"invoice_url\":\"https://sandbox.nowpayments.io/payment/fixture\"}");
            })) { BaseAddress = config.ProviderBaseAddress };
            var client = new NowPaymentsClient(http, Options.Create(config), db, billing, clock);
            var attempt = await client.CheckoutAsync(1, order.Id, default);
            Assert.Equal(BillingStates.Pending, attempt.State);
            Assert.Equal("fixture-invoice", attempt.InvoiceId);
            Assert.Equal("https://sandbox.nowpayments.io/payment/fixture", attempt.CheckoutUrl);
            await client.CheckoutAsync(1, order.Id, default);
            Assert.Equal(3, requests); Assert.Equal(1, invoices);
        }
    }

    [BillingPostgresFact]
    public async Task SandboxPollingPreservesWholeBatchAndCheckpointWhenRealFinancialUpdateArrives()
    {
        foreach (var paymentKind in new[] { "successful_payment", "refunded_payment" })
        {
            await Reset();
            var retried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var offsets = new List<int>();
            var bot = new TelegramBotClient("123456:fixture-token", new HttpClient(new FixtureHandler(async (request, ct) =>
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var offset = body.RootElement.GetProperty("offset").GetInt32();
                lock (offsets) offsets.Add(offset);
                if (offsets.Count >= 2) { retried.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }
                return "{\"ok\":true,\"result\":[{\"update_id\":99,\"message\":{\"message_id\":1,\"date\":1793059200,\"chat\":{\"id\":1,\"type\":\"private\"},\"text\":\"/sandboxsubscription\"}},"
                    + "{\"update_id\":100,\"message\":{\"message_id\":2,\"date\":1793059200,\"chat\":{\"id\":1,\"type\":\"private\"},\"" + paymentKind
                    + "\":{\"currency\":\"XTR\",\"total_amount\":1450,\"invoice_payload\":\"fixture\",\"telegram_payment_charge_id\":\"real-charge\",\"provider_payment_charge_id\":\"\"}}}]}";
            })));
            await using var connectionDb = Db();
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionDb.Database.GetConnectionString()));
            services.AddSingleton<IOptions<BillingOptions>>(Options.Create(new BillingOptions { SandboxMode = true, SandboxInitialTelegramOffset = 99 }));
            // No command or financial adapters: the batch must be rejected before either is resolved.
            await using var provider = services.BuildServiceProvider();
            await using var limiter = new TelegramGlobalRateLimiter();
            var handler = new TelegramCommandUpdateHandler(provider.GetRequiredService<IServiceScopeFactory>(), limiter, NullLogger<TelegramCommandUpdateHandler>.Instance);
            using var worker = new TelegramUpdateReceiverWorker(bot, handler, Options.Create(new TelegramBotOptions { BotToken = "fixture" }), NullLogger<TelegramUpdateReceiverWorker>.Instance, provider.GetRequiredService<IServiceScopeFactory>());
            await worker.StartAsync(default);
            try { await retried.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { await worker.StopAsync(default); }
            Assert.Equal(new[] { 99, 99 }, offsets);
            await using var check = Db();
            Assert.Equal(99, (await check.Set<BillingCheckpoint>().SingleAsync(x => x.Key == BillingStates.TelegramUpdates)).Offset);
            Assert.Empty(await check.Set<PaymentReceipt>().ToListAsync());
            Assert.Empty(await check.Set<AccessGrant>().ToListAsync());
        }
    }

    [BillingPostgresFact]
    public async Task SandboxSimulationRetainsNormalVerificationAndCannotDuplicateAccess()
    {
        foreach (var (scenario, status, amount, grants) in new[]
        {
            ("success", "finished", 0, 0), // Actual sandbox incompatibility: finished is insufficient without paid funds.
            ("failed", "failed", 0, 0),
            ("partially_paid", "partially_paid", 10, 0),
            ("success", "finished", 29, 1)
        })
        {
            await Reset(); var clock = new Clock(); await using var db = Db();
            var config = new BillingOptions { SandboxMode = true, OperatorUserIds = [1], CryptoSalesEnabled = true, NowPaymentsApiKey = "fixture" };
            var billing = Service(db, clock, config: config);
            var order = (await billing.CreateOrderAsync(1, true, default)).Value;
            var attempt = (await billing.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
            attempt.InvoiceId = "fixture-invoice"; attempt.State = BillingStates.Pending; await db.SaveChangesAsync();
            var simulations = 0;
            var http = new HttpClient(new FixtureHandler(async (request, ct) =>
            {
                Assert.Equal("api-sandbox.nowpayments.io", request.RequestUri!.Host);
                if (request.Method == HttpMethod.Post)
                {
                    simulations++; Assert.EndsWith("/invoice-payment", request.RequestUri.AbsolutePath);
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                    Assert.Equal("fixture-invoice", body.RootElement.GetProperty("iid").GetString());
                    Assert.Equal(scenario, body.RootElement.GetProperty("case").GetString());
                    return "{\"payment_id\":\"fixture-payment\"}";
                }
                return JsonSerializer.Serialize(new { payment_id = "fixture-payment", invoice_id = "fixture-invoice", order_id = order.Id.ToString("N"),
                    pay_currency = "usdtbsc", pay_amount = 29, actually_paid = amount, price_currency = "usd", price_amount = 29, payment_status = status });
            })) { BaseAddress = config.ProviderBaseAddress };
            var client = new NowPaymentsClient(http, Options.Create(config), db, billing, clock);
            Assert.True(await client.SimulateAsync(1, order.Id, scenario, default));
            Assert.False(await client.SimulateAsync(1, order.Id, scenario, default));
            await client.PollAsync("fixture-payment", default);
            Assert.Equal(1, simulations);
            Assert.Equal(grants, await db.Set<AccessGrant>().CountAsync());
            if (status == "finished" && amount == 0)
                Assert.Contains(await db.Set<PaymentReceipt>().ToListAsync(), x => x.State == BillingStates.Review);
        }
    }
}
