using Infrastructure.Blockchain;
using Microsoft.Extensions.Configuration;
using SupportedChain = Domain.Common.SupportedChain;

namespace UnitTests.Infrastructure;

public sealed class BlockchainNetworkOptionsTests
{
    [Fact]
    public void BuildHttpRpcUrl_AppendsApiKeyToBaseUrl()
    {
        var options = new BlockchainNetworkOptions
        {
            HttpRpcUrl = "https://example-rpc.test/v3/",
            RpcApiKey = "secret-key"
        };

        Assert.Equal("https://example-rpc.test/v3/secret-key", options.BuildHttpRpcUrl());
    }

    [Fact]
    public void BuildWebSocketRpcUrl_ReplacesApiKeyPlaceholder()
    {
        var options = new BlockchainNetworkOptions
        {
            WebSocketRpcUrl = "wss://example-rpc.test/ws/{ApiKey}",
            RpcApiKey = "secret-key"
        };

        Assert.Equal("wss://example-rpc.test/ws/secret-key", options.BuildWebSocketRpcUrl());
    }

    [Fact]
    public void Resolve_IgnoresDisabledNetworks()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["BlockchainNetworks:Networks:0:Chain"] = "Bsc",
            ["BlockchainNetworks:Networks:0:Enabled"] = "true",
            ["BlockchainNetworks:Networks:0:WebSocketRpcUrl"] = "wss://bsc.example/",
            ["BlockchainNetworks:Networks:1:Chain"] = "Base",
            ["BlockchainNetworks:Networks:1:Enabled"] = "false",
            ["BlockchainNetworks:Networks:1:WebSocketRpcUrl"] = "wss://base.example/"
        });

        var networks = BlockchainNetworkOptionsResolver.Resolve(configuration);

        var network = Assert.Single(networks);
        Assert.Equal(SupportedChain.Bsc, network.Chain);
    }

    [Fact]
    public void Resolve_FailsForDuplicateEnabledChains()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["BlockchainNetworks:Networks:0:Chain"] = "Bsc",
            ["BlockchainNetworks:Networks:0:Enabled"] = "true",
            ["BlockchainNetworks:Networks:0:WebSocketRpcUrl"] = "wss://bsc-a.example/",
            ["BlockchainNetworks:Networks:1:Chain"] = "Bsc",
            ["BlockchainNetworks:Networks:1:Enabled"] = "true",
            ["BlockchainNetworks:Networks:1:WebSocketRpcUrl"] = "wss://bsc-b.example/"
        });

        Assert.Throws<InvalidOperationException>(() => BlockchainNetworkOptionsResolver.Resolve(configuration));
    }

    [Fact]
    public void Resolve_UsesLegacyBlockchainIndexerWhenNetworkListIsEmpty()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["BlockchainIndexer:Chain"] = "Base",
            ["BlockchainIndexer:WebSocketRpcUrl"] = "wss://base.example/",
            ["BlockchainIndexer:RpcApiKey"] = "secret-key"
        });

        var networks = BlockchainNetworkOptionsResolver.Resolve(configuration);

        var network = Assert.Single(networks);
        Assert.Equal(SupportedChain.Base, network.Chain);
        Assert.Equal("wss://base.example/secret-key", network.BuildWebSocketRpcUrl());
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
