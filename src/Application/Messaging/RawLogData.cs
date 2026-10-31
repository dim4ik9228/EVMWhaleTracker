namespace Application.Messaging;

/// <summary>
/// Raw smart-contract log data. ERC-20 transfers use indexed address topics and a uint256 amount in <see cref="Data"/>.
/// </summary>
public sealed record RawLogData(
    string TransactionHash,
    string ContractAddress,
    IReadOnlyList<string> Topics,
    string Data,
    ulong? LogIndex = null,
    bool Removed = false,
    string? BlockHash = null,
    DateTimeOffset? ObservedAtUtc = null);
