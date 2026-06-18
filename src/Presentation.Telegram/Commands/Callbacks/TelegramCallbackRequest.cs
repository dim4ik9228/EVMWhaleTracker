namespace Presentation.Telegram.Commands.Callbacks;

/// <summary>
/// Normalized Telegram callback query data passed into callback processing.
/// </summary>
public sealed record TelegramCallbackRequest(
    long TelegramUserId,
    string? TelegramUsername,
    long ChatId,
    int MessageId,
    string Data,
    bool IsPrivateChat);
