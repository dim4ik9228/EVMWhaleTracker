using System.Diagnostics;
using Presentation.Telegram.Services;

namespace UnitTests.Presentation;

public sealed class TelegramSharedRateLimiterTests
{
    [Fact]
    public async Task DifferentSourcesShareOnePerChatAllowanceWhileOtherChatsProceed()
    {
        await using var limiter = new TelegramGlobalRateLimiter();
        await limiter.WaitAsync(1, default); // EVM dispatcher reserves this allowance.
        var elapsed = Stopwatch.StartNew();
        var core = limiter.WaitAsync(1, default);
        await limiter.WaitAsync(2, default);
        Assert.False(core.IsCompleted);
        await core;
        Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(900));
    }

    [Fact]
    public async Task CancelledChatWaitDoesNotHoldTheSharedChatGate()
    {
        await using var limiter = new TelegramGlobalRateLimiter();
        await limiter.WaitAsync(1, default);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => limiter.WaitAsync(1, cancelled.Token));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await limiter.WaitAsync(1, deadline.Token);
    }
}
