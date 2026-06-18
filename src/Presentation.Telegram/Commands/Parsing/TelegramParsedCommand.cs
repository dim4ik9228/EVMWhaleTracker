namespace Presentation.Telegram.Commands.Parsing;

/// <summary>
/// Parsed slash-command token and trailing argument text from a Telegram message.
/// </summary>
public sealed record TelegramParsedCommand(string Name, string Arguments);
