namespace Application.Messaging;

public sealed record NotificationPayload(
    long TelegramUserId,
    string MarkdownMessage,
    string? WalletAddress = null,
    string? TransactionHash = null);
