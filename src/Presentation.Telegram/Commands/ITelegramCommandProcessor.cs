namespace Presentation.Telegram.Commands;

/// <summary>
/// Handles user-facing Telegram commands and converts them into application requests.
/// </summary>
public interface ITelegramCommandProcessor
{
    Task<TelegramCommandResponse?> ProcessAsync(TelegramCommandRequest request, CancellationToken cancellationToken);
}
