namespace Infrastructure.Blockchain;

/// <summary>Network configuration contracts shared by resolution and host binding.</summary>
public static class BlockchainConfiguration
{
    public const string NetworksSection = "BlockchainNetworks";
    public const string IndexerSection = "BlockchainIndexer";
    public const string SharedApiKey = "BlockchainNetworks:RpcApiKey";
    public const string LegacyApiKey = "BlockchainIndexer:RpcApiKey";
}
