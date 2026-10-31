using Application.Billing;
using Domain.Entities;
using Infrastructure.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace UnitTests.Infrastructure;

public sealed partial class BillingTests
{
    private static BillingOptions TestOptions() => new()
    {
        TestCheckoutEnabled = true, OperatorUserIds = [1], MerchantUsdtBscVerified = true,
        NowPaymentsApiKey = "fixture", NowPaymentsIpnSecret = "fixture", CallbackUrl = "https://fixture.invalid/ipn"
    };

    [Fact]
    public async Task OperatorTestDefaultsOffAndChecksReadinessBeforeDatabaseAccess()
    {
        var clock = new Clock();
        using var db = new global::Infrastructure.Persistence.AppDbContext(new DbContextOptionsBuilder<global::Infrastructure.Persistence.AppDbContext>().Options);
        Assert.False(new BillingOptions().TestCheckoutEnabled);
        Assert.True((await Service(db, clock, config: new()).CreateOperatorTestOrderAsync(1, default)).IsFailure);
        var options = TestOptions();
        Assert.True(options.IsValid()); Assert.True(options.CanTestCheckout(1)); Assert.False(options.CanTestCheckout(2));
        Assert.True((await Service(db, clock, config: options).CreateOperatorTestOrderAsync(2, default)).IsFailure);
        options.MerchantUsdtBscVerified = false; Assert.False(options.IsValid());
        options = TestOptions(); options.NowPaymentsApiKey = ""; Assert.False(options.IsValid());
        options = TestOptions(); options.NowPaymentsIpnSecret = ""; Assert.False(options.IsValid());
        options = TestOptions(); options.CallbackUrl = "http://fixture.invalid/ipn"; Assert.False(options.IsValid());
        options = TestOptions(); options.OperatorUserIds = []; Assert.False(options.IsValid());
        options = TestOptions(); options.SandboxMode = true; Assert.False(options.IsValid());
        foreach (var price in new[] { 0m, -1m, 29.01m, .001m })
        { options = TestOptions(); options.TestCheckoutUsdPrice = price; Assert.False(options.IsValid()); }
    }

    [BillingPostgresFact]
    public async Task OperatorTestSnapshotSeparatesPublicOrdersRejectsStarsAndVerifiesStoredPrice()
    {
        await Reset(); var clock = new Clock(); await using var db = Db();
        var options = TestOptions(); options.StarsSalesEnabled = true; options.CryptoSalesEnabled = true;
        options.SupportContact = "support"; options.TermsUrl = "https://fixture.invalid/terms";
        var billing = Service(db, clock, config: options);
        var normal = (await billing.CreateOrderAsync(1, false, default)).Value;
        var test = (await billing.CreateOperatorTestOrderAsync(1, default)).Value;
        Assert.NotEqual(normal.Id, test.Id); Assert.Equal(29m, normal.UsdPrice); Assert.Equal(1450, normal.StarsPrice);
        Assert.Equal(1m, test.UsdPrice); Assert.Equal(0, test.StarsPrice);
        Assert.Equal(normal.Id, (await billing.CreateOrderAsync(1, false, default)).Value.Id);
        Assert.True((await billing.PrepareAttemptAsync(1, test.Id, BillingPlans.StarsProvider, default)).IsFailure);
        options.StarsSalesEnabled = false; options.CryptoSalesEnabled = false;
        Assert.True((await billing.CreateOrderAsync(2, false, default)).IsFailure);
        var attempt = (await billing.PrepareAttemptAsync(1, test.Id, BillingPlans.CryptoProvider, default)).Value;
        attempt.InvoiceId = "invoice"; await db.SaveChangesAsync();
        options.TestCheckoutUsdPrice = 2m;
        Assert.Equal(1m, (await db.Set<PurchaseOrder>().AsNoTracking().SingleAsync(x => x.Id == test.Id)).UsdPrice);
        var changed = (await billing.CreateOperatorTestOrderAsync(1, default)).Value;
        Assert.NotEqual(test.Id, changed.Id); Assert.Equal(2m, changed.UsdPrice);
        Assert.True((await billing.PrepareAttemptAsync(2, test.Id, BillingPlans.CryptoProvider, default)).IsFailure);
        options.TestCheckoutEnabled = false;
        Assert.True((await billing.PrepareAttemptAsync(1, test.Id, BillingPlans.CryptoProvider, default)).IsFailure);
        decimal providerPrice = 2m;
        var client = new NowPaymentsClient(new HttpClient(new FixtureHandler((request, ct) => Task.FromResult(
            "{\"payment_id\":\"payment\",\"invoice_id\":\"invoice\",\"order_id\":\"" + test.Id.ToString("N") +
            "\",\"pay_currency\":\"usdtbsc\",\"pay_amount\":1,\"actually_paid\":1,\"price_currency\":\"usd\",\"price_amount\":" +
            providerPrice.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"payment_status\":\"finished\"}")))
            { BaseAddress = new("https://fixture.invalid/v1/") }, Options.Create(options), db, billing, clock);
        await client.PollAsync("payment", default);
        Assert.Empty(await db.Set<AccessGrant>().ToListAsync());
        providerPrice = 1m; await client.PollAsync("payment", default);
        Assert.Single(await db.Set<AccessGrant>().ToListAsync());
        Assert.Equal(1m, test.UsdPrice);
    }

