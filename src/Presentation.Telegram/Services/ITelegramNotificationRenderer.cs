using Telegram.Bot.Types.Enums;

namespace Presentation.Telegram.Services;

/// <summary>
/// Renders structured wallet activity into Telegram-ready text.
/// </summary>
public interface ITelegramNotificationRenderer
{
    /// <summary>
    /// Renders an enriched notification and optionally adds an edit deep link for the resolved bot username.
    /// </summary>
    TelegramRenderedMessage Render(EnrichedTelegramNotification notification, string? botUsername = null);
}

/// <summary>
/// Telegram message text and parse mode produced by a renderer.
/// </summary>
public sealed record TelegramRenderedMessage(
    string Text,
    ParseMode ParseMode);
