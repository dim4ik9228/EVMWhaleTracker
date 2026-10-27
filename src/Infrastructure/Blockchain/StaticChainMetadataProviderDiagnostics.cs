using Domain.Common;

namespace Infrastructure.Blockchain;

/// <summary>Configuration failures for static chain metadata resolution.</summary>
internal static class StaticChainMetadataProviderDiagnostics
{
    public static string ChainNotConfigured(SupportedChain chain) => $"Chain metadata is not configured for {chain}.";
}
