using System.Threading.Channels;
using Application.Polymarket;

namespace Infrastructure.Polymarket;

/// <summary>Ordered source work; barriers acknowledge durability of all preceding log frames.</summary>
internal sealed record PolymarketPolygonFrame(PolymarketChainLog? Log = null, long? Head = null, TaskCompletionSource? Barrier = null);

/// <summary>Persists a bounded snapshot of queued frames before evaluating its latest head.</summary>
internal static class PolymarketPolygonFrameProcessor
{
    internal static async Task RunAsync(ChannelReader<PolymarketPolygonFrame> reader, int capacity,
        Func<PolymarketChainLog, CancellationToken, Task> persist,
        Func<long, CancellationToken, Task> evaluateHead, CancellationToken ct)
    {
        while (await reader.WaitToReadAsync(ct))
        {
            // Snapshot the available work so continuous ingress cannot starve finality processing.
            var remaining = Math.Min(capacity, reader.Count);
            long? head = null;
            while (remaining-- > 0 && reader.TryRead(out var frame))
            {
                ct.ThrowIfCancellationRequested();
                if (frame.Log is { } log) await persist(log, ct);
                // Preserve arrival order, including lower heads after a reorg.
                if (frame.Head is { } next) head = next;
                frame.Barrier?.TrySetResult();
            }
            if (head is { } latest) await evaluateHead(latest, ct);
        }
    }
}
