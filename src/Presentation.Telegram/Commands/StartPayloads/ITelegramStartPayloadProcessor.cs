using Presentation.Telegram.Commands;

namespace Presentation.Telegram.Commands.StartPayloads;

/// <summary>
/// Handles Telegram /start payloads, including notification edit deep links.
/// </summary>
public interface ITelegramStartPayloadProcessor
{
    /// <summary>
    /// Processes the argument text after /start for a Telegram user.
    /// </summary>
    Task<TelegramCommandResponse> ProcessAsync(
        long telegramUserId,
        string arguments,
        CancellationToken cancellationToken);
}
