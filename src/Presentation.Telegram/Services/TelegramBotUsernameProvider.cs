using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace Presentation.Telegram.Services;

/// <summary>
/// Caches the bot username reported by Telegram for the configured bot token.
/// </summary>
public sealed class TelegramBotUsernameProvider(
    ITelegramBotClient telegramBotClient,
    ILogger<TelegramBotUsernameProvider> logger)
    : ITelegramBotUsernameProvider
{
    private string? _botUsername;
    private bool _resolutionAttempted;

    /// <inheritdoc />
    public async Task<string?> GetBotUsernameAsync(CancellationToken cancellationToken)
    {
        if (_resolutionAttempted)
        {
            return _botUsername;
        }

        try
        {
            var botUser = await telegramBotClient.GetMe(cancellationToken);
            _botUsername = botUser.Username;
            if (string.IsNullOrWhiteSpace(_botUsername))
            {
                logger.LogWarning("Telegram getMe did not return a bot username; edit deep links will be omitted.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not resolve Telegram bot username via getMe; edit deep links will be omitted.");
        }
        finally
        {
            _resolutionAttempted = true;
        }

        return _botUsername;
    }
}
