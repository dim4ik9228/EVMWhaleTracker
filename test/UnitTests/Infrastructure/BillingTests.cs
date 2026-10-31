using Domain.Common;
using System.Security.Cryptography;
using System.Text;
using Application.Billing;
using Application.TrackedWallets;
using Application.Polymarket;
using Domain.Entities;
using Infrastructure.Billing;
using Infrastructure.Caching;
using Infrastructure.Hyperliquid;
using Infrastructure.Persistence;
using Infrastructure.Polymarket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Presentation.Telegram.Billing;
using Presentation.Telegram.Configuration;
using Presentation.Telegram.Handlers;
using Presentation.Telegram.Services;
using Presentation.Telegram.Workers;
using Telegram.Bot;
using System.Text.Json;

namespace UnitTests.Infrastructure;

public sealed class BillingPostgresFactAttribute : FactAttribute
{
    public BillingPostgresFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BILLING_TEST_POSTGRES"))) Skip = "Explicit disposable BillingQa PostgreSQL required."; }
}

public sealed partial class BillingTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static AppDbContext Db()
    {
        var connection = Environment.GetEnvironmentVariable("BILLING_TEST_POSTGRES") ?? throw new InvalidOperationException("Explicit QA connection required.");
        var b = new NpgsqlConnectionStringBuilder(connection);
        if (b.Database != "BillingQa" || b.Port != 55433 || b.Host is not ("localhost" or "127.0.0.1")) throw new InvalidOperationException("Only disposable BillingQa on loopback55433 is permitted.");
        return new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
    }
    private static async Task Reset()
    {
        await using var db = Db(); await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE \"PaymentReceipt\", \"PaymentAttempt\", \"AccessGrant\", \"PurchaseOrder\", \"BillingAccount\", \"BillingAudit\", \"BillingOutbox\", \"BillingCheckpoint\", \"HyperliquidReservation\", \"TrackedWallets\" CASCADE");
    }
    private static BillingService Service(AppDbContext db, Clock clock, EntitlementPolicy? policy = null, BillingOptions? config = null)
    {
        var opts = Options.Create(config ?? new BillingOptions { EnforcementEnabled = true, StarsSalesEnabled = true, CryptoSalesEnabled = true });
        policy ??= new(opts, clock);
        return new(db, opts, clock, policy, new InMemoryCacheService(policy), new(), Options.Create(new HyperliquidOptions()), Options.Create(new PolymarketPolygonOptions()));
    }
    private static TrackedWallet Wallet(int id, long owner = 1) => TrackedWallet.Create(global::Domain.ValueObjects.Address.Normalize("0x" + id.ToString("x40")).Value, owner).Value;

    [BillingPostgresFact]
    public async Task ParallelFreeBoundaryAndFirstAccountAreSerialized()
    {
        await Reset(); var clock = new Clock();
        for (var i = 1; i <= 9; i++) { await using var db = Db(); Assert.True((await Service(db, clock).AddWalletAsync(Wallet(i), default)).IsSuccess); }
        var results = await Task.WhenAll(Enumerable.Range(10, 8).Select(async i => { await using var db = Db(); return await Service(db, clock).AddWalletAsync(Wallet(i), default); }));
        Assert.Single(results, x => x.IsSuccess);
        await using var check = Db(); Assert.Equal(10, await check.TrackedWallets.CountAsync()); Assert.Single(await check.Set<BillingAccount>().ToListAsync());
    }
    [BillingPostgresFact]
    public async Task ConcurrentMethodsFulfilOnceRetainLosingMoneyAndReplayDoesNotExtend()
    {
        await Reset(); var clock = new Clock(); Guid starsId, cryptoId;
        await using (var db = Db())
        {
            var service = Service(db, clock); var order = (await service.CreateOrderAsync(1, false, default)).Value;
            starsId = (await service.PrepareAttemptAsync(1, order.Id, BillingPlans.StarsProvider, default)).Value.Id;
            var crypto = (await service.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
            crypto.ExpectedAmount = 29.123456789m; cryptoId = crypto.Id; await db.SaveChangesAsync();
        }
        var receipts = new[] { new FinancialReceipt(BillingPlans.StarsProvider, "paid:charge", "charge", starsId, 1, BillingPlans.Xtr, 1450),
            new FinancialReceipt(BillingPlans.CryptoProvider, "settled:payment", "payment", cryptoId, null, BillingPlans.UsdtBsc, 29.123456789m) };
        await Task.WhenAll(receipts.Select(async r => { await using var db = Db(); await Service(db, clock).ReceiveAsync(r, default); }));
        await using var check = Db(); Assert.Single(await check.Set<AccessGrant>().ToListAsync());
        Assert.Single(await check.Set<PaymentReceipt>().Where(x => x.State == "DuplicatePayment").ToListAsync());
        var grant = await check.Set<AccessGrant>().SingleAsync(); Assert.Equal(clock.Now.AddHours(720), grant.EndsAt);
        await Service(check, clock).ReceiveAsync(receipts[0], default); Assert.Equal(2, await check.Set<PaymentReceipt>().CountAsync());
    }
    [BillingPostgresFact]
    public async Task RefundBeforeSuccessRevokesOnlyItsGrantAndRenewalRetainsDates()
    {
        await Reset(); var clock = new Clock();
        await using var db = Db(); var service = Service(db, clock);
        var order = (await service.CreateOrderAsync(1, false, default)).Value;
        var attempt = (await service.PrepareAttemptAsync(1, order.Id, BillingPlans.StarsProvider, default)).Value;
        await service.ReceiveAsync(new(BillingPlans.StarsProvider, "refund:first", "first", attempt.Id, 1, BillingPlans.Xtr, 1450, true), default);
        await service.ReceiveAsync(new(BillingPlans.StarsProvider, "paid:first", "first", attempt.Id, 1, BillingPlans.Xtr, 1450), default);
        Assert.NotNull((await db.Set<AccessGrant>().SingleAsync()).ReversedAt);
        var next = (await service.CreateOrderAsync(1, true, default)).Value;
        var nextAttempt = (await service.PrepareAttemptAsync(1, next.Id, BillingPlans.StarsProvider, default)).Value;
        await service.ReceiveAsync(new(BillingPlans.StarsProvider, "paid:second", "second", nextAttempt.Id, 1, BillingPlans.Xtr, 1450), default);
        Assert.Single(await db.Set<AccessGrant>().Where(x => x.ReversedAt == null).ToListAsync());
    }
    [BillingPostgresFact]
    public async Task ProFiftiethSucceedsExpiryPreservesSelectionAndPreferences()
    {
        await Reset(); var clock = new Clock(); var opts = Options.Create(new BillingOptions { EnforcementEnabled = true }); var policy = new EntitlementPolicy(opts, clock);
        await using var db = Db(); db.Add(new AccessGrant { Id = Guid.NewGuid(), UserId = 1, StartsAt = clock.Now, EndsAt = clock.Now.AddHours(720) }); await db.SaveChangesAsync();
        var service = Service(db, clock, policy);
        for (var i = 1; i <= 50; i++) Assert.True((await service.AddWalletAsync(Wallet(i), default)).IsSuccess);
        Assert.True((await service.AddWalletAsync(Wallet(51), default)).IsFailure);
        var before = await service.SummaryAsync(1, default); Assert.Equal(50, before.Effective);
        var selected = before.FreeSelection.ToHashSet(); var extra = await db.TrackedWallets.FirstAsync(x => !selected.Contains(x.Id));
        clock.Now = clock.Now.AddHours(720); Assert.False(policy.Allows(extra.Id, TrackingMode.Evm));
        await service.RefreshAsync(1, default); var after = await service.SummaryAsync(1, default);
        Assert.False(after.Pro); Assert.Equal(10, after.Effective); Assert.Equal(50, after.Saved);
        Assert.Equal(before.FreeSelection, after.FreeSelection);
    }
    [BillingPostgresFact]
    public async Task WrongOwnerCurrencyAndFractionalUnderpaymentCannotUnlock()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var service = Service(db, clock);
        var order = (await service.CreateOrderAsync(1, false, default)).Value;
        var a = (await service.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
        a.ExpectedAmount = 29.123456789m; await db.SaveChangesAsync();
        await service.ReceiveAsync(new(BillingPlans.CryptoProvider, "under", "p1", a.Id, null, BillingPlans.UsdtBsc, 29.123456788m), default);
        await service.ReceiveAsync(new(BillingPlans.CryptoProvider, "owner", "p2", a.Id, 2, BillingPlans.UsdtBsc, 30m), default);
        await service.ReceiveAsync(new(BillingPlans.CryptoProvider, "currency", "p3", a.Id, 1, "usdt", 30m), default);
        Assert.Empty(await db.Set<AccessGrant>().ToListAsync()); Assert.Equal(3, await db.Set<PaymentReceipt>().CountAsync(x => x.State == "Review"));
    }
    [BillingPostgresFact]
    public async Task ActualPollingDoesNotAcknowledgeFinancialUpdateUntilPersistenceSucceeds()
    {
        await Reset(); var clock = new Clock(); Guid attemptId;
        await using (var db = Db())
        {
            var service = Service(db, clock); var order = (await service.CreateOrderAsync(1, false, default)).Value;
            attemptId = (await service.PrepareAttemptAsync(1, order.Id, BillingPlans.StarsProvider, default)).Value.Id;
        }
        var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var offsets = new List<int>(); var failures = 0;
        var http = new FixtureHandler(async (request, ct) =>
        {
            var body = await request.Content!.ReadAsStringAsync(ct); using var json = JsonDocument.Parse(body);
            var offset = json.RootElement.TryGetProperty("offset", out var value) ? value.GetInt32() : 0;
            lock (offsets) offsets.Add(offset);
            if (offset >= 101) { acknowledged.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }
            return "{\"ok\":true,\"result\":[{\"update_id\":100,\"message\":{\"message_id\":1,\"date\":1793059200,\"from\":{\"id\":1,\"is_bot\":false,\"first_name\":\"Test\"},\"chat\":{\"id\":1,\"type\":\"private\"},\"successful_payment\":{\"currency\":\"XTR\",\"total_amount\":1450,\"invoice_payload\":\"" + attemptId.ToString("N") + "\",\"telegram_payment_charge_id\":\"pollcharge\",\"provider_payment_charge_id\":\"\"}}}]}";
        });
        var bot = new TelegramBotClient("123456:fixture-token", new HttpClient(http));
        var services = new ServiceCollection();
        await using var connectionDb = Db(); var connection = connectionDb.Database.GetConnectionString();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connection));
        services.AddSingleton<ITelegramBotClient>(bot);
        services.AddScoped<IBillingService>(sp => new FailOnceBilling(Service(sp.GetRequiredService<AppDbContext>(), clock), () => Interlocked.Increment(ref failures) == 1));
        var opts = Options.Create(new BillingOptions()); services.AddSingleton<IOptions<BillingOptions>>(opts);
        services.AddScoped(sp => new NowPaymentsClient(new HttpClient(), opts, sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<IBillingService>(), clock));
        services.AddScoped<TelegramBilling>();
        await using var provider = services.BuildServiceProvider(); await using var limiter = new TelegramGlobalRateLimiter();
        var handler = new TelegramCommandUpdateHandler(provider.GetRequiredService<IServiceScopeFactory>(), limiter, NullLogger<TelegramCommandUpdateHandler>.Instance);
        using var worker = new TelegramUpdateReceiverWorker(bot, handler, Options.Create(new TelegramBotOptions { BotToken = "fixture" }), NullLogger<TelegramUpdateReceiverWorker>.Instance, provider.GetRequiredService<IServiceScopeFactory>());
        await worker.StartAsync(default);
        try { await acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { await worker.StopAsync(default); }
        Assert.Equal(new[] { 0, 0, 101 }, offsets.Take(3));
        await using var check = Db(); Assert.Single(await check.Set<AccessGrant>().ToListAsync());
        Assert.Equal(101, (await check.Set<BillingCheckpoint>().SingleAsync(x => x.Key == "TelegramUpdates")).Offset);
    }

    [BillingPostgresFact]
    public async Task AuditedAttachmentRecoversMissedFirstCallback()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var service = Service(db, clock);
        var order = (await service.CreateOrderAsync(1, false, default)).Value;
        var attempt = (await service.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
        attempt.InvoiceId = "invoice-fixture"; attempt.State = "Pending"; await db.SaveChangesAsync();
        var handler = new FixtureHandler((request, ct) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            Assert.EndsWith("/payment/fixture-payment", path);
            return Task.FromResult("{\"payment_id\":\"fixture-payment\",\"invoice_id\":\"invoice-fixture\",\"order_id\":\"" + order.Id.ToString("N") + "\",\"pay_currency\":\"usdtbsc\",\"pay_amount\":29.125123,\"actually_paid\":29.125123,\"price_currency\":\"usd\",\"price_amount\":29,\"payment_status\":\"finished\"}");
        });
        var client = new NowPaymentsClient(new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/v1/") }, Options.Create(new BillingOptions { NowPaymentsApiKey = "fixture" }), db, service, clock);
        Assert.True(await client.AttachAsync(99, 1, attempt.Id, "invoice-fixture", "fixture-payment", "missed callback", default));
        Assert.Single(await db.Set<AccessGrant>().ToListAsync()); Assert.Equal("fixture-payment", attempt.ExternalId);
    }

    [BillingPostgresFact]
    public async Task CrossHostSelectionCycleRejectsOldEnrollmentEvenWhenCurrentlyEligible()
    {
        await Reset(); var clock = new Clock(); var opts = Options.Create(new BillingOptions { EnforcementEnabled = true });
        var oldPolicy = new EntitlementPolicy(opts, clock); var wallet = Wallet(1);
        await using (var first = Db()) { await Service(first, clock, oldPolicy).AddWalletAsync(wallet, default); }
        var old = oldPolicy.Get(wallet.Id)!;
        clock.Now = clock.Now.AddSeconds(1);
        var newPolicy = new EntitlementPolicy(opts, clock);
        await using (var second = Db())
        {
            var billing = Service(second, clock, newPolicy); var before = await billing.SummaryAsync(1, default);
            Assert.True((await billing.SelectAsync(1, [], null, before.Revision, default)).IsSuccess);
            var paused = await billing.SummaryAsync(1, default);
            Assert.True((await billing.SelectAsync(1, [wallet.Id], null, paused.Revision, default)).IsSuccess);
        }
        // Host A's memory still approves the old revision; the authoritative boundary must reject it.
        Assert.True(oldPolicy.Allows(wallet.Id, TrackingMode.Evm, clock.Now, old.Revision));
        var services = new ServiceCollection(); await using var sample = Db();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(sample.Database.GetConnectionString()));
        await using var provider = services.BuildServiceProvider();
        var access = new RecipientAccess(provider.GetRequiredService<IServiceScopeFactory>(), opts, clock, oldPolicy);
        Assert.False(await access.AllowsAsync(1, wallet.Id, TrackingMode.Evm, clock.Now, old.Revision, default));
        var current = newPolicy.Get(wallet.Id)!;
        var refreshed = new RecipientAccess(provider.GetRequiredService<IServiceScopeFactory>(), opts, clock, newPolicy);
        Assert.True(await refreshed.AllowsAsync(1, wallet.Id, TrackingMode.Evm, clock.Now, current.Revision, default));
        Assert.False(await refreshed.AllowsAsync(1, wallet.Id, TrackingMode.Evm, old.ActiveSince, current.Revision, default));
    }

    [BillingPostgresFact]
    public async Task FailedSettingsCommandCannotCommitPartialChangesOrAdvanceRevision()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var billing = Service(db, clock); var wallet = Wallet(1);
        await billing.AddWalletAsync(wallet, default); var before = (await billing.SummaryAsync(1, default)).Revision;
        var handler = new UpdateTrackedWalletSettingsCommandHandler(new TrackedWalletRepository(db), new InMemoryCacheService());
        var request = new UpdateTrackedWalletSettingsCommand(1, wallet.Address.Value, 123, true, true, true, new string('a', 101));
        var behavior = new BillingMutationBehavior<UpdateTrackedWalletSettingsCommand, Result<TrackedWalletDetails>>(db, billing);
        var result = await behavior.Handle(request, token => handler.Handle(request, token), default);
        Assert.True(result.IsFailure);
        await using var check = Db(); Assert.Equal(0m, (await check.TrackedWallets.SingleAsync()).MinimumUsdValue);
        Assert.Equal(before, (await check.Set<BillingAccount>().SingleAsync()).Revision);
    }

    [BillingPostgresFact]
    public async Task MissedExpiryThenRenewalInvalidatesHyperliquidButKeepsSelectedFreePolymarket()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var billing = Service(db, clock); var wallet = Wallet(1);
        db.Add(new AccessGrant { Id = Guid.NewGuid(), UserId = 1, StartsAt = clock.Now, EndsAt = clock.Now.AddMinutes(10) }); await db.SaveChangesAsync();
        await billing.AddWalletAsync(wallet, default);
        var hl = new HyperliquidSetting { WalletId = wallet.Id, Enabled = true, Generation = Guid.NewGuid(), ActivatedAt = clock.Now };
        var pm = new PolymarketSetting { WalletId = wallet.Id, Enabled = true, Generation = Guid.NewGuid(), ActivatedAt = clock.Now, ActivationBlock = 100, SourceAddress = wallet.Address.Value };
        db.AddRange(hl, pm, new HyperliquidReservation { Address = wallet.Address.Value }); await db.SaveChangesAsync();
        var summary = await billing.SummaryAsync(1, default);
        await billing.SelectAsync(1, [wallet.Id], wallet.Id, summary.Revision, default);
        var hlGeneration = hl.Generation; var pmGeneration = pm.Generation;
        clock.Now = clock.Now.AddMinutes(11);
        db.Add(new AccessGrant { Id = Guid.NewGuid(), UserId = 1, StartsAt = clock.Now, EndsAt = clock.Now.AddDays(30) }); await db.SaveChangesAsync();
        // No expiry refresh ran between periods.
        await billing.RefreshAsync(1, default);
        Assert.NotEqual(hlGeneration, hl.Generation); Assert.Equal(clock.Now, hl.ActivatedAt);
        Assert.Equal(pmGeneration, pm.Generation); Assert.Equal(100, pm.ActivationBlock); Assert.False(pm.BillingPaused);
    }

    [BillingPostgresFact]
    public async Task EarlyIpnRemainsRecoverableUntilInvoiceResponseIsSaved()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var service = Service(db, clock);
        var order = (await service.CreateOrderAsync(1, false, default)).Value;
        var attempt = (await service.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
        var inbox = new PaymentReceipt { Id = Guid.NewGuid(), Provider = BillingPlans.CryptoProvider, EventKey = "early", ChargeId = "early-payment", Evidence = "{\"payment_id\":\"early-payment\"}", State = BillingStates.Inbox, ReceivedAt = clock.Now };
        db.Add(inbox); await db.SaveChangesAsync();
        var handler = new FixtureHandler((request, ct) => Task.FromResult("{\"payment_id\":\"early-payment\",\"invoice_id\":\"early-invoice\",\"order_id\":\"" + order.Id.ToString("N") + "\",\"pay_currency\":\"usdtbsc\",\"pay_amount\":29.25,\"actually_paid\":29.25,\"price_currency\":\"usd\",\"price_amount\":29,\"payment_status\":\"finished\"}"));
        var client = new NowPaymentsClient(new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/v1/") }, Options.Create(new BillingOptions()), db, service, clock);
        await client.ProcessInboxAsync(inbox, default); Assert.Equal(BillingStates.AwaitingProvider, inbox.State); Assert.Empty(await db.Set<AccessGrant>().ToListAsync());
        attempt.InvoiceId = "early-invoice"; await db.SaveChangesAsync();
        await client.ProcessInboxAsync(inbox, default); Assert.Equal(BillingStates.Processed, inbox.State); Assert.Single(await db.Set<AccessGrant>().ToListAsync());
    }

    [BillingPostgresFact]
    public async Task ContinuousEarlyRenewalKeepsDurableEvmBoundaryWhileLateRenewalResetsIt()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var service = Service(db, clock);
        db.Add(new AccessGrant { Id = Guid.NewGuid(), UserId = 1, StartsAt = clock.Now, EndsAt = clock.Now.AddMinutes(10) }); await db.SaveChangesAsync();
        for (var i = 1; i <= 11; i++) await service.AddWalletAsync(Wallet(i), default);
        var summary = await service.SummaryAsync(1, default);
        var paidOnly = await db.Set<WalletAccessState>().SingleAsync(x => !summary.FreeSelection.Contains(x.WalletId));
        var original = paidOnly.EnrollmentRevision; var starts = paidOnly.ActiveSince;
        clock.Now = clock.Now.AddSeconds(30);
        db.Add(new AccessGrant { Id = Guid.NewGuid(), UserId = 1, StartsAt = clock.Now.AddMinutes(9.5), EndsAt = clock.Now.AddMinutes(20) }); await db.SaveChangesAsync();
        await service.RefreshAsync(1, default); Assert.Equal(original, paidOnly.EnrollmentRevision); Assert.Equal(starts, paidOnly.ActiveSince);
        clock.Now = clock.Now.AddSeconds(61); // A local fail-closed freshness gap must invalidate pending work too.
        await service.RefreshAsync(1, default); Assert.True(paidOnly.EnrollmentRevision > original); Assert.Equal(clock.Now, paidOnly.ActiveSince);
        clock.Now = clock.Now.AddMinutes(21);
        db.Add(new AccessGrant { Id = Guid.NewGuid(), UserId = 1, StartsAt = clock.Now, EndsAt = clock.Now.AddDays(30) }); await db.SaveChangesAsync();
        await service.RefreshAsync(1, default); Assert.True(paidOnly.EnrollmentRevision > original); Assert.Equal(clock.Now, paidOnly.ActiveSince);
    }

    [BillingPostgresFact]
    public async Task FreePolymarketDeniesSecondEnableButSwitchesAtomicallyAtGlobalCapacity()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var opts = Options.Create(new BillingOptions { EnforcementEnabled = true });
        var policy = new EntitlementPolicy(opts, clock);
        var service = new BillingService(db, opts, clock, policy, new InMemoryCacheService(policy), new(), Options.Create(new HyperliquidOptions()),
            Options.Create(new PolymarketPolygonOptions { Enabled = true, MaxUniqueAddresses = 1 }), new BillingRpc(), new BillingProfiles(clock));
        var first = Wallet(1); var second = Wallet(2); await service.AddWalletAsync(first, default); await service.AddWalletAsync(second, default);
        var initial = new PolymarketSetting { WalletId = first.Id, Enabled = true, SourceAddress = first.Address.Value, Generation = Guid.NewGuid(), ActivatedAt = clock.Now, ActivationBlock = 10 };
        var next = new PolymarketSetting { WalletId = second.Id, SourceAddress = second.Address.Value, Generation = Guid.NewGuid(), ActivatedAt = clock.Now };
        db.AddRange(initial, next); await db.SaveChangesAsync();
        var summary = await service.SummaryAsync(1, default);
        await service.SelectAsync(1, [first.Id, second.Id], first.Id, summary.Revision, default);
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await BillingService.LockOwnerAsync(db, 1, default);
            Assert.Equal(BillingErrors.Limit, (await service.CanEnableAsync(1, second.Id, TrackingMode.Polymarket, default)).Error);
            await tx.RollbackAsync();
        }
        var before = await service.SummaryAsync(1, default); var oldGeneration = initial.Generation;
        Assert.True((await service.SelectAsync(1, before.FreeSelection, second.Id, before.Revision, default)).IsSuccess);
        Assert.True(initial.Enabled); Assert.True(initial.BillingPaused); Assert.NotEqual(oldGeneration, initial.Generation);
        Assert.True(next.Enabled); Assert.False(next.BillingPaused); Assert.Equal(1000, next.ActivationBlock);
        Assert.False(policy.Allows(first.Id, TrackingMode.Polymarket)); Assert.True(policy.Allows(second.Id, TrackingMode.Polymarket));
        Assert.Single(await db.Set<PolymarketSetting>().Where(x => x.Enabled && !x.BillingPaused).ToListAsync());
    }

    [BillingPostgresFact]
    public async Task MultiplePolymarketRestorationsCountUncommittedAdmissions()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var opts = Options.Create(new BillingOptions { EnforcementEnabled = true });
        var policy = new EntitlementPolicy(opts, clock);
        var service = new BillingService(db, opts, clock, policy, new InMemoryCacheService(policy), new(), Options.Create(new HyperliquidOptions()),
            Options.Create(new PolymarketPolygonOptions { Enabled = true, MaxUniqueAddresses = 1 }), new BillingRpc(), new BillingProfiles(clock));
        db.Add(new AccessGrant { Id = Guid.NewGuid(), UserId = 1, StartsAt = clock.Now, EndsAt = clock.Now.AddDays(30) }); await db.SaveChangesAsync();
        foreach (var id in new[] { 1, 2 })
        {
            var wallet = Wallet(id); await service.AddWalletAsync(wallet, default);
            db.Add(new PolymarketSetting { WalletId = wallet.Id, Enabled = true, BillingPaused = true, Generation = Guid.NewGuid(), SourceAddress = wallet.Address.Value });
        }
        await db.SaveChangesAsync(); await service.RefreshAsync(1, default);
        Assert.Single(await db.Set<PolymarketSetting>().Where(x => x.Enabled && !x.BillingPaused).ToListAsync());
        Assert.Equal(BillingDiagnostics.CapacityPaused, (await db.Set<PolymarketSetting>().SingleAsync(x => x.BillingPaused)).BillingPauseReason);
    }

    [BillingPostgresFact]
    public async Task PersonalHyperliquidLimitIsCheckedBeforeSharedSourceAdmission()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var service = Service(db, clock);
        db.Add(new AccessGrant { Id = Guid.NewGuid(), UserId = 1, StartsAt = clock.Now, EndsAt = clock.Now.AddDays(30) }); await db.SaveChangesAsync();
        for (var id = 1; id <= 10; id++)
        {
            var wallet = Wallet(id); await service.AddWalletAsync(wallet, default);
            db.Add(new HyperliquidSetting { WalletId = wallet.Id, Enabled = true, Generation = Guid.NewGuid(), ActivatedAt = clock.Now });
        }
        await db.SaveChangesAsync(); var eleventh = Wallet(11); await service.AddWalletAsync(eleventh, default);
        // The requested source already has another follower, so shared admission itself costs no slot.
        var otherOwner = Wallet(11, 2); db.Add(otherOwner); db.Add(new HyperliquidReservation { Address = eleventh.Address.Value.ToLowerInvariant() });
        var last = await db.TrackedWallets.SingleAsync(x => x.UserId == 1 && x.Address == Wallet(10).Address);
        var paused = await db.Set<HyperliquidSetting>().SingleAsync(x => x.WalletId == last.Id); paused.BillingPaused = true;
        db.Remove(await db.Set<HyperliquidReservation>().SingleAsync(x => x.Address == last.Address.Value.ToLowerInvariant()));
        db.Add(new HyperliquidSetting { WalletId = otherOwner.Id, Enabled = true, Generation = Guid.NewGuid(), ActivatedAt = clock.Now }); await db.SaveChangesAsync();
        var settings = new HyperliquidSettings(db, Options.Create(new HyperliquidOptions()), new(), service);
        Assert.Equal(BillingErrors.Limit, (await settings.SetAsync(1, eleventh.Id, true, default)).Error);
        Assert.False(await db.Set<HyperliquidSetting>().AnyAsync(x => x.WalletId == eleventh.Id));
    }

    [BillingPostgresFact]
    public async Task KnownChargeRefundHistoryWithoutPayloadRevokesAndReviewCanBeRepaired()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var service = Service(db, clock);
        var order = (await service.CreateOrderAsync(1, false, default)).Value;
        var attempt = (await service.PrepareAttemptAsync(1, order.Id, BillingPlans.StarsProvider, default)).Value;
        await service.ReceiveAsync(new(BillingPlans.StarsProvider, BillingKeys.Paid("history-charge"), "history-charge", null, 1, BillingPlans.Xtr, 1450, Verified: false), default);
        await service.ReceiveAsync(new(BillingPlans.StarsProvider, BillingKeys.Paid("history-charge"), "history-charge", attempt.Id, 1, BillingPlans.Xtr, 1450), default);
        Assert.Single(await db.Set<AccessGrant>().Where(x => x.ReversedAt == null).ToListAsync());
        var bot = new TelegramBotClient("123456:fixture-token", new HttpClient(new FixtureHandler((request, ct) => Task.FromResult("{\"ok\":true,\"result\":{\"transactions\":[{\"id\":\"history-charge\",\"amount\":1450,\"date\":1793059200,\"receiver\":{\"type\":\"user\",\"user\":{\"id\":1,\"is_bot\":false,\"first_name\":\"Test\"},\"transaction_type\":\"invoice_payment\"}}]}}"))));
        var services = new ServiceCollection(); services.AddSingleton(db); services.AddSingleton<IBillingService>(service);
        await using var provider = services.BuildServiceProvider();
        using var worker = new BillingWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BillingWorker>.Instance, clock);
        await worker.ReconcileStarsAsync(provider, bot, default);
        Assert.NotNull((await db.Set<AccessGrant>().SingleAsync()).ReversedAt);
    }

    [BillingPostgresFact]
    public async Task SettledInvoiceReconciliationImportsChildAndRecoversLaterRefundWithoutIpns()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var billing = Service(db, clock);
        var order = (await billing.CreateOrderAsync(1, false, default)).Value;
        var attempt = (await billing.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
        attempt.InvoiceId = "settled-invoice"; attempt.ExternalId = "original-payment"; attempt.ExpectedAmount = 29m;
        await db.SaveChangesAsync();
        await billing.ReceiveAsync(new(BillingPlans.CryptoProvider, BillingKeys.Settlement("original-payment", 29), "original-payment", attempt.Id, null, BillingPlans.UsdtBsc, 29), default);
        var originalReceipt = await db.Set<PaymentReceipt>().SingleAsync(x => x.State == BillingStates.Applied);
        var refunded = false; var calls = 0;
        var handler = new FixtureHandler((request, ct) =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.Contains("/payment/", request.RequestUri!.AbsolutePath);
            calls++; var path = request.RequestUri!.PathAndQuery;
            var child = path.EndsWith("/child-payment"); var id = child ? "child-payment" : "original-payment";
            return Task.FromResult("{\"payment_id\":\"" + id + "\",\"invoice_id\":\"settled-invoice\",\"order_id\":\"" + order.Id.ToString("N")
                + "\",\"pay_currency\":\"usdtbsc\",\"pay_amount\":29,\"actually_paid\":" + (child ? "31" : "29")
                + ",\"price_currency\":\"usd\",\"price_amount\":29,\"payment_status\":\"" + (!child && refunded ? "refunded" : "finished") + "\"}");
        });
        var client = new NowPaymentsClient(new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/v1/") },
            Options.Create(new BillingOptions { NowPaymentsApiKey = "fixture" }), db, billing, clock);
        var services = new ServiceCollection(); services.AddSingleton(db); services.AddSingleton(client);
        await using var provider = services.BuildServiceProvider();
        using var worker = new BillingWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BillingWorker>.Instance, clock);
        attempt.RetryAt = clock.Now; await db.SaveChangesAsync();
        await worker.ReconcileCryptoAsync(provider, default);
        await client.ImportAsync(99, "child-payment", "dashboard evidence", default);
        Assert.Single(await db.Set<AccessGrant>().ToListAsync());
        var childReceipt = await db.Set<PaymentReceipt>().SingleAsync(x => x.ChargeId == "child-payment" && x.Amount == 31m);
        Assert.Equal(BillingStates.DuplicatePayment, childReceipt.State); Assert.Equal(attempt.Id, childReceipt.AttemptId);
        Assert.Equal("original-payment", attempt.ExternalId); Assert.Equal(29m, attempt.ExpectedAmount);
        var previousCalls = calls;
        await worker.ReconcileCryptoAsync(provider, default); Assert.Equal(previousCalls, calls); // No hot polling after success.
        clock.Now = clock.Now.AddMinutes(16); refunded = true;
        await worker.ReconcileCryptoAsync(provider, default);
        Assert.NotNull((await db.Set<AccessGrant>().SingleAsync()).ReversedAt);
        Assert.Equal(BillingStates.Applied, originalReceipt.State); // Transport refresh never rewrites financial evidence.
        Assert.Single(await db.Set<PaymentReceipt>().Where(x => x.ChargeId == "original-payment" && x.Reversal && x.State == BillingStates.Reversed).ToListAsync());
        Assert.Single(await db.Set<AccessGrant>().ToListAsync());
    }

    [BillingPostgresFact]
    public async Task SettledRefundStatusRecoveryStillWorksWithoutMerchantJwtCredentials()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var billing = Service(db, clock);
        var order = (await billing.CreateOrderAsync(1, false, default)).Value;
        var attempt = (await billing.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
        attempt.InvoiceId = "api-only-invoice"; attempt.ExternalId = "api-only-payment"; attempt.ExpectedAmount = 29m; await db.SaveChangesAsync();
        await billing.ReceiveAsync(new(BillingPlans.CryptoProvider, BillingKeys.Settlement("api-only-payment", 29), "api-only-payment", attempt.Id, null, BillingPlans.UsdtBsc, 29), default);
        var handler = new FixtureHandler((request, ct) =>
        {
            Assert.EndsWith("/payment/api-only-payment", request.RequestUri!.AbsolutePath);
            return Task.FromResult("{\"payment_id\":\"api-only-payment\",\"invoice_id\":\"api-only-invoice\",\"order_id\":\"" + order.Id.ToString("N") + "\",\"pay_currency\":\"usdtbsc\",\"pay_amount\":29,\"actually_paid\":29,\"price_currency\":\"usd\",\"price_amount\":29,\"payment_status\":\"refunded\"}");
        });
        var client = new NowPaymentsClient(new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/v1/") }, Options.Create(new BillingOptions { NowPaymentsApiKey = "fixture" }), db, billing, clock);
        var services = new ServiceCollection(); services.AddSingleton(db); services.AddSingleton(client);
        await using var provider = services.BuildServiceProvider();
        using var worker = new BillingWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BillingWorker>.Instance, clock);
        attempt.RetryAt = clock.Now; await db.SaveChangesAsync();
        await worker.ReconcileCryptoAsync(provider, default);
        Assert.NotNull((await db.Set<AccessGrant>().SingleAsync()).ReversedAt);
        Assert.Equal(BillingStates.Reversed, attempt.State); Assert.True(attempt.RetryAt > clock.Now);
    }

    [BillingPostgresFact]
    public async Task DuplicateStarsPaymentCanBeRefundedWithoutRevokingWinningCryptoGrantOrRetryingRefund()
    {
        await Reset(); var clock = new Clock(); await using var db = Db(); var billing = Service(db, clock);
        var order = (await billing.CreateOrderAsync(1, false, default)).Value;
        var stars = (await billing.PrepareAttemptAsync(1, order.Id, BillingPlans.StarsProvider, default)).Value;
        var crypto = (await billing.PrepareAttemptAsync(1, order.Id, BillingPlans.CryptoProvider, default)).Value;
        crypto.ExpectedAmount = 29m; await db.SaveChangesAsync();
        await billing.ReceiveAsync(new(BillingPlans.CryptoProvider, BillingKeys.Settlement("crypto-winner", 29), "crypto-winner", crypto.Id, null, BillingPlans.UsdtBsc, 29), default);
        await billing.ReceiveAsync(new(BillingPlans.StarsProvider, BillingKeys.Paid("stars-duplicate"), "stars-duplicate", stars.Id, 1, BillingPlans.Xtr, 1450), default);
        var grant = await db.Set<AccessGrant>().SingleAsync(); var through = grant.EndsAt; var refundCalls = 0;
        var bot = new TelegramBotClient("123456:fixture-token", new HttpClient(new FixtureHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/refundStarPayment"))
            {
                refundCalls++; using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal(1, body.RootElement.GetProperty("user_id").GetInt64());
                Assert.Equal("stars-duplicate", body.RootElement.GetProperty("telegram_payment_charge_id").GetString());
                return "{\"ok\":true,\"result\":true}";
            }
            return "{\"ok\":true,\"result\":{\"message_id\":1,\"date\":1793059200,\"chat\":{\"id\":99,\"type\":\"private\"},\"text\":\"fixture\"}}";
        })));
        var opts = Options.Create(new BillingOptions { OperatorUserIds = [99] });
        var adapter = new TelegramBilling(billing, db, opts, new NowPaymentsClient(new HttpClient(), opts, db, billing, clock), bot);
        var update = new Telegram.Bot.Types.Update { Id = 1, Message = new() { From = new() { Id = 99, FirstName = "Operator" },
            Chat = new() { Id = 99, Type = Telegram.Bot.Types.Enums.ChatType.Private }, Text = "/billing_refund 1 stars-duplicate confirm duplicate-method-payment" } };
        Assert.True(await adapter.HandleAsync(update, default));
        Assert.True(await adapter.HandleAsync(update, default));
        Assert.Equal(1, refundCalls);
        Assert.Single(await db.Set<BillingAudit>().Where(x => x.Action == BillingStates.RefundRequested && x.Reference == "stars-duplicate").ToListAsync());
        Assert.Equal(BillingStates.Reversed, (await db.Set<PaymentReceipt>().SingleAsync(x => x.ChargeId == "stars-duplicate" && x.Reversal)).State);
        Assert.Null(grant.ReversedAt); Assert.Equal(through, grant.EndsAt); Assert.Equal(crypto.Id, (await db.Set<PaymentReceipt>().SingleAsync(x => x.Id == grant.ReceiptId)).AttemptId);
        Assert.Single(await db.Set<AccessGrant>().ToListAsync());
    }

    private sealed class BillingProfiles(Clock clock) : IPolymarketProfileResolver
    {
        public Task<PolymarketProfileLookup> ResolveAsync(string address, CancellationToken cancellationToken) => Task.FromResult(new PolymarketProfileLookup(RuntimeStates.Profile.Found,
            new PolymarketProfile(address, address, null, null, false, clock.Now, "fixture")));
    }
    private sealed class BillingRpc : IPolymarketPolygonRpc
    {
        public Task VerifyChainAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<long> HeadAsync(CancellationToken cancellationToken) => Task.FromResult(1000L);
        public Task<long> FinalizedHeadAsync(CancellationToken cancellationToken) => Task.FromResult(900L);
        public Task<PolymarketChainBlock> BlockAsync(long number, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PolymarketChainLog>> LogsAsync(object filter, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixtureHandler(Func<HttpRequestMessage, CancellationToken, Task<string>> send) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host == "fixture.invalid")
            {
                Assert.Null(request.Headers.Authorization);
                Assert.DoesNotContain("/auth", request.RequestUri.AbsolutePath);
                Assert.False(request.RequestUri.AbsolutePath.EndsWith("/payment/", StringComparison.Ordinal));
                Assert.DoesNotContain("invoiceId=", request.RequestUri.Query);
            }
            return new(System.Net.HttpStatusCode.OK) { Content = new StringContent(await send(request, ct), Encoding.UTF8, "application/json") };
        }
    }
    private sealed class FailOnceBilling(IBillingService inner, Func<bool> fail) : IBillingService
    {
        public Task ReceiveAsync(FinancialReceipt receipt, CancellationToken ct) => fail() ? Task.FromException(new IOException("Injected persistence failure")) : inner.ReceiveAsync(receipt, ct);
        public Task<Result> AddWalletAsync(TrackedWallet wallet, CancellationToken ct) => inner.AddWalletAsync(wallet, ct);
        public Task<Result> CanEnableAsync(long owner, Guid wallet, TrackingMode mode, CancellationToken ct) => inner.CanEnableAsync(owner, wallet, mode, ct);
        public Task<BillingSummary> SummaryAsync(long owner, CancellationToken ct) => inner.SummaryAsync(owner, ct);
        public Task<Result> SelectAsync(long owner, IReadOnlyCollection<Guid> wallets, Guid? pm, long revision, CancellationToken ct) => inner.SelectAsync(owner, wallets, pm, revision, ct);
        public Task<Result<PurchaseOrder>> CreateOrderAsync(long owner, bool renewal, CancellationToken ct) => inner.CreateOrderAsync(owner, renewal, ct);
        public Task<Result<PaymentAttempt>> PrepareAttemptAsync(long owner, Guid order, string provider, CancellationToken ct) => inner.PrepareAttemptAsync(owner, order, provider, ct);
        public Task<Result> PreCheckoutAsync(long owner, Guid attempt, string currency, long total, CancellationToken ct) => inner.PreCheckoutAsync(owner, attempt, currency, total, ct);
        public Task RefreshAsync(long owner, CancellationToken ct) => inner.RefreshAsync(owner, ct);
    }
    [Theory]
    [InlineData("{\"z\":29.00,\"a\":{\"z\":1e-7,\"a\":-0},\"s\":\"29.00\"}", "{\"a\":{\"a\":0,\"z\":1e-7},\"s\":\"29.00\",\"z\":29}")]
    [InlineData("{\"when\":\"2026-10-27T00:00:00Z\",\"n\":1e-6,\"large\":1e21}", "{\"large\":1e+21,\"n\":0.000001,\"when\":\"2026-10-27T00:00:00Z\"}")]
    [InlineData("{\"10\":1,\"2\":2,\"a\":[{\"z\":\"é/<\",\"a\":true}]}", "{\"2\":2,\"10\":1,\"a\":[{\"a\":true,\"z\":\"é/<\"}]}")]
    public void ProviderCanonicalFixturesPreserveTypesAndNumbers(string body, string canonical)
    {
        Assert.Equal(canonical, NowPaymentsSignature.Canonicalize(body));
        var sig = Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes("secret"), Encoding.UTF8.GetBytes(canonical)));
        Assert.True(NowPaymentsSignature.Verify(body, sig, " secret "));
        Assert.False(NowPaymentsSignature.Verify(body + " ", sig[..126] + "00", "secret"));
        Assert.False(NowPaymentsSignature.Verify("{\"a\":1,\"a\":2}", sig, "secret"));
    }
}
