using Application.Abstractions;
using Domain.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Nethereum.JsonRpc.WebSocketClient;
using Nethereum.Web3;

namespace Infrastructure.Blockchain;

/// <summary>
/// Resolves ERC-20 token metadata through on-chain contract calls and caches lookup results.
/// </summary>
public sealed class NethereumTokenMetadataProvider : ITokenMetadataProvider, IDisposable
{
    private static readonly TimeSpan SuccessCacheDuration = TimeSpan.FromHours(12);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(10);

    private const string FallbackSymbol = "TOKEN";

    private const string Erc20MetadataAbi =
        """
        [
          {"constant":true,"inputs":[],"name":"symbol","outputs":[{"name":"","type":"string"}],"type":"function"},
          {"constant":true,"inputs":[],"name":"name","outputs":[{"name":"","type":"string"}],"type":"function"},
          {"constant":true,"inputs":[],"name":"decimals","outputs":[{"name":"","type":"uint8"}],"type":"function"}
        ]
        """;


    private readonly IReadOnlyDictionary<SupportedChain, Web3> _web3ByChain;
    private readonly IMemoryCache _cache;
    private readonly ILogger<NethereumTokenMetadataProvider> _logger;
    private readonly List<IDisposable> _ownedTransports = [];

    public NethereumTokenMetadataProvider(
        IReadOnlyList<BlockchainNetworkOptions> networks,
        IMemoryCache cache,
        ILogger<NethereumTokenMetadataProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(networks);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);

        _cache = cache;
        _logger = logger;

        var web3ByChain = new Dictionary<SupportedChain, Web3>();
        foreach (var network in networks.Where(network => network.Enabled))
        {
            var httpRpcUrl = network.BuildHttpRpcUrl();
            if (!string.IsNullOrWhiteSpace(httpRpcUrl))
            {
                web3ByChain[network.Chain] = new Web3(httpRpcUrl);
                continue;
            }

            var webSocketRpcUrl = network.BuildWebSocketRpcUrl();
            if (!string.IsNullOrWhiteSpace(webSocketRpcUrl))
            {
                var requestClient = new WebSocketClient(webSocketRpcUrl);
                _ownedTransports.Add(requestClient);
                web3ByChain[network.Chain] = new Web3(requestClient);
            }
        }

        if (web3ByChain.Count == 0)
        {
            throw new InvalidOperationException(NethereumTokenMetadataProviderDiagnostics.NetworkEndpointRequired);
        }

        _web3ByChain = web3ByChain;
    }


    public async Task<Result<TokenMetadata>> GetAsync(
        SupportedChain chain,
        string tokenContractAddress,
        CancellationToken cancellationToken)
    {
        if (!_web3ByChain.TryGetValue(chain, out var web3))
        {
            return Result<TokenMetadata>.Failure(TokenMetadataErrors.Unavailable);
        }

        if (StaticTokenMetadataCatalog.TryGet(chain, tokenContractAddress, out var staticMetadata))
        {
            return Result<TokenMetadata>.Success(staticMetadata);
        }

        var cacheKey = BuildCacheKey(chain, tokenContractAddress.ToLowerInvariant());
        if (_cache.TryGetValue<CachedMetadataLookup>(cacheKey, out var cachedLookup) && cachedLookup is not null)
        {
            return cachedLookup.ToResult();
        }

        try
        {
            var contract = web3.Eth.GetContract(Erc20MetadataAbi, tokenContractAddress);
            var symbolResult = await TryCallStringAsync(contract.GetFunction("symbol"), FallbackSymbol);
            var nameResult = await TryCallStringAsync(contract.GetFunction("name"), null);
            var decimalsResult = await TryCallByteAsync(contract.GetFunction("decimals"), 18);

            if (symbolResult.UsedFallback && decimalsResult.UsedFallback)
            {
                _logger.LogWarning(
                    NethereumTokenMetadataProviderDiagnostics.RequiredMetadataUnavailable,
                    tokenContractAddress,
                    chain,
                    FailureCacheDuration);

                _cache.Set(cacheKey, CachedMetadataLookup.Failure(TokenMetadataErrors.Unavailable), FailureCacheDuration);
                return Result<TokenMetadata>.Failure(TokenMetadataErrors.Unavailable);
            }

            var metadata = new TokenMetadata(
                chain,
                tokenContractAddress,
                string.IsNullOrWhiteSpace(symbolResult.Value) ? FallbackSymbol : symbolResult.Value.Trim(),
                string.IsNullOrWhiteSpace(nameResult.Value) ? null : nameResult.Value.Trim(),
                decimalsResult.Value);

            _cache.Set(cacheKey, CachedMetadataLookup.Success(metadata), SuccessCacheDuration);
            return Result<TokenMetadata>.Success(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, NethereumTokenMetadataProviderDiagnostics.MetadataLookupFailed, tokenContractAddress, chain);
            _cache.Set(cacheKey, CachedMetadataLookup.Failure(TokenMetadataErrors.Unavailable), FailureCacheDuration);
            return Result<TokenMetadata>.Failure(TokenMetadataErrors.Unavailable);
        }
    }

    public void Dispose()
    {
        foreach (var ownedTransport in _ownedTransports)
        {
            ownedTransport.Dispose();
        }
    }

    private static async Task<MetadataCallResult<string?>> TryCallStringAsync(Nethereum.Contracts.Function function, string? fallback)
    {
        try
        {
            return MetadataCallResult<string?>.Success(await function.CallAsync<string>());
        }
        catch
        {
            return MetadataCallResult<string?>.Fallback(fallback);
        }
    }

    private static async Task<MetadataCallResult<int>> TryCallByteAsync(Nethereum.Contracts.Function function, int fallback)
    {
        try
        {
            return MetadataCallResult<int>.Success(await function.CallAsync<int>());
        }
        catch
        {
            return MetadataCallResult<int>.Fallback(fallback);
        }
    }

    private sealed record CachedMetadataLookup(TokenMetadata? Metadata, Error Error)
    {
        public static CachedMetadataLookup Success(TokenMetadata metadata) => new(metadata, Error.None);

        public static CachedMetadataLookup Failure(Error error) => new(null, error);

        public Result<TokenMetadata> ToResult()
            => Metadata is null
                ? Result<TokenMetadata>.Failure(Error)
                : Result<TokenMetadata>.Success(Metadata);
    }

    private readonly record struct MetadataCallResult<T>(T Value, bool UsedFallback)
    {
        public static MetadataCallResult<T> Success(T value) => new(value, false);

        public static MetadataCallResult<T> Fallback(T value) => new(value, true);
    }
    private static string BuildCacheKey(SupportedChain chain, string address) => $"token-metadata:{chain}:{address}";
}
