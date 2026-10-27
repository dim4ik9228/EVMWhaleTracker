namespace Presentation.Telegram.Configuration;

/// <summary>Startup validation and host diagnostic text.</summary>
internal static class HostDiagnostics
{
    public const string ConnectionStringRequired = "ConnectionStrings:Default must be configured.";
    public const string InvalidPolymarketOptions = "Polymarket options exceed supported bounds";
    public const string InvalidPolygonOptions = "Polymarket Polygon options exceed supported bounds";
    public const string InvalidHyperliquidOptions = "Hyperliquid options exceed supported bounds";
    public const string NoNetworks = "No blockchain networks are configured. Blockchain workers are disabled.";
    public const string TelegramDisabled = "TelegramBot:BotToken is not configured. Telegram workers are disabled.";
    public static string EnabledNetworks(string chains) => $"Enabled blockchain networks: {chains}";
}
