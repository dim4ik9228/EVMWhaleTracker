using System.Globalization;
using Application.TrackedWallets;
using Application.Polymarket;
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
    ITrackedWalletMenuResponseFactory menuResponseFactory,
    IPolymarketSettings? polymarket = null)
    : ITelegramPendingActionProcessor
{

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
                messageText.Trim().Equals(TelegramUiText.UseSuggestion, StringComparison.OrdinalIgnoreCase) && pendingAction.SuggestedAlias is not null ? pendingAction.SuggestedAlias : messageText,
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
            PendingTelegramActionType.PolymarketMinimum or PendingTelegramActionType.PolymarketSource =>
                await HandlePolymarketAsync(telegramUserId, pendingAction, messageText, cancellationToken),
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
            return new TelegramCommandResponse(TelegramUiText.AliasPrompt);
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
            return new TelegramCommandResponse(TelegramUiText.RenamePrompt);
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
            return new TelegramCommandResponse(TelegramPendingActionProcessorText.MinimumUsdInvalid);
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

    private async Task<TelegramCommandResponse> HandlePolymarketAsync(long userId, PendingTelegramAction action, string text, CancellationToken ct)
    {
        if (polymarket is null) return new(TelegramUiText.PolymarketUnavailable);
        if (action.Type == PendingTelegramActionType.PolymarketMinimum && (!TryParseDecimal(text, out _) || text.Contains('-')))
            return new(TelegramPendingActionProcessorText.InvalidPolymarketMinimum);
        var result = action.Type == PendingTelegramActionType.PolymarketSource
            ? await polymarket.BindAsync(userId, action.WalletId!.Value, text.Trim(), action.Generation ?? Guid.Empty, ct)
            : await polymarket.MinimumAsync(userId, action.WalletId!.Value, ParseMinimum(text), action.Generation ?? Guid.Empty, ct);
        if (result.IsFailure) return new(TelegramCommandText.HtmlEncode(result.Error.Description));
        pendingActions.Clear(userId);
        return new(TelegramPendingActionProcessorText.PolymarketSettingSaved);
    }

    private static decimal ParseMinimum(string text) { TryParseDecimal(text, out var value); return value; }

    private static bool TryParseDecimal(string value, out decimal result)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result)
            || decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result);
}
