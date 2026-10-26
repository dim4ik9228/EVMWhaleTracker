using Domain.Common;

namespace Application.Polymarket;

/// <summary>Public provider identity remains separate from the owner's alias.</summary>
public sealed record PolymarketProfile(string ResolvedAddress, string? TradingAddress, string? Name,
    string? Pseudonym, bool DisplayUsernamePublic, DateTimeOffset LookedUpAt, string Provenance)
{
    public string? PublicName => DisplayUsernamePublic ? Name ?? Pseudonym : Pseudonym;
}

/// <summary>Absence and temporary failure are deliberately distinct lookup outcomes.</summary>
public sealed record PolymarketProfileLookup(string State, PolymarketProfile? Profile, bool Stale = false);

/// <summary>Bounded public-only lookup; never accesses keys or trading credentials.</summary>
public interface IPolymarketProfileResolver
{
    Task<PolymarketProfileLookup> ResolveAsync(string address, CancellationToken cancellationToken);
}

/// <summary>Owner-scoped binding, independent minimum, and observable coverage.</summary>
public sealed record PolymarketWalletStatus(bool Enabled, Guid Generation, string OriginalAddress,
    string? SourceAddress, string? ProposedAddress, decimal MinimumTradeValue, string Status,
    PolymarketProfile? Profile);

/// <summary>Explicit owner-confirmed account binding; mutations invalidate pending generation work.</summary>
public interface IPolymarketSettings
{
    Task<Result<PolymarketWalletStatus>> GetAsync(long userId, Guid walletId, CancellationToken cancellationToken);
    Task<Result<PolymarketWalletStatus>> BindAsync(long userId, Guid walletId, string address, Guid expectedGeneration, CancellationToken cancellationToken);
    Task<Result<PolymarketWalletStatus>> SetAsync(long userId, Guid walletId, bool enabled, Guid expectedGeneration, CancellationToken cancellationToken);
    Task<Result<PolymarketWalletStatus>> MinimumAsync(long userId, Guid walletId, decimal minimum, Guid expectedGeneration, CancellationToken cancellationToken);
}

/// <summary>Normalized individual activity; nullable values are unavailable, never invented zeroes.</summary>
public sealed record PolymarketActivity(string Id, string SourceAddress, DateTimeOffset Time, string Action,
    string MarketId, string? Title, string? Outcome, decimal? Shares, decimal? Price, decimal? CashValue,
    string QuoteCurrency, string? MarketSlug, string? TransactionHash, decimal? CollateralFee = null);

/// <summary>Recipient-specific presentation contract.</summary>
public sealed record PolymarketNotification(Guid WalletId, string Alias, PolymarketActivity Activity);

/// <summary>Telegram transport with eligibility evaluated immediately before each send attempt.</summary>
public interface IPolymarketNotificationSender
{
    Task<bool> SendAsync(long userId, PolymarketNotification notification,
        Func<CancellationToken, Task<bool>> stillEligible, CancellationToken cancellationToken);
}
