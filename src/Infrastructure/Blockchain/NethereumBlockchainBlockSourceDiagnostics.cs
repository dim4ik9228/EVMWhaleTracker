namespace Infrastructure.Blockchain;

/// <summary>Named diagnostic messages and structured log templates for NethereumBlockchainBlockSource.</summary>
internal static class NethereumBlockchainBlockSourceDiagnostics
{
    public const string RpcEndpointRequired = "Either HttpRpcUrl or WebSocketRpcUrl must be configured for the blockchain indexer.";
    public const string BlockReturnedNoDataFromRPC = "Block {BlockNumber} returned no data from RPC.";
    public const string LoadedBlockTransactionsElapsedMs = "Loaded block {BlockNumber}: transactions={TransactionCount}, elapsedMs={ElapsedMilliseconds}.";
    public const string WebSocketEndpointRequired = "WebSocketRpcUrl must be configured to stream new block headers.";
    public const string IgnoringWebsocketUnsubscribeFailure = "Ignoring websocket unsubscribe failure.";
    public const string IgnoringWebsocketStopFailure = "Ignoring websocket stop failure.";
}
