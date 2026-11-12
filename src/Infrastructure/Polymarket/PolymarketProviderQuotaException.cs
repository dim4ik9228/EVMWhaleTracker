using System.Text.Json;

namespace Infrastructure.Polymarket;

/// <summary>Safe provider capacity failure shared by HTTP and WebSocket paths.</summary>
public sealed class PolymarketProviderQuotaException(TimeSpan retryAfter) : IOException(PolymarketPolygonRpcDiagnostics.ProviderThrottled)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
    internal static TimeSpan UntilReset(DateTimeOffset now) => now.UtcDateTime.Date.AddDays(1) - now.UtcDateTime;
    internal static void ThrowIfQuota(JsonElement root, int fallbackSeconds)
    {
        if (!root.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object) return;
        var code = error.TryGetProperty("code", out var c) && c.TryGetInt32(out var n) ? n : 0;
        var daily = code is 402 or -33000;
        var throughput = code is 429 or -33200;
        // Infura can put its event code alongside a generic JSON-RPC code.
        if (error.TryGetProperty("event", out var e) && e.TryGetInt32(out var eventCode))
        { daily |= eventCode == -33000; throughput |= eventCode == -33200; }
        if (daily || throughput) throw new PolymarketProviderQuotaException(daily
            ? UntilReset(DateTimeOffset.UtcNow) : TimeSpan.FromSeconds(Math.Min(fallbackSeconds, 60)));
    }
}
