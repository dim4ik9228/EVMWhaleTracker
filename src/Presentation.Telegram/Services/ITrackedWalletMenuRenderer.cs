using Application.TrackedWallets;
using Application.Abstractions;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Presentation.Telegram.Services;

/// <summary>
/// Renders tracked-wallet settings menus for Telegram inline keyboard flows.
/// </summary>
public interface ITrackedWalletMenuRenderer
{
    /// <summary>
    /// Renders the main settings menu for a tracked wallet.
    /// </summary>
    TelegramMenuMessage RenderOverview(TrackedWalletDetails wallet, IReadOnlyList<ChainMetadata> configuredChains);

    /// <summary>
    /// Renders the configured-network checklist, treating empty enabled chains as all configured chains.
    /// </summary>
    TelegramMenuMessage RenderNetworks(TrackedWalletDetails wallet, IReadOnlyList<ChainMetadata> configuredChains);

    /// <summary>
    /// Renders the confirmation prompt shown before deleting a tracked wallet.
    /// </summary>
    TelegramMenuMessage RenderDeleteConfirmation(TrackedWalletDetails wallet);
}

/// <summary>
/// Telegram menu text, parse mode, and inline keyboard markup produced by a menu renderer.
/// </summary>
public sealed record TelegramMenuMessage(
    string Text,
    ParseMode ParseMode,
    InlineKeyboardMarkup ReplyMarkup);
