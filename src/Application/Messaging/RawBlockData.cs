using Domain.Common;

namespace Application.Messaging;

/// <summary>
/// Raw on-chain activity batch sent through the in-memory processing pipeline.
/// </summary>
public sealed record RawBlockData(
    SupportedChain Chain,
    ulong BlockNumber,
    string BlockHash,
    DateTimeOffset BlockTimestampUtc,
    IReadOnlyList<RawTransactionData> Transactions,
    IReadOnlyList<RawLogData> Logs);
