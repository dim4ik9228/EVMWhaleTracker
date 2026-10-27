using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Application.Abstractions;
using Domain.Common;
using Domain.ValueObjects;
using Infrastructure.Blockchain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.Util;

namespace Infrastructure.Ens;

/// <summary>
/// ENS reverse lookup provider backed by Ethereum mainnet RPC eth_call requests.
/// </summary>
public sealed class EthereumEnsReverseLookupProvider(
    HttpClient httpClient,
    IOptions<BlockchainNetworksOptions> blockchainNetworksOptions,
    IOptions<EnsReverseLookupOptions> ensOptions,
    IMemoryCache cache,
    ILogger<EthereumEnsReverseLookupProvider> logger)
    : IEnsReverseLookupProvider
{
    private const string RpcVersion = "2.0";
    private const string EthCallMethod = "eth_call";
    private const string LatestBlock = "latest";
    private const string EnsRegistryAddress = "0x00000000000C2E074eC69A0dFb2997BA6C7d2e1e";
    private const string ResolverSelector = "0178b8bf";
    private const string NameSelector = "691f3431";
    private static readonly JsonSerializerOptions JsonRpcSerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<string?> ResolveNameAsync(Address address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);

        var options = ensOptions.Value;
        if (!options.Enabled)
        {
            return null;
        }

        var cacheKey = BuildCacheKey(address.Value);
        if (cache.TryGetValue<EnsCacheEntry>(cacheKey, out var cached) && cached is not null)
        {
            return cached.Name;
        }

        var rpcUrl = GetEthereumHttpRpcUrl();
        if (string.IsNullOrWhiteSpace(rpcUrl))
        {
            return null;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.Timeout);

        try
        {
            var reverseNode = NameHash($"{address.Value[2..].ToLowerInvariant()}.addr.reverse");
            var resolver = await GetResolverAsync(rpcUrl, reverseNode, timeoutCts.Token);
            if (string.IsNullOrWhiteSpace(resolver))
            {
                cache.Set(cacheKey, EnsCacheEntry.Miss, options.MissCacheDuration);
                return null;
            }

            var name = await GetNameAsync(rpcUrl, resolver, reverseNode, timeoutCts.Token);
            if (!IsValidEnsName(name))
            {
                cache.Set(cacheKey, EnsCacheEntry.Miss, options.MissCacheDuration);
                return null;
            }

            cache.Set(cacheKey, new EnsCacheEntry(name), options.SuccessCacheDuration);
            return name;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            cache.Set(cacheKey, EnsCacheEntry.Failure, options.FailureCacheDuration);
            logger.LogDebug(EthereumEnsReverseLookupProviderDiagnostics.ReverseLookupTimedOut, address.Value);
            return null;
        }
        catch (Exception ex)
        {
            cache.Set(cacheKey, EnsCacheEntry.Failure, options.FailureCacheDuration);
            logger.LogDebug(ex, EthereumEnsReverseLookupProviderDiagnostics.ReverseLookupFailed, address.Value);
            return null;
        }
    }

    private string? GetEthereumHttpRpcUrl()
    {
        var options = blockchainNetworksOptions.Value;
        var ethereum = options.Networks.FirstOrDefault(network => network.Chain == SupportedChain.Ethereum);
        if (ethereum is null)
        {
            return null;
        }

        var rpcApiKey = string.IsNullOrWhiteSpace(ethereum.RpcApiKey)
            ? options.RpcApiKey
            : ethereum.RpcApiKey;

        return BlockchainNetworkOptions.AppendApiKey(ethereum.HttpRpcUrl, rpcApiKey);
    }

    private async Task<string?> GetResolverAsync(string rpcUrl, byte[] node, CancellationToken cancellationToken)
    {
        var data = "0x" + ResolverSelector + Convert.ToHexString(node).ToLowerInvariant();
        var result = await EthCallAsync(rpcUrl, EnsRegistryAddress, data, cancellationToken);
        if (string.IsNullOrWhiteSpace(result) || result.Length < 66)
        {
            return null;
        }

        var addressHex = result[^40..];
        if (addressHex.All(character => character == '0'))
        {
            return null;
        }

        return "0x" + addressHex;
    }

    private async Task<string?> GetNameAsync(
        string rpcUrl,
        string resolverAddress,
        byte[] node,
        CancellationToken cancellationToken)
    {
        var data = "0x" + NameSelector + Convert.ToHexString(node).ToLowerInvariant();
        var result = await EthCallAsync(rpcUrl, resolverAddress, data, cancellationToken);
        return DecodeAbiString(result);
    }

    private async Task<string?> EthCallAsync(
        string rpcUrl,
        string to,
        string data,
        CancellationToken cancellationToken)
    {
        var request = new JsonRpcRequest(
            Jsonrpc: RpcVersion,
            Method: EthCallMethod,
            Params:
            [
                new JsonRpcCall(to, data),
                LatestBlock
            ],
            Id: 1);

        using var response = await httpClient.PostAsJsonAsync(
            rpcUrl,
            request,
            JsonRpcSerializerOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.TryGetProperty("error", out var error))
        {
            logger.LogDebug(EthereumEnsReverseLookupProviderDiagnostics.RpcFailure, error.ToString());
            return null;
        }

        return document.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String
            ? result.GetString()
            : null;
    }

    private static byte[] NameHash(string name)
    {
        var node = new byte[32];
        if (string.IsNullOrWhiteSpace(name))
        {
            return node;
        }

        var labels = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (var index = labels.Length - 1; index >= 0; index--)
        {
            var labelHash = Keccak256(Encoding.UTF8.GetBytes(labels[index]));
            var combined = new byte[64];
            Buffer.BlockCopy(node, 0, combined, 0, 32);
            Buffer.BlockCopy(labelHash, 0, combined, 32, 32);
            node = Keccak256(combined);
        }

        return node;
    }

    private static byte[] Keccak256(byte[] value)
        => Sha3Keccack.Current.CalculateHash(value);

    private static string? DecodeAbiString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hex = value[2..];
        if (hex.Length < 128)
        {
            return null;
        }

        if (!TryParseWordAsInt(hex.AsSpan(64, 64), out var length) || length <= 0 || length > 255)
        {
            return null;
        }

        var dataStart = 128;
        var dataLength = length * 2;
        if (hex.Length < dataStart + dataLength)
        {
            return null;
        }

        var bytes = Convert.FromHexString(hex.Substring(dataStart, dataLength));
        return Encoding.UTF8.GetString(bytes).Trim();
    }

    private static bool TryParseWordAsInt(ReadOnlySpan<char> word, out int value)
    {
        value = 0;
        if (word.Length != 64)
        {
            return false;
        }

        var significant = word[48..];
        return int.TryParse(significant, System.Globalization.NumberStyles.HexNumber, null, out value);
    }

    private static bool IsValidEnsName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name)
            && name.Length <= 255
            && name.Contains('.', StringComparison.Ordinal)
            && !name.Any(char.IsWhiteSpace);
    }

    private sealed record EnsCacheEntry(string? Name)
    {
        public static readonly EnsCacheEntry Miss = new((string?)null);
        public static readonly EnsCacheEntry Failure = new((string?)null);
    }

    private sealed record JsonRpcRequest(
        string Jsonrpc,
        string Method,
        object[] Params,
        int Id);

    private sealed record JsonRpcCall(
        string To,
        string Data);
    private static string BuildCacheKey(string address) => $"ens-reverse:{address}";
}
