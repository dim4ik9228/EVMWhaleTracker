using Application.Polymarket;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Polymarket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace UnitTests.Infrastructure.Polymarket;

public sealed class PolymarketPostgresFactAttribute : FactAttribute
{
    public PolymarketPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POLYMARKET_TEST_POSTGRES")))
            Skip = "Requires explicit disposable POLYMARKET_TEST_POSTGRES; production defaults are never used.";
    }
}

public sealed class PolymarketPolygonBudgetPostgresTests
{
    [PolymarketPostgresFact]
    public async Task ActiveOnlyValidationAndPendingSelectionRevalidateReactivatedSource()
    {
        var connection = Environment.GetEnvironmentVariable("POLYMARKET_TEST_POSTGRES")!;
        var config = new NpgsqlConnectionStringBuilder(connection);
        if (config.Database != "PolymarketQa" || config.Port != 55434 || config.Host is not ("localhost" or "127.0.0.1"))
            throw new InvalidOperationException("Only disposable loopback PolymarketQa on port 55434 is permitted.");
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"PolymarketCheckpoint\", \"PolymarketRawLog\" CASCADE");
        var active = "0x" + new string('1', 40); var inactive = "0x" + new string('2', 40);
        db.AddRange(new PolymarketCheckpoint { SourceAddress = active, CoveredBlock = 10, CoveredBlockHash = "hash10" },
            new PolymarketCheckpoint { SourceAddress = inactive, CoveredBlock = 20, CoveredBlockHash = "hash20" });
        db.Add(new PolymarketRawLog { Id = "inactive", TopicsJson = System.Text.Json.JsonSerializer.Serialize(new[] { PolymarketContractRegistry.WalletTopic(inactive) }), StoredAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var rpc = new Rpc();
        var store = new PolymarketPolygonStore(db, rpc, new Markets(), Options.Create(new PolymarketPolygonOptions()), new());
        Assert.False(await store.ValidateAsync([active], default));
        Assert.Equal(new long[] { 10 }, rpc.Reads);
        Assert.False(await store.HasPendingAsync([active], default));
        Assert.True(await store.HasPendingAsync([inactive], default));
        await store.FinalizeAsync(100, default);
        Assert.Equal(new long[] { 10 }, rpc.Reads);
        Assert.False(await store.ValidateAsync([active, inactive], default));
        Assert.Equal(new long[] { 10, 10, 20 }, rpc.Reads);

        // A source can change branch after validation and before the audit commits.
        rpc.Hashes[10] = "replacement10";
        Assert.False(await store.CoveredAsync([active], new(30, "hash30", "parent", DateTimeOffset.UtcNow), default));
        var checkpoint = await db.Set<PolymarketCheckpoint>().AsNoTracking().SingleAsync(x => x.SourceAddress == active);
        Assert.Equal(0L, checkpoint.CoveredBlock);
        Assert.Equal(1L, checkpoint.ScanNextBlock);
        Assert.Equal(20L, (await db.Set<PolymarketCheckpoint>().AsNoTracking().SingleAsync(x => x.SourceAddress == inactive)).CoveredBlock);

        // Even when the old checkpoint remains canonical, a changed range anchor
        // refuses coverage. A subsequent fresh, successful attempt may advance.
        rpc.Hashes[30] = "replacement30";
        rpc.Reads.Clear();
        Assert.False(await store.CoveredAsync([active], new(30, "hash30", "parent", DateTimeOffset.UtcNow), default));
        Assert.Equal(new long[] { 0, 30 }, rpc.Reads);
        Assert.Equal(0L, (await db.Set<PolymarketCheckpoint>().AsNoTracking().SingleAsync(x => x.SourceAddress == active)).CoveredBlock);
        rpc.Reads.Clear();
        Assert.True(await store.CoveredAsync([active], new(30, "replacement30", "parent", DateTimeOffset.UtcNow), default));
        Assert.Equal(new long[] { 0, 30 }, rpc.Reads);
        Assert.Equal(30L, (await db.Set<PolymarketCheckpoint>().AsNoTracking().SingleAsync(x => x.SourceAddress == active)).CoveredBlock);
    }
    private sealed class Markets : IPolymarketMarketResolver
    {
        public Task<PolymarketMarket?> ResolveAsync(string family, string tokenId, CancellationToken ct) => Task.FromResult<PolymarketMarket?>(null);
    }
    private sealed class Rpc : IPolymarketPolygonRpc
    {
        public List<long> Reads = [];
        public Dictionary<long, string> Hashes = [];
        public Task<PolymarketChainBlock> BlockAsync(long number, CancellationToken ct)
        { Reads.Add(number); return Task.FromResult(new PolymarketChainBlock(number, Hashes.GetValueOrDefault(number, "hash" + number), "parent", DateTimeOffset.UtcNow)); }
        public Task<long> HeadAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<long> FinalizedHeadAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task VerifyChainAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<PolymarketChainLog>> LogsAsync(object filter, CancellationToken ct) => throw new NotSupportedException();
    }
}
