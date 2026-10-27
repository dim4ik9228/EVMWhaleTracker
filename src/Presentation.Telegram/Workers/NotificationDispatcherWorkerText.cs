namespace Presentation.Telegram.Workers;

/// <summary>Named presentation text and formatting templates for NotificationDispatcherWorker.</summary>
internal static class NotificationDispatcherWorkerText
{
    public const string InvalidRecipient = "Skipping notification with invalid Telegram user id.";
    public const string NotificationSent = "Sent Telegram notification to chat {ChatId} for wallet {TrackedWalletAddress} and transaction {TransactionHash}.";
    public const string NotificationSendFailed = "Unexpected failure while sending notification for chat {ChatId}.";
}
