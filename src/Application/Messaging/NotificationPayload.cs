namespace Application.Messaging;

public sealed record NotificationPayload(
    long TelegramUserId,
    Domain.Common.SupportedChain Chain,
    NotificationOperation Operation,
    string TrackedWalletAddress,
    string? TrackedWalletAlias,
    string? CounterpartyAddress,
    string? CounterpartyAlias,
    string TransactionHash,
    string AssetSymbol,
    string? AssetName,
    string? TokenContractAddress,
    decimal Amount,
    decimal? UsdValue,
    ulong BlockNumber,
    DateTimeOffset BlockTimestampUtc)
{
    public string WalletAddress => TrackedWalletAddress;
}

public enum NotificationOperation
{
    Received,
    Sent,
    SelfTransfer
}
