using Presentation.Telegram.Commands;
using Application.Abstractions;
using Application.Hyperliquid;
using Application.Polymarket;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Types.Enums;
using Application.TrackedWallets;
using Infrastructure.Blockchain;
using MediatR;
using Presentation.Telegram.Commands.PendingActions;
using Presentation.Telegram.Services;

namespace Presentation.Telegram.Commands.Callbacks;

/// <summary>
/// Handles tracked-wallet inline keyboard callbacks and coordinates menu rendering and mutations.
/// </summary>
public sealed class TelegramCallbackProcessor(
    ISender sender,
    IPendingTelegramActionStore pendingActions,
    ITrackedWalletMenuRenderer menuRenderer,
    IChainMetadataProvider chainMetadataProvider,
    IReadOnlyList<BlockchainNetworkOptions> configuredNetworks,
    IHyperliquidSettings? hyperliquid = null,
    IPolymarketSettings? polymarket = null)
    : ITelegramCallbackProcessor
{
    /// <summary>
    /// Processes one callback query, validating ownership before rendering menus or changing wallet settings.
    /// </summary>
    public async Task<TelegramCallbackResponse?> ProcessAsync(
        TelegramCallbackRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.IsPrivateChat)
        {
            return Alert(TelegramCallbackProcessorText.PrivateChatRequired);
        }

        if (!TelegramCallbackDataParser.TryParse(request.Data, out var callbackData))
        {
            return Alert(TelegramUiText.MenuUnavailable);
        }

        if (callbackData.Action == TelegramCallbackAction.Close)
        {
            return new TelegramCallbackResponse(
                TelegramCallbackMessageAction.DeleteMessage,
                AnswerText: TelegramUiText.Closed);
        }

        var walletResult = await sender.Send(
            new GetTrackedWalletQuery(request.TelegramUserId, callbackData.WalletId),
            cancellationToken);

        if (walletResult.IsFailure)
        {
            return Alert(TelegramCallbackProcessorText.WalletNotFound);
        }

        return callbackData.Action switch
        {
            TelegramCallbackAction.Polymarket or TelegramCallbackAction.PolymarketToggle or TelegramCallbackAction.PolymarketBind
                or TelegramCallbackAction.PolymarketMinimum or TelegramCallbackAction.PolymarketAlias or TelegramCallbackAction.PolymarketSource
                => await HandlePolymarketAsync(request.TelegramUserId, walletResult.Value, callbackData, cancellationToken),
            TelegramCallbackAction.Hyperliquid or TelegramCallbackAction.HyperliquidToggle => await HandleHyperliquidAsync(request.TelegramUserId, callbackData, cancellationToken),
            TelegramCallbackAction.Overview => Edit(RenderOverview(walletResult.Value), TelegramUiText.Done),
            TelegramCallbackAction.Networks => Edit(RenderNetworks(walletResult.Value)),
            TelegramCallbackAction.NetworkToggle => await HandleNetworkToggleAsync(
                request.TelegramUserId,
                walletResult.Value,
                callbackData,
                cancellationToken),
            TelegramCallbackAction.DeselectAllNetworks => Alert(TelegramCallbackProcessorText.LastNetworkRequired),
            TelegramCallbackAction.Rename => HandlePrompt(
                request.TelegramUserId,
                PendingTelegramAction.RenameTrackedWallet(walletResult.Value.Id),
                TelegramUiText.RenamePrompt),
            TelegramCallbackAction.MinimumUsd => HandlePrompt(
                request.TelegramUserId,
                PendingTelegramAction.UpdateMinimumUsdValue(walletResult.Value.Id),
                TelegramCallbackProcessorText.MinimumUsdPrompt),
            TelegramCallbackAction.DeletePrompt => Edit(RenderDeleteConfirmation(walletResult.Value)),
            TelegramCallbackAction.DeleteConfirm => await HandleDeleteConfirmAsync(
                request.TelegramUserId,
                walletResult.Value.Id,
                cancellationToken),
            _ => Alert(TelegramUiText.MenuUnavailable)
        };
    }

    private async Task<TelegramCallbackResponse> HandlePolymarketAsync(long userId, TrackedWalletDetails wallet, TelegramCallbackData callback, CancellationToken ct)
    {
        if (polymarket is null) return Alert(TelegramUiText.PolymarketUnavailable);
        var result = await polymarket.GetAsync(userId, wallet.Id, ct);
        if (result.IsFailure) return Alert(result.Error.Description);
        var status = result.Value;
        if (callback.Action != TelegramCallbackAction.Polymarket && callback.Generation != status.Generation)
            return Alert(TelegramCallbackProcessorText.PolymarketMenuStale);
        switch (callback.Action)
        {
            case TelegramCallbackAction.PolymarketToggle:
                result = await polymarket.SetAsync(userId, wallet.Id, !status.Enabled, status.Generation, ct); break;
            case TelegramCallbackAction.PolymarketBind:
                if (status.ProposedAddress is null) return Alert(TelegramCallbackProcessorText.VerifiedAccountMissing);
                result = await polymarket.BindAsync(userId, wallet.Id, status.ProposedAddress, status.Generation, ct); break;
            case TelegramCallbackAction.PolymarketMinimum:
                return HandlePrompt(userId, new(PendingTelegramActionType.PolymarketMinimum, WalletId: wallet.Id, Generation: status.Generation),
                    TelegramCallbackProcessorText.PolymarketMinimumPrompt);
            case TelegramCallbackAction.PolymarketSource:
                return HandlePrompt(userId, new(PendingTelegramActionType.PolymarketSource, WalletId: wallet.Id, Generation: status.Generation),
                    TelegramCallbackProcessorText.PolymarketSourcePrompt);
            case TelegramCallbackAction.PolymarketAlias:
                if (status.Profile?.PublicName is not { } name) return Alert(TelegramCallbackProcessorText.PublicNameMissing);
                var renamed = await sender.Send(new UpdateTrackedWalletAliasCommand(userId, wallet.Id, name), ct);
                if (renamed.IsFailure) return Alert(renamed.Error.Description);
                wallet = renamed.Value; break;
        }
        if (result.IsFailure) return Alert(result.Error.Description);
        status = result.Value;
        var id = wallet.Id.ToString("N");
        var gen = Convert.ToBase64String(status.Generation.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string E(string? value) => System.Net.WebUtility.HtmlEncode(value ?? TelegramUiText.Unresolved);
        List<InlineKeyboardButton[]> rows = [
            [InlineKeyboardButton.WithCallbackData(status.Enabled ? TelegramUiText.TurnOff : TelegramUiText.TurnOn, $"{TelegramInteractionTokens.Callbacks.Polymarket}:{TelegramInteractionTokens.Callbacks.Toggle}:{id}:{gen}")],
            [InlineKeyboardButton.WithCallbackData(TelegramUiText.TradeMinimum, $"{TelegramInteractionTokens.Callbacks.Polymarket}:{TelegramInteractionTokens.Callbacks.Minimum}:{id}:{gen}"), InlineKeyboardButton.WithCallbackData(TelegramUiText.SupplyAccount, $"{TelegramInteractionTokens.Callbacks.Polymarket}:{TelegramInteractionTokens.Callbacks.Source}:{id}:{gen}")]
        ];
        if (status.ProposedAddress is not null) rows.Add([InlineKeyboardButton.WithCallbackData(TelegramCallbackProcessorText.ConfirmAccountRebind, $"{TelegramInteractionTokens.Callbacks.Polymarket}:{TelegramInteractionTokens.Callbacks.Bind}:{id}:{gen}")]);
        if (status.Profile?.PublicName is not null) rows.Add([InlineKeyboardButton.WithCallbackData(TelegramCallbackProcessorText.UsePublicName, $"{TelegramInteractionTokens.Callbacks.Polymarket}:{TelegramInteractionTokens.Callbacks.Alias}:{id}:{gen}")]);
        rows.Add([InlineKeyboardButton.WithCallbackData(TelegramUiText.Refresh, $"{TelegramInteractionTokens.Callbacks.Wallet}:{TelegramInteractionTokens.Callbacks.Version}:{TelegramInteractionTokens.Callbacks.Predictions}:{id}"), InlineKeyboardButton.WithCallbackData(TelegramUiText.Back, $"{TelegramInteractionTokens.Callbacks.Wallet}:{TelegramInteractionTokens.Callbacks.Version}:{TelegramInteractionTokens.Callbacks.Overview}:{id}")]);
        return Edit(new TelegramMenuMessage(TelegramCallbackProcessorText.PolymarketDetails(E(wallet.Alias), E(status.Profile?.PublicName), E(status.OriginalAddress), E(status.SourceAddress), E(status.ProposedAddress), status.MinimumTradeValue.ToString(System.Globalization.CultureInfo.InvariantCulture), E(status.Status)), ParseMode.Html, new InlineKeyboardMarkup(rows)));
    }

    private async Task<TelegramCallbackResponse> HandleHyperliquidAsync(long userId, TelegramCallbackData callback, CancellationToken ct)
    {
        if (hyperliquid is null) return Alert(TelegramCallbackProcessorText.HyperliquidIsUnavailable);
        var result = callback.Action == TelegramCallbackAction.HyperliquidToggle && callback.Enabled is { } enabled
            ? await hyperliquid.SetAsync(userId, callback.WalletId, enabled, ct, callback.Generation)
            : await hyperliquid.GetAsync(userId, callback.WalletId, ct);
        if (result.IsFailure) return Alert(result.Error.Description);
        var status = result.Value;
        var id = callback.WalletId.ToString("N");
        var generation = Convert.ToBase64String(status.Generation.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return Edit(new TelegramMenuMessage(TelegramCallbackProcessorText.HyperliquidDetails(System.Net.WebUtility.HtmlEncode(status.Status)), ParseMode.Html,
            new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData(status.Enabled ? TelegramUiText.TurnOff : TelegramUiText.TurnOn, $"{TelegramInteractionTokens.Callbacks.HyperliquidToggle}:{id}:{(status.Enabled ? TelegramInteractionTokens.Callbacks.Disabled : TelegramInteractionTokens.Callbacks.Enabled)}:{generation}")],
                [InlineKeyboardButton.WithCallbackData(TelegramUiText.Refresh, $"{TelegramInteractionTokens.Callbacks.Wallet}:{TelegramInteractionTokens.Callbacks.Version}:{TelegramInteractionTokens.Callbacks.Hyperliquid}:{id}"), InlineKeyboardButton.WithCallbackData(TelegramUiText.Back, $"{TelegramInteractionTokens.Callbacks.Wallet}:{TelegramInteractionTokens.Callbacks.Version}:{TelegramInteractionTokens.Callbacks.Overview}:{id}")]
            ])));
    }

    private async Task<TelegramCallbackResponse> HandleNetworkToggleAsync(
        long telegramUserId,
        TrackedWalletDetails wallet,
        TelegramCallbackData callbackData,
        CancellationToken cancellationToken)
    {
        if (callbackData.Chain is not { } chain || callbackData.Enabled is not { } enabled)
        {
            return Alert(TelegramUiText.MenuUnavailable);
        }

        if (!configuredNetworks.Any(network => network.Chain == chain))
        {
            return Alert(TelegramCallbackProcessorText.NetworkNotConfigured);
        }

        var result = await sender.Send(
            new SetTrackedWalletChainTrackingCommand(telegramUserId, wallet.Id, chain, enabled),
            cancellationToken);

        if (result.IsFailure)
        {
            return Alert(result.Error.Description);
        }

        return Edit(RenderNetworks(result.Value), TelegramUiText.Updated);
    }

    private async Task<TelegramCallbackResponse> HandleDeleteConfirmAsync(
        long telegramUserId,
        Guid walletId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new RemoveTrackedWalletByIdCommand(telegramUserId, walletId),
            cancellationToken);

        if (result.IsFailure)
        {
            return Alert(result.Error.Description);
        }

        return new TelegramCallbackResponse(
            TelegramCallbackMessageAction.EditMessage,
            Text: TelegramCallbackProcessorText.TrackedWalletDeleted,
            AnswerText: TelegramUiText.Deleted);
    }

    private TelegramCallbackResponse HandlePrompt(
        long telegramUserId,
        PendingTelegramAction action,
        string prompt)
    {
        pendingActions.Set(telegramUserId, action);
        return new TelegramCallbackResponse(
            TelegramCallbackMessageAction.EditMessage,
            Text: prompt,
            AnswerText: TelegramCallbackProcessorText.WaitingForYourReply);
    }

    private TelegramMenuMessage RenderOverview(TrackedWalletDetails wallet)
        => menuRenderer.RenderOverview(wallet, GetConfiguredChainMetadata());

    private TelegramMenuMessage RenderNetworks(TrackedWalletDetails wallet)
        => menuRenderer.RenderNetworks(wallet, GetConfiguredChainMetadata());

    private TelegramMenuMessage RenderDeleteConfirmation(TrackedWalletDetails wallet)
        => menuRenderer.RenderDeleteConfirmation(wallet);

    private IReadOnlyList<ChainMetadata> GetConfiguredChainMetadata()
        => configuredNetworks
            .Select(network => chainMetadataProvider.Get(network.Chain))
            .ToArray();

    private static TelegramCallbackResponse Edit(TelegramMenuMessage menu, string? answerText = null)
        => new(
            TelegramCallbackMessageAction.EditMessage,
            menu.Text,
            menu.ParseMode,
            menu.ReplyMarkup,
            answerText);

    private static TelegramCallbackResponse Alert(string text)
        => new(
            TelegramCallbackMessageAction.None,
            AnswerText: text,
            ShowAlert: true);
}
