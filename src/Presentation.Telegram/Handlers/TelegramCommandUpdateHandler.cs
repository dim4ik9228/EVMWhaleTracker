using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Presentation.Telegram.Commands;
using Presentation.Telegram.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Presentation.Telegram.Handlers;

public sealed class TelegramCommandUpdateHandler(
    IServiceScopeFactory serviceScopeFactory,
    TelegramGlobalRateLimiter rateLimiter,
    ILogger<TelegramCommandUpdateHandler> logger)
    : IUpdateHandler
{
    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(botClient);
        ArgumentNullException.ThrowIfNull(update);

        if (update.Message?.Text is not { } messageText)
        {
            return;
        }

        if (update.Message.From is null)
        {
            return;
        }

        using var scope = serviceScopeFactory.CreateScope();
        var commandProcessor = scope.ServiceProvider.GetRequiredService<ITelegramCommandProcessor>();

        TelegramCommandResponse? response;
        try
        {
            response = await commandProcessor.ProcessAsync(
                new TelegramCommandRequest(
                    TelegramUserId: update.Message.From.Id,
                    TelegramUsername: update.Message.From.Username,
                    MessageText: messageText,
                    IsPrivateChat: update.Message.Chat.Type == ChatType.Private),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process Telegram update {UpdateId}.", update.Id);
            return;
        }

        if (response is null)
        {
            return;
        }

        try
        {
            await rateLimiter.WaitAsync(cancellationToken);
            await botClient.SendMessage(
                chatId: update.Message.Chat.Id,
                text: response.Text,
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the host is shutting down.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send Telegram command response for update {UpdateId}.", update.Id);
        }
    }

    public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Telegram update handling failed at {Source}.", source);
        return Task.CompletedTask;
    }
}
