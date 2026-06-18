using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Presentation.Telegram.Commands;

/// <summary>
/// Telegram command response text and optional reply markup ready to send back.
/// </summary>
public sealed record TelegramCommandResponse(
    string Text,
    ParseMode ParseMode = ParseMode.Html,
    ReplyMarkup? ReplyMarkup = null,
    bool DeleteSourceMessage = false);
