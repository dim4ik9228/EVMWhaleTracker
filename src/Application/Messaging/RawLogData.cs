namespace Application.Messaging;

public sealed record RawLogData(
    string TransactionHash,
    string ContractAddress,
    IReadOnlyList<string> Topics,
    string Data);
