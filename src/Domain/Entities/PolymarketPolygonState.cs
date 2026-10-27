using Domain.Common;
namespace Domain.Entities;

/// <summary>Full Polygon source identity and immutable wire payload, separate from normalized activities.</summary>
public sealed class PolymarketRawLog
{
    public string Id { get; set; } = "";
    public int ChainId { get; set; } = 137;
    public string Contract { get; set; } = "";
    public long BlockNumber { get; set; }
    public string BlockHash { get; set; } = "";
    public string TransactionHash { get; set; } = "";
    public int TransactionIndex { get; set; }
    public int LogIndex { get; set; }
    public string TopicsJson { get; set; } = "";
    public string Data { get; set; } = "";
    public bool Removed { get; set; }
    public DateTimeOffset StoredAt { get; set; }
    public string DecodeState { get; set; } = RuntimeStates.Normalization.Pending;
}

/// <summary>Retained ancestry allows checkpoint validation and bounded common-ancestor rewinds.</summary>
public sealed class PolymarketBlock
{
    public long Number { get; set; }
    public string Hash { get; set; } = "";
    public string ParentHash { get; set; } = "";
    public DateTimeOffset Time { get; set; }
}
