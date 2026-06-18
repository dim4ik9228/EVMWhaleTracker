using Presentation.Telegram.Commands;

namespace Presentation.Telegram.Commands.Wallets;

/// <summary>
/// Handles wallet-management slash commands that mutate or read tracked-wallet application state.
/// </summary>
public interface ITelegramWalletCommandProcessor
{
    /// <summary>
    /// Processes /add command arguments for the Telegram user.
    /// </summary>
    Task<TelegramCommandResponse> HandleAddAsync(long telegramUserId, string arguments, CancellationToken cancellationToken);

    /// <summary>
    /// Processes /list for the Telegram user.
    /// </summary>
    Task<TelegramCommandResponse> HandleListAsync(long telegramUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Processes /remove command arguments for the Telegram user.
    /// </summary>
    Task<TelegramCommandResponse> HandleRemoveAsync(long telegramUserId, string arguments, CancellationToken cancellationToken);

    /// <summary>
    /// Persists a tracked wallet with an already-resolved alias and returns a command response.
    /// </summary>
    Task<TelegramCommandResponse> AddWalletAsync(
        long telegramUserId,
        string rawAddress,
        string alias,
        CancellationToken cancellationToken);
}
