namespace Presentation.Telegram.Commands.Callbacks;

/// <summary>
/// Processes Telegram callback queries emitted by inline tracked-wallet settings menus.
/// </summary>
public interface ITelegramCallbackProcessor
{
    /// <summary>
    /// Converts a callback request into a message edit/delete action and callback query answer.
    /// </summary>
    Task<TelegramCallbackResponse?> ProcessAsync(TelegramCallbackRequest request, CancellationToken cancellationToken);
}
