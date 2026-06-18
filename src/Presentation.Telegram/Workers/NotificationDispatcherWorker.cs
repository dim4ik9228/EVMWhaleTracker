using Application.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Presentation.Telegram.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
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
    private readonly ITelegramNotificationEnricher _notificationEnricher;
    private readonly ITelegramNotificationRenderer _notificationRenderer;
    private readonly TelegramNotificationSendRetryPolicy _sendRetryPolicy = new();
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _chatLimiters = new();
    private readonly ILogger<NotificationDispatcherWorker> _logger;
    private string? _resolvedBotUsername;
    private bool _botUsernameResolutionAttempted;

    public NotificationDispatcherWorker(
        OnChainPipelineChannels channels,
        ITelegramBotClient telegramBotClient,
        TelegramGlobalRateLimiter rateLimiter,
        ITelegramNotificationEnricher notificationEnricher,
        ITelegramNotificationRenderer notificationRenderer,
        ILogger<NotificationDispatcherWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(telegramBotClient);
        ArgumentNullException.ThrowIfNull(rateLimiter);
        ArgumentNullException.ThrowIfNull(notificationEnricher);
        ArgumentNullException.ThrowIfNull(notificationRenderer);
        ArgumentNullException.ThrowIfNull(logger);

        _channels = channels;
        _telegramBotClient = telegramBotClient;
        _logger = logger;
        _rateLimiter = rateLimiter;
        _notificationEnricher = notificationEnricher;
        _notificationRenderer = notificationRenderer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var payload in _channels.AggregatedNotificationPayloadChannel.Reader.ReadAllAsync(stoppingToken))
        {
            if (payload.TelegramUserId <= 0)
            {
                _logger.LogWarning("Skipping notification with invalid Telegram user id.");
                continue;
            }

            var chatLimiter = _chatLimiters.GetOrAdd(payload.TelegramUserId, _ => new SemaphoreSlim(1, 1));
            var chatLimiterAcquired = false;

            try
            {
                await chatLimiter.WaitAsync(stoppingToken);
                chatLimiterAcquired = true;
                var enrichedPayload = await _notificationEnricher.EnrichAsync(payload, stoppingToken);
                var botUsername = await ResolveBotUsernameAsync(stoppingToken);
                var renderedMessage = _notificationRenderer.Render(enrichedPayload, botUsername);
                var sent = await SendWithRetryAsync(payload, renderedMessage, stoppingToken);
                if (sent)
                {
                    _logger.LogInformation(
                        "Sent Telegram notification to chat {ChatId} for wallet {TrackedWalletAddress} and transaction {TransactionHash}.",
                        payload.TelegramUserId,
                        payload.TrackedWalletAddress,
                        payload.TransactionHash);
                }
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
                if (chatLimiterAcquired)
                {
                    _ = ReleaseChatLimiterAfterDelayAsync(chatLimiter, stoppingToken);
                }
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
    }

    private async Task<string?> ResolveBotUsernameAsync(CancellationToken cancellationToken)
    {
        if (_botUsernameResolutionAttempted)
        {
            return _resolvedBotUsername;
        }

        try
        {
            var botUser = await _telegramBotClient.GetMe(cancellationToken);
            _resolvedBotUsername = botUser.Username;
            if (string.IsNullOrWhiteSpace(_resolvedBotUsername))
            {
                _logger.LogWarning("Telegram getMe did not return a bot username; notification edit links will be omitted.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve Telegram bot username via getMe; notification edit links will be omitted.");
        }
        finally
        {
            _botUsernameResolutionAttempted = true;
        }

        return _resolvedBotUsername;
    }

    private Task<bool> SendWithRetryAsync(
        AggregatedNotificationPayload payload,
        TelegramRenderedMessage renderedMessage,
        CancellationToken cancellationToken)
        => _sendRetryPolicy.SendAsync(
            payload.TelegramUserId,
            waitForPermitAsync: ct => _rateLimiter.WaitAsync(ct),
            sendAsync: async ct =>
            {
                await _telegramBotClient.SendMessage(
                    chatId: payload.TelegramUserId,
                    text: renderedMessage.Text,
                    parseMode: renderedMessage.ParseMode,
                    linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                    cancellationToken: ct);
            },
            _logger,
            cancellationToken);

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
