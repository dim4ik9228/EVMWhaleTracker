using System.Threading.Channels;
using Application.Polymarket;
using Infrastructure.Polymarket;

namespace UnitTests.Infrastructure.Polymarket;

public sealed class PolymarketPolygonFrameProcessorTests
{
    [Fact]
    public async Task BurstPersistsLogsAndBarriersBeforeOneLatestHeadEvaluation()
    {
        var queue = Queue();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var log = Log();
        foreach (var frame in new PolymarketPolygonFrame[] { new(Head: 100), new(log), new(Barrier: barrier), new(Head: 101), new(log with { Removed = true }), new(Head: 102) })
            Assert.True(queue.Writer.TryWrite(frame));
        queue.Writer.Complete();
        var persisted = new List<PolymarketChainLog>();
        var heads = new List<long>();

        await PolymarketPolygonFrameProcessor.RunAsync(queue.Reader, 16,
            (item, _) => { persisted.Add(item); return Task.CompletedTask; },
            (head, _) =>
            {
                Assert.True(barrier.Task.IsCompletedSuccessfully);
                Assert.Equal(new[] { false, true }, persisted.Select(x => x.Removed));
                heads.Add(head);
                return Task.CompletedTask;
            }, CancellationToken.None);

        Assert.Equal(new long[] { 102 }, heads);
        Assert.Equal(log, persisted[0]);
    }

    [Fact]
    public async Task BarrierDoesNotCompleteBeforePrecedingPersistenceCommits()
    {
        var queue = Queue();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Writer.TryWrite(new(Log())); queue.Writer.TryWrite(new(Barrier: barrier)); queue.Writer.Complete();
        var processing = PolymarketPolygonFrameProcessor.RunAsync(queue.Reader, 16,
            async (_, token) => { entered.SetResult(); await commit.Task.WaitAsync(token); },
            (_, _) => Task.CompletedTask, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(barrier.Task.IsCompleted);
        commit.SetResult();
        await processing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(barrier.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ContinuousArrivalsCannotStarveHeadEvaluation()
    {
        var queue = Queue();
        queue.Writer.TryWrite(new(Head: 100)); queue.Writer.TryWrite(new(Log()));
        var events = new List<string>();
        await PolymarketPolygonFrameProcessor.RunAsync(queue.Reader, 16,
            (_, _) =>
            {
                events.Add("persist");
                if (events.Count == 1) { queue.Writer.TryWrite(new(Log())); queue.Writer.Complete(); }
                return Task.CompletedTask;
            },
            (_, _) => { events.Add("head"); return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(new[] { "persist", "head", "persist" }, events);
    }

    [Fact]
    public async Task ReorgUsesLastArrivingHeadEvenWhenLower()
    {
        var queue = Queue();
        queue.Writer.TryWrite(new(Head: 100)); queue.Writer.TryWrite(new(Head: 98)); queue.Writer.Complete();
        var heads = new List<long>();
        await PolymarketPolygonFrameProcessor.RunAsync(queue.Reader, 16, (_, _) => Task.CompletedTask,
            (head, _) => { heads.Add(head); return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(new long[] { 98 }, heads);
    }

    [Fact]
    public async Task FailedPersistenceCannotAcknowledgeBarrierOrAdvanceHead()
    {
        var queue = Queue();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Writer.TryWrite(new(Head: 100)); queue.Writer.TryWrite(new(Log())); queue.Writer.TryWrite(new(Barrier: barrier));
        var evaluated = false;
        await Assert.ThrowsAsync<IOException>(() => PolymarketPolygonFrameProcessor.RunAsync(queue.Reader, 16,
            (_, _) => throw new IOException(), (_, _) => { evaluated = true; return Task.CompletedTask; }, CancellationToken.None));
        Assert.False(barrier.Task.IsCompleted);
        Assert.False(evaluated);
    }

    [Fact]
    public async Task SnapshotLimitRetainsLaterLogsHeadsAndRecoveryBarrier()
    {
        var queue = Queue();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var log = Log();
        foreach (var frame in new PolymarketPolygonFrame[]
            { new(Head: 100), new(log), new(Head: 101), new(log with { Removed = true }), new(Barrier: barrier) })
            Assert.True(queue.Writer.TryWrite(frame));
        queue.Writer.Complete();
        var persisted = new List<bool>();
        var heads = new List<long>();

        await PolymarketPolygonFrameProcessor.RunAsync(queue.Reader, 2,
            (item, _) => { persisted.Add(item.Removed); return Task.CompletedTask; },
            (head, _) =>
            {
                Assert.False(barrier.Task.IsCompleted);
                Assert.Equal(head == 100 ? new[] { false } : new[] { false, true }, persisted);
                heads.Add(head);
                return Task.CompletedTask;
            }, CancellationToken.None);

        Assert.Equal(new long[] { 100, 101 }, heads);
        Assert.True(barrier.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task HeadFailureLeavesLaterRecoveryBarrierUnacknowledged()
    {
        var queue = Queue();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Writer.TryWrite(new(Head: 100)); queue.Writer.TryWrite(new(Barrier: barrier));

        await Assert.ThrowsAsync<IOException>(() => PolymarketPolygonFrameProcessor.RunAsync(queue.Reader, 1,
            (_, _) => Task.CompletedTask, (_, _) => throw new IOException(), CancellationToken.None));

        Assert.False(barrier.Task.IsCompleted);
        Assert.Equal(1, queue.Reader.Count);
    }

    [Fact]
    public async Task CancellationInterruptsPersistenceAndLeavesBarrierUnacknowledged()
    {
        var queue = Queue();
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        queue.Writer.TryWrite(new(Log())); queue.Writer.TryWrite(new(Barrier: barrier));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PolymarketPolygonFrameProcessor.RunAsync(queue.Reader, 16,
            async (_, token) => { cancellation.Cancel(); await Task.Delay(Timeout.Infinite, token); },
            (_, _) => throw new InvalidOperationException(), cancellation.Token));
        Assert.False(barrier.Task.IsCompleted);
    }

    private static Channel<PolymarketPolygonFrame> Queue() => Channel.CreateBounded<PolymarketPolygonFrame>(
        new BoundedChannelOptions(16) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    private static PolymarketChainLog Log() => new("contract", 99, "hash", "transaction", 0, 0, [], "0x", false);
}
