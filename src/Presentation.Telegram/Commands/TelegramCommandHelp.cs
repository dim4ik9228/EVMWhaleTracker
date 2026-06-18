namespace Presentation.Telegram.Commands;

/// <summary>
/// Static help text shown for /start, /help, and unknown commands.
/// </summary>
public static class TelegramCommandHelp
{
    /// <summary>
    /// HTML-formatted command help.
    /// </summary>
    public static readonly string Text =
        """
        Welcome to EVMWhaleTracker.

        Available commands:
        <code>/add &lt;address&gt; [name]</code>
        <code>/list</code>
        <code>/remove &lt;address&gt;</code>

        Examples:
        <code>/add 0x52908400098527886E0F7030069857D2E4169EE7 Main Address</code>
        <code>/add 0x52908400098527886e0f7030069857d2e4169ee7</code>
        <code>/remove 0x52908400098527886E0F7030069857D2E4169EE7</code>
        """;
}
