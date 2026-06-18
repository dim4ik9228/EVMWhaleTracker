using System.Collections.Concurrent;

namespace Presentation.Telegram.Commands.PendingActions;

/// <summary>
/// Volatile per-user store for text input expected after a Telegram prompt.
/// </summary>
public sealed class InMemoryPendingTelegramActionStore : IPendingTelegramActionStore
{
    private readonly ConcurrentDictionary<long, PendingTelegramAction> _actions = new();

    /// <summary>
    /// Saves or replaces the pending text action for a Telegram user.
    /// </summary>
    public void Set(long telegramUserId, PendingTelegramAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _actions[telegramUserId] = action;
    }

    /// <summary>
    /// Reads the pending text action for a Telegram user without consuming it.
    /// </summary>
    public bool TryGet(long telegramUserId, out PendingTelegramAction action)
        => _actions.TryGetValue(telegramUserId, out action!);

    /// <summary>
    /// Clears the pending text action after it succeeds or is abandoned.
    /// </summary>
    public bool Clear(long telegramUserId)
        => _actions.TryRemove(telegramUserId, out _);
}
