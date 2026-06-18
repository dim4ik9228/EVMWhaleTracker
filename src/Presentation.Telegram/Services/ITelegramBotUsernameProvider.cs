namespace Presentation.Telegram.Services;

/// <summary>
/// Resolves the Telegram bot username used to construct deep links back into the bot.
/// </summary>
public interface ITelegramBotUsernameProvider
{
    /// <summary>
    /// Returns the bot username from Telegram getMe, caching the result for subsequent calls.
    /// </summary>
    Task<string?> GetBotUsernameAsync(CancellationToken cancellationToken);
}
