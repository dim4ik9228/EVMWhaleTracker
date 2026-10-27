namespace Infrastructure.Ens;

/// <summary>Named diagnostic messages and structured log templates for EthereumEnsReverseLookupProvider.</summary>
internal static class EthereumEnsReverseLookupProviderDiagnostics
{
    public const string ReverseLookupTimedOut = "ENS reverse lookup timed out for address {Address}.";
    public const string ReverseLookupFailed = "ENS reverse lookup failed for address {Address}.";
    public const string RpcFailure = "ENS RPC error: {Error}";
}
