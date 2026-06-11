using Domain.Common;

namespace Application.Messaging;

public sealed record RawBlockData(
    SupportedChain Chain,
    ulong BlockNumber,
    string BlockHash,
    DateTimeOffset BlockTimestampUtc,
    IReadOnlyList<RawTransactionData> Transactions,
    IReadOnlyList<RawLogData> Logs);
