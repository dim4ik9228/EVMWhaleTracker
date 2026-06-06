namespace Infrastructure.Blockchain;

public sealed class BlockchainIndexerOptions
{
    public bool EnableRpcDiagnostics { get; init; }

    public bool EnableTokenTransferReceiptFetching { get; init; }

    public string? HttpRpcUrl { get; init; }

    public string? WebSocketRpcUrl { get; init; }

    public bool PreferWebSocket { get; init; } = true;

    public ulong? StartBlockNumber { get; init; }

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan WebSocketReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);
}
