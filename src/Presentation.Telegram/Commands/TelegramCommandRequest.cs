namespace Presentation.Telegram.Commands;

public sealed record TelegramCommandRequest(
    long TelegramUserId,
    string? TelegramUsername,
    string MessageText,
    bool IsPrivateChat);
