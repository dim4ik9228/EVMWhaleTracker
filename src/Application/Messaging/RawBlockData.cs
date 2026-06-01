namespace Application.Messaging;

public sealed record RawBlockData(
    ulong BlockNumber,
    string BlockHash,
    DateTimeOffset BlockTimestampUtc,
    IReadOnlyList<RawTransactionData> Transactions,
    IReadOnlyList<RawLogData> Logs);
