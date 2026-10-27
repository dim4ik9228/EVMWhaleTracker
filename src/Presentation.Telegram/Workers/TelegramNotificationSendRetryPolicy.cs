using Microsoft.Extensions.Logging;
using Telegram.Bot.Exceptions;

namespace Presentation.Telegram.Workers;

internal sealed class TelegramNotificationSendRetryPolicy
{
    private const int MaxTelegramSendAttempts = 2;

    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    public TelegramNotificationSendRetryPolicy()
        : this(Task.Delay)
    {
    }

    internal TelegramNotificationSendRetryPolicy(Func<TimeSpan, CancellationToken, Task> delayAsync)
    {
        ArgumentNullException.ThrowIfNull(delayAsync);

        _delayAsync = delayAsync;
    }

    public async Task<bool> SendAsync(
        long chatId,
        Func<CancellationToken, Task> waitForPermitAsync,
        Func<CancellationToken, Task> sendAsync,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(waitForPermitAsync);
        ArgumentNullException.ThrowIfNull(sendAsync);
        ArgumentNullException.ThrowIfNull(logger);

        for (var attempt = 1; attempt <= MaxTelegramSendAttempts; attempt++)
        {
            try
            {
                await waitForPermitAsync(cancellationToken);
                await sendAsync(cancellationToken);

                return true;
            }
            catch (ApiRequestException ex) when (TryGetRetryAfterDelay(ex, out var retryAfterDelay) && attempt < MaxTelegramSendAttempts)
            {
                logger.LogWarning(
                    ex,
                    TelegramNotificationSendRetryPolicyText.NotificationRateLimited,
                    chatId,
                    retryAfterDelay);

                await _delayAsync(retryAfterDelay, cancellationToken);
            }
            catch (ApiRequestException ex)
            {
                logger.LogWarning(
                    ex,
                    TelegramNotificationSendRetryPolicyText.NotificationRejected,
                    chatId,
                    ex.ErrorCode);
                return false;
            }
        }

        return false;
    }

    private static bool TryGetRetryAfterDelay(ApiRequestException exception, out TimeSpan delay)
    {
        var retryAfterSeconds = exception.Parameters?.RetryAfter;
        if (retryAfterSeconds is > 0)
        {
            delay = TimeSpan.FromSeconds(retryAfterSeconds.Value);
            return true;
        }

        delay = TimeSpan.Zero;
        return false;
    }
}
