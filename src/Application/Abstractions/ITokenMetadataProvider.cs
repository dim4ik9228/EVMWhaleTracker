using Domain.Common;

namespace Application.Abstractions;

public interface ITokenMetadataProvider
{
    Task<Result<TokenMetadata>> GetAsync(
        SupportedChain chain,
        string tokenContractAddress,
        CancellationToken cancellationToken);
}

public sealed record TokenMetadata(
    SupportedChain Chain,
    string ContractAddress,
    string Symbol,
    string? Name,
    int Decimals);