    [BillingPostgresFact]
    public async Task OperatorTestCheckoutSendsStoredDollarPriceAndAcceptsStringProviderQuotes()
    {
        await Reset(); var clock = new Clock(); await using var db = Db();
        var options = TestOptions(); var billing = Service(db, clock, config: options);
        var order = (await billing.CreateOperatorTestOrderAsync(1, default)).Value;
        string? invoiceBody = null;
        var client = new NowPaymentsClient(new HttpClient(new FixtureHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/min-amount")) return "{\"min_amount\":\"0.085\"}";
            if (request.RequestUri.AbsolutePath.EndsWith("/estimate")) return "{\"estimated_amount\":\"1\"}";
            invoiceBody = await request.Content!.ReadAsStringAsync(ct);
            return "{\"id\":\"fixture-invoice\",\"invoice_url\":\"https://fixture.invalid/checkout\"}";
        })) { BaseAddress = new("https://fixture.invalid/v1/") }, Options.Create(options), db, billing, clock);
        options.TestCheckoutUsdPrice = 2m;
        var attempt = await client.CheckoutAsync(1, order.Id, default);
        Assert.Equal("fixture-invoice", attempt.InvoiceId);
        using var body = System.Text.Json.JsonDocument.Parse(invoiceBody!);
        Assert.Equal(1m, body.RootElement.GetProperty("price_amount").GetDecimal());
        Assert.Equal("usdtbsc", body.RootElement.GetProperty("pay_currency").GetString());
        Assert.Equal(order.Id.ToString("N"), body.RootElement.GetProperty("order_id").GetString());
    }

    [BillingPostgresFact]
    public async Task OperatorTestRejectsEvenPersistedStarsAttemptAtPreCheckoutAndSettlement()
    {
        await Reset(); var clock = new Clock(); await using var db = Db();
        var options = TestOptions(); options.StarsSalesEnabled = true;
        options.SupportContact = "support"; options.TermsUrl = "https://fixture.invalid/terms";
        var billing = Service(db, clock, config: options);
        var order = (await billing.CreateOperatorTestOrderAsync(1, default)).Value;
        var attempt = new PaymentAttempt { Id = Guid.NewGuid(), OrderId = order.Id, Provider = BillingPlans.StarsProvider,
            Currency = BillingPlans.Xtr, ExpectedAmount = 0, CreatedAt = clock.Now, RetryAt = clock.Now };
        db.Add(attempt); await db.SaveChangesAsync();
        Assert.True((await billing.PreCheckoutAsync(1, attempt.Id, BillingPlans.Xtr, 0, default)).IsFailure);
        await billing.ReceiveAsync(new(BillingPlans.StarsProvider, "paid:test-stars", "test-stars", attempt.Id, 1, BillingPlans.Xtr, 0), default);
        Assert.Empty(await db.Set<AccessGrant>().ToListAsync());
        Assert.False(order.Fulfilled);
    }
}
