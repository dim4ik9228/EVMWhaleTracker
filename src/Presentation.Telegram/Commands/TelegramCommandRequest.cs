namespace Presentation.Telegram.Commands;

/// <summary>
/// Normalized Telegram message data passed into command processing.
/// </summary>
public sealed record TelegramCommandRequest(
    long TelegramUserId,
    string? TelegramUsername,
    string MessageText,
    bool IsPrivateChat);
