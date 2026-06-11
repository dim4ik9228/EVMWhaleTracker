using Domain.Common;

namespace Application.Abstractions;

/// <summary>
/// Resolves USD prices for native assets and token contracts.
/// </summary>
public interface IAssetPriceProvider
{
    Task<Result<AssetUsdPrice>> GetUsdPriceAsync(
        SupportedChain chain,
        string? tokenContractAddress,
        string assetSymbol,
        CancellationToken cancellationToken);
}

/// <summary>
/// USD price quote with source metadata for diagnostics.
/// </summary>
public sealed record AssetUsdPrice(
    decimal PriceUsd,
    DateTimeOffset QuotedAtUtc,
    string Source);
