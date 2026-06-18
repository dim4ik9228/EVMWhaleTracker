using Application.TrackedWallets;
using Presentation.Telegram.Commands;

namespace Presentation.Telegram.Commands.Menus;

/// <summary>
/// Converts tracked-wallet menu render output into command responses.
/// </summary>
public interface ITrackedWalletMenuResponseFactory
{
    /// <summary>
    /// Builds a Telegram command response containing the tracked-wallet overview menu.
    /// </summary>
    TelegramCommandResponse CreateOverviewResponse(TrackedWalletDetails wallet, bool deleteSourceMessage = false);
}
