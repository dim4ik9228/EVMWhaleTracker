using System.Net;
using System.Text.Json;
using Application.Polymarket;
using Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace Infrastructure.Polymarket;

/// <summary>Size-bounded deduplicated public lookup with positive/negative TTL and stale-on-error.</summary>
public sealed class PolymarketProfileResolver(HttpClient client, IOptions<PolymarketOptions> options) : IPolymarketProfileResolver
{
    private sealed record Entry(PolymarketProfileLookup Value, DateTimeOffset Expires);
    private readonly Dictionary<string, Entry> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1);
    private DateTimeOffset _budgetStarted = DateTimeOffset.UtcNow;
    private int _requests;
    public async Task<PolymarketProfileLookup> ResolveAsync(string address, CancellationToken ct)
    {
        if (!options.Value.Enabled) return new("Off", null);
        var normalized = Address.Normalize(address);
        if (normalized.IsFailure) return new("Unresolved", null);
        address = normalized.Value.Value.ToLowerInvariant();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        try { await _gate.WaitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new("Unavailable", null); }
        try
        {
            _cache.TryGetValue(address, out var cached);
            if (cached?.Expires > DateTimeOffset.UtcNow) return cached.Value;
            try
            {
                if (DateTimeOffset.UtcNow - _budgetStarted >= TimeSpan.FromMinutes(1))
                {
                    _budgetStarted = DateTimeOffset.UtcNow;
                    _requests = 0;
                }
                if (_requests >= options.Value.RequestsPerMinute)
                    throw new HttpRequestException("Public profile request budget exhausted.");
                _requests++;
                using var response = await client.GetAsync(options.Value.ProfileUrl + "?address=" + Uri.EscapeDataString(address),
                    HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                PolymarketProfileLookup result;
                if (response.StatusCode == HttpStatusCode.NotFound) result = new("Missing", null);
                else
                {
                    response.EnsureSuccessStatusCode();
                    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                    using var buffer = new MemoryStream();
                    var bytes = new byte[8192]; int count;
                    while ((count = await stream.ReadAsync(bytes, timeout.Token)) > 0)
                    {
                        if (buffer.Length + count > options.Value.MaximumResponseBytes) throw new InvalidDataException("Profile response exceeds limit.");
                        buffer.Write(bytes, 0, count);
                    }
                    using var json = JsonDocument.Parse(buffer.ToArray());
                    var root = json.RootElement;
                    if (root.ValueKind == JsonValueKind.Null) result = new("Missing", null);
                    else if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected public profile object.");
                    else
                    {
                        if (!root.TryGetProperty("proxyWallet", out _)) throw new InvalidDataException("Missing documented profile identity field.");
                        var proxyValue = root.GetProperty("proxyWallet");
                        if (proxyValue.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                            throw new InvalidDataException("Invalid public profile trading-wallet type.");
                        var proxy = proxyValue.ValueKind == JsonValueKind.String ? proxyValue.GetString() : null;
                        var valid = proxy is null ? null : Address.Normalize(proxy);
                        if (valid?.IsFailure == true)
                            throw new InvalidDataException("Invalid public profile trading-wallet address.");
                        var trading = valid?.IsSuccess == true ? valid.Value.Value.ToLowerInvariant() : null;
                        var profile = new PolymarketProfile(address, trading, Text(root, "name", 100), Text(root, "pseudonym", 100),
                            root.TryGetProperty("displayUsernamePublic", out var visible) && visible.ValueKind == JsonValueKind.True,
                            DateTimeOffset.UtcNow, "Gamma public-profile");
                        result = new("Found", profile);
                    }
                }
                if (_cache.Count >= options.Value.ProfileCacheCapacity && !_cache.ContainsKey(address))
                    _cache.Remove(_cache.OrderBy(x => x.Value.Expires).First().Key);
                _cache[address] = new(result, DateTimeOffset.UtcNow.Add(result.State == "Found"
                    ? TimeSpan.FromHours(options.Value.ProfileTtlHours) : TimeSpan.FromMinutes(options.Value.MissingProfileTtlMinutes)));
                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or IOException or OperationCanceledException)
            {
                // Briefly cache transport failures without confusing them with confirmed absence.
                var result = cached?.Value.Profile is not null ? cached.Value with { State = "Unavailable", Stale = true } : new PolymarketProfileLookup("Unavailable", null);
                if (_cache.Count >= options.Value.ProfileCacheCapacity && !_cache.ContainsKey(address))
                    _cache.Remove(_cache.OrderBy(x => x.Value.Expires).First().Key);
                _cache[address] = new(result, DateTimeOffset.UtcNow.AddSeconds(30));
                return result;
            }
        }
        finally { _gate.Release(); }
    }
    private static string? Text(JsonElement root, string key, int maximum)
    {
        if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var safe = new string((value.GetString() ?? "").Where(c => !char.IsControl(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).Take(maximum).ToArray()).Trim();
        return safe.Length == 0 ? null : safe;
    }
}
