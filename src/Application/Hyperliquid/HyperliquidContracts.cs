using Domain.Common;

namespace Application.Hyperliquid;

/// <summary>HyperCore source identity is independent of EVM chain IDs and transaction envelopes.</summary>
public sealed record HyperliquidActivity(string Id, string Address, string Stream, long Time,
    string Action, string Market, string OrderId, decimal Size, decimal Price, decimal? Usd,
    decimal? ClosedPnl, bool LiquidatedVictim = false, string? LedgerFacts = null, string? TransactionHash = null, string? PositionSide = null);

/// <summary>Owner-specific activity data passed to the presentation renderer.</summary>
public sealed record HyperliquidNotification(Guid WalletId, string DisplayText, string Address, IReadOnlyList<HyperliquidActivity> Activities);

/// <summary>Owner-scoped enrollment and current source health.</summary>
public sealed record HyperliquidWalletStatus(bool Enabled, Guid Generation, string Status);

/// <summary>Owner-checked atomic admission port.</summary>
public interface IHyperliquidSettings
{
    Task<Result<HyperliquidWalletStatus>> GetAsync(long userId, Guid walletId, CancellationToken cancellationToken);
    Task<Result<HyperliquidWalletStatus>> SetAsync(long userId, Guid walletId, bool enabled, CancellationToken cancellationToken, Guid? expectedGeneration = null);
}

/// <summary>Replaceable public provider HTTP transport; no account credentials or trading operations.</summary>
public interface IHyperliquidInfoClient
{
    Task<string> QueryAsync(object request, int weight, CancellationToken cancellationToken);
}

/// <summary>Replaceable bounded websocket transport.</summary>
public interface IHyperliquidSocket : IAsyncDisposable
{
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);
    Task SendAsync(object message, CancellationToken cancellationToken);
    Task<string> ReceiveAsync(int maximumBytes, CancellationToken cancellationToken);
}

/// <summary>Creates a fresh public socket for each bounded reconnect attempt.</summary>
public interface IHyperliquidSocketFactory
{
    IHyperliquidSocket Create();
}

/// <summary>Source-independent rendered Telegram sender sharing global and per-chat budgets.</summary>
public interface IHyperliquidNotificationSender
{
    Task<bool> SendAsync(long userId, HyperliquidNotification notification, Func<CancellationToken, Task<bool>> stillEligible, CancellationToken cancellationToken);
}
