using Application.Abstractions;
using Domain.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.JsonRpc.WebSocketClient;
using Nethereum.Web3;

namespace Infrastructure.Blockchain;

/// <summary>
/// Resolves ERC-20 token metadata through on-chain contract calls and caches successful lookups.
/// </summary>
public sealed class NethereumTokenMetadataProvider : ITokenMetadataProvider, IDisposable
{
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

    public NethereumTokenMetadataProvider(
        IOptions<BlockchainIndexerOptions> options,
        IMemoryCache cache,
        ILogger<NethereumTokenMetadataProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);

        var settings = options.Value;
        Chain = settings.Chain;
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

    private SupportedChain Chain { get; }

    public async Task<Result<TokenMetadata>> GetAsync(
        SupportedChain chain,
        string tokenContractAddress,
        CancellationToken cancellationToken)
    {
        if (chain != Chain)
        {
            return Result<TokenMetadata>.Failure(MetadataUnavailable);
        }

        var cacheKey = $"token-metadata:{chain}:{tokenContractAddress.ToLowerInvariant()}";
        if (_cache.TryGetValue<TokenMetadata>(cacheKey, out var cachedMetadata) && cachedMetadata is not null)
        {
            return Result<TokenMetadata>.Success(cachedMetadata);
        }

        try
        {
            var contract = _web3.Eth.GetContract(Erc20MetadataAbi, tokenContractAddress);
            var symbol = await TryCallStringAsync(contract.GetFunction("symbol"), "TOKEN");
            var name = await TryCallStringAsync(contract.GetFunction("name"), null);
            var decimals = await TryCallByteAsync(contract.GetFunction("decimals"), 18);

            var metadata = new TokenMetadata(
                chain,
                tokenContractAddress,
                string.IsNullOrWhiteSpace(symbol) ? "TOKEN" : symbol.Trim(),
                string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                decimals);

            _cache.Set(cacheKey, metadata, TimeSpan.FromHours(12));
            return Result<TokenMetadata>.Success(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve token metadata for {TokenContractAddress} on {Chain}.", tokenContractAddress, chain);
            return Result<TokenMetadata>.Failure(MetadataUnavailable);
        }
    }

    public void Dispose()
    {
        _ownedTransport?.Dispose();
    }

    private static async Task<string?> TryCallStringAsync(Nethereum.Contracts.Function function, string? fallback)
    {
        try
        {
            return await function.CallAsync<string>();
        }
        catch
        {
            return fallback;
        }
    }

    private static async Task<int> TryCallByteAsync(Nethereum.Contracts.Function function, int fallback)
    {
        try
        {
            return await function.CallAsync<int>();
        }
        catch
        {
            return fallback;
        }
    }
}
