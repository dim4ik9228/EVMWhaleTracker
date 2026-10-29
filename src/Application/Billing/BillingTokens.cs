namespace Application.Billing;

/// <summary>Stable ledger states and provider identities. These values are persisted and must not be renamed casually.</summary>
public static class BillingStates
{
    public const string Creating = "Creating", CreationUnknown = "CreationUnknown", Pending = "Pending", Settled = "Settled", Reversed = "Reversed";
    public const string Received = "Received", Review = "Review", DuplicatePayment = "DuplicatePayment", Applied = "Applied", ReversalPending = "ReversalPending";
    public const string Inbox = "Inbox", AwaitingProvider = "AwaitingProvider", Processed = "Processed", ReconciliationNeedsCredentials = "ReconciliationNeedsCredentials";
    public const string AwaitingIdentity = "AwaitingIdentity", IdentityAttention = "IdentityAttention", ImportPayment = "ImportPayment";
    public const string Activated = "Activated", Expired = "Expired", PaymentReview = "PaymentReview";
    public const string Grant = "Grant", Revoke = "Revoke", RefundRequested = "RefundRequested", AttachPayment = "AttachPayment";
    public const string TelegramUpdates = "TelegramUpdates", StarsHistory = "StarsHistory", ProPlan = "Pro.v1";
}

/// <summary>Stable receipt identities distinguish notification replay from distinct money movements.</summary>
public static class BillingKeys
{
    public static string Paid(string charge) => "paid:" + charge;
    public static string Refund(string charge) => "refund:" + charge;
    public static string Settlement(string payment, decimal amount) => "settled:" + payment + ":" + amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public static string Discovered(string payment) => "discovered:" + payment;
    public static string Purchase(Guid order) => "purchase:" + order;
    public static string Receipt(Guid receipt) => "receipt:" + receipt;
    public static string Expiry(long owner, long revision) => "expiry:" + owner + ":" + revision;
    public static string Ipn(string hash) => "ipn:" + hash;
}
