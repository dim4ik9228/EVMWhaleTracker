using System.Text.Json;
using Application.Polymarket;
using Microsoft.Extensions.Options;

namespace Infrastructure.Polymarket;

/// <summary>Coalesced, bounded positive/negative Gamma lookup; arbitrary provider links never leave this adapter.</summary>
public sealed class PolymarketMarketResolver(HttpClient http, IOptions<PolymarketPolygonOptions> options) : IPolymarketMarketResolver
{
    private sealed record Entry(PolymarketMarket? Value, DateTimeOffset Expires);
    private readonly Dictionary<string, Entry> _cache = new();
    private readonly SemaphoreSlim _gate = new(1);
    public async Task<PolymarketMarket?> ResolveAsync(string family, string tokenId, CancellationToken ct)
    {
        if (!options.Value.Enabled) return null;
        var key = family + ":" + tokenId;
        await _gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(key, out var entry) && entry.Expires > DateTimeOffset.UtcNow) return entry.Value;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            PolymarketMarket? value = null;
            try
            {
                var version = family == "position-v2" ? "v2" : "v1";
                using var response = await http.GetAsync(options.Value.MarketsUrl + "?clob_token_ids=" + Uri.EscapeDataString(tokenId) + "&version=" + version,
                    HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var buffer = new MemoryStream(); var bytes = new byte[8192]; int n;
                while ((n = await stream.ReadAsync(bytes, timeout.Token)) != 0)
                { if (buffer.Length + n > options.Value.MaximumResponseBytes) throw new InvalidDataException("Gamma response too large."); buffer.Write(bytes, 0, n); }
                using var document = JsonDocument.Parse(buffer.ToArray());
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var market in document.RootElement.EnumerateArray())
                    {
                        var ids = Array(market, "clobTokenIds"); var outcomes = Array(market, "outcomes");
                        var index = System.Array.IndexOf(ids, tokenId);
                        if (index < 0 || index >= outcomes.Length || !market.TryGetProperty("question", out var question)) continue;
                        // Require exact token membership; named selections are never relabelled YES/NO.
                        value = new(question.GetString() ?? tokenId, outcomes[index], market.TryGetProperty("slug", out var slug) ? slug.GetString() : null, version);
                        break;
                    }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The normalizer has a shorter enrichment deadline than the HTTP adapter.
                // Retain a negative cache entry so an outage does not start a request per fill.
                Cache(key, null);
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or InvalidDataException or OperationCanceledException or InvalidOperationException or ArgumentException) { }
            Cache(key, value);
            return value;
        }
        finally { _gate.Release(); }
    }
    private void Cache(string key, PolymarketMarket? value)
    {
        if (_cache.Count >= options.Value.MetadataCacheCapacity) _cache.Remove(_cache.OrderBy(x => x.Value.Expires).First().Key);
        _cache[key] = new(value, DateTimeOffset.UtcNow.Add(value is null ? TimeSpan.FromSeconds(30) : TimeSpan.FromHours(12)));
    }
    private static string[] Array(JsonElement market, string key)
    {
        if (!market.TryGetProperty(key, out var value)) return [];
        if (value.ValueKind == JsonValueKind.String)
        { using var nested = JsonDocument.Parse(value.GetString()!); return nested.RootElement.EnumerateArray().Select(x => x.GetString()!).ToArray(); }
        return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(x => x.GetString()!).ToArray() : [];
    }
}
