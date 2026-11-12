namespace Infrastructure.Polymarket;

/// <summary>Single-flight RPC scheduling; durable pending work is fast, quiet maintenance and coverage audits are sparse.</summary>
internal sealed class PolymarketPolygonWorkScheduler(PolymarketPolygonOptions options, TimeProvider clock)
{
    private DateTimeOffset _pending = DateTimeOffset.MinValue;
    private DateTimeOffset _maintenance = clock.GetUtcNow().AddSeconds(options.MaintenanceSeconds);
    private DateTimeOffset _audit = clock.GetUtcNow().AddSeconds(options.CoverageAuditSeconds);

    internal async Task TickAsync(Func<Task<bool>> hasPending, Func<Task> finalize,
        Func<Task> maintain, Func<Task> audit)
    {
        var now = clock.GetUtcNow();
        var maintenance = now >= _maintenance;
        if (maintenance || now >= _pending && await hasPending())
        {
            await finalize();
            _pending = clock.GetUtcNow().AddSeconds(options.PendingRetrySeconds);
            if (maintenance)
            {
                await maintain();
                _maintenance = clock.GetUtcNow().AddSeconds(options.MaintenanceSeconds);
            }
        }
        if (clock.GetUtcNow() >= _audit)
        {
            await audit();
            _audit = clock.GetUtcNow().AddSeconds(options.CoverageAuditSeconds);
        }
    }
}
