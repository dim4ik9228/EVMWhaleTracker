using Application.Polymarket;
using Infrastructure.Polymarket;

namespace UnitTests.Infrastructure.Polymarket;

public sealed class PolymarketPolygonCatchupTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task AnyFailedFilterPreventsBarrierAndCoverage(int failedFilter)
    {
        var rpc = new Rpc { FailAt = failedFilter };
        var barriers = 0; var commits = 0; var persisted = 0;
        await Assert.ThrowsAsync<IOException>(() => Run(rpc,
            _ => { persisted++; return Task.CompletedTask; },
            () => { barriers++; return Task.CompletedTask; },
            _ => { commits++; return Task.FromResult(true); }));
        Assert.Equal(failedFilter, rpc.LogCalls);
        Assert.Equal(failedFilter - 1, persisted);
        Assert.Equal(0, barriers); Assert.Equal(0, commits);
    }

    [Fact]
    public async Task FiveDurableFiltersAndLiveBarrierPrecedeCoverage()
    {
        var rpc = new Rpc(); var persisted = 0; var covered = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = Run(rpc, _ => { persisted++; return Task.CompletedTask; },
            async () => { entered.SetResult(); await release.Task; },
            _ => { covered = true; return Task.FromResult(true); });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(5, rpc.LogCalls); Assert.Equal(5, persisted); Assert.False(covered);
        release.SetResult(); await run;
        Assert.True(covered); Assert.Equal(new long[] { 20 }, rpc.BlockReads);
    }

    [Fact]
    public async Task CancellationAtBarrierPreventsCoverage()
    {
        using var cancellation = new CancellationTokenSource();
        var covered = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(new Rpc(), _ => Task.CompletedTask,
            () => { cancellation.Cancel(); return Task.CompletedTask; },
            _ => { covered = true; return Task.FromResult(true); }, cancellation.Token));
        Assert.False(covered);
    }

    [Fact]
    public async Task RejectedCanonicalCommitRequiresRecovery()
    {
        var rpc = new Rpc();
        await Assert.ThrowsAsync<InvalidDataException>(() => Run(rpc, _ => Task.CompletedTask,
            () => Task.CompletedTask, _ => Task.FromResult(false)));
        Assert.Equal(5, rpc.LogCalls);
    }

    private static Task Run(Rpc rpc, Func<IReadOnlyList<PolymarketChainLog>, Task> persist,
        Func<Task> barrier, Func<PolymarketChainBlock, Task<bool>> covered, CancellationToken ct = default)
        => PolymarketPolygonCatchup.RunAsync(11, 20, new(),
            Enumerable.Range(1, 5).Select(i => new Func<long, long, object>((_, _) => i)).ToArray(),
            rpc, persist, barrier, covered, ct);

    private sealed class Rpc : IPolymarketPolygonRpc
    {
        public int FailAt; public int LogCalls; public List<long> BlockReads = [];
        public Task<PolymarketChainBlock> BlockAsync(long number, CancellationToken ct)
        { BlockReads.Add(number); return Task.FromResult(new PolymarketChainBlock(number, "anchor", "parent", DateTimeOffset.UtcNow)); }
        public Task<IReadOnlyList<PolymarketChainLog>> LogsAsync(object filter, CancellationToken ct)
        { if (++LogCalls == FailAt) throw new IOException(); return Task.FromResult<IReadOnlyList<PolymarketChainLog>>([]); }
        public Task<long> HeadAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<long> FinalizedHeadAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task VerifyChainAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
