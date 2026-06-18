namespace Presentation.Telegram.Commands.PendingActions;

/// <summary>
/// Stores in-flight Telegram text actions for private chats.
/// </summary>
public interface IPendingTelegramActionStore
{
    /// <summary>
    /// Stores the action that should consume the user's next private text message.
    /// </summary>
    void Set(long telegramUserId, PendingTelegramAction action);

    /// <summary>
    /// Reads the action awaiting the user's next private text message, if any.
    /// </summary>
    bool TryGet(long telegramUserId, out PendingTelegramAction action);

    /// <summary>
    /// Removes any pending text action for the Telegram user.
    /// </summary>
    bool Clear(long telegramUserId);
}

/// <summary>
/// A pending text response expected from a Telegram user.
/// </summary>
public sealed record PendingTelegramAction(
    PendingTelegramActionType Type,
    string? RawAddress = null,
    Guid? WalletId = null)
{
    /// <summary>
    /// Creates a pending alias action for the two-step add-wallet flow.
    /// </summary>
    public static PendingTelegramAction AddWalletAlias(string rawAddress)
        => new(PendingTelegramActionType.AddWalletAlias, RawAddress: rawAddress);

    /// <summary>
    /// Creates a pending rename action for an existing tracked wallet.
    /// </summary>
    public static PendingTelegramAction RenameTrackedWallet(Guid walletId)
        => new(PendingTelegramActionType.RenameTrackedWallet, WalletId: walletId);

    /// <summary>
    /// Creates a pending minimum USD threshold update for an existing tracked wallet.
    /// </summary>
    public static PendingTelegramAction UpdateMinimumUsdValue(Guid walletId)
        => new(PendingTelegramActionType.UpdateMinimumUsdValue, WalletId: walletId);
}

/// <summary>
/// Kinds of private text replies that can be claimed by Telegram command processing.
/// </summary>
public enum PendingTelegramActionType
{
    /// <summary>
    /// The next message supplies an alias for a newly added wallet address.
    /// </summary>
    AddWalletAlias,

    /// <summary>
    /// The next message supplies a replacement alias for an existing tracked wallet.
    /// </summary>
    RenameTrackedWallet,

    /// <summary>
    /// The next message supplies a replacement minimum USD threshold.
    /// </summary>
    UpdateMinimumUsdValue
}
