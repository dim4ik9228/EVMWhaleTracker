namespace Presentation.Telegram.Services;

/// <summary>Named presentation text and formatting templates for TrackedWalletMenuRenderer.</summary>
internal static class TrackedWalletMenuRendererText
{
    public const string MinUSD = "💵 Min USD";
    public const string DeselectAllNetworks = "Deselect All Networks";
    public const string NetworksPrompt = "Choose ✅ Networks to add and press ☑️ Done:";
    public const string ThisWallet = "this wallet";
    public const string NoRuntimeNetworksConfigured = "No runtime networks configured";

    public static string Overview(string displayName, string chains, string address, string minimum) =>
        $"""
            <b>{displayName}</b>
            {chains}
            <code>{address}</code>
            Minimum USD: <code>${minimum}</code>
            """;
    public static string DeleteConfirmation(string displayName, string address) =>
        $"""
            Delete {displayName}?
            <code>{address}</code>
            """;

    public const string Main = "Main";
    public const string Networks = "🛰 Networks";
    public const string Hyperliquid = "Hyperliquid";
    public const string Polymarket = "Polymarket";
    public const string Rename = "✏️ Rename";
    public const string Delete = "🗑 Delete";
    public const string Back = "↩️ Back";
    public const string Cancel = "❌ Cancel";
    public const string Done = "☑️ Done";
    public const string ArbitrumShortName = "ARB";
    public const string BnbShortName = "BNB";
    public const string EthereumShortName = "ETH";
}
