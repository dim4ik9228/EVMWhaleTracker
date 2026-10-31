using Application.Swaps;
using Domain.Common;
using Microsoft.Extensions.Options;

namespace Infrastructure.Swaps;

/// <summary>Bounded public-only singleflight. Recipient cancellation never cancels another recipient's fetch.</summary>
public sealed class SwapEvidenceCache(ITransactionExecutionEvidenceProvider provider, IOptions<SwapEnrichmentOptions> options)
{
    private readonly object _gate = new();
    private readonly Dictionary<ExecutionIdentity, Entry> _entries = [];
    private readonly Dictionary<ExecutionIdentity, TimeEntry> _blockTimes = [];
    public Task<Result<DateTimeOffset>> GetBlockTimeAsync(ExecutionIdentity identity, DateTimeOffset deadline, CancellationToken shutdown)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var key in _blockTimes.Where(x => x.Value.ExpiresAt <= now && x.Value.Task.IsCompleted).Select(x => x.Key).ToArray()) _blockTimes.Remove(key);
            if (_blockTimes.TryGetValue(identity, out var found)) return found.Task;
            if (_blockTimes.Count >= options.Value.CacheCapacity)
            {
                var evict = _blockTimes.Where(x => x.Value.Task.IsCompleted).OrderBy(x => x.Value.ExpiresAt).FirstOrDefault();
                if (evict.Value is null) return Task.FromResult(Result<DateTimeOffset>.Failure(SwapErrors.Limited));
                _blockTimes.Remove(evict.Key);
            }
            var task = FetchBlockTimeAsync(identity, deadline, shutdown);
            _blockTimes.Add(identity, new(task, now + options.Value.CacheTtl));
            return task;
        }
    }
    private async Task<Result<DateTimeOffset>> FetchBlockTimeAsync(ExecutionIdentity identity, DateTimeOffset deadline, CancellationToken shutdown)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        var remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining > TimeSpan.Zero) budget.CancelAfter(remaining); else budget.Cancel();
        try { return await provider.GetBlockTimeAsync(identity, budget.Token); }
        catch (OperationCanceledException) when (!shutdown.IsCancellationRequested)
        { return Result<DateTimeOffset>.Failure(SwapErrors.Unavailable); }
    }
    public Task<Result<TransactionExecutionEvidence>> GetAsync(ExecutionIdentity identity, DateTimeOffset deadline, CancellationToken shutdown)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var key in _entries.Where(x => x.Value.ExpiresAt <= now && x.Value.Task.IsCompleted).Select(x => x.Key).ToArray()) _entries.Remove(key);
            if (_entries.TryGetValue(identity, out var found)) return found.Task;
            if (_entries.Count >= options.Value.CacheCapacity)
            {
                var evict = _entries.Where(x => x.Value.Task.IsCompleted).OrderBy(x => x.Value.ExpiresAt).FirstOrDefault();
                if (evict.Value is null) return Task.FromResult(Result<TransactionExecutionEvidence>.Failure(SwapErrors.Limited));
                _entries.Remove(evict.Key);
            }
            var task = FetchAsync(identity, deadline, shutdown);
            _entries.Add(identity, new(task, now + options.Value.CacheTtl)); return task;
        }
    }
    private async Task<Result<TransactionExecutionEvidence>> FetchAsync(ExecutionIdentity identity, DateTimeOffset deadline, CancellationToken shutdown)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        var remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) return Result<TransactionExecutionEvidence>.Failure(SwapErrors.Unavailable);
        budget.CancelAfter(remaining);
        try
        {
            var result = await provider.GetAsync(identity, budget.Token);
            if (result.IsFailure && result.Error == SwapErrors.Unavailable)
            {
                await Task.Delay(100, budget.Token);
                result = await provider.GetAsync(identity, budget.Token);
            }
            return result;
        }
        catch (OperationCanceledException) when (!shutdown.IsCancellationRequested)
        { return Result<TransactionExecutionEvidence>.Failure(SwapErrors.Unavailable); }
    }
    private sealed record Entry(Task<Result<TransactionExecutionEvidence>> Task, DateTimeOffset ExpiresAt);
    private sealed record TimeEntry(Task<Result<DateTimeOffset>> Task, DateTimeOffset ExpiresAt);
}
