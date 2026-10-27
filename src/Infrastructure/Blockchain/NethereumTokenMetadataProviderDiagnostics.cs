namespace Infrastructure.Blockchain;

/// <summary>Named diagnostic messages and structured log templates for NethereumTokenMetadataProvider.</summary>
internal static class NethereumTokenMetadataProviderDiagnostics
{
    public const string NetworkEndpointRequired = "At least one blockchain network RPC endpoint must be configured for token metadata resolution.";
    public const string RequiredMetadataUnavailable = "Failed to resolve required token metadata for {TokenContractAddress} on {Chain}; suppressing retries for {FailureCacheDuration}.";
    public const string MetadataLookupFailed = "Failed to resolve token metadata for {TokenContractAddress} on {Chain}.";
}
