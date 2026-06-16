using Application.Messaging;
using Telegram.Bot.Types.Enums;

namespace Presentation.Telegram.Services;

/// <summary>
/// Renders structured wallet activity into Telegram-ready text.
/// </summary>
public interface ITelegramNotificationRenderer
{
    Task<TelegramRenderedMessage> RenderAsync(NotificationPayload payload, CancellationToken cancellationToken);

    Task<TelegramRenderedMessage> RenderAsync(AggregatedNotificationPayload payload, CancellationToken cancellationToken);
}

/// <summary>
/// Telegram message text and parse mode produced by a renderer.
/// </summary>
public sealed record TelegramRenderedMessage(
    string Text,
    ParseMode ParseMode);
