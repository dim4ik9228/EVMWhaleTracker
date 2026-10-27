namespace Infrastructure.Blockchain;

/// <summary>Named diagnostic messages and structured log templates for BlockchainNetworkOptionsResolver.</summary>
internal static class BlockchainNetworkOptionsResolverDiagnostics
{
    public static string DuplicateNetworks(string chains) => $"Duplicate enabled blockchain network configuration: {chains}.";
    public static string WebSocketEndpointRequired(Domain.Common.SupportedChain chain) => $"Blockchain network {chain} is enabled but WebSocketRpcUrl is not configured.";
    public static string RpcEndpointRequired(Domain.Common.SupportedChain chain) => $"Blockchain network {chain} must configure HttpRpcUrl or WebSocketRpcUrl.";
}
