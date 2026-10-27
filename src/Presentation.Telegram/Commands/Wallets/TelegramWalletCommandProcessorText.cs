namespace Presentation.Telegram.Commands.Wallets;

/// <summary>Named presentation text and formatting templates for TelegramWalletCommandProcessor.</summary>
internal static class TelegramWalletCommandProcessorText
{
    public static string ProfileNameSuggestion(string nameKind, string suggestion) => $"Polymarket {nameKind}: <b>{suggestion}</b>\nReply <code>use</code> to use this suggestion, or send your own name.";
    public const string WatchlistEmpty = "You are not tracking any wallets yet.";
    public const string WatchlistHeading = "Your Wallets Watchlist:";
    public static string WalletListEntry(string displayName, string address) => $"<b>{displayName}</b> · <code>{address}</code>";
    public static string HtmlLink(string url) => $" | <a href=\"{url}\">✏️</a>";

    public static string WalletRemoved(string address) =>
        $"""
            Tracked wallet removed successfully.

            Address: <code>{address}</code>
            """;
    public static string WalletAdded(string walletLine, string minimum, string chains) =>
        $"""
            Added to watchlist ✅

            {walletLine}
            Minimum USD: <code>${minimum}</code>
            Networks: <code>{chains}</code>
            """;
    public const string AddUsage = "Usage: /add &lt;address&gt; [name]";
    public const string RemoveUsage = "Usage: /remove &lt;address&gt;";
}
