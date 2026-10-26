using Microsoft.Extensions.Configuration;

namespace Infrastructure.Blockchain;

/// <summary>
/// Builds the enabled blockchain network list from the preferred multi-network config, with a legacy single-network fallback.
/// </summary>
public static class BlockchainNetworkOptionsResolver
{
    public static IReadOnlyList<BlockchainNetworkOptions> Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var multiNetworkOptions = configuration
            .GetSection("BlockchainNetworks")
            .Get<BlockchainNetworksOptions>();

        var configuredNetworks = multiNetworkOptions?.Networks ?? [];
        if (configuredNetworks.Count == 0)
        {
            var legacyOptions = configuration
                .GetSection("BlockchainIndexer")
                .Get<BlockchainIndexerOptions>();

            if (legacyOptions is null
                || (string.IsNullOrWhiteSpace(legacyOptions.HttpRpcUrl)
                    && string.IsNullOrWhiteSpace(legacyOptions.WebSocketRpcUrl)))
            {
                return [];
            }

            configuredNetworks =
            [
                new BlockchainNetworkOptions
                {
                    Chain = legacyOptions.Chain,
                    Enabled = true,
                    EnableRpcDiagnostics = legacyOptions.EnableRpcDiagnostics,
                    HttpRpcUrl = legacyOptions.HttpRpcUrl,
                    WebSocketRpcUrl = legacyOptions.WebSocketRpcUrl,
                    RpcApiKey = configuration["BlockchainIndexer:RpcApiKey"],
                    PreferWebSocket = legacyOptions.PreferWebSocket,
                    StartBlockNumber = legacyOptions.StartBlockNumber,
                    PollInterval = legacyOptions.PollInterval,
                    WebSocketReconnectDelay = legacyOptions.WebSocketReconnectDelay
                }
            ];
        }

        var enabledNetworks = configuredNetworks
            .Where(network => network.Enabled && network.Chain != Domain.Common.SupportedChain.Arc)
            .Select(network => ApplySharedSettings(network, multiNetworkOptions))
            .ToArray();

        var duplicateChains = enabledNetworks
            .GroupBy(network => network.Chain)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateChains.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate enabled blockchain network configuration: {string.Join(", ", duplicateChains)}.");
        }

        foreach (var network in enabledNetworks)
        {
            if (string.IsNullOrWhiteSpace(network.BuildWebSocketRpcUrl()))
            {
                throw new InvalidOperationException(
                    $"Blockchain network {network.Chain} is enabled but WebSocketRpcUrl is not configured.");
            }

            if (string.IsNullOrWhiteSpace(network.BuildHttpRpcUrl())
                && string.IsNullOrWhiteSpace(network.BuildWebSocketRpcUrl()))
            {
                throw new InvalidOperationException(
                    $"Blockchain network {network.Chain} must configure HttpRpcUrl or WebSocketRpcUrl.");
            }
        }

        return enabledNetworks;
    }

    private static BlockchainNetworkOptions ApplySharedSettings(
        BlockchainNetworkOptions network,
        BlockchainNetworksOptions? sharedOptions)
    {
        if (sharedOptions is null)
        {
            return network;
        }

        return new BlockchainNetworkOptions
        {
            Chain = network.Chain,
            Enabled = network.Enabled,
            EnableRpcDiagnostics = sharedOptions.EnableRpcDiagnostics || network.EnableRpcDiagnostics,
            HttpRpcUrl = network.HttpRpcUrl,
            WebSocketRpcUrl = network.WebSocketRpcUrl,
            RpcApiKey = string.IsNullOrWhiteSpace(network.RpcApiKey) ? sharedOptions.RpcApiKey : network.RpcApiKey,
            PreferWebSocket = sharedOptions.PreferWebSocket,
            StartBlockNumber = sharedOptions.StartBlockNumber ?? network.StartBlockNumber,
            PollInterval = sharedOptions.PollInterval,
            WebSocketReconnectDelay = sharedOptions.WebSocketReconnectDelay
        };
    }
}
