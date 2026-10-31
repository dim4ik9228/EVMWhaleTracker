namespace Domain.Entities;

/// <summary>Durable logical notification and atomic outbox. Wallet ID is its activation generation.</summary>
public sealed class SwapDecision
{
    public Guid Id { get; set; }
    public int Chain { get; set; }
    public string TransactionHash { get; set; } = "";
    public Guid WalletId { get; set; }
    public long UserId { get; set; }
    public string WalletAddress { get; set; } = "";
    public string BlockHash { get; set; } = "";
    public ulong BlockNumber { get; set; }
    public DateTimeOffset FirstObservedAt { get; set; }
    public DateTimeOffset Deadline { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset WalletRevision { get; set; }
    public long? EntitlementRevision { get; set; }
    public DateTimeOffset? EntitlementValidUntil { get; set; }
    public string ObservationsJson { get; set; } = "[]";
    public string State { get; set; } = "candidate";
    public string Reason { get; set; } = "";
    public string? PayloadJson { get; set; }
    public Guid? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}
