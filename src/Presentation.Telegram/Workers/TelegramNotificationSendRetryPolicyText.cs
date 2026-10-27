namespace Presentation.Telegram.Workers;

/// <summary>Named presentation text and formatting templates for TelegramNotificationSendRetryPolicy.</summary>
internal static class TelegramNotificationSendRetryPolicyText
{
    public const string NotificationRateLimited = "Telegram rate limited notification for chat {ChatId}. Retrying after {RetryAfterDelay}.";
    public const string NotificationRejected = "Telegram API rejected notification for chat {ChatId} with code {ErrorCode}.";
}
