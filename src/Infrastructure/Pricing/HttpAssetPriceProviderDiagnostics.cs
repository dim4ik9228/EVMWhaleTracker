namespace Infrastructure.Pricing;

/// <summary>Named diagnostic messages and structured log templates for HttpAssetPriceProvider.</summary>
internal static class HttpAssetPriceProviderDiagnostics
{
    public const string NativePriceLookupFailed = "Failed to resolve native USD price for {Chain}.";
    public const string TokenPriceLookupFailed = "Failed to resolve token USD price for {TokenContractAddress} on {Chain}.";
}
