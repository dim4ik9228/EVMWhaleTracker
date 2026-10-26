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
    HyperEvm = 999,
    Robinhood = 4663,
    // Retired compatibility value for persisted numeric settings. Never reuse 5042.
    // Excluded from runtime catalogs, configuration resolution and Telegram actions.
    Arc = 5042
}
