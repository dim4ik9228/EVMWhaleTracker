using System.Numerics;
using Application.Messaging;
using Domain.Common;

namespace Application.Swaps;

/// <summary>Public, block-version-specific execution identity; never contains recipient policy.</summary>
public sealed record ExecutionIdentity(SupportedChain Chain, string TransactionHash, string BlockHash);
/// <summary>A complete canonical transaction and successful execution tree.</summary>
public sealed record TransactionExecutionEvidence(ExecutionIdentity Identity, string Sender, string Destination,
    BigInteger Value, ulong BlockNumber, DateTimeOffset BlockTime, IReadOnlyList<RawLogData> Logs,
    IReadOnlyList<ExecutionCall> Calls, IReadOnlyList<VerifiedPool> Pools);
/// <summary>Parity trace frame. Failed ancestors invalidate all descendants.</summary>
public sealed record ExecutionCall(int[] Path, string Type, string From, string To, BigInteger Value,
    string Input, bool Failed);
/// <summary>Pool identity verified by the pinned factory at the evidence block.</summary>
public sealed record VerifiedPool(string Address, string Token0, string Token1, int Fee);
/// <summary>Raw wallet accounting result, independent of decimals and user thresholds.</summary>
public sealed record RawSwap(string InputAsset, BigInteger InputAmount, string OutputAsset, BigInteger OutputAmount);
/// <summary>Display boundary for a verified two-asset swap. Null contract denotes native ETH.</summary>
public sealed record SwapAsset(string? ContractAddress, string Symbol, decimal Amount);
/// <summary>Typed two-sided notification; USD is only the input-side quote.</summary>
public sealed record VerifiedSwap(SwapAsset Input, SwapAsset Output, decimal? InputUsdValue, string Protocol = "Uniswap V3");
/// <summary>Targeted execution enrichment only; token detection remains websocket subscriptions.</summary>
public interface ITransactionExecutionEvidenceProvider
{
    Task<Result<TransactionExecutionEvidence>> GetAsync(ExecutionIdentity identity, CancellationToken cancellationToken);
    /// <summary>Canonical inclusion time for honest fallback and enrollment checks, independent of trace entitlement.</summary>
    Task<Result<DateTimeOffset>> GetBlockTimeAsync(ExecutionIdentity identity, CancellationToken cancellationToken);
}
/// <summary>Pre-filter durable intake contract. True means the ordinary path must not also emit.</summary>
public interface ISwapCandidateIntake
{
    bool IsEnabled(SupportedChain chain);
    Task ObserveAsync(RawLogData log, RawBlockData block, CancellationToken cancellationToken);
}
/// <summary>Send-time durable ownership and settings validation, and delivery acknowledgement.</summary>
public interface ISwapDeliveryGuard
{
    Task<bool> AllowsAsync(AggregatedNotificationPayload payload, CancellationToken cancellationToken);
    Task CompleteAsync(AggregatedNotificationPayload payload, bool sent, CancellationToken cancellationToken);
}
/// <summary>Expected execution evidence failures owned by the Application port.</summary>
public static class SwapErrors
{
    public static readonly Error Unavailable = new("Swap.Unavailable", "Execution evidence is unavailable.");
    public static readonly Error Unsupported = new("Swap.Unsupported", "Execution evidence method is unsupported.");
    public static readonly Error Inconsistent = new("Swap.Inconsistent", "Execution evidence is inconsistent or incomplete.");
    public static readonly Error NonCanonical = new("Swap.NonCanonical", "The triggering block is no longer canonical.");
    public static readonly Error Limited = new("Swap.Limited", "Execution evidence exceeds the provider budget.");
    public static readonly Error NotSwap = new("Swap.NotVerified", "Execution does not prove a supported swap.");
}
