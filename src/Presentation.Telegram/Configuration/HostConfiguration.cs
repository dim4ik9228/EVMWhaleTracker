namespace Presentation.Telegram.Configuration;

/// <summary>Composition-root configuration keys, named HTTP clients and host actions.</summary>
internal static class HostConfiguration
{
    public const string BlockchainIndexer = Infrastructure.Blockchain.BlockchainConfiguration.IndexerSection;
    public const string BlockchainNetworks = Infrastructure.Blockchain.BlockchainConfiguration.NetworksSection;
    public const string AssetPrices = "AssetPrices";
    public const string TelegramBot = "TelegramBot";
    public const string EnsReverseLookup = "EnsReverseLookup";
    public const string ConnectionString = "ConnectionStrings:Default";
    public const string Polymarket = "Polymarket";
    public const string PolymarketPolygon = "PolymarketPolygon";
    public const string RpcApiKey = Infrastructure.Blockchain.BlockchainConfiguration.SharedApiKey;
    public const string Hyperliquid = "Hyperliquid";
    public const string BotToken = "BotToken";
    public const string ImportAddressLabels = "import-address-labels";
}
