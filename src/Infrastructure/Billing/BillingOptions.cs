namespace Infrastructure.Billing;

/// <summary>Independent rollout controls. Receipt accounting remains active when sales are disabled.</summary>
public sealed class BillingOptions
{
    public bool EnforcementEnabled { get; set; }
    public bool StarsSalesEnabled { get; set; }
    public bool CryptoSalesEnabled { get; set; }
    public string SupportContact { get; set; } = "";
    public string TermsUrl { get; set; } = "";
    public string NowPaymentsApiKey { get; set; } = "";
    public string NowPaymentsIpnSecret { get; set; } = "";
    public string CallbackUrl { get; set; } = "";
    public bool MerchantUsdtBscVerified { get; set; }
    public long[] OperatorUserIds { get; set; } = [];
    public string OperatorIds { get; set; } = "";
    public bool IsOperator(long userId) => OperatorUserIds.Contains(userId) || OperatorIds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(x => long.TryParse(x, out var id) && id == userId);
    public int ProjectionFreshnessSeconds { get; set; } = 60;
    public bool IsValid() => ProjectionFreshnessSeconds is >= 5 and <= 300
        && OperatorIds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).All(x => long.TryParse(x, out var id) && id > 0)
        && (!(StarsSalesEnabled || CryptoSalesEnabled) || !string.IsNullOrWhiteSpace(SupportContact)
            && Uri.TryCreate(TermsUrl, UriKind.Absolute, out var terms) && terms.Scheme == "https")
        && (!CryptoSalesEnabled || MerchantUsdtBscVerified && !string.IsNullOrWhiteSpace(NowPaymentsApiKey)
            && !string.IsNullOrWhiteSpace(NowPaymentsIpnSecret) && Uri.TryCreate(CallbackUrl, UriKind.Absolute, out var callback) && callback.Scheme == "https");
}
