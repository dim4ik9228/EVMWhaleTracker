namespace Presentation.Telegram.Commands;

/// <summary>
/// HTML-formatted command response text ready to send back to Telegram.
/// </summary>
public sealed record TelegramCommandResponse(string Text);
