using Domain.Common;
namespace Domain.Entities;

/// <summary>Independent, owner-specific HyperCore enrollment; generations invalidate queued deliveries.</summary>
public sealed class HyperliquidSetting
{
    public Guid WalletId { get; set; }
    public bool Enabled { get; set; }
    public Guid Generation { get; set; }
    public DateTimeOffset ActivatedAt { get; set; }
}

/// <summary>A distinct address retains capacity until its old socket has been closed.</summary>
public sealed class HyperliquidReservation
{
    public string Address { get; set; } = "";
}

/// <summary>Durable canonical source inbox. Payload contains exact decimal values.</summary>
public sealed class HyperliquidEvent
{
    public string Id { get; set; } = "";
    public string Address { get; set; } = "";
    public string Stream { get; set; } = "";
    public long Time { get; set; }
    public string Payload { get; set; } = "";
    public DateTimeOffset StoredAt { get; set; }
}

/// <summary>Recovery boundary advances only after the bounded history and live overlap have been durably accepted.</summary>
public sealed class HyperliquidCheckpoint
{
    public string Key { get; set; } = "";
    public long Time { get; set; }
    public string? Gap { get; set; }
}

/// <summary>Recipient outbox retains activation identity and delivery outcome across restarts.</summary>
public sealed class HyperliquidDelivery
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
