using Application.Abstractions;
using Domain.Common;

namespace Infrastructure.Blockchain;

/// <summary>
/// Small catalog of trusted token metadata overrides for common or non-standard contracts.
/// </summary>
public static class StaticTokenMetadataCatalog
{
    private static readonly IReadOnlyDictionary<string, TokenMetadata> Overrides =
        new Dictionary<string, TokenMetadata>
        {
            [Key(SupportedChain.Ethereum, "0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48")] =
                Metadata(SupportedChain.Ethereum, "0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48", "USDC", "USD Coin", 6),
            [Key(SupportedChain.Ethereum, "0xdAC17F958D2ee523a2206206994597C13D831ec7")] =
                Metadata(SupportedChain.Ethereum, "0xdAC17F958D2ee523a2206206994597C13D831ec7", "USDT", "Tether USD", 6),
            [Key(SupportedChain.Ethereum, "0x8d0D000Ee44948FC98c9B98A4FA4921476f08B0d")] =
                Metadata(SupportedChain.Ethereum, "0x8d0D000Ee44948FC98c9B98A4FA4921476f08B0d", "USD1", "World Liberty Financial USD", 18),
            [Key(SupportedChain.Ethereum, "0x6B175474E89094C44Da98b954EedeAC495271d0F")] =
                Metadata(SupportedChain.Ethereum, "0x6B175474E89094C44Da98b954EedeAC495271d0F", "DAI", "Dai Stablecoin", 18),
            [Key(SupportedChain.Ethereum, "0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2")] =
                Metadata(SupportedChain.Ethereum, "0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2", "WETH", "Wrapped Ether", 18),

            [Key(SupportedChain.Bsc, "0x55d398326f99059fF775485246999027B3197955")] =
                Metadata(SupportedChain.Bsc, "0x55d398326f99059fF775485246999027B3197955", "USDT", "Tether USD", 18),
            [Key(SupportedChain.Bsc, "0x8AC76a51cc950d9822D68b83fE1Ad97B32Cd580d")] =
                Metadata(SupportedChain.Bsc, "0x8AC76a51cc950d9822D68b83fE1Ad97B32Cd580d", "USDC", "Binance-Peg USD Coin", 18),
            [Key(SupportedChain.Bsc, "0x8d0D000Ee44948FC98c9B98A4FA4921476f08B0d")] =
                Metadata(SupportedChain.Bsc, "0x8d0D000Ee44948FC98c9B98A4FA4921476f08B0d", "USD1", "World Liberty Financial USD", 18),
            [Key(SupportedChain.Bsc, "0xbb4CdB9CBd36B01bD1cBaEBF2De08d9173bc095c")] =
                Metadata(SupportedChain.Bsc, "0xbb4CdB9CBd36B01bD1cBaEBF2De08d9173bc095c", "WBNB", "Wrapped BNB", 18),

            [Key(SupportedChain.Base, "0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913")] =
                Metadata(SupportedChain.Base, "0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913", "USDC", "USD Coin", 6),
            [Key(SupportedChain.Base, "0x4200000000000000000000000000000000000006")] =
                Metadata(SupportedChain.Base, "0x4200000000000000000000000000000000000006", "WETH", "Wrapped Ether", 18),

            [Key(SupportedChain.Arbitrum, "0xaf88d065e77c8cC2239327C5EDb3A432268e5831")] =
                Metadata(SupportedChain.Arbitrum, "0xaf88d065e77c8cC2239327C5EDb3A432268e5831", "USDC", "USD Coin", 6),
            [Key(SupportedChain.Arbitrum, "0xFF970A61A04b1cA14834A43f5dE4533eBDDB5CC8")] =
                Metadata(SupportedChain.Arbitrum, "0xFF970A61A04b1cA14834A43f5dE4533eBDDB5CC8", "USDC.e", "Bridged USDC", 6),
            [Key(SupportedChain.Arbitrum, "0xfd086bc7cd5c481dcc9c85ebe478a1c0b69fcbb9")] =
                Metadata(SupportedChain.Arbitrum, "0xfd086bc7cd5c481dcc9c85ebe478a1c0b69fcbb9", "USDT", "Tether USD", 6),
            [Key(SupportedChain.Arbitrum, "0xDA10009cBd5D07dd0CeCc66161FC93D7c9000da1")] =
                Metadata(SupportedChain.Arbitrum, "0xDA10009cBd5D07dd0CeCc66161FC93D7c9000da1", "DAI", "Dai Stablecoin", 18),
            [Key(SupportedChain.Arbitrum, "0x82aF49447D8a07e3bd95BD0d56f35241523fBab1")] =
                Metadata(SupportedChain.Arbitrum, "0x82aF49447D8a07e3bd95BD0d56f35241523fBab1", "WETH", "Wrapped Ether", 18),

            [Key(SupportedChain.HyperEvm, "0xb88339CB7199b77E23DB6E890353E22632Ba630f")] =
                Metadata(SupportedChain.HyperEvm, "0xb88339CB7199b77E23DB6E890353E22632Ba630f", "USDC", "USD Coin", 6)
        };

    public static bool TryGet(
        SupportedChain chain,
        string tokenContractAddress,
        out TokenMetadata metadata)
        => Overrides.TryGetValue(Key(chain, tokenContractAddress), out metadata!);

    private static string Key(SupportedChain chain, string tokenContractAddress)
        => $"{chain}:{tokenContractAddress.ToLowerInvariant()}";

    private static TokenMetadata Metadata(
        SupportedChain chain,
        string contractAddress,
        string symbol,
        string name,
        int decimals)
        => new(chain, contractAddress, symbol, name, decimals);
}
