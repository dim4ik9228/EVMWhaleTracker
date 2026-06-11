namespace Presentation.Telegram.Commands;

/// <summary>
/// Stores in-flight wallet alias prompts for private Telegram chats.
/// </summary>
public interface IPendingWalletAliasRequestStore
{
    void Set(long telegramUserId, PendingWalletAliasRequest request);

    bool TryGet(long telegramUserId, out PendingWalletAliasRequest request);

    bool Clear(long telegramUserId);
}

/// <summary>
/// Pending wallet address awaiting a user-provided alias.
/// </summary>
public sealed record PendingWalletAliasRequest(string RawAddress);
