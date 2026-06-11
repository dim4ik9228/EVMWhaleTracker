using Domain.Common;

namespace Application.Abstractions;

/// <summary>
/// Resolves display metadata for ERC-20/BEP-20 token contracts.
/// </summary>
public interface ITokenMetadataProvider
{
    Task<Result<TokenMetadata>> GetAsync(
        SupportedChain chain,
        string tokenContractAddress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Token metadata needed to convert raw transfer amounts into human-readable amounts.
/// </summary>
public sealed record TokenMetadata(
    SupportedChain Chain,
    string ContractAddress,
    string Symbol,
    string? Name,
    int Decimals);
