using Domain.Common;
namespace Infrastructure.Hyperliquid;

/// <summary>Public HyperCore adapter bounds. Disabled configuration opens no provider connections.</summary>
public sealed class HyperliquidOptions
{
    public bool Enabled { get; set; }
    public string WebSocketUrl { get; set; } = "wss://api.hyperliquid.xyz/ws";
    public string InfoUrl { get; set; } = "https://api.hyperliquid.xyz/info";
    public int MaxUniqueWallets { get; set; } = 10;
    public int PingSeconds { get; set; } = 20;
    public int PongTimeoutSeconds { get; set; } = 40;
    public int MaximumFrameBytes { get; set; } = 2097152;
    public int QueueCapacity { get; set; } = 128;
    public int MaximumRecoveryPages { get; set; } = 8;
    public int RecoveryHours { get; set; } = 24;
    public int RetentionDays { get; set; } = 7;
    public int AggregationMilliseconds { get; set; } = 1000;

    /// <summary>Disabled Core configuration cannot prevent independent EVM workers from starting.</summary>
    public bool IsValidForStartup() => !Enabled || IsValid();

    public bool IsValid() => MaxUniqueWallets is >= 1 and <= 10 && PingSeconds is >= 5 and <= 20
        && PongTimeoutSeconds > PingSeconds && MaximumFrameBytes is >= 1024 and <= 8388608
        && QueueCapacity is >= 1 and <= 4096 && MaximumRecoveryPages is >= 1 and <= 100
        && RecoveryHours is >= 1 and <= 72 && RetentionDays >= 4 && AggregationMilliseconds is >= 100 and <= 5000;
}

/// <summary>Dedicated Core version wakes enrollment reconciliation independently of EVM cache membership.</summary>
public sealed class HyperliquidChangeSignal
{
    private long _version;
    private long _accepted;
    private long _duplicates;
    public long Version => Interlocked.Read(ref _version);
    public long Accepted => Interlocked.Read(ref _accepted);
    public long Duplicates => Interlocked.Read(ref _duplicates);
    public void AcceptedEvent() => Interlocked.Increment(ref _accepted);
    public void DuplicateEvent() => Interlocked.Increment(ref _duplicates);
    public void Changed() => Interlocked.Increment(ref _version);
    public string Status { get; set; } = RuntimeStates.Checkpoint.Connecting;
}
