using Domain.Common;
using Domain.Entities;

namespace Application.Billing;

/// <summary>Authorized, bounded administration of manual access, separate from financial records.</summary>
public interface IBillingAdministration
{
    bool IsOperator(long actor);
    Task<Result<AdminAccount>> AccountAsync(long actor, long target, int page, CancellationToken ct);
    Task<Result<AdminPreview>> PreviewAsync(long actor, long target, int hours, Guid? revoke, string reason, CancellationToken ct);
    Task<Result> ConfirmAsync(long actor, AdminPreview preview, CancellationToken ct);
    /// <summary>Preserves the explicit expert command's ability to reverse any grant without issuing a refund.</summary>
    Task<Result> RevokeExpertAsync(long actor, long target, Guid grant, string reason, CancellationToken ct);
}
/// <summary>Bounded target view; only operator grants can be selected for revocation.</summary>
public sealed record AdminAccount(long UserId, long Revision, bool Pro, DateTimeOffset? Until, IReadOnlyList<AccessGrant> Grants, int Page, bool More);
/// <summary>Actor-bound expiring operation; ObservedAt binds the preview to ledger time boundaries and its identity is the durable audit primary key.</summary>
public sealed record AdminPreview(Guid OperationId, long Actor, long Target, long Revision, int Hours, Guid? Revoke,
    string Reason, DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateTimeOffset ExpiresAt, DateTimeOffset ObservedAt);
/// <summary>Expected administration failures.</summary>
public static class BillingAdminErrors
{
    public static readonly Error Unauthorized = new("BillingAdmin.Unauthorized", "Operator access is required.");
    public static readonly Error Unknown = new("BillingAdmin.Unknown", "Enter a positive numeric ID for an existing bot user.");
    public static readonly Error Invalid = new("BillingAdmin.Invalid", "Choose a duration of 1–8760 hours and a reason of 1–300 characters.");
    public static readonly Error Expired = new("BillingAdmin.Expired", "This action expired. Open /admin to start again.");
    public static readonly Error Stale = new("BillingAdmin.Stale", "Access changed. Open the account and create a fresh preview.");
    public static readonly Error ManualOnly = new("BillingAdmin.ManualOnly", "Only an active operator grant can be revoked here.");
}
