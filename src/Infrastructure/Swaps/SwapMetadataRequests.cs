using Application.Abstractions;
using Domain.Common;

namespace Infrastructure.Swaps;

/// <summary>
/// Coalesces public metadata lookups and bounds actual provider work, including adapters
/// that ignore cancellation. Abandoning a caller never releases an unfinished lookup's slot.
/// </summary>
public sealed class SwapMetadataRequests(ITokenMetadataProvider provider, int capacity)
{
    private readonly object _gate = new();
    private readonly Dictionary<(SupportedChain Chain, string Address), Task<Result<TokenMetadata>>> _pending = [];

    public Task<Result<TokenMetadata>> GetAsync(SupportedChain chain, string address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (chain, address.ToLowerInvariant());
        Task<Result<TokenMetadata>> request;
        lock (_gate)
        {
            if (!_pending.TryGetValue(key, out request!))
            {
                if (_pending.Count >= capacity) return Task.FromResult(Result<TokenMetadata>.Failure(TokenMetadataErrors.Unavailable));
                var completion = new TaskCompletionSource<Result<TokenMetadata>>(TaskCreationOptions.RunContinuationsAsynchronously);
                request = completion.Task;
                _pending.Add(key, request);
                _ = ResolveAsync(key, completion);
            }
        }
        return request.WaitAsync(cancellationToken);
    }

    private async Task ResolveAsync((SupportedChain Chain, string Address) key, TaskCompletionSource<Result<TokenMetadata>> completion)
    {
        Result<TokenMetadata> result;
        try
        {
            // Recipient deadlines cannot cancel a shared lookup. The bounded slot stays
            // held until the adapter really finishes; saturated admission fails immediately.
            result = await provider.GetAsync(key.Chain, key.Address, CancellationToken.None);
        }
        catch (Exception) { result = Result<TokenMetadata>.Failure(TokenMetadataErrors.Unavailable); }
        lock (_gate) _pending.Remove(key);
        completion.TrySetResult(result);
    }
}
