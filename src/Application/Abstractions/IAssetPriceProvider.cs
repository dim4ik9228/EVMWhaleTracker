using Domain.Common;

namespace Application.Abstractions;

public interface IAssetPriceProvider
{
    Task<Result<AssetUsdPrice>> GetUsdPriceAsync(
        SupportedChain chain,
        string? tokenContractAddress,
        string assetSymbol,
        CancellationToken cancellationToken);
}

public sealed record AssetUsdPrice(
    decimal PriceUsd,
    DateTimeOffset QuotedAtUtc,
    string Source);
