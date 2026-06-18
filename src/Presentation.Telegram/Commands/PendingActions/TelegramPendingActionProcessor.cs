using System.Globalization;
using Application.TrackedWallets;
using MediatR;
using Presentation.Telegram.Commands;
using Presentation.Telegram.Commands.Menus;
using Presentation.Telegram.Commands.Wallets;

namespace Presentation.Telegram.Commands.PendingActions;

/// <summary>
/// Applies pending text replies for add-wallet aliases and inline wallet setting prompts.
/// </summary>
public sealed class TelegramPendingActionProcessor(
    ISender sender,
    IPendingTelegramActionStore pendingActions,
    ITelegramWalletCommandProcessor walletCommandProcessor,
    ITrackedWalletMenuResponseFactory menuResponseFactory)
    : ITelegramPendingActionProcessor
{
    private const string AliasPrompt = "Please give a name to this wallet";

    /// <inheritdoc />
    public async Task<TelegramCommandResponse> ProcessAsync(
        long telegramUserId,
        PendingTelegramAction pendingAction,
        string messageText,
        CancellationToken cancellationToken)
        => pendingAction.Type switch
        {
            PendingTelegramActionType.AddWalletAlias => await HandlePendingAliasAsync(
                telegramUserId,
                pendingAction.RawAddress ?? string.Empty,
                messageText,
                cancellationToken),
            PendingTelegramActionType.RenameTrackedWallet => await HandlePendingRenameAsync(
                telegramUserId,
                pendingAction.WalletId.GetValueOrDefault(),
                messageText,
                cancellationToken),
            PendingTelegramActionType.UpdateMinimumUsdValue => await HandlePendingMinimumUsdAsync(
                telegramUserId,
                pendingAction.WalletId.GetValueOrDefault(),
                messageText,
                cancellationToken),
            _ => new TelegramCommandResponse(TelegramCommandHelp.Text)
        };

    private async Task<TelegramCommandResponse> HandlePendingAliasAsync(
        long telegramUserId,
        string rawAddress,
        string messageText,
        CancellationToken cancellationToken)
    {
        var alias = messageText.Trim();
        if (string.IsNullOrWhiteSpace(alias))
        {
            return new TelegramCommandResponse(AliasPrompt);
        }

        pendingActions.Clear(telegramUserId);
        return await walletCommandProcessor.AddWalletAsync(telegramUserId, rawAddress, alias, cancellationToken);
    }

    private async Task<TelegramCommandResponse> HandlePendingRenameAsync(
        long telegramUserId,
        Guid walletId,
        string messageText,
        CancellationToken cancellationToken)
    {
        var alias = messageText.Trim();
        if (string.IsNullOrWhiteSpace(alias))
        {
            return new TelegramCommandResponse("Send a new name for this wallet.");
        }

        var result = await sender.Send(
            new UpdateTrackedWalletAliasCommand(telegramUserId, walletId, alias),
            cancellationToken);

        if (result.IsFailure)
        {
            return new TelegramCommandResponse(TelegramCommandText.FormatError(result.Error));
        }

        pendingActions.Clear(telegramUserId);
        return menuResponseFactory.CreateOverviewResponse(result.Value);
    }

    private async Task<TelegramCommandResponse> HandlePendingMinimumUsdAsync(
        long telegramUserId,
        Guid walletId,
        string messageText,
        CancellationToken cancellationToken)
    {
        if (!TryParseDecimal(messageText.Trim(), out var minimumUsdValue) || minimumUsdValue < 0m)
        {
            return new TelegramCommandResponse("Send a non-negative minimum transaction USD value.");
        }

        var result = await sender.Send(
            new UpdateTrackedWalletMinimumUsdValueCommand(telegramUserId, walletId, minimumUsdValue),
            cancellationToken);

        if (result.IsFailure)
        {
            return new TelegramCommandResponse(TelegramCommandText.FormatError(result.Error));
        }

        pendingActions.Clear(telegramUserId);
        return menuResponseFactory.CreateOverviewResponse(result.Value);
    }

    private static bool TryParseDecimal(string value, out decimal result)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result)
            || decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result);
}
