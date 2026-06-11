using Domain.Common;

namespace Infrastructure.Blockchain;

/// <summary>
/// Runtime configuration for blockchain RPC connections and optional native block scanning.
/// </summary>
public sealed class BlockchainIndexerOptions
{
    public SupportedChain Chain { get; init; } = SupportedChain.Ethereum;

    public bool EnableRpcDiagnostics { get; init; }

    public string? HttpRpcUrl { get; init; }

    public string? WebSocketRpcUrl { get; init; }

    public bool PreferWebSocket { get; init; } = true;

    public ulong? StartBlockNumber { get; init; }

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan WebSocketReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);
}
