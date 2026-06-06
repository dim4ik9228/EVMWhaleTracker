using Application.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Presentation.Telegram.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;
using System.Collections.Concurrent;

namespace Presentation.Telegram.Workers;

public sealed class NotificationDispatcherWorker : BackgroundService
{
    private readonly OnChainPipelineChannels _channels;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly TelegramGlobalRateLimiter _rateLimiter;
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _chatLimiters = new();
    private readonly ILogger<NotificationDispatcherWorker> _logger;
    public NotificationDispatcherWorker(
        OnChainPipelineChannels channels,
        ITelegramBotClient telegramBotClient,
        TelegramGlobalRateLimiter rateLimiter,
        ILogger<NotificationDispatcherWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(telegramBotClient);
        ArgumentNullException.ThrowIfNull(rateLimiter);
        ArgumentNullException.ThrowIfNull(logger);

        _channels = channels;
        _telegramBotClient = telegramBotClient;
        _logger = logger;
        _rateLimiter = rateLimiter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var payload in _channels.NotificationPayloadChannel.Reader.ReadAllAsync(stoppingToken))
        {
            if (payload.TelegramUserId <= 0)
            {
                _logger.LogWarning("Skipping notification with invalid Telegram user id.");
                continue;
            }

            var chatLimiter = _chatLimiters.GetOrAdd(payload.TelegramUserId, _ => new SemaphoreSlim(1, 1));

            try
            {
                await chatLimiter.WaitAsync(stoppingToken);
                await _rateLimiter.WaitAsync(stoppingToken);
                await _telegramBotClient.SendMessage(
                    chatId: payload.TelegramUserId,
                    text: payload.MarkdownMessage,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: stoppingToken);
            }
            catch (ApiRequestException ex)
            {
                _logger.LogWarning(ex, "Telegram API rejected notification for chat {ChatId} with code {ErrorCode}.", payload.TelegramUserId, ex.ErrorCode);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected failure while sending notification for chat {ChatId}.", payload.TelegramUserId);
            }
            finally
            {
                _ = ReleaseChatLimiterAfterDelayAsync(chatLimiter, stoppingToken);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
    }

    private static async Task ReleaseChatLimiterAfterDelayAsync(SemaphoreSlim chatLimiter, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            chatLimiter.Release();
        }
        catch (OperationCanceledException)
        {
            // Expected when the host is shutting down.
        }
    }
}
