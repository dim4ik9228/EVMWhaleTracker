namespace Presentation.Telegram.Commands;

public interface ITelegramCommandProcessor
{
    Task<TelegramCommandResponse?> ProcessAsync(TelegramCommandRequest request, CancellationToken cancellationToken);
}
