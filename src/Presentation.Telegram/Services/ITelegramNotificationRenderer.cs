using Application.Messaging;
using Telegram.Bot.Types.Enums;

namespace Presentation.Telegram.Services;

public interface ITelegramNotificationRenderer
{
    TelegramRenderedMessage Render(NotificationPayload payload);
}

public sealed record TelegramRenderedMessage(
    string Text,
    ParseMode ParseMode);
