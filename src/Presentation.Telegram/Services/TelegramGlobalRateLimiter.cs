namespace Presentation.Telegram.Services;

public sealed class TelegramGlobalRateLimiter : IAsyncDisposable
{
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
            await _refillLoop.ConfigureAwait(false);
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

        while (await timer.WaitForNextTickAsync(_shutdown.Token).ConfigureAwait(false))
        {
            var missingPermits = _permitsPerSecond - _semaphore.CurrentCount;
            if (missingPermits > 0)
            {
                _semaphore.Release(missingPermits);
            }
        }
    }
}
