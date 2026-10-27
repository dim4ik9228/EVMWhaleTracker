using Presentation.Telegram.Commands;
namespace Presentation.Telegram.Handlers;

/// <summary>Named presentation text and formatting templates for TelegramCommandUpdateHandler.</summary>
internal static class TelegramCommandUpdateHandlerText
{
    public const string FailedToProcessTelegramUpdate = "Failed to process Telegram update {UpdateId}.";
    public const string CommandResponseSendFailed = "Failed to send Telegram command response for update {UpdateId}.";
    public const string FailedToProcessTelegramCallbackUpdate = "Failed to process Telegram callback update {UpdateId}.";
    public const string SomethingWentWrong = "Something went wrong.";
    public const string CallbackResponseApplyFailed = "Failed to apply Telegram callback response for update {UpdateId}.";
    public const string TelegramUpdateHandlingFailedAt = "Telegram update handling failed at {Source}.";
}
