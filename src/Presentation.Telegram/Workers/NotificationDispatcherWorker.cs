using Application.Billing;
using Application.Messaging;
using Application.Swaps;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Presentation.Telegram.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using System.Collections.Concurrent;

namespace Presentation.Telegram.Workers;

/// <summary>
/// Sends rendered notification payloads to Telegram while respecting global and per-chat rate limits.
/// </summary>
public sealed class NotificationDispatcherWorker : BackgroundService
{
    /// <summary>Dedicated no-hidden-retry client; other Telegram flows preserve their existing transport.</summary>
    public const string ClientKey = "EvmNotificationDelivery";
    private readonly IEntitlementPolicy? _entitlements;
    private readonly IRecipientAccess? _recipientAccess;
    private readonly ISwapDeliveryGuard? _swapGuard;
    private readonly OnChainPipelineChannels _channels;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly TelegramGlobalRateLimiter _rateLimiter;
    private readonly ITelegramNotificationEnricher _notificationEnricher;
    private readonly ITelegramNotificationRenderer _notificationRenderer;
    private readonly ITelegramBotUsernameProvider _botUsernameProvider;
    private readonly TelegramNotificationSendRetryPolicy _sendRetryPolicy = new();
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _chatLimiters = new();
    private readonly ILogger<NotificationDispatcherWorker> _logger;

    public NotificationDispatcherWorker(
        OnChainPipelineChannels channels,
        [Microsoft.Extensions.DependencyInjection.FromKeyedServices(ClientKey)] ITelegramBotClient telegramBotClient,
        TelegramGlobalRateLimiter rateLimiter,
        ITelegramNotificationEnricher notificationEnricher,
        ITelegramNotificationRenderer notificationRenderer,
        ITelegramBotUsernameProvider botUsernameProvider,
        ILogger<NotificationDispatcherWorker> logger, IEntitlementPolicy? entitlements = null, IRecipientAccess? recipientAccess = null, ISwapDeliveryGuard? swapGuard = null)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(telegramBotClient);
        ArgumentNullException.ThrowIfNull(rateLimiter);
        ArgumentNullException.ThrowIfNull(notificationEnricher);
        ArgumentNullException.ThrowIfNull(notificationRenderer);
        ArgumentNullException.ThrowIfNull(botUsernameProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _entitlements = entitlements;
        _recipientAccess = recipientAccess;
        _swapGuard = swapGuard;
        _channels = channels;
        _telegramBotClient = telegramBotClient;
        _logger = logger;
        _rateLimiter = rateLimiter;
        _notificationEnricher = notificationEnricher;
        _notificationRenderer = notificationRenderer;
        _botUsernameProvider = botUsernameProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var payload in _channels.AggregatedNotificationPayloadChannel.Reader.ReadAllAsync(stoppingToken))
        {
            if (payload.TelegramUserId <= 0)
            {
                _logger.LogWarning(NotificationDispatcherWorkerText.InvalidRecipient);
                continue;
            }

            var chatLimiter = _chatLimiters.GetOrAdd(payload.TelegramUserId, _ => new SemaphoreSlim(1, 1));
            var chatLimiterAcquired = false;

            try
            {
                await chatLimiter.WaitAsync(stoppingToken);
                chatLimiterAcquired = true;
                var enrichedPayload = await _notificationEnricher.EnrichAsync(payload, stoppingToken);
                var botUsername = await _botUsernameProvider.GetBotUsernameAsync(stoppingToken);
                var renderedMessage = _notificationRenderer.Render(enrichedPayload, botUsername);
                var sent = await SendWithRetryAsync(payload, renderedMessage, stoppingToken);
                if (payload.SwapDecisionId is not null && _swapGuard is not null && sent.HasValue)
                    await _swapGuard.CompleteAsync(payload, sent.Value, stoppingToken);
                if (sent == true)
                {
                    _logger.LogInformation(
                        NotificationDispatcherWorkerText.NotificationSent,
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
                _logger.LogError(ex, NotificationDispatcherWorkerText.NotificationSendFailed, payload.TelegramUserId);
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

    private async Task<bool?> SendWithRetryAsync(
        AggregatedNotificationPayload payload,
        TelegramRenderedMessage renderedMessage,
        CancellationToken cancellationToken)
    {
        var delivered = false;
        var permanentlyRejected = false;
        var completed = await _sendRetryPolicy.SendAsync(
            payload.TelegramUserId,
            waitForPermitAsync: ct => _rateLimiter.WaitAsync(payload.TelegramUserId, ct),
            sendAsync: async ct =>
            {
                if (payload.SwapDecisionId is not null && _swapGuard is null) return;
                if (_swapGuard is not null && !await _swapGuard.AllowsAsync(payload, ct)) return;
                if (_recipientAccess is not null && !await _recipientAccess.AllowsAsync(payload.TelegramUserId, payload.TrackedWalletId, TrackingMode.Evm, payload.BlockTimestampUtc, payload.EntitlementRevision, ct)) return;
                if (_entitlements?.EnforcementEnabled == true && (!_entitlements.Allows(payload.TrackedWalletId, TrackingMode.Evm, payload.BlockTimestampUtc, payload.EntitlementRevision)
                    || payload.EntitlementValidUntil is { } end && DateTimeOffset.UtcNow >= end)) return;
                try
                {
                    await _telegramBotClient.SendMessage(
                        chatId: payload.TelegramUserId,
                        text: renderedMessage.Text,
                        parseMode: renderedMessage.ParseMode,
                        linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                        cancellationToken: ct);
                    delivered = true;
                }
                catch (ApiRequestException ex) when (ex.ErrorCode is 400 or 403)
                {
                    // Invalid messages and blocked/deleted recipients cannot be repaired by
                    // replaying this outbox item. Authentication/rate/server failures can.
                    permanentlyRejected = true;
                    throw;
                }
            },
            _logger,
            cancellationToken);
        return completed ? delivered : permanentlyRejected ? false : null;
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
