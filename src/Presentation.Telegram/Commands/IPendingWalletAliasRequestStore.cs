namespace Presentation.Telegram.Commands;

public interface IPendingWalletAliasRequestStore
{
    void Set(long telegramUserId, PendingWalletAliasRequest request);

    bool TryGet(long telegramUserId, out PendingWalletAliasRequest request);

    bool Clear(long telegramUserId);
}

public sealed record PendingWalletAliasRequest(string RawAddress);
