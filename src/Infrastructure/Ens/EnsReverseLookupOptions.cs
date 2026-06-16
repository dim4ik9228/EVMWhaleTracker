namespace Infrastructure.Ens;

public sealed class EnsReverseLookupOptions
{
    public bool Enabled { get; init; } = true;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan SuccessCacheDuration { get; init; } = TimeSpan.FromDays(7);

    public TimeSpan MissCacheDuration { get; init; } = TimeSpan.FromDays(1);

    public TimeSpan FailureCacheDuration { get; init; } = TimeSpan.FromMinutes(10);
}
