namespace Infrastructure.Workers;

/// <summary>Named diagnostic messages and structured log templates for WalletTrackerWorker.</summary>
internal static class WalletTrackerWorkerDiagnostics
{
    public const string MetadataUnavailable = "Skipping ERC-20 transfer {TransactionHash} because token metadata for {TokenContractAddress} could not be resolved: {Error}.";
    public const string UnpricedTransferSuppressed = "Could not resolve USD price for {AssetSymbol} on {Chain}. Skipping notification because the minimum USD value is positive. Error: {Error}.";
    public const string UnpricedTransferAllowed = "Could not resolve USD price for {AssetSymbol} on {Chain}. Sending notification without USD value because the minimum USD value is zero. Error: {Error}.";
}
