using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Presentation.Telegram.Commands;
using Presentation.Telegram.Commands.Callbacks;
using Presentation.Telegram.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Presentation.Telegram.Handlers;

/// <summary>
/// Bridges Telegram polling updates into scoped command processing and HTML command responses.
/// </summary>
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

        if (update.CallbackQuery is { } callbackQuery)
        {
            await HandleCallbackQueryAsync(botClient, update, callbackQuery, cancellationToken);
            return;
        }

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
            logger.LogError(ex, TelegramCommandUpdateHandlerText.FailedToProcessTelegramUpdate, update.Id);
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
                parseMode: response.ParseMode,
                replyMarkup: response.ReplyMarkup,
                cancellationToken: cancellationToken);

            if (response.DeleteSourceMessage)
            {
                await DeleteMessageBestEffortAsync(
                    botClient,
                    update.Message.Chat.Id,
                    update.Message.MessageId,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the host is shutting down.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, TelegramCommandUpdateHandlerText.CommandResponseSendFailed, update.Id);
        }
    }

    private async Task HandleCallbackQueryAsync(
        ITelegramBotClient botClient,
        Update update,
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        if (callbackQuery.Message is not { } message || callbackQuery.Data is null)
        {
            await AnswerCallbackBestEffortAsync(botClient, callbackQuery.Id, TelegramUiText.MenuUnavailable, true, cancellationToken);
            return;
        }

        using var scope = serviceScopeFactory.CreateScope();
        var callbackProcessor = scope.ServiceProvider.GetRequiredService<ITelegramCallbackProcessor>();

        TelegramCallbackResponse? response;
        try
        {
            response = await callbackProcessor.ProcessAsync(
                new TelegramCallbackRequest(
                    TelegramUserId: callbackQuery.From.Id,
                    TelegramUsername: callbackQuery.From.Username,
                    ChatId: message.Chat.Id,
                    MessageId: message.MessageId,
                    Data: callbackQuery.Data,
                    IsPrivateChat: message.Chat.Type == ChatType.Private),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, TelegramCommandUpdateHandlerText.FailedToProcessTelegramCallbackUpdate, update.Id);
            await AnswerCallbackBestEffortAsync(botClient, callbackQuery.Id, TelegramCommandUpdateHandlerText.SomethingWentWrong, true, cancellationToken);
            return;
        }

        if (response is null)
        {
            await AnswerCallbackBestEffortAsync(botClient, callbackQuery.Id, TelegramUiText.MenuUnavailable, true, cancellationToken);
            return;
        }

        try
        {
            await rateLimiter.WaitAsync(cancellationToken);

            switch (response.MessageAction)
            {
                case TelegramCallbackMessageAction.EditMessage when response.Text is not null:
                    await botClient.EditMessageText(
                        chatId: message.Chat.Id,
                        messageId: message.MessageId,
                        text: response.Text,
                        parseMode: response.ParseMode,
                        replyMarkup: response.ReplyMarkup,
                        cancellationToken: cancellationToken);
                    break;
                case TelegramCallbackMessageAction.DeleteMessage:
                    await DeleteMessageBestEffortAsync(
                        botClient,
                        message.Chat.Id,
                        message.MessageId,
                        cancellationToken);
                    break;
            }

            await AnswerCallbackBestEffortAsync(
                botClient,
                callbackQuery.Id,
                response.AnswerText,
                response.ShowAlert,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the host is shutting down.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, TelegramCommandUpdateHandlerText.CallbackResponseApplyFailed, update.Id);
            await AnswerCallbackBestEffortAsync(botClient, callbackQuery.Id, TelegramCommandUpdateHandlerText.SomethingWentWrong, true, cancellationToken);
        }
    }

    private static async Task DeleteMessageBestEffortAsync(
        ITelegramBotClient botClient,
        ChatId chatId,
        int messageId,
        CancellationToken cancellationToken)
    {
        try
        {
            await botClient.DeleteMessage(chatId, messageId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Telegram may deny deleting older or user-authored messages; the menu should still work.
        }
    }

    private static async Task AnswerCallbackBestEffortAsync(
        ITelegramBotClient botClient,
        string callbackQueryId,
        string? text,
        bool showAlert,
        CancellationToken cancellationToken)
    {
        try
        {
            await botClient.AnswerCallbackQuery(
                callbackQueryId,
                text,
                showAlert: showAlert,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Best-effort spinner cleanup.
        }
    }

    public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
    {
        logger.LogError(exception, TelegramCommandUpdateHandlerText.TelegramUpdateHandlingFailedAt, source);
        return Task.CompletedTask;
    }
}
