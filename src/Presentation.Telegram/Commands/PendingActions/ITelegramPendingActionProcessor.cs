using Presentation.Telegram.Commands;

namespace Presentation.Telegram.Commands.PendingActions;

/// <summary>
/// Consumes private text messages that answer an earlier Telegram prompt.
/// </summary>
public interface ITelegramPendingActionProcessor
{
    /// <summary>
    /// Processes a pending action using the user's next private text message.
    /// </summary>
    Task<TelegramCommandResponse> ProcessAsync(
        long telegramUserId,
        PendingTelegramAction pendingAction,
        string messageText,
        CancellationToken cancellationToken);
}
