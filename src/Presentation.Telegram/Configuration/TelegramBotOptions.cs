namespace Presentation.Telegram.Configuration;

/// <summary>
/// Telegram bot configuration loaded from app settings, user secrets, or environment variables.
/// </summary>
public sealed class TelegramBotOptions
{
    public string BotToken { get; init; } = string.Empty;
}
