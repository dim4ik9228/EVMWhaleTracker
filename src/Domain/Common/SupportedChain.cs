namespace Domain.Common;

/// <summary>
/// EVM-compatible chains supported by tracking, metadata, pricing, and explorer-link services.
/// </summary>
public enum SupportedChain
{
    Ethereum = 1,
    Bsc = 56,
    Gnosis = 100,
    Polygon = 137,
    Rootstock = 30,
    Optimism = 10,
    Celo = 42220,
    Arbitrum = 42161,
    Base = 8453,
    HyperEvm = 999
}
