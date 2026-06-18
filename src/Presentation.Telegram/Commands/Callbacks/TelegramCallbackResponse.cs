using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Presentation.Telegram.Commands.Callbacks;

/// <summary>
/// Result of a callback query, including the menu mutation and callback-answer behavior.
/// </summary>
public sealed record TelegramCallbackResponse(
    TelegramCallbackMessageAction MessageAction,
    string? Text = null,
    ParseMode ParseMode = ParseMode.Html,
    InlineKeyboardMarkup? ReplyMarkup = null,
    string? AnswerText = null,
    bool ShowAlert = false);

/// <summary>
/// Telegram message operation requested after processing a callback query.
/// </summary>
public enum TelegramCallbackMessageAction
{
    /// <summary>
    /// Leave the menu message unchanged.
    /// </summary>
    None,

    /// <summary>
    /// Replace the existing menu message text and markup.
    /// </summary>
    EditMessage,

    /// <summary>
    /// Delete the existing menu message.
    /// </summary>
    DeleteMessage
}
