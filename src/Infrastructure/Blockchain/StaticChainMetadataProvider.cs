using Application.Abstractions;
using Domain.Common;

namespace Infrastructure.Blockchain;

/// <summary>
/// Static catalog for chain display names, native symbols, and explorer base URLs.
/// </summary>
public sealed class StaticChainMetadataProvider : IChainMetadataProvider
{
    private static readonly IReadOnlyDictionary<SupportedChain, ChainMetadata> Metadata =
        new Dictionary<SupportedChain, ChainMetadata>
        {
            [SupportedChain.Ethereum] = new(SupportedChain.Ethereum, "Ethereum", "ETH", "ETH", 18, "https://etherscan.io"),
            [SupportedChain.Bsc] = new(SupportedChain.Bsc, "BNB Chain", "BNB", "BNB", 18, "https://bscscan.com"),
            [SupportedChain.Gnosis] = new(SupportedChain.Gnosis, "Gnosis", "GNO", "xDAI", 18, "https://gnosisscan.io"),
            [SupportedChain.Polygon] = new(SupportedChain.Polygon, "Polygon", "Polygon", "MATIC", 18, "https://polygonscan.com"),
            [SupportedChain.Rootstock] = new(SupportedChain.Rootstock, "Rootstock", "RBTC", "RBTC", 18, "https://rootstock.blockscout.com"),
            [SupportedChain.Optimism] = new(SupportedChain.Optimism, "Optimism", "OP", "ETH", 18, "https://optimistic.etherscan.io"),
            [SupportedChain.Celo] = new(SupportedChain.Celo, "Celo", "CELO", "CELO", 18, "https://celoscan.io"),
            [SupportedChain.Arbitrum] = new(SupportedChain.Arbitrum, "Arbitrum", "Arbitrum", "ETH", 18, "https://arbiscan.io"),
            [SupportedChain.Base] = new(SupportedChain.Base, "Base", "Base", "ETH", 18, "https://basescan.org"),
            [SupportedChain.Robinhood] = new(SupportedChain.Robinhood, "Robinhood Chain", "RH", "ETH", 18, "https://robinhoodchain.blockscout.com"),
            [SupportedChain.HyperEvm] = new(SupportedChain.HyperEvm, "HyperEVM", "HyperEVM", "HYPE", 18, "https://hyperscan.com")
        };

    public ChainMetadata Get(SupportedChain chain)
        => Metadata.TryGetValue(chain, out var metadata)
            ? metadata
            : throw new InvalidOperationException($"Chain metadata is not configured for {chain}.");
}
