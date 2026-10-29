using Domain.Common;
using Domain.Entities;

namespace Application.Billing;

/// <summary>Versioned public allowance and price contract.</summary>
public enum TrackingMode { Evm, Polymarket, Hyperliquid }

/// <summary>Versioned public allowance and price contract.</summary>
public static class BillingPlans
{
    public const int FreeWallets = 10, ProWallets = 50, ProHyperliquid = 10, Stars = 1450, DurationHours = 720;
    public const decimal Usd = 29m;
    public const string StarsProvider = "Stars", CryptoProvider = "NOWPayments", Xtr = "XTR", UsdtBsc = "usdtbsc";
}

/// <summary>Expected billing business failures.</summary>
public static class BillingErrors
{
    public static readonly Error Limit = new("Billing.Limit", "Your plan's saved-wallet limit is reached. Open /subscription.");
    public static readonly Error ProRequired = new("Billing.ProRequired", "Pro is required. Open /subscription.");
    public static readonly Error Selection = new("Billing.Selection", "This entry is paused by your Free selection. Open /subscription.");
    public static readonly Error InvalidSelection = new("Billing.InvalidSelection", "Select up to ten of your own wallets and one opted-in Polymarket entry among them.");
    public static readonly Error Unavailable = new("Billing.Unavailable", "Checkout is unavailable. Contact /paysupport.");
    public static readonly Error InvalidPayment = new("Billing.InvalidPayment", "The payment could not be matched to an outstanding purchase.");
    public static readonly Error Stale = new("Billing.Stale", "Subscription settings changed. Reopen /subscription.");
}

/// <summary>Known permission bounds; stale projections fail closed even when refresh is unavailable.</summary>
public sealed record WalletEntitlement(long UserId, Guid WalletId, long Revision, bool Evm, bool Polymarket, bool Hyperliquid,
    DateTimeOffset ValidUntil, DateTimeOffset ActiveSince, DateTimeOffset? PolymarketUntil = null, DateTimeOffset? HyperliquidUntil = null);

/// <summary>Process-local entitlement read model used by cache, matching and delivery.</summary>
public interface IEntitlementPolicy
{
    bool EnforcementEnabled { get; }
    WalletEntitlement? Get(Guid walletId);
    bool Allows(Guid walletId, TrackingMode mode, DateTimeOffset? activityAt = null, long? revision = null);
}

/// <summary>Authoritative final recipient check after asynchronous delivery waits, including cross-process ledger changes.</summary>
public interface IRecipientAccess
{
    Task<bool> AllowsAsync(long owner, Guid walletId, TrackingMode mode, DateTimeOffset? activityAt, long? enrollmentRevision, CancellationToken ct);
}

/// <summary>Atomic admission and owner-scoped billing operations.</summary>
public interface IBillingService
{
    Task<Result> AddWalletAsync(TrackedWallet wallet, CancellationToken ct);
    Task<Result> CanEnableAsync(long userId, Guid walletId, TrackingMode mode, CancellationToken ct);
    Task<BillingSummary> SummaryAsync(long userId, CancellationToken ct);
    Task<Result> SelectAsync(long userId, IReadOnlyCollection<Guid> wallets, Guid? polymarketWallet, long revision, CancellationToken ct);
    Task<Result<PurchaseOrder>> CreateOrderAsync(long userId, bool renewal, CancellationToken ct);
    Task<Result<PaymentAttempt>> PrepareAttemptAsync(long userId, Guid orderId, string provider, CancellationToken ct);
    Task<Result> PreCheckoutAsync(long userId, Guid attemptId, string currency, long total, CancellationToken ct);
    Task ReceiveAsync(FinancialReceipt receipt, CancellationToken ct);
    Task RefreshAsync(long userId, CancellationToken ct);
}

/// <summary>Provider-authenticated receipt passed into the common ledger; provider DTOs stay outside Domain.</summary>
public sealed record FinancialReceipt(string Provider, string EventKey, string ChargeId, Guid? AttemptId, long? UserId,
    string Currency, decimal Amount, bool Reversal = false, string Evidence = "", bool Verified = true);

/// <summary>Private owner subscription view.</summary>
public sealed record BillingSummary(long UserId, long Revision, bool Pro, int Saved, int Effective, DateTimeOffset? PaidUntil,
    IReadOnlyList<Guid> FreeSelection, Guid? FreePolymarket, IReadOnlyList<AccessGrant> Periods, int HyperliquidCapacity, int PolymarketCapacity);
