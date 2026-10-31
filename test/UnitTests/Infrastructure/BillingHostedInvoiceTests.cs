using System.Text.Json;
using Application.Billing;
using Domain.Entities;
using Infrastructure.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace UnitTests.Infrastructure;

public sealed partial class BillingTests
{
    private static NowPaymentsClient HostedClient(global::Infrastructure.Persistence.AppDbContext db, Clock clock,
        Guid order, Func<string, (decimal Quote, decimal Paid, string State, string Currency, string Network, decimal Usd, string? Order)> status, IBillingService? ledger = null)
        => new(new HttpClient(new FixtureHandler((request, ct) =>
        {
            Assert.Null(request.Headers.Authorization);
            var id = request.RequestUri!.Segments.Last();
            var p = status(id);
            return Task.FromResult(JsonSerializer.Serialize(new { payment_id = id, invoice_id = "hosted-invoice",
                order_id = p.Order ?? order.ToString("N"), pay_currency = p.Currency, pay_amount = p.Quote,
                actually_paid = p.Paid, payment_status = p.State, network = p.Network,
                price_currency = "usd", price_amount = p.Usd }));
        })) { BaseAddress = new("https://fixture.invalid/v1/") },
            Options.Create(new BillingOptions { NowPaymentsApiKey = "fixture" }), db, ledger ?? Service(db, clock), clock);

