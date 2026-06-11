using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Abstractions;
using Domain.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Pricing;

public sealed class HttpAssetPriceProvider : IAssetPriceProvider
{
    private static readonly Error PriceUnavailable = Error.Validation(
        "AssetPrice.Unavailable",
        "Asset USD price could not be resolved.");

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly AssetPriceOptions _options;
    private readonly ILogger<HttpAssetPriceProvider> _logger;

    public HttpAssetPriceProvider(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<AssetPriceOptions> options,
        ILogger<HttpAssetPriceProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<AssetUsdPrice>> GetUsdPriceAsync(
        SupportedChain chain,
        string? tokenContractAddress,
        string assetSymbol,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Result<AssetUsdPrice>.Failure(PriceUnavailable);
        }

        var cacheKey = $"asset-price:{chain}:{tokenContractAddress?.ToLowerInvariant() ?? assetSymbol.ToUpperInvariant()}";
        if (_cache.TryGetValue<AssetUsdPrice>(cacheKey, out var cachedPrice) && cachedPrice is not null)
        {
            return Result<AssetUsdPrice>.Success(cachedPrice);
        }

        var price = string.IsNullOrWhiteSpace(tokenContractAddress)
            ? await GetNativePriceAsync(chain, cancellationToken)
            : await GetTokenPriceAsync(chain, tokenContractAddress, cancellationToken);

        if (price.IsSuccess)
        {
            _cache.Set(cacheKey, price.Value, _options.CacheDuration);
        }

        return price;
    }

    private async Task<Result<AssetUsdPrice>> GetNativePriceAsync(
        SupportedChain chain,
        CancellationToken cancellationToken)
    {
        if (!_options.NativeCoinGeckoIds.TryGetValue(chain, out var coinId))
        {
            return Result<AssetUsdPrice>.Failure(PriceUnavailable);
        }

        try
        {
            var url = $"https://api.coingecko.com/api/v3/simple/price?ids={Uri.EscapeDataString(coinId)}&vs_currencies=usd";
            using var document = await _httpClient.GetFromJsonAsync<JsonDocument>(url, cancellationToken);
            if (document is null ||
                !document.RootElement.TryGetProperty(coinId, out var coinElement) ||
                !coinElement.TryGetProperty("usd", out var usdElement) ||
                !usdElement.TryGetDecimal(out var priceUsd))
            {
                return Result<AssetUsdPrice>.Failure(PriceUnavailable);
            }

            return Result<AssetUsdPrice>.Success(new AssetUsdPrice(priceUsd, DateTimeOffset.UtcNow, "CoinGecko"));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve native USD price for {Chain}.", chain);
            return Result<AssetUsdPrice>.Failure(PriceUnavailable);
        }
    }

    private async Task<Result<AssetUsdPrice>> GetTokenPriceAsync(
        SupportedChain chain,
        string tokenContractAddress,
        CancellationToken cancellationToken)
    {
        if (!_options.DexScreenerChainIds.TryGetValue(chain, out var dexChainId))
        {
            return Result<AssetUsdPrice>.Failure(PriceUnavailable);
        }

        try
        {
            var url = $"https://api.dexscreener.com/latest/dex/tokens/{Uri.EscapeDataString(tokenContractAddress)}";
            using var document = await _httpClient.GetFromJsonAsync<JsonDocument>(url, cancellationToken);
            if (document is null ||
                !document.RootElement.TryGetProperty("pairs", out var pairsElement) ||
                pairsElement.ValueKind != JsonValueKind.Array)
            {
                return Result<AssetUsdPrice>.Failure(PriceUnavailable);
            }

            foreach (var pair in pairsElement.EnumerateArray())
            {
                if (!pair.TryGetProperty("chainId", out var chainIdElement) ||
                    !string.Equals(chainIdElement.GetString(), dexChainId, StringComparison.OrdinalIgnoreCase) ||
                    !pair.TryGetProperty("priceUsd", out var priceUsdElement))
                {
                    continue;
                }

                var priceText = priceUsdElement.GetString();
                if (decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var priceUsd))
                {
                    return Result<AssetUsdPrice>.Success(new AssetUsdPrice(priceUsd, DateTimeOffset.UtcNow, "DexScreener"));
                }
            }

            return Result<AssetUsdPrice>.Failure(PriceUnavailable);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve token USD price for {TokenContractAddress} on {Chain}.", tokenContractAddress, chain);
            return Result<AssetUsdPrice>.Failure(PriceUnavailable);
        }
    }
}
