using Application.Polymarket;

namespace Infrastructure.Polymarket;

/// <summary>Finite five-filter recovery/audits; every range requires durable results and an ordered live barrier.</summary>
internal static class PolymarketPolygonCatchup
{
    internal static async Task RunAsync(long from, long target, PolymarketPolygonOptions options,
        Func<long, long, object>[] filters, IPolymarketPolygonRpc rpc,
        Func<IReadOnlyList<PolymarketChainLog>, Task> persist, Func<Task> barrier,
        Func<PolymarketChainBlock, Task<bool>> covered, CancellationToken ct)
    {
        if (target - from > options.RecoveryBlocks) throw new PolymarketPolygonRecoveryBudgetException();
        var span = options.LogRangeBlocks;
        var requests = 0;
        while (from <= target)
        {
            ct.ThrowIfCancellationRequested();
            var to = Math.Min(target, from + span - 1);
            // Capture before querying: a fresh hash only after querying cannot prove the
            // results came from the branch on which coverage will be committed.
            var anchor = await rpc.BlockAsync(to, ct);
            try
            {
                foreach (var filter in filters)
                {
                    if (++requests > options.RecoveryRequests) throw new PolymarketPolygonRecoveryBudgetException();
                    await persist(await rpc.LogsAsync(filter(from, to), ct));
                }
            }
            catch (PolymarketLogRangeException) when (span > 1)
            { span = Math.Max(1, span / 2); continue; }
            await barrier();
            ct.ThrowIfCancellationRequested();
            if (!await covered(anchor))
                throw new InvalidDataException(PolymarketPolygonWorkerDiagnostics.PolygonCanonicalRewindRequiresRecovery);
            from = to + 1;
        }
    }
}

/// <summary>A bounded recovery must reconnect and expose a gap rather than continue unlimited historical work.</summary>
internal sealed class PolymarketPolygonRecoveryBudgetException : Exception;
