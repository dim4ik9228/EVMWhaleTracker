namespace Presentation.Telegram.Billing;

/// <summary>Billing Telegram command and callback protocol.</summary>
public static class BillingUiTokens
{
    public const string TestCheckout = "/billing_test_checkout", TestCrypto = "billing:testcrypto:";
    public const string Subscription = "/subscription", Terms = "/terms", Support = "/paysupport", SelectionCommand = "/free_selection";
    public const string Grant = "/billing_grant", Refund = "/billing_refund", Revoke = "/billing_revoke", Preview = "/billing_preview", Attach = "/billing_attach", Import = "/billing_import", Recovery = "/billing_recovery";
    public const string Prefix = "billing:", Open = "billing:open", Buy = "billing:buy", Agree = "billing:agree", Selection = "billing:selection", Crypto = "billing:crypto:";
    public const string CompactPrefix = "b:", SelectSlot = "s", SelectPolymarket = "p", Confirm = "confirm", Empty = "-";
    public const string Pro = "billing:pro", History = "billing:history", Help = "billing:help";
    public const string SelectionPage = "billing:page:";
    public static string Slot(long revision, Guid wallet, int page = 0) => $"b:s:{revision}:{wallet:N}:{page}";
    public static string Polymarket(long revision, Guid wallet, int page = 0) => $"b:p:{revision}:{wallet:N}:{page}";
}
