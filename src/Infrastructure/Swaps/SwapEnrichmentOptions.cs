using Domain.Common;

namespace Infrastructure.Swaps;

/// <summary>Bounded default-off Ethereum enrichment. Qualification must precede enablement.</summary>
public sealed class SwapEnrichmentOptions
{
    public bool Enabled { get; set; }
    public bool ProviderQualified { get; set; }
    public List<SupportedChain> EnabledChains { get; set; } = [SupportedChain.Ethereum];
    public string RegistryVersion { get; set; } = "ethereum-uniswap-v3-1";
    public TimeSpan DecisionDeadline { get; set; } = TimeSpan.FromSeconds(3);
    public int Concurrency { get; set; } = 4;
    public int AdmissionCapacity { get; set; } = 256;
    public int PendingCapacity { get; set; } = 10000;
    public int CacheCapacity { get; set; } = 256;
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan ProviderCooldown { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RequestSpacing { get; set; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);
    public int MaximumResponseBytes { get; set; } = 4 * 1024 * 1024;
    public bool IsEnabled(SupportedChain chain) => Enabled && ProviderQualified && chain == SupportedChain.Ethereum && EnabledChains.Contains(chain);
    public bool IsValid() => Concurrency is > 0 and <= 16 && AdmissionCapacity is > 0 and <= 4096
        && PendingCapacity is > 0 and <= 100000 && CacheCapacity is > 0 and <= 4096
        && DecisionDeadline > TimeSpan.Zero && DecisionDeadline <= TimeSpan.FromSeconds(10)
        && Retention >= TimeSpan.FromDays(7) && CacheTtl > TimeSpan.Zero && RequestSpacing > TimeSpan.Zero
        && ProviderCooldown > TimeSpan.Zero && MaximumResponseBytes is >= 1024 and <= 16777216
        && RegistryVersion == "ethereum-uniswap-v3-1" && EnabledChains.All(x => x == SupportedChain.Ethereum);
}
/// <summary>Credential-free diagnostic templates.</summary>
public static class SwapDiagnostics
{
    public const string Decision = "Swap decision {DecisionId}: {Outcome}; queue age {AgeMs} ms";
    public const string Provider = "Swap evidence {Outcome}; elapsed {ElapsedMs} ms; RPC calls {Calls}";
    public const string StoreUnavailable = "Swap durable intake unavailable; applying chain backpressure";
    public const string InvalidOptions = "Invalid SwapEnrichment limits or Ethereum registry";
    public const string IngressGap = "Token ingress overflow on {Chain}; source degraded, possible event gap; disconnecting with cooldown";
}
