namespace Presentation.Telegram.Commands.Parsing;

/// <summary>
/// Parses Telegram slash commands while tolerating bot username suffixes such as /add@BotName.
/// </summary>
public static class TelegramCommandParser
{
    /// <summary>
    /// Attempts to parse a Telegram message into a normalized command name and argument string.
    /// </summary>
    public static bool TryParse(string messageText, out TelegramParsedCommand command)
    {
        command = new TelegramParsedCommand(string.Empty, string.Empty);

        var trimmed = messageText.Trim();
        if (!trimmed.StartsWith('/'))
        {
            return false;
        }

        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var name = NormalizeCommandToken(parts[0]);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        command = new TelegramParsedCommand(
            name,
            parts.Length > 1 ? parts[1].Trim() : string.Empty);
        return true;
    }

    private static string NormalizeCommandToken(string token)
    {
        var command = token.Trim();
        if (command.StartsWith('/'))
        {
            command = command[1..];
        }

        var atIndex = command.IndexOf('@');
        if (atIndex >= 0)
        {
            command = command[..atIndex];
        }

        return command.ToLowerInvariant();
    }
}
