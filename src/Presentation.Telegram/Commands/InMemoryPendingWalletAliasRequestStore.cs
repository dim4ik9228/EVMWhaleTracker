using System.Collections.Concurrent;

namespace Presentation.Telegram.Commands;

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
