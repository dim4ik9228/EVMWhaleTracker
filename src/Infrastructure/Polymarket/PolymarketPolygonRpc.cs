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
    private long _retryAfter;
    public async Task VerifyChainAsync(CancellationToken ct)
    { if (Hex((await CallAsync("eth_chainId", [], ct)).GetString()!) != 137) throw new InvalidDataException("Polymarket endpoint is not Polygon 137."); }
    public async Task<long> HeadAsync(CancellationToken ct) => Hex((await CallAsync("eth_blockNumber", [], ct)).GetString()!);
    public async Task<long> FinalizedHeadAsync(CancellationToken ct)
    {
        var block = await CallAsync("eth_getBlockByNumber", ["finalized", false], ct);
        if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("number", out var number))
            throw new InvalidDataException("Polygon finalized boundary unavailable; finalization remains paused.");
        return Hex(number.GetString()!);
    }
    public async Task<PolymarketChainBlock> BlockAsync(long number, CancellationToken ct)
    {
        var b = await CallAsync("eth_getBlockByNumber", [Quantity(number), false], ct);
        return new(Hex(b.GetProperty("number").GetString()!), b.GetProperty("hash").GetString()!.ToLowerInvariant(),
            b.GetProperty("parentHash").GetString()!.ToLowerInvariant(), DateTimeOffset.FromUnixTimeSeconds(Hex(b.GetProperty("timestamp").GetString()!)));
    }
    public async Task<IReadOnlyList<PolymarketChainLog>> LogsAsync(object filter, CancellationToken ct)
    {
        var result = await CallAsync("eth_getLogs", [filter], ct);
        if (result.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid Polygon logs response.");
        return result.EnumerateArray().Select(ParseLog).ToArray();
    }
    private async Task<JsonElement> CallAsync(string method, object[] parameters, CancellationToken ct)
    {
        if (!options.Value.Enabled) throw new InvalidOperationException("Polygon acquisition is off.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        await _requestGate.WaitAsync(timeout.Token);
        try
        {
            var delay = Math.Max(_retryAfter, _lastRequest + _lastInterval) - Environment.TickCount64;
            if (delay > 0) await Task.Delay(TimeSpan.FromMilliseconds(delay), timeout.Token);
            _lastRequest = Environment.TickCount64;
            // Log queries consume substantially more provider credits than block/header reads.
            _lastInterval = method == "eth_getLogs" ? Math.Max(1000, options.Value.RpcMinimumIntervalMilliseconds)
                : options.Value.RpcMinimumIntervalMilliseconds;
            return await SendCoreAsync(method, parameters, timeout.Token);
        }
        finally { _requestGate.Release(); }
    }
    private async Task<JsonElement> SendCoreAsync(string method, object[] parameters, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.HttpEndpoint)
        { Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = parameters }) };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if ((int)response.StatusCode == 429)
        {
            var wait = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(10));
            _retryAfter = Environment.TickCount64 + (long)Math.Clamp(wait.TotalMilliseconds, 1000, 60000);
            throw new HttpRequestException("Polygon provider throttled; shared RPC cooldown is active.", null, response.StatusCode);
        }
        if ((int)response.StatusCode == 413 && method == "eth_getLogs") throw new PolymarketLogRangeException();
        if (!response.IsSuccessStatusCode) throw new InvalidDataException($"Polygon provider HTTP {(int)response.StatusCode}; check network access and credentials.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(bytes, ct)) != 0)
        { if (buffer.Length + count > options.Value.MaximumResponseBytes) { if (method == "eth_getLogs") throw new PolymarketLogRangeException(); throw new InvalidDataException("Polygon response exceeds configured limit."); } buffer.Write(bytes, 0, count); }
        using var json = JsonDocument.Parse(buffer.ToArray());
        if (json.RootElement.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
        {
            var code = error.TryGetProperty("code", out var c) && c.TryGetInt32(out var value) ? value : 0;
            var text = error.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "";
            if (method == "eth_getLogs" && (text.Contains("more than", StringComparison.OrdinalIgnoreCase)
                || text.Contains("block range", StringComparison.OrdinalIgnoreCase) || text.Contains("response size", StringComparison.OrdinalIgnoreCase)))
                throw new PolymarketLogRangeException();
            throw new InvalidDataException($"Polygon {method} rejected (RPC code {code}); recovery remains pending.");
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
