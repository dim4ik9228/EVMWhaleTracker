using Application.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Presentation.Telegram.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using System.Collections.Concurrent;

namespace Presentation.Telegram.Workers;

/// <summary>
/// Sends rendered notification payloads to Telegram while respecting global and per-chat rate limits.
/// </summary>
public sealed class NotificationDispatcherWorker : BackgroundService
{
    private readonly OnChainPipelineChannels _channels;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly TelegramGlobalRateLimiter _rateLimiter;
    private readonly ITelegramNotificationRenderer _notificationRenderer;
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _chatLimiters = new();
    private readonly ILogger<NotificationDispatcherWorker> _logger;
    public NotificationDispatcherWorker(
        OnChainPipelineChannels channels,
        ITelegramBotClient telegramBotClient,
        TelegramGlobalRateLimiter rateLimiter,
        ITelegramNotificationRenderer notificationRenderer,
        ILogger<NotificationDispatcherWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(telegramBotClient);
        ArgumentNullException.ThrowIfNull(rateLimiter);
        ArgumentNullException.ThrowIfNull(notificationRenderer);
        ArgumentNullException.ThrowIfNull(logger);

        _channels = channels;
        _telegramBotClient = telegramBotClient;
        _logger = logger;
        _rateLimiter = rateLimiter;
        _notificationRenderer = notificationRenderer;
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
                var renderedMessage = _notificationRenderer.Render(payload);
                await _telegramBotClient.SendMessage(
                    chatId: payload.TelegramUserId,
                    text: renderedMessage.Text,
                    parseMode: renderedMessage.ParseMode,
                    cancellationToken: stoppingToken);

                _logger.LogInformation(
                    "Sent Telegram notification to chat {ChatId} for wallet {WalletAddress} and transaction {TransactionHash}.",
                    payload.TelegramUserId,
                    payload.WalletAddress,
                    payload.TransactionHash);
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