    [BillingPostgresFact]
    public async Task HostedInvoiceUnpaidFirstQuoteDoesNotBlockPaidSiblingAndRefundUsesWinningIdentity()
    {
        foreach (var paidFirst in new[] { false, true })
        {
            await Reset(); var clock = new Clock(); await using var db = Db(); var billing = Service(db, clock);
            var order = (await billing.CreateOrderAsync(1, false, default)).Value;
            order.UsdPrice = 1;
            var attempt = (await billing.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
            attempt.InvoiceId = "hosted-invoice"; await db.SaveChangesAsync();
            var refunded = false;
            var client = HostedClient(db, clock, order.Id, id => id == "unpaid"
                ? (0.99648702m, 0m, "waiting", "usdtbsc", "bsc", 1m, null)
                : (0.99630628m, refunded ? 0m : 0.99630628m, refunded ? "refunded" : "finished", "usdtbsc", "bsc", 1m, null));
            await client.PollAsync(paidFirst ? "paid" : "unpaid", default);
            await client.PollAsync(paidFirst ? "unpaid" : "paid", default);
            await client.PollAsync("paid", default);
            var grant = await db.Set<AccessGrant>().SingleAsync();
            Assert.Equal("paid", (await db.Set<PaymentReceipt>().SingleAsync(x => x.Id == grant.ReceiptId)).ChargeId);
            Assert.Equal(paidFirst ? "paid" : "unpaid", attempt.ExternalId);
            Assert.Equal(paidFirst ? 0.99630628m : 0.99648702m, attempt.ExpectedAmount);
            Assert.True(order.Fulfilled); Assert.Null(grant.ReversedAt);
            refunded = true; await client.PollAsync("paid", default); await client.PollAsync("paid", default);
            Assert.NotNull(grant.ReversedAt); Assert.Equal(BillingStates.Reversed, attempt.State);
            Assert.Equal("paid", (await db.Set<PaymentReceipt>().SingleAsync(x => x.Reversal)).ChargeId);
            Assert.Single(await db.Set<AccessGrant>().ToListAsync());
        }
    }

    [BillingPostgresFact]
    public async Task HostedInvoiceConcurrentFundedSiblingsGrantOnceAndDuplicateRefundPreservesWinner()
    {
        await Reset(); var clock = new Clock(); Guid orderId;
        await using (var db = Db())
        {
            var billing = Service(db, clock); var order = (await billing.CreateOrderAsync(1, false, default)).Value;
            order.UsdPrice = 1; orderId = order.Id;
            var attempt = (await billing.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
            attempt.InvoiceId = "hosted-invoice"; attempt.ExternalId = "unpaid"; attempt.ExpectedAmount = 0.99648702m;
            await db.SaveChangesAsync();
        }
        await Task.WhenAll(new[] { "paid-a", "paid-b" }.Select(async id =>
        {
            await using var db = Db();
            await HostedClient(db, clock, orderId, key => (key == "paid-a" ? 0.99630628m : 0.995m,
                key == "paid-a" ? 0.99630628m : 0.995m, "finished", "usdtbsc", "bsc", 1m, null)).PollAsync(id, default);
        }));
        await using var check = Db();
        var grant = await check.Set<AccessGrant>().SingleAsync();
        var loser = await check.Set<PaymentReceipt>().SingleAsync(x => x.State == BillingStates.DuplicatePayment);
        await HostedClient(check, clock, orderId, id => (loser.Amount, 0m, "refunded", "usdtbsc", "bsc", 1m, null)).PollAsync(loser.ChargeId, default);
        Assert.Null(grant.ReversedAt); Assert.Equal(BillingStates.Settled, (await check.Set<PaymentAttempt>().SingleAsync()).State);
        Assert.Equal(BillingStates.Reversed, (await check.Set<PaymentReceipt>().SingleAsync(x => x.Reversal)).State);
        Assert.Single(await check.Set<AccessGrant>().ToListAsync());
    }

    [BillingPostgresFact]
    public async Task HostedWinnerSurvivesUnpaidPrimaryPollingAndPrimaryDuplicateRefund()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var billing = Service(db, clock);
        var order = (await billing.CreateOrderAsync(1, false, default)).Value; order.UsdPrice = 1;
        var attempt = (await billing.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
        attempt.InvoiceId = "hosted-invoice"; await db.SaveChangesAsync();
        var primaryState = "waiting";
        var client = HostedClient(db, clock, order.Id, id => id == "primary"
            ? (0.99648702m, primaryState == "finished" ? 0.99648702m : 0m, primaryState, "usdtbsc", "bsc", 1m, null)
            : (0.99630628m, 0.99630628m, "finished", "usdtbsc", "bsc", 1m, null));
        await client.PollAsync("primary", default); await client.PollAsync("winner", default);
        var grant = await db.Set<AccessGrant>().SingleAsync();
        foreach (var state in new[] { "waiting", "failed", "refunded", "finished", "refunded", "refunded" })
        {
            primaryState = state; await client.PollAsync("primary", default);
            await db.Entry(attempt).ReloadAsync(); await db.Entry(grant).ReloadAsync();
            Assert.Equal(BillingStates.Settled, attempt.State); Assert.Equal(0.99630628m, attempt.PaidAmount);
            Assert.Equal("primary", attempt.ExternalId); Assert.Equal(0.99648702m, attempt.ExpectedAmount);
            Assert.Null(grant.ReversedAt); Assert.True(order.Fulfilled);
        }
        Assert.Single(await db.Set<AccessGrant>().ToListAsync());
        Assert.Equal("winner", (await db.Set<PaymentReceipt>().SingleAsync(x => x.Id == grant.ReceiptId)).ChargeId);
        Assert.Equal(BillingStates.Reversed, (await db.Set<PaymentReceipt>().SingleAsync(x => x.Reversal)).State);
    }

    [BillingPostgresFact]
    public async Task HostedSiblingRejectsWrongQuoteCurrencyNetworkOrderPriceAndUnderpayment()
    {
        foreach (var mismatch in new[] { "quote", "currency", "network", "order", "price", "underpaid", "zero" })
        {
            await Reset(); var clock = new Clock(); await using var db = Db(); var billing = Service(db, clock);
            var order = (await billing.CreateOrderAsync(1, false, default)).Value; order.UsdPrice = 1;
            var attempt = (await billing.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
            attempt.InvoiceId = "hosted-invoice"; attempt.ExternalId = "unpaid"; attempt.ExpectedAmount = 0.99648702m;
            await db.SaveChangesAsync();
            var changed = false;
            var client = HostedClient(db, clock, order.Id, id => (changed && mismatch == "quote" ? 0.995m : 0.99630628m,
                changed ? mismatch == "underpaid" ? 0.99630627m : mismatch == "zero" ? 0m : 0.99630628m : 0m,
                changed ? "finished" : "waiting", changed && mismatch == "currency" ? "usdteth" : "usdtbsc",
                changed && mismatch == "network" ? "eth" : "bsc", changed && mismatch == "price" ? 2m : 1m,
                changed && mismatch == "order" ? Guid.NewGuid().ToString("N") : null));
            await client.PollAsync("paid", default); changed = true; await client.PollAsync("paid", default);
            Assert.Empty(await db.Set<AccessGrant>().ToListAsync()); Assert.False(order.Fulfilled);
            Assert.Equal("unpaid", attempt.ExternalId); Assert.Equal(0.99648702m, attempt.ExpectedAmount);
        }
    }
    [BillingPostgresFact]
    public async Task HostedReviewedSiblingImportRecoversAfterQuoteCommitFailureWithSalesDisabled()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var billing = Service(db, clock);
        var order = (await billing.CreateOrderAsync(1, false, default)).Value; order.UsdPrice = 1;
        var attempt = (await billing.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
        attempt.InvoiceId = "hosted-invoice"; attempt.ExternalId = "unpaid"; attempt.ExpectedAmount = 0.99648702m;
        db.Add(new PaymentReceipt { Id = Guid.NewGuid(), Provider = BillingPlans.CryptoProvider,
            EventKey = BillingKeys.Discovered("paid"), ChargeId = "paid", AttemptId = attempt.Id,
            State = BillingStates.Review, ReceivedAt = clock.Now, RetryAt = clock.Now.AddMinutes(15) });
        db.Add(new PaymentReceipt { Id = Guid.NewGuid(), Provider = BillingPlans.CryptoProvider,
            EventKey = BillingKeys.Settlement("paid", 0.99630628m) + ":review", ChargeId = "paid", AttemptId = attempt.Id,
            Amount = 0.99630628m, Currency = BillingPlans.UsdtBsc, State = BillingStates.Review, ReceivedAt = clock.Now });
        await db.SaveChangesAsync();
        var fail = true;
        var disabled = Service(db, clock, config: new BillingOptions());
        var client = HostedClient(db, clock, order.Id,
            id => (0.99630628m, 0.99630628m, "finished", "usdtbsc", "bsc", 1m, null),
            new FailOnceBilling(disabled, () => fail));
        await Assert.ThrowsAsync<IOException>(() => client.ImportAsync(99, "paid", "recheck hosted payment", default));
        Assert.Single(await db.Set<PaymentReceipt>().Where(x => x.EventKey == BillingKeys.Quote("paid")).ToListAsync());
        Assert.Empty(await db.Set<AccessGrant>().ToListAsync());
        fail = false; await client.PollAsync("paid", default);
        Assert.Empty(await db.Set<PaymentReceipt>().Where(x => x.ChargeId == "paid" && x.State == BillingStates.Review).ToListAsync());
        var notices = await db.Set<BillingOutbox>().CountAsync();
        await client.ImportAsync(99, "paid", "repeat recheck", default); await client.PollAsync("paid", default);
        Assert.Single(await db.Set<AccessGrant>().ToListAsync()); Assert.Equal(notices, await db.Set<BillingOutbox>().CountAsync());
        Assert.Equal("unpaid", attempt.ExternalId); Assert.Equal(0.99648702m, attempt.ExpectedAmount);
    }

}
