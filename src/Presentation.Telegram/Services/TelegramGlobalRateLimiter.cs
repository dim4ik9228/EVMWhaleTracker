namespace Presentation.Telegram.Services;

/// <summary>
/// Process-wide token bucket for Telegram's global message-rate limit.
/// </summary>
public sealed class TelegramGlobalRateLimiter : IAsyncDisposable
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, DateTimeOffset> _chatNext = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, SemaphoreSlim> _chats = new();

    /// <summary>Reserves the same per-chat and global allowance for every notification source.</summary>
    public async Task WaitAsync(long chatId, CancellationToken cancellationToken)
    {
        var gate = _chats.GetOrAdd(chatId, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_chatNext.TryGetValue(chatId, out var next))
            {
                var delay = next - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            }
            await WaitAsync(cancellationToken);
            _chatNext[chatId] = DateTimeOffset.UtcNow.AddSeconds(1);
        }
        finally { gate.Release(); }
    }
    private readonly int _permitsPerSecond;
    private readonly SemaphoreSlim _semaphore;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _refillLoop;

    public TelegramGlobalRateLimiter(int permitsPerSecond = 30)
    {
        if (permitsPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(permitsPerSecond));
        }

        _permitsPerSecond = permitsPerSecond;
        _semaphore = new SemaphoreSlim(permitsPerSecond, permitsPerSecond);
        _refillLoop = Task.Run(RefillLoopAsync);
    }

    public Task WaitAsync(CancellationToken cancellationToken) => _semaphore.WaitAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        _shutdown.Cancel();

        return new ValueTask(CleanupAsync());
    }

    private async Task CleanupAsync()
    {
        try
        {
            await _refillLoop;
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        finally
        {
            _shutdown.Dispose();
            _semaphore.Dispose();
        }
    }

    private async Task RefillLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (await timer.WaitForNextTickAsync(_shutdown.Token))
        {
            var missingPermits = _permitsPerSecond - _semaphore.CurrentCount;
            if (missingPermits > 0)
            {
                _semaphore.Release(missingPermits);
            }
        }
    }
}
