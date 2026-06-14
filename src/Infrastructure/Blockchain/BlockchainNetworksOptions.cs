using Domain.Common;

namespace Infrastructure.Blockchain;

/// <summary>
/// Runtime configuration for all EVM networks that can publish token-transfer events.
/// </summary>
public sealed class BlockchainNetworksOptions
{
    public bool EnableRpcDiagnostics { get; init; }

    public string? RpcApiKey { get; init; }

    public bool PreferWebSocket { get; init; } = true;

    public ulong? StartBlockNumber { get; init; }

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan WebSocketReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan SubscriptionRefreshInterval { get; init; } = TimeSpan.FromMinutes(1);

    public List<BlockchainNetworkOptions> Networks { get; init; } = [];
}

/// <summary>
/// RPC endpoints and listener behavior for one supported EVM network.
/// </summary>
public sealed class BlockchainNetworkOptions
{
    public SupportedChain Chain { get; init; } = SupportedChain.Ethereum;

    public bool Enabled { get; init; } = true;

    public bool EnableRpcDiagnostics { get; init; }

    public string? HttpRpcUrl { get; init; }

    public string? WebSocketRpcUrl { get; init; }

    public string? RpcApiKey { get; init; }

    public bool PreferWebSocket { get; init; } = true;

    public ulong? StartBlockNumber { get; init; }

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan WebSocketReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan SubscriptionRefreshInterval { get; init; } = TimeSpan.FromMinutes(1);

    public string? BuildHttpRpcUrl() => AppendApiKey(HttpRpcUrl, RpcApiKey);

    public string? BuildWebSocketRpcUrl() => AppendApiKey(WebSocketRpcUrl, RpcApiKey);

    internal static string? AppendApiKey(string? rpcUrl, string? rpcApiKey)
    {
        if (string.IsNullOrWhiteSpace(rpcUrl))
        {
            return null;
        }

        var trimmedUrl = rpcUrl.Trim();
        if (string.IsNullOrWhiteSpace(rpcApiKey))
        {
            return trimmedUrl;
        }

        var escapedApiKey = Uri.EscapeDataString(rpcApiKey.Trim());
        return trimmedUrl
            .Replace("{ApiKey}", escapedApiKey, StringComparison.OrdinalIgnoreCase)
            .Replace("{RpcApiKey}", escapedApiKey, StringComparison.OrdinalIgnoreCase) is { } replaced
            && !string.Equals(replaced, trimmedUrl, StringComparison.Ordinal)
                ? replaced
                : trimmedUrl + escapedApiKey;
    }
}
