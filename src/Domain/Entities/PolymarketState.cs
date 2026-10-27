using Domain.Common;
namespace Domain.Entities;

/// <summary>Owner-specific opt-in and explicitly confirmed source address.</summary>
public sealed class PolymarketSetting
{
    public Guid WalletId { get; set; }
    public bool Enabled { get; set; }
    public Guid Generation { get; set; }
    public DateTimeOffset ActivatedAt { get; set; }
    public long? ActivationBlock { get; set; }
    public string? SourceAddress { get; set; }
    public string? ProposedAddress { get; set; }
    public string? ProfileJson { get; set; }
    public bool NeedsAttention { get; set; }
    public decimal MinimumTradeValue { get; set; }
}

/// <summary>Durable inbox identity, preserved independently of delivery attempts.</summary>
public sealed class PolymarketEvent
{
    public string Id { get; set; } = "";
    public string SourceAddress { get; set; } = "";
    public DateTimeOffset Time { get; set; }
    public string Payload { get; set; } = "";
    public DateTimeOffset StoredAt { get; set; }
    public long BlockNumber { get; set; }
    public string BlockHash { get; set; } = "";
    public string RawLogId { get; set; } = "";
    public string DecoderVersion { get; set; } = "";
    public bool Canonical { get; set; } = true;
}

/// <summary>Covered query boundaries and resumable scan cursor; latest event time is separate.</summary>
public sealed class PolymarketCheckpoint
{
    public string SourceAddress { get; set; } = "";
    public DateTimeOffset? LastSuccessfulScan { get; set; }
    public DateTimeOffset? LatestActivity { get; set; }
    public DateTimeOffset? WindowStart { get; set; }
    public DateTimeOffset? WindowEnd { get; set; }
    public string? Cursor { get; set; }
    public string? Gap { get; set; }
    public string Status { get; set; } = RuntimeStates.Checkpoint.Starting;
    public long? CoveredBlock { get; set; }
    public string? CoveredBlockHash { get; set; }
    public long? ScanNextBlock { get; set; }
    public long? ScanTargetBlock { get; set; }
    public string? RegistryVersion { get; set; }
}

/// <summary>Persistent recipient work tied to the original activation generation.</summary>
public sealed class PolymarketDelivery
{
    public Guid Id { get; set; }
    public string EventId { get; set; } = "";
    public Guid WalletId { get; set; }
    public Guid Generation { get; set; }
    public long UserId { get; set; }
    public string State { get; set; } = RuntimeStates.Delivery.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset DueAt { get; set; }
}
