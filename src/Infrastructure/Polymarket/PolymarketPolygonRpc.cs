using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Polymarket;
using Microsoft.Extensions.Options;

namespace Infrastructure.Polymarket;

/// <summary>Size/time-limited JSON-RPC adapter. Error messages deliberately omit secret-bearing endpoints.</summary>
public sealed class PolymarketPolygonRpc(HttpClient http, IOptions<PolymarketPolygonOptions> options) : IPolymarketPolygonRpc
{
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private long _lastRequest;
    private int _lastInterval;
    private DateOnly _requestDay;
    private int _dailyRequests;
    private int _consecutiveHttpThrottles;
    private long _requestCount;
    private long _logRequestCount;
    /// <summary>Attempted HTTP requests in this process, excluding calls refused by the local budget.</summary>
    public long RequestCount => Interlocked.Read(ref _requestCount);
    public long LogRequestCount => Interlocked.Read(ref _logRequestCount);
    public async Task VerifyChainAsync(CancellationToken ct)
    { if (Hex((await CallAsync(PolymarketWireTokens.ChainId, [], ct)).GetString()!) != 137) throw new InvalidDataException(PolymarketPolygonRpcDiagnostics.PolymarketEndpointIsNotPolygon); }
    public async Task<long> HeadAsync(CancellationToken ct) => Hex((await CallAsync(PolymarketWireTokens.BlockNumber, [], ct)).GetString()!);
    public async Task<long> FinalizedHeadAsync(CancellationToken ct)
    {
        var block = await CallAsync(PolymarketWireTokens.BlockByNumber, [PolymarketWireTokens.FinalizedBlock, false], ct);
        if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("number", out var number))
            throw new InvalidDataException(PolymarketPolygonRpcDiagnostics.FinalizedBoundaryUnavailable);
        return Hex(number.GetString()!);
    }
    public async Task<PolymarketChainBlock> BlockAsync(long number, CancellationToken ct)
    {
        var b = await CallAsync(PolymarketWireTokens.BlockByNumber, [Quantity(number), false], ct);
        return new(Hex(b.GetProperty("number").GetString()!), b.GetProperty("hash").GetString()!.ToLowerInvariant(),
            b.GetProperty("parentHash").GetString()!.ToLowerInvariant(), DateTimeOffset.FromUnixTimeSeconds(Hex(b.GetProperty("timestamp").GetString()!)));
    }
    public async Task<IReadOnlyList<PolymarketChainLog>> LogsAsync(object filter, CancellationToken ct)
    {
        var result = await CallAsync(PolymarketWireTokens.Logs, [filter], ct);
        if (result.ValueKind != JsonValueKind.Array) throw new InvalidDataException(PolymarketPolygonRpcDiagnostics.InvalidPolygonLogsResponse);
        return result.EnumerateArray().Select(ParseLog).ToArray();
    }
    private async Task<JsonElement> CallAsync(string method, object[] parameters, CancellationToken ct)
    {
        if (!options.Value.Enabled) throw new InvalidOperationException(PolymarketPolygonRpcDiagnostics.PolygonAcquisitionIsOff);
        await _requestGate.WaitAsync(ct);
        try
        {
            var delay = options.Value.CooldownRemaining.TotalMilliseconds;
            if (delay > 0) throw new PolymarketProviderQuotaException(TimeSpan.FromMilliseconds(delay));
            var now = DateTimeOffset.UtcNow;
            var day = DateOnly.FromDateTime(now.UtcDateTime);
            if (_requestDay != day) { _requestDay = day; _dailyRequests = 0; }
            if (_dailyRequests >= options.Value.DailyHttpRequestLimit)
                throw new PolymarketProviderQuotaException(options.Value.ExtendCooldown(PolymarketProviderQuotaException.UntilReset(now)));
            delay = _lastRequest + _lastInterval - Environment.TickCount64;
            if (delay > 0) await Task.Delay(TimeSpan.FromMilliseconds(delay), ct);
            // A socket quota can arrive while this HTTP caller is waiting for pacing.
            delay = Interlocked.Read(ref options.Value.CooldownUntilTicks) - Environment.TickCount64;
            if (delay > 0) throw new PolymarketProviderQuotaException(TimeSpan.FromMilliseconds(delay));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            _dailyRequests++;
            Interlocked.Increment(ref _requestCount);
            if (method == PolymarketWireTokens.Logs) Interlocked.Increment(ref _logRequestCount);
            _lastRequest = Environment.TickCount64;
            // Log queries consume substantially more provider credits than block/header reads.
            _lastInterval = method == PolymarketWireTokens.Logs ? Math.Max(1000, options.Value.RpcMinimumIntervalMilliseconds)
                : options.Value.RpcMinimumIntervalMilliseconds;
            try
            {
                var result = await SendCoreAsync(method, parameters, timeout.Token);
                _consecutiveHttpThrottles = 0;
                return result;
            }
            catch (PolymarketProviderQuotaException quota)
            {
                throw new PolymarketProviderQuotaException(options.Value.ExtendCooldown(quota.RetryAfter));
            }
        }
        finally { _requestGate.Release(); }
    }
    private async Task<JsonElement> SendCoreAsync(string method, object[] parameters, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.HttpEndpoint)
        { Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = parameters }) };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if ((int)response.StatusCode == 402)
            throw new PolymarketProviderQuotaException(PolymarketProviderQuotaException.UntilReset(DateTimeOffset.UtcNow));
        if ((int)response.StatusCode == 429)
        {
            _consecutiveHttpThrottles = Math.Min(6, _consecutiveHttpThrottles + 1);
            var wait = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
            // Explicit provider guidance retains its bounded range. With no guidance,
            // repeated throttles become progressively sparse until a healthy RPC response.
            throw new PolymarketProviderQuotaException(wait is { } specified
                ? TimeSpan.FromMilliseconds(Math.Clamp(specified.TotalMilliseconds, 1000, 60000))
                : TimeSpan.FromSeconds(Math.Min(300, 10 * Math.Pow(2, _consecutiveHttpThrottles - 1))));
        }
        if ((int)response.StatusCode == 413 && method == PolymarketWireTokens.Logs) throw new PolymarketLogRangeException();
        if (!response.IsSuccessStatusCode) throw new InvalidDataException(PolymarketPolygonRpcDiagnostics.ProviderHttpFailure((int)response.StatusCode));
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(bytes, ct)) != 0)
        { if (buffer.Length + count > options.Value.MaximumResponseBytes) { if (method == PolymarketWireTokens.Logs) throw new PolymarketLogRangeException(); throw new InvalidDataException(PolymarketPolygonRpcDiagnostics.PolygonResponseExceedsConfiguredLimit); } buffer.Write(bytes, 0, count); }
        using var json = JsonDocument.Parse(buffer.ToArray());
        PolymarketProviderQuotaException.ThrowIfQuota(json.RootElement, options.Value.QuotaBackoffSeconds);
        if (json.RootElement.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
        {
            var code = error.TryGetProperty("code", out var c) && c.TryGetInt32(out var value) ? value : 0;
            var text = error.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "";
            if (method == PolymarketWireTokens.Logs && (text.Contains(PolymarketPolygonRpcDiagnostics.MoreThan, StringComparison.OrdinalIgnoreCase)
                || text.Contains(PolymarketPolygonRpcDiagnostics.BlockRange, StringComparison.OrdinalIgnoreCase) || text.Contains(PolymarketPolygonRpcDiagnostics.ResponseSize, StringComparison.OrdinalIgnoreCase)))
                throw new PolymarketLogRangeException();
            // Only include a known tag or parsed numeric height, never arbitrary provider/request text.
            if (method == PolymarketWireTokens.BlockByNumber && parameters.FirstOrDefault() is string target)
            {
                if (target == PolymarketWireTokens.FinalizedBlock)
                    throw new InvalidDataException(PolymarketPolygonRpcDiagnostics.ProviderBlockRpcFailure(PolymarketWireTokens.FinalizedBlock, code));
                if (target.StartsWith("0x", StringComparison.Ordinal) && long.TryParse(target.AsSpan(2), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out var height) && height >= 0)
                    throw new InvalidDataException(PolymarketPolygonRpcDiagnostics.ProviderBlockRpcFailure(height.ToString(CultureInfo.InvariantCulture), code));
            }
            throw new InvalidDataException(PolymarketPolygonRpcDiagnostics.ProviderRpcFailure(method, code));
        }
        return json.RootElement.GetProperty("result").Clone();
    }
    public static string Quantity(long value) => "0x" + value.ToString("x", CultureInfo.InvariantCulture);
    public static long Hex(string value) => long.Parse(value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    public static PolymarketChainLog ParseLog(JsonElement l) => new(l.GetProperty("address").GetString()!.ToLowerInvariant(),
        Hex(l.GetProperty("blockNumber").GetString()!), l.GetProperty("blockHash").GetString()!.ToLowerInvariant(),
        l.GetProperty("transactionHash").GetString()!.ToLowerInvariant(), checked((int)Hex(l.GetProperty("transactionIndex").GetString()!)),
        checked((int)Hex(l.GetProperty("logIndex").GetString()!)), l.GetProperty("topics").EnumerateArray().Select(t => t.GetString()!.ToLowerInvariant()).ToArray(),
        l.GetProperty("data").GetString()!.ToLowerInvariant(), l.TryGetProperty("removed", out var removed) && removed.GetBoolean());
}

/// <summary>Only range/response-size limits justify adaptive log-range splitting; throttling does not.</summary>
public sealed class PolymarketLogRangeException : Exception;
