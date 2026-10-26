using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Application.Hyperliquid;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure.Hyperliquid;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace UnitTests.Infrastructure;

public sealed class HyperliquidPostgresFactAttribute : FactAttribute
{
    public HyperliquidPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HYPERLIQUID_TEST_POSTGRES")))
            Skip = "Requires explicit disposable HYPERLIQUID_TEST_POSTGRES; production defaults are never used.";
    }
}

public sealed class HyperliquidPostgresTests
{
    private static string ConnectionString
    {
        get
        {
            var connection = Environment.GetEnvironmentVariable("HYPERLIQUID_TEST_POSTGRES") ?? throw new InvalidOperationException("Explicit test connection required");
            var builder = new NpgsqlConnectionStringBuilder(connection);
            if (builder.Database != "HyperliquidQa" || builder.Port != 55433 || builder.Host is not ("localhost" or "127.0.0.1"))
                throw new InvalidOperationException("Tests may write only to the disposable loopback HyperliquidQa database on port 55433.");
            return connection;
        }
    }
    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
    private static async Task ResetAsync()
    {
        await using var db = Db();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"HyperliquidDelivery\", \"HyperliquidEvent\", \"HyperliquidSetting\", \"HyperliquidReservation\", \"HyperliquidCheckpoint\", \"TrackedWallets\" CASCADE");
    }
    private static TrackedWallet Wallet(int number, long user = 1)
        => TrackedWallet.Create(Address.Normalize("0x" + number.ToString("x40")).Value, user).Value;
    private static HyperliquidSettings Settings(AppDbContext db, int max = 10)
        => new(db, Options.Create(new HyperliquidOptions { Enabled = true, MaxUniqueWallets = max }), new());

    [HyperliquidPostgresFact]
    public async Task OptOutDuringDeliveryPermitWaitCancelsWithoutSendingOrResurrectingWork()
    {
        await ResetAsync(); var wallet = Wallet(1);
        await using (var db = Db())
        {
            db.Add(wallet); await db.SaveChangesAsync(); await Settings(db).SetAsync(1, wallet.Id, true, default);
            var setting = await db.Set<HyperliquidSetting>().SingleAsync(); setting.ActivatedAt = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
            await new HyperliquidStore(db).AcceptAsync(new("inflight", wallet.Address.Value.ToLowerInvariant(), "userFills",
                DateTimeOffset.UtcNow.AddSeconds(-2).ToUnixTimeMilliseconds(), "Open long", "BTC", "1", 1, 10, 10, null), false, default);
        }
        var sender = new WaitingSender(); await using var services = Services(new(), new());
        using var worker = new HyperliquidDeliveryWorker(services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new HyperliquidOptions { Enabled = true }), sender, NullLogger<HyperliquidDeliveryWorker>.Instance);
        await worker.StartAsync(default);
        try
        {
            await sender.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using (var db = Db()) Assert.True((await Settings(db).SetAsync(1, wallet.Id, false, default)).IsSuccess);
            sender.Permit.TrySetResult();
            await sender.Checked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await UntilAsync(async () => { await using var db = Db(); return (await db.Set<HyperliquidDelivery>().SingleAsync()).State == "Cancelled"; });
            await Task.Delay(300);
            await using var check = Db(); var delivery = await check.Set<HyperliquidDelivery>().SingleAsync();
            Assert.Equal("Cancelled", delivery.State); Assert.Equal(0, delivery.Attempts); Assert.False(sender.Sent);
        }
        finally { sender.Permit.TrySetResult(); await worker.StopAsync(default); }
    }

    [HyperliquidPostgresFact]
    public async Task PartialSubscriptionRejectionClosesSocketWithoutAdvancingRecovery()
    {
        await ResetAsync(); var wallet = Wallet(1);
        await using (var db = Db()) { db.Add(wallet); await db.SaveChangesAsync(); await Settings(db).SetAsync(1, wallet.Id, true, default); }
        var factory = new FakeSockets(acknowledge: false); var info = new FakeInfo();
        await using var services = Services(factory, info); using var worker = Worker(services); await worker.StartAsync(default);
        try
        {
            await UntilAsync(() => factory.Sockets.Count == 1 && factory.Sockets.Single().Subscriptions.Count == 2);
            var socket = factory.Sockets.Single(); var subscription = socket.Subscriptions.First();
            socket.Push($$$"""{"channel":"subscriptionResponse","data":{"subscription":{{{subscription}}}}}""");
            socket.Push("""{"channel":"error","data":"subscription rejected"}""");
            await UntilAsync(() => socket.Disposed && factory.Sockets.Count >= 2);
            Assert.Equal(0, info.FillCalls);
            await using var check = Db(); Assert.Empty(await check.Set<HyperliquidCheckpoint>().ToListAsync());
        }
        finally { await worker.StopAsync(default); }
    }

    [HyperliquidPostgresFact]
    public async Task MissingPongClosesSilentSocketAndRebuildsSubscriptions()
    {
        await ResetAsync(); var wallet = Wallet(1);
        await using (var db = Db()) { db.Add(wallet); await db.SaveChangesAsync(); await Settings(db).SetAsync(1, wallet.Id, true, default); }
        var factory = new FakeSockets(pong: false);
        await using var services = Services(factory, new(), new HyperliquidOptions { Enabled = true, PingSeconds = 5, PongTimeoutSeconds = 6 });
        using var worker = Worker(services); await worker.StartAsync(default);
        try
        {
            await UntilAsync(() => factory.Sockets.Count == 1 && factory.Sockets.Single().Subscriptions.Count == 2);
            var first = factory.Sockets.Single();
            await UntilAsync(() => first.Disposed && factory.Sockets.Count >= 2, 20000);
            Assert.True(first.Pings > 0);
        }
        finally { await worker.StopAsync(default); }
    }

    [HyperliquidPostgresFact]
    public async Task FullLiveQueueClosesSocketRatherThanSilentlyDroppingSourceFrames()
    {
        await ResetAsync(); var wallet = Wallet(1);
        await using (var db = Db()) { db.Add(wallet); await db.SaveChangesAsync(); await Settings(db).SetAsync(1, wallet.Id, true, default); }
        var factory = new FakeSockets(); var info = new FakeInfo { MetadataGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var services = Services(factory, info, new HyperliquidOptions { Enabled = true, QueueCapacity = 1 });
        using var worker = Worker(services); await worker.StartAsync(default);
        try
        {
            await UntilAsync(() => factory.Sockets.Count == 1 && factory.Sockets.Single().Subscriptions.Count == 2);
            var first = factory.Sockets.Single();
            var frame = $$$"""{"channel":"userFills","data":{"user":"{{{wallet.Address.Value}}}","isSnapshot":false,"fills":[]}}""";
            first.Push(frame); first.Push(frame); first.Push(frame);
            await UntilAsync(() => first.Disposed);
            await using var check = Db(); Assert.Empty(await check.Set<HyperliquidCheckpoint>().ToListAsync());
        }
        finally { await worker.StopAsync(default); }
    }

    [HyperliquidPostgresFact]
    public async Task SuccessfulInclusivePaginationAndRestartKeepOneSourceAndRecipientPerFill()
    {
        await ResetAsync(); var wallet = Wallet(1); var time = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        await using (var db = Db())
        {
            db.Add(wallet); await db.SaveChangesAsync(); await Settings(db).SetAsync(1, wallet.Id, true, default);
            (await db.Set<HyperliquidSetting>().SingleAsync()).ActivatedAt = DateTimeOffset.FromUnixTimeMilliseconds(time);
            await db.SaveChangesAsync();
        }
        var firstPage = "[" + string.Join(',', Enumerable.Range(0, 2000).Select(i => HyperliquidProtocolTests.Fill(tid: i, time: time + i).GetRawText())) + "]";
        var overlapPage = "[" + HyperliquidProtocolTests.Fill(tid: 1999, time: time + 1999).GetRawText() + "," + HyperliquidProtocolTests.Fill(tid: 2000, time: time + 2000).GetRawText() + "]";
        var factory = new FakeSockets(); var info = new FakeInfo { FillPage = start => start <= time ? firstPage : start == time + 1999 ? overlapPage : "[]" };
        await using var services = Services(factory, info);
        using (var worker = Worker(services))
        {
            await worker.StartAsync(default);
            try { await UntilAsync(async () => { await using var db = Db(); return await db.Set<HyperliquidCheckpoint>().AnyAsync(x => x.Key.EndsWith("userFills") && x.Time > time + 2000); }, 30000); }
            finally { await worker.StopAsync(default); }
        }
        using (var restarted = Worker(services))
        {
            var previousCalls = info.FillCalls; await restarted.StartAsync(default);
            try { await UntilAsync(() => info.FillCalls > previousCalls); }
            finally { await restarted.StopAsync(default); }
        }
        await using var check = Db();
        Assert.Equal(2001, await check.Set<HyperliquidEvent>().CountAsync());
        Assert.Equal(2001, await check.Set<HyperliquidDelivery>().CountAsync());
    }

    [HyperliquidPostgresFact]
    public async Task TenThousandFillRetentionLimitCreatesGapWithoutAdvancingCompleteness()
    {
        await ResetAsync(); var wallet = Wallet(1); var time = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
        await using (var db = Db())
        {
            db.Add(wallet); await db.SaveChangesAsync(); await Settings(db).SetAsync(1, wallet.Id, true, default);
            (await db.Set<HyperliquidSetting>().SingleAsync()).ActivatedAt = DateTimeOffset.FromUnixTimeMilliseconds(time); await db.SaveChangesAsync();
        }
        var info = new FakeInfo { FillPage = start =>
        {
            var offset = Math.Max(0, (int)(start - time));
            return "[" + string.Join(',', Enumerable.Range(offset, Math.Min(2000, 10000 - offset))
                .Select(i => HyperliquidProtocolTests.Fill(tid: i, time: time + i, coin: "UNKNOWN").GetRawText())) + "]";
        } };
        await using var services = Services(new(), info); using var worker = Worker(services); await worker.StartAsync(default);
        try
        {
            await UntilAsync(async () => { await using var db = Db(); return await db.Set<HyperliquidCheckpoint>().AnyAsync(x => x.Key.EndsWith("userFills") && x.Gap != null); }, 20000);
            await using var check = Db(); var checkpoint = await check.Set<HyperliquidCheckpoint>().SingleAsync(x => x.Key.EndsWith("userFills"));
            Assert.Equal(0, checkpoint.Time); Assert.Contains("retention exhausted", checkpoint.Gap);
            Assert.Empty(await check.Set<HyperliquidEvent>().ToListAsync());
            Assert.InRange(info.FillCalls, 5, 6);
        }
        finally { await worker.StopAsync(default); }
    }

    [HyperliquidPostgresFact]
    public async Task InvalidPausedCapacityCannotAdmitMoreThanTheProviderMaximum()
    {
        await ResetAsync(); await using var db = Db(); var wallets = Enumerable.Range(1, 11).Select(i => Wallet(i)).ToArray();
        db.AddRange(wallets); await db.SaveChangesAsync();
        var settings = new HyperliquidSettings(db, Options.Create(new HyperliquidOptions { Enabled = false, MaxUniqueWallets = 99 }), new());
        foreach (var wallet in wallets.Take(10)) Assert.True((await settings.SetAsync(1, wallet.Id, true, default)).IsSuccess);
        Assert.True((await settings.SetAsync(1, wallets[10].Id, true, default)).IsFailure);
        Assert.Equal(10, await db.Set<HyperliquidReservation>().CountAsync());
    }

    [HyperliquidPostgresFact]
    public async Task PausedSettingsReleaseUnusedCapacityButAnActiveOwnerKeepsItsReservations()
    {
        await ResetAsync(); await using var db = Db();
        var a = Wallet(1); var b = Wallet(2); db.AddRange(a, b); await db.SaveChangesAsync();
        var paused = new HyperliquidSettings(db, Options.Create(new HyperliquidOptions { Enabled = false, MaxUniqueWallets = 1 }), new());
        Assert.True((await paused.SetAsync(1, a.Id, true, default)).IsSuccess);
        Assert.True((await paused.SetAsync(1, a.Id, false, default)).IsSuccess);
        Assert.Empty(await db.Set<HyperliquidReservation>().ToListAsync());
        Assert.True((await paused.SetAsync(1, b.Id, true, default)).IsSuccess);

        await using var lease = new NpgsqlConnection(ConnectionString); await lease.OpenAsync();
        await using (var acquire = new NpgsqlCommand($"SELECT pg_advisory_lock({HyperliquidWorker.OwnerLock})", lease)) await acquire.ExecuteScalarAsync();
        try
        {
            Assert.True((await paused.SetAsync(1, b.Id, false, default)).IsSuccess);
            Assert.Single(await db.Set<HyperliquidReservation>().ToListAsync());
            Assert.True((await paused.SetAsync(1, a.Id, true, default)).IsFailure);
        }
        finally
        {
            await using var release = new NpgsqlCommand($"SELECT pg_advisory_unlock({HyperliquidWorker.OwnerLock})", lease);
            await release.ExecuteScalarAsync();
        }
        Assert.True((await paused.SetAsync(1, a.Id, true, default)).IsSuccess);
    }

    [HyperliquidPostgresFact]
    public async Task RecipientRotationReachesFifthChatWhileEarlierChatsRemainBacklogged()
    {
        await ResetAsync();
        await using (var db = Db())
        {
            var wallets = Enumerable.Range(1, 5).Select(i => Wallet(1, i)).ToArray();
            db.AddRange(wallets); await db.SaveChangesAsync();
            foreach (var wallet in wallets) await Settings(db).SetAsync(wallet.UserId, wallet.Id, true, default);
            foreach (var setting in await db.Set<HyperliquidSetting>().ToListAsync()) setting.ActivatedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
            var store = new HyperliquidStore(db);
            for (var i = 0; i < 20; i++)
                await store.AcceptAsync(new("fairness-" + i, wallets[0].Address.Value.ToLowerInvariant(), "userNonFundingLedgerUpdates",
                    DateTimeOffset.UtcNow.AddSeconds(-2).ToUnixTimeMilliseconds(), "deposit", "USDC", "", 1, 1, 1, null), false, default);
        }
        var sender = new SuccessfulSender(); await using var services = Services(new(), new());
        using var worker = new HyperliquidDeliveryWorker(services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new HyperliquidOptions { Enabled = true }), sender, NullLogger<HyperliquidDeliveryWorker>.Instance);
        await worker.StartAsync(default);
        try
        {
            await UntilAsync(() => sender.Delivered.Distinct().Count() == 5);
            Assert.InRange(sender.Delivered.Count(x => x == 1), 1, 3);
            await using var check = Db();
            Assert.True(await check.Set<HyperliquidDelivery>().AnyAsync(x => x.UserId == 1 && x.State == "Pending"));
        }
        finally { await worker.StopAsync(default); }
    }

    [HyperliquidPostgresFact]
    public async Task ConcurrentFinalSlotRequestsHaveExactlyOneWinnerAndNoPartialOptIn()
    {
        await ResetAsync();
        var wallets = Enumerable.Range(1, 11).Select(x => Wallet(x)).ToArray();
        await using (var db = Db()) { db.AddRange(wallets); await db.SaveChangesAsync(); }
        for (var i = 0; i < 9; i++) { await using var db = Db(); Assert.True((await Settings(db).SetAsync(1, wallets[i].Id, true, default)).IsSuccess); }
        var results = await Task.WhenAll(wallets.Skip(9).Select(async wallet =>
        { await using var db = Db(); return await Settings(db).SetAsync(1, wallet.Id, true, default); }));
        Assert.Single(results, x => x.IsSuccess);
        Assert.Single(results, x => x.IsFailure);
        await using var check = Db();
        Assert.Equal(10, await check.Set<HyperliquidReservation>().CountAsync());
        Assert.Equal(10, await check.Set<HyperliquidSetting>().CountAsync(x => x.Enabled));
    }

    [HyperliquidPostgresFact]
    public async Task SharedAddressOwnersUseOneSlotAndOwnershipIsEnforced()
    {
        await ResetAsync();
        var a = Wallet(1, 1); var b = Wallet(1, 2); var c = Wallet(2, 3);
        await using var db = Db(); db.AddRange(a, b, c); await db.SaveChangesAsync();
        var settings = Settings(db, 1);
        Assert.True((await settings.SetAsync(1, a.Id, true, default)).IsSuccess);
        Assert.True((await settings.SetAsync(2, b.Id, true, default)).IsSuccess);
        Assert.True((await settings.SetAsync(1, b.Id, false, default)).IsFailure);
        Assert.True((await settings.SetAsync(3, c.Id, true, default)).IsFailure);
        Assert.Single(await db.Set<HyperliquidReservation>().ToListAsync());
        Assert.Equal(2, (await db.Set<HyperliquidSetting>().ToListAsync()).Count(x => x.Enabled));
    }

    [HyperliquidPostgresFact]
    public async Task CapacityIsNotReleasedUntilOldSocketClosureBoundary()
    {
        await ResetAsync();
        await using var db = Db(); var a = Wallet(1); var b = Wallet(2); db.AddRange(a, b); await db.SaveChangesAsync();
        var settings = Settings(db, 1);
        await settings.SetAsync(1, a.Id, true, default);
        await settings.SetAsync(1, a.Id, false, default);
        Assert.True((await settings.SetAsync(1, b.Id, true, default)).IsFailure);
        await new HyperliquidStore(db).ReleaseUnusedReservationsAsync(default);
        Assert.True((await settings.SetAsync(1, b.Id, true, default)).IsSuccess);
    }

    [HyperliquidPostgresFact]
    public async Task DurableFanoutThresholdGenerationAndCheckpointCrashBoundary()
    {
        await ResetAsync();
        var wallet = Wallet(1); wallet.UpdateMinimumUsdValue(10);
        await using var db = Db(); db.Add(wallet); await db.SaveChangesAsync();
        var settings = Settings(db); await settings.SetAsync(1, wallet.Id, true, default);
        var activation = (await db.Set<HyperliquidSetting>().SingleAsync()).ActivatedAt.ToUnixTimeMilliseconds();
        var store = new HyperliquidStore(db);
        HyperliquidActivity Event(string id, decimal? usd, long time) => new(id, wallet.Address.Value.ToLowerInvariant(), "userFills", time, "Open long", "BTC", "1", 1, 10, usd, null);
        await store.AcceptAsync(Event("old", 100, activation - 1), false, default);
        await store.AcceptAsync(Event("below", 9.999999999m, activation), false, default);
        await store.AcceptAsync(Event("equal", 10, activation), false, default);
        await store.AcceptAsync(Event("equal", 10, activation), false, default);
        Assert.Single(await db.Set<HyperliquidDelivery>().ToListAsync());
        Assert.Empty(await db.Set<HyperliquidCheckpoint>().ToListAsync()); // Accepted live events do not claim completeness.
        var generation = (await db.Set<HyperliquidSetting>().SingleAsync()).Generation;
        await settings.SetAsync(1, wallet.Id, false, default);
        await settings.SetAsync(1, wallet.Id, true, default);
        db.ChangeTracker.Clear();
        Assert.NotEqual(generation, (await db.Set<HyperliquidSetting>().SingleAsync()).Generation);
        Assert.Equal("Cancelled", (await db.Set<HyperliquidDelivery>().SingleAsync()).State);
        await using var restarted = Db();
        Assert.Equal(3, await restarted.Set<HyperliquidEvent>().CountAsync());
        Assert.Single(await restarted.Set<HyperliquidDelivery>().ToListAsync());
    }

    [HyperliquidPostgresFact]
    public async Task MultipleHostsUseOneSocketAndSharedAddressUsesTwoSubscriptions()
    {
        await ResetAsync();
        var a = Wallet(1); var b = Wallet(1, 2);
        await using (var db = Db()) { db.AddRange(a, b); await db.SaveChangesAsync(); await Settings(db).SetAsync(1, a.Id, true, default); await Settings(db).SetAsync(2, b.Id, true, default); }
        var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var fill = HyperliquidProtocolTests.Fill(time: time);
        var factory = new FakeSockets(); var info = new FakeInfo { Fills = "[" + fill.GetRawText() + "]" };
        await using var services = Services(factory, info);
        using var first = Worker(services); using var second = Worker(services);
        await first.StartAsync(default); await second.StartAsync(default);
        try
        {
            await UntilAsync(() => factory.Sockets.TryPeek(out var s) && s.Subscriptions.Count == 2);
            await Task.Delay(1200);
            Assert.Single(factory.Sockets);
            var socket = factory.Sockets.Single();
            var address = a.Address.Value.ToLowerInvariant();
            socket.Push($$$"""{"channel":"userFills","data":{"user":"{{{address}}}","isSnapshot":true,"fills":[{{{HyperliquidProtocolTests.Fill(tid: 99, time: 1).GetRawText()}}},{{{fill.GetRawText()}}}]}}""");
            socket.Push($$$"""{"channel":"userFills","data":{"user":"{{{address}}}","isSnapshot":false,"fills":[{{{fill.GetRawText()}}}]}}""");
            await UntilAsync(async () => { await using var db = Db(); return await db.Set<HyperliquidDelivery>().CountAsync() == 2; });
            await using var check = Db();
            Assert.Equal(2, await check.Set<HyperliquidEvent>().CountAsync());
            Assert.Equal(2, await check.Set<HyperliquidDelivery>().CountAsync());
        }
        finally { await first.StopAsync(default); await second.StopAsync(default); }
    }

    [HyperliquidPostgresFact]
    public async Task RecoveryWaitsForEveryDesiredSubscriptionAcknowledgment()
    {
        await ResetAsync(); var wallet = Wallet(1);
        await using (var db = Db()) { db.Add(wallet); await db.SaveChangesAsync(); await Settings(db).SetAsync(1, wallet.Id, true, default); }
        var factory = new FakeSockets(acknowledge: false); var info = new FakeInfo();
        await using var services = Services(factory, info);
        using var worker = Worker(services); await worker.StartAsync(default);
        try
        {
            await UntilAsync(() => factory.Sockets.Count == 1 && factory.Sockets.Single().Subscriptions.Count == 2);
            await Task.Delay(150);
            Assert.Equal(0, info.FillCalls);
            var socket = factory.Sockets.Single();
            socket.Push("""{"channel":"subscriptionResponse","data":{"subscription":{"user":"unrelated","type":"userFills"}}}""");
            foreach (var subscription in socket.Subscriptions)
                socket.Push($$$"""{"channel":"subscriptionResponse","data":{"subscription":{{{subscription}}}}}""");
            await UntilAsync(() => info.FillCalls > 0);
        }
        finally { await worker.StopAsync(default); }
    }

    [HyperliquidPostgresFact]
    public async Task StaleEnableCannotUndoNewerOptOutAndRepeatedEnableIsIdempotent()
    {
        await ResetAsync(); await using var db = Db(); var wallet = Wallet(1); db.Add(wallet); await db.SaveChangesAsync();
        var settings = Settings(db);
        var first = await settings.SetAsync(1, wallet.Id, true, default, Guid.Empty);
        var repeated = await settings.SetAsync(1, wallet.Id, true, default, Guid.Empty);
        Assert.Equal(first.Value.Generation, repeated.Value.Generation);
        await settings.SetAsync(1, wallet.Id, false, default, first.Value.Generation);
        var stale = await settings.SetAsync(1, wallet.Id, true, default, Guid.Empty);
        Assert.True(stale.IsFailure);
        Assert.False((await settings.GetAsync(1, wallet.Id, default)).Value.Enabled);
    }

    [HyperliquidPostgresFact]
    public async Task RetentionKeepsPendingSourceWorkAndPrunesOnlyFinishedExpiredWork()
    {
        await ResetAsync(); await using var db = Db(); var wallet = Wallet(1); db.Add(wallet); await db.SaveChangesAsync();
        var old = DateTimeOffset.UtcNow.AddDays(-10);
        db.AddRange(new HyperliquidEvent { Id = "pending", StoredAt = old }, new HyperliquidEvent { Id = "done", StoredAt = old });
        db.AddRange(new HyperliquidDelivery { Id = Guid.NewGuid(), EventId = "pending", WalletId = wallet.Id, DueAt = old },
            new HyperliquidDelivery { Id = Guid.NewGuid(), EventId = "done", WalletId = wallet.Id, DueAt = old, State = "Sent" });
        await db.SaveChangesAsync();
        await new HyperliquidStore(db).PruneAsync(7, default);
        Assert.Equal("pending", (await db.Set<HyperliquidEvent>().AsNoTracking().SingleAsync()).Id);
        Assert.Equal("Pending", (await db.Set<HyperliquidDelivery>().AsNoTracking().SingleAsync()).State);
    }

    [HyperliquidPostgresFact]
    public async Task BlockedRecipientDoesNotBlockOtherOwnerAndRestartKeepsPendingDelivery()
    {
        await ResetAsync();
        var a = Wallet(1); var b = Wallet(1, 2);
        await using (var db = Db())
        {
            db.AddRange(a, b); await db.SaveChangesAsync();
            await Settings(db).SetAsync(1, a.Id, true, default); await Settings(db).SetAsync(2, b.Id, true, default);
            foreach (var setting in await db.Set<HyperliquidSetting>().ToListAsync()) setting.ActivatedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
            await new HyperliquidStore(db).AcceptAsync(new("delivery", a.Address.Value.ToLowerInvariant(), "userFills", DateTimeOffset.UtcNow.AddSeconds(-2).ToUnixTimeMilliseconds(), "Open long", "BTC", "1", 1, 10, 10, null), false, default);
        }
        var sender = new FakeSender();
        await using var services = Services(new(), new());
        using var worker = new HyperliquidDeliveryWorker(services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new HyperliquidOptions { Enabled = true }), sender, NullLogger<HyperliquidDeliveryWorker>.Instance);
        await worker.StartAsync(default);
        try
        {
            await UntilAsync(async () => { await using var db = Db(); return await db.Set<HyperliquidDelivery>().AnyAsync(x => x.UserId == 2 && x.State == "Sent"); });
            await using var check = Db();
            Assert.Equal("Pending", (await check.Set<HyperliquidDelivery>().SingleAsync(x => x.UserId == 1)).State);
            Assert.Equal(1, (await check.Set<HyperliquidDelivery>().SingleAsync(x => x.UserId == 1)).Attempts);
            Assert.Contains(2, sender.Delivered);
            Assert.DoesNotContain(1, sender.Delivered);
        }
        finally { await worker.StopAsync(default); }
    }

    [HyperliquidPostgresFact]
    public async Task LosingPostgresIngestionSessionClosesStaleSocketBeforeReconnection()
    {
        await ResetAsync(); var wallet = Wallet(1);
        await using (var db = Db()) { db.Add(wallet); await db.SaveChangesAsync(); await Settings(db).SetAsync(1, wallet.Id, true, default); }
        var factory = new FakeSockets();
        await using var services = Services(factory, new());
        using var worker = Worker(services); await worker.StartAsync(default);
        try
        {
            await UntilAsync(() => factory.Sockets.Count == 1 && factory.Sockets.Single().Subscriptions.Count == 2);
            var old = factory.Sockets.Single();
            await using var connection = new NpgsqlConnection(ConnectionString); await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT pg_terminate_backend(pid) FROM pg_locks WHERE locktype='advisory' AND classid=0 AND objid=74210002 AND granted", connection);
            Assert.True((bool)(await command.ExecuteScalarAsync())!);
            await UntilAsync(() => old.Disposed);
            await UntilAsync(() => factory.Sockets.Count >= 2);
            Assert.True(old.Disposed);
        }
        finally { await worker.StopAsync(default); }
    }

    [HyperliquidPostgresFact]
    public async Task FullSameTimestampRecoveryPageStallsWithGapAndNoCompletenessAdvance()
    {
        await ResetAsync(); var wallet = Wallet(1);
        long time;
        await using (var db = Db()) { db.Add(wallet); await db.SaveChangesAsync(); await Settings(db).SetAsync(1, wallet.Id, true, default); time = (await db.Set<HyperliquidSetting>().SingleAsync()).ActivatedAt.ToUnixTimeMilliseconds(); }
        var factory = new FakeSockets(); var info = new FakeInfo { Fills = "[" + string.Join(',', Enumerable.Range(1, 2000).Select(i => HyperliquidProtocolTests.Fill(tid: i, time: time).GetRawText())) + "]" };
        await using var services = Services(factory, info);
        using var worker = Worker(services);
        await worker.StartAsync(default);
        try
        {
            await UntilAsync(async () => { await using var db = Db(); return await db.Set<HyperliquidCheckpoint>().AnyAsync(x => x.Key.EndsWith("userFills") && x.Gap != null); }, 30000);
            await using var check = Db();
            var checkpoint = await check.Set<HyperliquidCheckpoint>().SingleAsync(x => x.Key.EndsWith("userFills"));
            Assert.Equal(0, checkpoint.Time);
            Assert.Contains("boundary", checkpoint.Gap);
            Assert.Equal(2000, await check.Set<HyperliquidEvent>().CountAsync());
            Assert.InRange(info.FillCalls, 1, 2);
        }
        finally { await worker.StopAsync(default); }
    }

    [Fact]
    public async Task DefaultOffMakesZeroCoreTransportCalls()
    {
        var factory = new FakeSockets(); var info = new FakeInfo();
        var services = new ServiceCollection().BuildServiceProvider();
        using var worker = new HyperliquidWorker(services.GetRequiredService<IServiceScopeFactory>(), factory, info, Options.Create(new HyperliquidOptions()), new(), NullLogger<HyperliquidWorker>.Instance);
        await worker.StartAsync(default); await worker.StopAsync(default);
        Assert.Empty(factory.Sockets); Assert.Equal(0, info.Calls);
    }

    private static ServiceProvider Services(FakeSockets factory, FakeInfo info, HyperliquidOptions? options = null)
    {
        var s = new ServiceCollection();
        s.AddDbContext<AppDbContext>(o => o.UseNpgsql(ConnectionString)); s.AddScoped<HyperliquidStore>();
        s.AddSingleton<IHyperliquidSocketFactory>(factory); s.AddSingleton<IHyperliquidInfoClient>(info);
        s.AddSingleton(Options.Create(options ?? new HyperliquidOptions { Enabled = true })); s.AddSingleton<HyperliquidChangeSignal>();
        return s.BuildServiceProvider();
    }
    private static HyperliquidWorker Worker(ServiceProvider s) => new(s.GetRequiredService<IServiceScopeFactory>(), s.GetRequiredService<IHyperliquidSocketFactory>(), s.GetRequiredService<IHyperliquidInfoClient>(), s.GetRequiredService<IOptions<HyperliquidOptions>>(), s.GetRequiredService<HyperliquidChangeSignal>(), NullLogger<HyperliquidWorker>.Instance);
    private static Task UntilAsync(Func<bool> test, int milliseconds = 10000) => UntilAsync(() => Task.FromResult(test()), milliseconds);
    private static async Task UntilAsync(Func<Task<bool>> test, int milliseconds = 10000)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(milliseconds);
        while (!await test()) { if (DateTimeOffset.UtcNow > deadline) throw new TimeoutException("Expected Hyperliquid state was not reached"); await Task.Delay(25); }
    }
    private sealed class FakeInfo : IHyperliquidInfoClient
    {
        public int Calls; public int FillCalls; public string Fills = "[]";
        public Func<long, string>? FillPage;
        public TaskCompletionSource? MetadataGate;
        public async Task<string> QueryAsync(object request, int weight, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(request));
            var type = json.RootElement.GetProperty("type").GetString();
            if (type == "meta")
            {
                if (MetadataGate is not null) await MetadataGate.Task.WaitAsync(ct);
                return """{"universe":[{"name":"BTC","szDecimals":5,"maxLeverage":40,"marginTableId":56}],"marginTables":[],"collateralToken":0}""";
            }
            if (type == "userFillsByTime") { Interlocked.Increment(ref FillCalls); return FillPage?.Invoke(json.RootElement.GetProperty("startTime").GetInt64()) ?? Fills; }
            return "[]";
        }
    }
    private sealed class FakeSockets(bool acknowledge = true, bool pong = true) : IHyperliquidSocketFactory
    {
        public ConcurrentQueue<FakeSocket> Sockets { get; } = new();
        public IHyperliquidSocket Create() { var socket = new FakeSocket(acknowledge, pong); Sockets.Enqueue(socket); return socket; }
    }
    private sealed class FakeSocket(bool acknowledge, bool pong) : IHyperliquidSocket
    {
        public volatile bool Disposed;
        public int Pings;
        private readonly Channel<string> _incoming = Channel.CreateUnbounded<string>();
        public ConcurrentQueue<string> Subscriptions { get; } = new();
        public Task ConnectAsync(Uri uri, CancellationToken ct) => Task.CompletedTask;
        public Task SendAsync(object message, CancellationToken ct)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(message));
            if (json.RootElement.GetProperty("method").GetString() == "ping") { Interlocked.Increment(ref Pings); if (pong) Push("""{"channel":"pong"}"""); }
            else { var subscription = json.RootElement.GetProperty("subscription").GetRawText(); Subscriptions.Enqueue(subscription); if (acknowledge) Push($$$"""{"channel":"subscriptionResponse","data":{"method":"subscribe","subscription":{{{subscription}}}}}"""); }
            return Task.CompletedTask;
        }
        public void Push(string message) => _incoming.Writer.TryWrite(message);
        public async Task<string> ReceiveAsync(int maximumBytes, CancellationToken ct) => await _incoming.Reader.ReadAsync(ct);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class FakeSender : IHyperliquidNotificationSender
    {
        public ConcurrentBag<long> Delivered { get; } = [];
        public async Task<bool> SendAsync(long userId, HyperliquidNotification notification, Func<CancellationToken, Task<bool>> stillEligible, CancellationToken ct)
        {
            if (!await stillEligible(ct)) return false;
            if (userId == 1) return false;
            Delivered.Add(userId); return true;
        }
    }

    private sealed class SuccessfulSender : IHyperliquidNotificationSender
    {
        public ConcurrentBag<long> Delivered { get; } = [];
        public async Task<bool> SendAsync(long userId, HyperliquidNotification notification, Func<CancellationToken, Task<bool>> stillEligible, CancellationToken ct)
        {
            if (!await stillEligible(ct)) return false;
            Delivered.Add(userId); return true;
        }
    }

    private sealed class WaitingSender : IHyperliquidNotificationSender
    {
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Permit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Checked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Sent;
        public async Task<bool> SendAsync(long userId, HyperliquidNotification notification, Func<CancellationToken, Task<bool>> stillEligible, CancellationToken ct)
        {
            Waiting.TrySetResult(); await Permit.Task.WaitAsync(ct);
            Sent = await stillEligible(ct); Checked.TrySetResult(); return Sent;
        }
    }
}
