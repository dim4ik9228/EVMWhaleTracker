using System.Collections.Concurrent;

namespace Presentation.Telegram.Commands;

/// <summary>
/// Volatile per-user store for the two-step /add wallet alias flow.
/// </summary>
public sealed class InMemoryPendingWalletAliasRequestStore : IPendingWalletAliasRequestStore
{
    private readonly ConcurrentDictionary<long, PendingWalletAliasRequest> _requests = new();

    public void Set(long telegramUserId, PendingWalletAliasRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _requests[telegramUserId] = request;
    }

    public bool TryGet(long telegramUserId, out PendingWalletAliasRequest request)
        => _requests.TryGetValue(telegramUserId, out request!);

    public bool Clear(long telegramUserId)
        => _requests.TryRemove(telegramUserId, out _);
}
