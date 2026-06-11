using Domain.Common;

namespace Application.Abstractions;

public interface IChainMetadataProvider
{
    ChainMetadata Get(SupportedChain chain);
}

public sealed record ChainMetadata(
    SupportedChain Chain,
    string DisplayName,
    string ShortName,
    string NativeSymbol,
    int NativeDecimals,
    string ExplorerBaseUrl)
{
    public string GetAddressUrl(string address)
        => $"{ExplorerBaseUrl.TrimEnd('/')}/address/{Uri.EscapeDataString(address)}";

    public string GetTransactionUrl(string transactionHash)
        => $"{ExplorerBaseUrl.TrimEnd('/')}/tx/{Uri.EscapeDataString(transactionHash)}";

    public string GetTokenUrl(string tokenContractAddress)
        => $"{ExplorerBaseUrl.TrimEnd('/')}/token/{Uri.EscapeDataString(tokenContractAddress)}";
}
