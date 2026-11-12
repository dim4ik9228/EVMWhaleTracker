using Domain.Common;
namespace Infrastructure.Polymarket;

/// <summary>Independent default-off acquisition switch and finite Polygon resource budgets.</summary>
public sealed class PolymarketPolygonOptions
{
    internal long CooldownUntilTicks;
    /// <summary>Extends the shared process cooldown atomically; late throughput failures cannot shorten a daily pause.</summary>
    internal TimeSpan ExtendCooldown(TimeSpan duration)
    {
        var deadline = Environment.TickCount64 + (long)Math.Ceiling(duration.TotalMilliseconds);
        var current = Interlocked.Read(ref CooldownUntilTicks);
        while (current < deadline)
        {
            var observed = Interlocked.CompareExchange(ref CooldownUntilTicks, deadline, current);
            if (observed == current) break;
            current = observed;
        }
        return CooldownRemaining;
    }
    internal TimeSpan CooldownRemaining => TimeSpan.FromMilliseconds(
        Math.Max(0, Interlocked.Read(ref CooldownUntilTicks) - Environment.TickCount64));
    public bool Enabled { get; set; }
    public string HttpRpcUrl { get; set; } = "https://polygon-mainnet.infura.io/v3/";
    public string WebSocketRpcUrl { get; set; } = "wss://polygon-mainnet.infura.io/ws/v3/";
    public string? RpcApiKey { get; set; }
    public string RegistryVersion { get; set; } = PolymarketContractRegistry.Version;
    public string[] Families { get; set; } = [PolymarketWireTokens.CurrentCtf, PolymarketWireTokens.CurrentNegativeCtf, PolymarketWireTokens.PositionV2, PolymarketWireTokens.LegacyCtf, PolymarketWireTokens.LegacyNegativeCtf];
    public string FinalityMode { get; set; } = PolymarketWireTokens.FinalizedMode;
    public int ConfirmationDepth { get; set; } = 128;
    public int MaxUniqueAddresses { get; set; } = 20;
    public int MaxSubscriptions { get; set; } = 5;
    public int QueueCapacity { get; set; } = 256;
    public int MaximumFrameBytes { get; set; } = 2097152;
    public int MaximumResponseBytes { get; set; } = 4194304;
    public int TimeoutSeconds { get; set; } = 15;
    public int RpcMinimumIntervalMilliseconds { get; set; } = 250;
    /// <summary>Pending finality/enrichment is retried at this minimum cadence; idle work uses maintenance.</summary>
    public int PendingRetrySeconds { get; set; } = 2;
    public int MaintenanceSeconds { get; set; } = 120;
    public int CoverageAuditSeconds { get; set; } = 300;
    public int DailySocketMessageLimit { get; set; } = 10000;
    public int QuotaBackoffSeconds { get; set; } = 3600;
    /// <summary>Hard process-local HTTP request cap per UTC day; excludes provider-charged socket deliveries.</summary>
    public int DailyHttpRequestLimit { get; set; } = 12000;
    public int RecoveryBlocks { get; set; } = 43200;
    public int LogRangeBlocks { get; set; } = 1000;
    public int RecoveryRequests { get; set; } = 200;
    public int RetainedAncestry { get; set; } = 512;
    public int RetentionDays { get; set; } = 14;
    public int EnrichmentWaitSeconds { get; set; } = 60;
    public int MetadataCacheCapacity { get; set; } = 2048;
    public string MarketsUrl { get; set; } = "https://gamma-api.polymarket.com/markets";
    public string HttpEndpoint => Endpoint(HttpRpcUrl);
    public string WebSocketEndpoint => Endpoint(WebSocketRpcUrl);
    private string Endpoint(string value) => string.IsNullOrWhiteSpace(RpcApiKey) ? value : value.TrimEnd('/') + "/" + Uri.EscapeDataString(RpcApiKey);
    public bool IsValid() => !Enabled || RegistryVersion == PolymarketContractRegistry.Version
        && Families.Length > 0 && Families.All(f => PolymarketContractRegistry.Exchanges.Any(e => e.Family == f))
        && FinalityMode is PolymarketWireTokens.FinalizedMode or PolymarketWireTokens.ConfirmationsMode
        && ConfirmationDepth is >= 16 and <= 1024 && RetainedAncestry >= ConfirmationDepth * 2 && RetainedAncestry <= 4096
        && MaxUniqueAddresses is >= 1 and <= 100 && MaxSubscriptions is >= 5 and <= 20
        && QueueCapacity is >= 16 and <= 4096 && MaximumFrameBytes is >= 4096 and <= 8388608
        && MaximumResponseBytes is >= 4096 and <= 8388608 && TimeoutSeconds is >= 3 and <= 60
        && RpcMinimumIntervalMilliseconds is >= 100 and <= 5000
        && PendingRetrySeconds is >= 2 and <= 30 && MaintenanceSeconds is >= 60 and <= 600
        && CoverageAuditSeconds is >= 300 and <= 3600 && DailySocketMessageLimit is >= 100 and <= 100000
        && QuotaBackoffSeconds is >= 60 and <= 86400 && DailyHttpRequestLimit is >= 100 and <= 100000
        && RecoveryBlocks is >= 1000 and <= 129600 && LogRangeBlocks is >= 1 and <= 10000
        // A recovery range is atomic across all five log filters; smaller budgets cannot advance it.
        && RecoveryRequests is >= 5 and <= 1000 && RetentionDays is >= 7 and <= 365
        && EnrichmentWaitSeconds is >= 5 and <= 600 && MetadataCacheCapacity is >= 16 and <= 10000
        && Safe(HttpRpcUrl, "https") && Safe(WebSocketRpcUrl, "wss") && Safe(MarketsUrl, "https");
    private static bool Safe(string value, string scheme) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == scheme && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}

/// <summary>Process-local observable health; durable account gaps are kept separately in checkpoints.</summary>
public sealed class PolymarketPolygonHealth
{
    public string Status { get; set; } = RuntimeStates.Checkpoint.Starting;
    public long Head { get; set; }
    public int DesiredSubscriptions { get; set; }
    public int AcknowledgedSubscriptions { get; set; }
    public bool ChainVerified { get; set; }
    public long Malformed { get; set; }
    public long Unsupported { get; set; }
    public long Duplicates { get; set; }
    public long? CoveredBlock { get; set; }
    public long CanonicalBoundary { get; set; }
    public long? CoverageLagBlocks { get; set; }
    public int CoverageGaps { get; set; }
    public int NormalizationPending { get; set; }
    public int EnrichmentPending { get; set; }
    public int DeliveryPending { get; set; }
    public int DeliveryFailed { get; set; }
    /// <summary>Backlog counts stop at this limit plus one; a larger count indicates saturation.</summary>
    public const int BacklogCountLimit = 10000;
}
