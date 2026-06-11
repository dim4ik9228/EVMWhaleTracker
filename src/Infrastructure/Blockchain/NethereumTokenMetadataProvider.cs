using Application.Abstractions;
using Domain.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    private const string Erc20MetadataAbi =
        """
        [
          {"constant":true,"inputs":[],"name":"symbol","outputs":[{"name":"","type":"string"}],"type":"function"},
          {"constant":true,"inputs":[],"name":"name","outputs":[{"name":"","type":"string"}],"type":"function"},
          {"constant":true,"inputs":[],"name":"decimals","outputs":[{"name":"","type":"uint8"}],"type":"function"}
        ]
        """;

    private static readonly Error MetadataUnavailable = Error.Validation(
        "TokenMetadata.Unavailable",
        "Token metadata could not be resolved.");

    private readonly Web3 _web3;
    private readonly IMemoryCache _cache;
    private readonly ILogger<NethereumTokenMetadataProvider> _logger;
    private readonly IDisposable? _ownedTransport;
    private readonly SupportedChain _chain;

    public NethereumTokenMetadataProvider(
        IOptions<BlockchainIndexerOptions> options,
        IMemoryCache cache,
        ILogger<NethereumTokenMetadataProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);

        var settings = options.Value;
        _chain = settings.Chain;
        _cache = cache;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(settings.HttpRpcUrl))
        {
            _web3 = new Web3(settings.HttpRpcUrl);
        }
        else if (!string.IsNullOrWhiteSpace(settings.WebSocketRpcUrl))
        {
            var requestClient = new WebSocketClient(settings.WebSocketRpcUrl);
            _ownedTransport = requestClient;
            _web3 = new Web3(requestClient);
        }
        else
        {
            throw new InvalidOperationException("Either HttpRpcUrl or WebSocketRpcUrl must be configured for token metadata resolution.");
        }
    }


    public async Task<Result<TokenMetadata>> GetAsync(
        SupportedChain chain,
        string tokenContractAddress,
        CancellationToken cancellationToken)
    {
        if (chain != _chain)
        {
            return Result<TokenMetadata>.Failure(MetadataUnavailable);
        }

        if (StaticTokenMetadataCatalog.TryGet(chain, tokenContractAddress, out var staticMetadata))
        {
            return Result<TokenMetadata>.Success(staticMetadata);
        }

        var cacheKey = $"token-metadata:{chain}:{tokenContractAddress.ToLowerInvariant()}";
        if (_cache.TryGetValue<CachedMetadataLookup>(cacheKey, out var cachedLookup) && cachedLookup is not null)
        {
            return cachedLookup.ToResult();
        }

        try
        {
            var contract = _web3.Eth.GetContract(Erc20MetadataAbi, tokenContractAddress);
            var symbolResult = await TryCallStringAsync(contract.GetFunction("symbol"), "TOKEN");
            var nameResult = await TryCallStringAsync(contract.GetFunction("name"), null);
            var decimalsResult = await TryCallByteAsync(contract.GetFunction("decimals"), 18);

            if (symbolResult.UsedFallback && decimalsResult.UsedFallback)
            {
                _logger.LogWarning(
                    "Failed to resolve required token metadata for {TokenContractAddress} on {Chain}; suppressing retries for {FailureCacheDuration}.",
                    tokenContractAddress,
                    chain,
                    FailureCacheDuration);

                _cache.Set(cacheKey, CachedMetadataLookup.Failure(MetadataUnavailable), FailureCacheDuration);
                return Result<TokenMetadata>.Failure(MetadataUnavailable);
            }

            var metadata = new TokenMetadata(
                chain,
                tokenContractAddress,
                string.IsNullOrWhiteSpace(symbolResult.Value) ? "TOKEN" : symbolResult.Value.Trim(),
                string.IsNullOrWhiteSpace(nameResult.Value) ? null : nameResult.Value.Trim(),
                decimalsResult.Value);

            _cache.Set(cacheKey, CachedMetadataLookup.Success(metadata), SuccessCacheDuration);
            return Result<TokenMetadata>.Success(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve token metadata for {TokenContractAddress} on {Chain}.", tokenContractAddress, chain);
            _cache.Set(cacheKey, CachedMetadataLookup.Failure(MetadataUnavailable), FailureCacheDuration);
            return Result<TokenMetadata>.Failure(MetadataUnavailable);
        }
    }

    public void Dispose()
    {
        _ownedTransport?.Dispose();
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
}
