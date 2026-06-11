using Microsoft.Extensions.Logging.Abstractions;
using Presentation.Telegram.Workers;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace UnitTests.Presentation;

public sealed class TelegramNotificationSendRetryPolicyTests
{
    [Fact]
    public async Task SendAsync_RetriesOnceAfterRetryAfter_AndReturnsSuccess()
    {
        var delays = new List<TimeSpan>();
        var policy = new TelegramNotificationSendRetryPolicy((delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });

        var permitCount = 0;
        var sendCount = 0;

        var sent = await policy.SendAsync(
            chatId: 7777,
            waitForPermitAsync: _ =>
            {
                permitCount++;
                return Task.CompletedTask;
            },
            sendAsync: _ =>
            {
                sendCount++;
                if (sendCount == 1)
                {
                    throw new ApiRequestException(
                        "Too Many Requests",
                        429,
                        new ResponseParameters { RetryAfter = 3 });
                }

                return Task.CompletedTask;
            },
            NullLogger.Instance,
            CancellationToken.None);

        Assert.True(sent);
        Assert.Equal(2, permitCount);
        Assert.Equal(2, sendCount);
        Assert.Equal([TimeSpan.FromSeconds(3)], delays);
    }

    [Fact]
    public async Task SendAsync_DoesNotRetryApiRequestExceptionWithoutRetryAfter()
    {
        var delays = new List<TimeSpan>();
        var policy = new TelegramNotificationSendRetryPolicy((delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });

        var sendCount = 0;

        var sent = await policy.SendAsync(
            chatId: 7777,
            waitForPermitAsync: _ => Task.CompletedTask,
            sendAsync: _ =>
            {
                sendCount++;
                throw new ApiRequestException("Bad Request: can't parse entities", 400);
            },
            NullLogger.Instance,
            CancellationToken.None);

        Assert.False(sent);
        Assert.Equal(1, sendCount);
        Assert.Empty(delays);
    }
}
