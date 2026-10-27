using Presentation.Telegram.Commands;
namespace Presentation.Telegram.Commands.PendingActions;

/// <summary>Named presentation text and formatting templates for TelegramPendingActionProcessor.</summary>
internal static class TelegramPendingActionProcessorText
{
    public const string MinimumUsdInvalid = "Send a non-negative minimum transaction USD value.";
    public const string InvalidPolymarketMinimum = "Send a non-negative Polymarket minimum gross trade value in collateral units (pUSD / USDC.e).";
    public const string PolymarketSettingSaved = "Polymarket setting saved. Open the wallet editor to review the account and tracking status.";
}
