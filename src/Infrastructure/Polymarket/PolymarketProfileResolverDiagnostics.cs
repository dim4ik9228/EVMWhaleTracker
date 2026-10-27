namespace Infrastructure.Polymarket;

/// <summary>Named diagnostic messages and structured log templates for PolymarketProfileResolver.</summary>
internal static class PolymarketProfileResolverDiagnostics
{
    public const string PublicProfileRequestBudgetExhausted = "Public profile request budget exhausted.";
    public const string ProfileResponseExceedsLimit = "Profile response exceeds limit.";
    public const string ExpectedPublicProfileObject = "Expected public profile object.";
    public const string MissingDocumentedProfileIdentityField = "Missing documented profile identity field.";
    public const string InvalidPublicProfileTradingWalletType = "Invalid public profile trading-wallet type.";
    public const string InvalidPublicProfileTradingWalletAddress = "Invalid public profile trading-wallet address.";
    public const string GammaPublicProfile = "Gamma public-profile";
}
