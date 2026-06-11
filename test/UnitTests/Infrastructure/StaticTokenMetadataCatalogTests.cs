using Domain.Common;
using Infrastructure.Blockchain;

namespace UnitTests.Infrastructure;

public sealed class StaticTokenMetadataCatalogTests
{
    public static TheoryData<SupportedChain, string, string, string, int> KnownTokens =>
        new()
        {
            { SupportedChain.Ethereum, "0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48", "USDC", "USD Coin", 6 },
            { SupportedChain.Ethereum, "0xdAC17F958D2ee523a2206206994597C13D831ec7", "USDT", "Tether USD", 6 },
            { SupportedChain.Ethereum, "0x8d0D000Ee44948FC98c9B98A4FA4921476f08B0d", "USD1", "World Liberty Financial USD", 18 },
            { SupportedChain.Bsc, "0x55d398326f99059fF775485246999027B3197955", "USDT", "Tether USD", 18 },
            { SupportedChain.Bsc, "0x8AC76a51cc950d9822D68b83fE1Ad97B32Cd580d", "USDC", "Binance-Peg USD Coin", 18 },
            { SupportedChain.Base, "0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913", "USDC", "USD Coin", 6 },
            { SupportedChain.Arbitrum, "0xaf88d065e77c8cC2239327C5EDb3A432268e5831", "USDC", "USD Coin", 6 },
            { SupportedChain.Arbitrum, "0xfd086bc7cd5c481dcc9c85ebe478a1c0b69fcbb9", "USDT", "Tether USD", 6 },
            { SupportedChain.HyperEvm, "0xb88339CB7199b77E23DB6E890353E22632Ba630f", "USDC", "USD Coin", 6 }
        };

    [Theory]
    [MemberData(nameof(KnownTokens))]
    public void TryGet_ReturnsKnownTokenMetadata(
        SupportedChain chain,
        string tokenContractAddress,
        string symbol,
        string name,
        int decimals)
    {
        var found = StaticTokenMetadataCatalog.TryGet(
            chain,
            tokenContractAddress.ToLowerInvariant(),
            out var metadata);

        Assert.True(found);
        Assert.Equal(chain, metadata.Chain);
        Assert.Equal(symbol, metadata.Symbol);
        Assert.Equal(name, metadata.Name);
        Assert.Equal(decimals, metadata.Decimals);
    }

    [Fact]
    public void TryGet_ReturnsFalse_WhenTokenIsNotOverriddenForChain()
    {
        var found = StaticTokenMetadataCatalog.TryGet(
            SupportedChain.Ethereum,
            "0x55d398326f99059ff775485246999027b3197955",
            out _);

        Assert.False(found);
    }
}
