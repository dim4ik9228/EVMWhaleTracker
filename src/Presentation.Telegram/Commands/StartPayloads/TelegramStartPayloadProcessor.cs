using Application.TrackedWallets;
using MediatR;
using Presentation.Telegram.Commands;
using Presentation.Telegram.Commands.Menus;
using Presentation.Telegram.Commands.Wallets;

namespace Presentation.Telegram.Commands.StartPayloads;

/// <summary>
/// Opens tracked-wallet menus from Telegram deep-link start payloads.
/// </summary>
public sealed class TelegramStartPayloadProcessor(
    ISender sender,
    ITrackedWalletMenuResponseFactory menuResponseFactory)
    : ITelegramStartPayloadProcessor
{
    /// <inheritdoc />
    public async Task<TelegramCommandResponse> ProcessAsync(
        long telegramUserId,
        string arguments,
        CancellationToken cancellationToken)
    {
        if (!TelegramStartPayloadParser.TryParseEditWalletPayload(arguments, out var walletId))
        {
            return new TelegramCommandResponse(TelegramCommandHelp.Text);
        }

        var result = await sender.Send(new GetTrackedWalletQuery(telegramUserId, walletId), cancellationToken);
        if (result.IsFailure)
        {
            return new TelegramCommandResponse(
                TelegramCommandText.FormatError(result.Error),
                DeleteSourceMessage: true);
        }

        return menuResponseFactory.CreateOverviewResponse(result.Value, deleteSourceMessage: true);
    }
}
