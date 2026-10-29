namespace Domain.Entities;

/// <summary>Durable EVM enrollment boundary shared by delivery hosts; freshness is tracked separately in memory.</summary>
public sealed class WalletAccessState
{
    public Guid WalletId { get; set; }
    public long AccountRevision { get; set; }
    public long EnrollmentRevision { get; set; }
    public bool EvmEnabled { get; set; }
    public DateTimeOffset ActiveSince { get; set; }
    public DateTimeOffset EvmUntil { get; set; }
    public DateTimeOffset? PolymarketUntil { get; set; }
    public DateTimeOffset? HyperliquidUntil { get; set; }
}

/// <summary>Owner concurrency identity and durable Free selection. Paid periods are independent ledger entries.</summary>
public sealed class BillingAccount
{
    public long UserId { get; set; }
    public long Revision { get; set; }
    public string FreeWalletIds { get; set; } = "[]";
    public Guid? FreePolymarketWalletId { get; set; }
    public bool SelectionInitialized { get; set; }
    public bool WasPro { get; set; }
}

/// <summary>Immutable purchase snapshot; fulfilment is claimed transactionally once.</summary>
public sealed class PurchaseOrder
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public string Plan { get; set; } = "Pro.v1";
    public int DurationHours { get; set; } = 720;
    public decimal UsdPrice { get; set; } = 29m;
    public int StarsPrice { get; set; } = 1450;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset CheckoutExpiresAt { get; set; }
    public bool Fulfilled { get; set; }
}

/// <summary>Opaque provider attempt, retained even after an ambiguous creation outcome.</summary>
public sealed class PaymentAttempt
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Provider { get; set; } = "";
    public string State { get; set; } = "Creating";
    public string? ExternalId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CheckoutUrl { get; set; }
    public string Currency { get; set; } = "";
    public decimal? ExpectedAmount { get; set; }
    public decimal? PaidAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset RetryAt { get; set; }
    public int Retries { get; set; }
}

/// <summary>Authenticated financial evidence; different charges never collapse into one notification.</summary>
public sealed class PaymentReceipt
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = "";
    public string EventKey { get; set; } = "";
    public string ChargeId { get; set; } = "";
    public Guid? AttemptId { get; set; }
    public long? UserId { get; set; }
    public string Currency { get; set; } = "";
    public decimal Amount { get; set; }
    public bool Reversal { get; set; }
    public string State { get; set; } = "Received";
    public string Evidence { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset RetryAt { get; set; }
    public int Retries { get; set; }
}

/// <summary>Half-open paid period. Reversal affects this period alone, preserving later purchases.</summary>
public sealed class AccessGrant
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? ReceiptId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public long? OperatorId { get; set; }
    public string? Reason { get; set; }
    public bool Contains(DateTimeOffset instant) => ReversedAt is null && StartsAt <= instant && instant < EndsAt;
}

/// <summary>Ledger notices and projection changes survive failures outside the settlement transaction.</summary>
public sealed class BillingOutbox
{
    public string Key { get; set; } = "";
    public long UserId { get; set; }
    public string Kind { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset RetryAt { get; set; }
    public int Attempts { get; set; }
}

/// <summary>Persisted polling cursor; offset advances only after financial ingestion commits.</summary>
public sealed class BillingCheckpoint
{
    public string Key { get; set; } = "";
    public long Offset { get; set; }
    public DateTimeOffset RetryAt { get; set; }
    public string? Attention { get; set; }
}

/// <summary>Append-only record of operator actions, including ambiguous refund requests.</summary>
public sealed class BillingAudit
{
    public Guid Id { get; set; }
    public long ActorId { get; set; }
    public long UserId { get; set; }
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? Reference { get; set; }
    public DateTimeOffset At { get; set; }
}
