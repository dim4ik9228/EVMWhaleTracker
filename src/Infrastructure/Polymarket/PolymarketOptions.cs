namespace Infrastructure.Polymarket;

/// <summary>Separately bounded public protocol options; global off prevents all source HTTP and sends.</summary>
public sealed class PolymarketOptions
{
    public bool Enabled { get; set; }
    public string ProfileUrl { get; set; } = "https://gamma-api.polymarket.com/public-profile";
    public string ActivityUrl { get; set; } = "https://data-api.polymarket.com/v2/activity";
    public int TimeoutSeconds { get; set; } = 5;
    public int PollSeconds { get; set; } = 10;
    public int MaxUniqueWallets { get; set; } = 20;
    public int RequestsPerMinute { get; set; } = 120;
    public int HttpConcurrency { get; set; } = 2;
    public int PageSize { get; set; } = 100;
    public int MaximumPages { get; set; } = 10;
    public int MaximumResponseBytes { get; set; } = 2097152;
    public int OverlapMinutes { get; set; } = 5;
    public int ReconciliationMinutes { get; set; } = 60;
    public int RecoveryHours { get; set; } = 24;
    public int RetentionDays { get; set; } = 7;
    public int ProfileTtlHours { get; set; } = 24;
    public int MissingProfileTtlMinutes { get; set; } = 60;
    public int ProfileCacheCapacity { get; set; } = 512;
    public bool IsValidForStartup() => !Enabled || TimeoutSeconds is >= 1 and <= 30 && PollSeconds is >= 10 and <= 3600
        && MaxUniqueWallets is >= 1 and <= 1000 && RequestsPerMinute is >= 1 and <= 600
        && HttpConcurrency is >= 1 and <= 8 && PageSize is >= 1 and <= 1000 && MaximumPages is >= 1 and <= 100
        && MaximumResponseBytes is >= 1024 and <= 8388608 && OverlapMinutes is >= 1 and <= 60
        && RecoveryHours is >= 1 and <= 72 && ReconciliationMinutes is >= 10 and <= 1440
        && RetentionDays is >= 2 and <= 365 && RetentionDays * 24 > RecoveryHours + 24
        && ProfileTtlHours is >= 1 and <= 168 && MissingProfileTtlMinutes is >= 1 and <= 1440
        && ProfileCacheCapacity is >= 1 and <= 10000 && MaxUniqueWallets * 60 / PollSeconds <= RequestsPerMinute
        && SafeEndpoint(ProfileUrl) && SafeEndpoint(ActivityUrl);
    private static bool SafeEndpoint(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}
