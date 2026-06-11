using Domain.Common;

namespace Application.Abstractions;

/// <summary>
/// Provides display and explorer metadata for supported chains.
/// </summary>
public interface IChainMetadataProvider
{
    ChainMetadata Get(SupportedChain chain);
}

/// <summary>
/// Chain display metadata and explorer URL helpers.
/// </summary>
public sealed record ChainMetadata(
    SupportedChain Chain,
    string DisplayName,
    string ShortName,
    string NativeSymbol,
    int NativeDecimals,
    string ExplorerBaseUrl)
{
    /// <summary>
    /// Builds an explorer URL for a wallet or contract address.
    /// </summary>
    public string GetAddressUrl(string address)
        => $"{ExplorerBaseUrl.TrimEnd('/')}/address/{Uri.EscapeDataString(address)}";

    /// <summary>
    /// Builds an explorer URL for a transaction hash.
    /// </summary>
    public string GetTransactionUrl(string transactionHash)
        => $"{ExplorerBaseUrl.TrimEnd('/')}/tx/{Uri.EscapeDataString(transactionHash)}";

    /// <summary>
    /// Builds an explorer URL for a token contract.
    /// </summary>
    public string GetTokenUrl(string tokenContractAddress)
        => $"{ExplorerBaseUrl.TrimEnd('/')}/token/{Uri.EscapeDataString(tokenContractAddress)}";
}
