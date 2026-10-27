namespace Presentation.Telegram.Services;

/// <summary>Named presentation text and formatting templates for TelegramNotificationRenderer.</summary>
internal static class TelegramNotificationRendererText
{
    public const string Sent = "Sent";
    public const string TxHash = "Tx hash";
    public static string AdditionalTransfers(int count) => $"...and {count} more transfers";
    public static string Line(string line) => $"{line}\n";

    public const string Received = "Received";
    public const string Self = "Self";
    public const string Activity = "Activity";
    public const string From = "From";
    public const string To = "To";
    public const string Unknown = "unknown";

    public static string TransferLine(string operation, string amount, string symbol, string usd, string role, string counterparty) => $"{operation}: {amount} {symbol}{usd} {role}: {counterparty}";
    public const string EditIcon = "✏️";
}
