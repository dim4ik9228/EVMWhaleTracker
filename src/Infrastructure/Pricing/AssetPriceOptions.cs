using Domain.Common;

namespace Infrastructure.Pricing;

/// <summary>
/// Configuration for external USD price lookups and short-lived price caching.
/// </summary>
public sealed class AssetPriceOptions
{
    public bool Enabled { get; init; } = true;

    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromSeconds(60);

    public string CoinGeckoSimplePriceUrl { get; init; } = "https://api.coingecko.com/api/v3/simple/price";

    public string DexScreenerTokenUrl { get; init; } = "https://api.dexscreener.com/latest/dex/tokens";

    public IReadOnlyDictionary<SupportedChain, string> NativeCoinGeckoIds { get; init; }
        = new Dictionary<SupportedChain, string>
        {
            [SupportedChain.Ethereum] = "ethereum",
            [SupportedChain.Bsc] = "binancecoin",
            [SupportedChain.Gnosis] = "xdai",
            [SupportedChain.Polygon] = "matic-network",
            [SupportedChain.Rootstock] = "rootstock-rsk",
            [SupportedChain.Optimism] = "ethereum",
            [SupportedChain.Celo] = "celo",
            [SupportedChain.Arbitrum] = "ethereum",
            [SupportedChain.Base] = "ethereum",
            [SupportedChain.HyperEvm] = "hyperliquid"
        };

    public IReadOnlyDictionary<SupportedChain, string> DexScreenerChainIds { get; init; }
        = new Dictionary<SupportedChain, string>
        {
            [SupportedChain.Ethereum] = "ethereum",
            [SupportedChain.Bsc] = "bsc",
            [SupportedChain.Gnosis] = "gnosis",
            [SupportedChain.Polygon] = "polygon",
            [SupportedChain.Rootstock] = "rsk",
            [SupportedChain.Optimism] = "optimism",
            [SupportedChain.Celo] = "celo",
            [SupportedChain.Arbitrum] = "arbitrum",
            [SupportedChain.Base] = "base",
            [SupportedChain.HyperEvm] = "hyperevm",
            [SupportedChain.Robinhood] = "robinhood"
        };
}
