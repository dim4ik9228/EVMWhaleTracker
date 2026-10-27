using Domain.Common;

namespace Application.Abstractions;

/// <summary>Named failures for AssetPrice operations; codes remain compatible.</summary>
public static class AssetPriceErrors
{
    public static readonly Error Unavailable = new("AssetPrice.Unavailable", "Asset USD price could not be resolved.");
}
