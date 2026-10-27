using Domain.Common;

namespace Application.Abstractions;

/// <summary>Named failures for TokenMetadata operations; codes remain compatible.</summary>
public static class TokenMetadataErrors
{
    public static readonly Error Unavailable = new("TokenMetadata.Unavailable", "Token metadata could not be resolved.");
}
