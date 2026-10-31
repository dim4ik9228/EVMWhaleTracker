namespace Presentation.Telegram.Workers;

/// <summary>Named presentation text and formatting templates for TelegramUpdateReceiverWorker.</summary>
internal static class TelegramUpdateReceiverWorkerText
{
    public const string BotTokenRequired = "TelegramBot:BotToken must be configured.";
    public const string TelegramUpdateReceiverStarted = "Telegram update receiver started.";
    public const string SandboxFinancialUpdatePending = "Sandbox polling halted at an unexpected real payment or refund; restore normal runtime to process the pending Telegram batch.";
}
