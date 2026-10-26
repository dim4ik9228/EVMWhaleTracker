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
            return Alert("Please open this bot in a private chat to manage tracked wallets.");
        }

        if (!TelegramCallbackDataParser.TryParse(request.Data, out var callbackData))
        {
            return Alert("This menu is no longer available.");
        }

        if (callbackData.Action == TelegramCallbackAction.Close)
        {
            return new TelegramCallbackResponse(
                TelegramCallbackMessageAction.DeleteMessage,
                AnswerText: "Closed.");
        }

        var walletResult = await sender.Send(
            new GetTrackedWalletQuery(request.TelegramUserId, callbackData.WalletId),
            cancellationToken);

        if (walletResult.IsFailure)
        {
            return Alert("Wallet not found.");
        }

        return callbackData.Action switch
        {
            TelegramCallbackAction.Polymarket or TelegramCallbackAction.PolymarketToggle or TelegramCallbackAction.PolymarketBind
                or TelegramCallbackAction.PolymarketMinimum or TelegramCallbackAction.PolymarketAlias or TelegramCallbackAction.PolymarketSource
                => await HandlePolymarketAsync(request.TelegramUserId, walletResult.Value, callbackData, cancellationToken),
            TelegramCallbackAction.Hyperliquid or TelegramCallbackAction.HyperliquidToggle => await HandleHyperliquidAsync(request.TelegramUserId, callbackData, cancellationToken),
            TelegramCallbackAction.Overview => Edit(RenderOverview(walletResult.Value), "Done."),
            TelegramCallbackAction.Networks => Edit(RenderNetworks(walletResult.Value)),
            TelegramCallbackAction.NetworkToggle => await HandleNetworkToggleAsync(
                request.TelegramUserId,
                walletResult.Value,
                callbackData,
                cancellationToken),
            TelegramCallbackAction.DeselectAllNetworks => Alert("At least one network must remain enabled."),
            TelegramCallbackAction.Rename => HandlePrompt(
                request.TelegramUserId,
                PendingTelegramAction.RenameTrackedWallet(walletResult.Value.Id),
                "Send a new name for this wallet."),
            TelegramCallbackAction.MinimumUsd => HandlePrompt(
                request.TelegramUserId,
                PendingTelegramAction.UpdateMinimumUsdValue(walletResult.Value.Id),
                "Send the minimum transaction USD value.\nUse 0 to notify on every matched transaction."),
            TelegramCallbackAction.DeletePrompt => Edit(RenderDeleteConfirmation(walletResult.Value)),
            TelegramCallbackAction.DeleteConfirm => await HandleDeleteConfirmAsync(
                request.TelegramUserId,
                walletResult.Value.Id,
                cancellationToken),
            _ => Alert("This menu is no longer available.")
        };
    }

    private async Task<TelegramCallbackResponse> HandlePolymarketAsync(long userId, TrackedWalletDetails wallet, TelegramCallbackData callback, CancellationToken ct)
    {
        if (polymarket is null) return Alert("Polymarket is unavailable.");
        var result = await polymarket.GetAsync(userId, wallet.Id, ct);
        if (result.IsFailure) return Alert(result.Error.Description);
        var status = result.Value;
        if (callback.Action != TelegramCallbackAction.Polymarket && callback.Generation != status.Generation)
            return Alert("This setting changed. Refresh the Polymarket menu.");
        switch (callback.Action)
        {
            case TelegramCallbackAction.PolymarketToggle:
                result = await polymarket.SetAsync(userId, wallet.Id, !status.Enabled, status.Generation, ct); break;
            case TelegramCallbackAction.PolymarketBind:
                if (status.ProposedAddress is null) return Alert("No verified trading account is available.");
                result = await polymarket.BindAsync(userId, wallet.Id, status.ProposedAddress, status.Generation, ct); break;
            case TelegramCallbackAction.PolymarketMinimum:
                return HandlePrompt(userId, new(PendingTelegramActionType.PolymarketMinimum, WalletId: wallet.Id, Generation: status.Generation),
                    "Send the Polymarket minimum gross trade value in collateral units (pUSD / USDC.e). Default: 0. Redemptions bypass this minimum.");
            case TelegramCallbackAction.PolymarketSource:
                return HandlePrompt(userId, new(PendingTelegramActionType.PolymarketSource, WalletId: wallet.Id, Generation: status.Generation),
                    "Send the profile's trading-wallet address. It must be verified by the public profile API. This creates a new binding and leaves tracking off.");
            case TelegramCallbackAction.PolymarketAlias:
                if (status.Profile?.PublicName is not { } name) return Alert("No public name is available.");
                var renamed = await sender.Send(new UpdateTrackedWalletAliasCommand(userId, wallet.Id, name), ct);
                if (renamed.IsFailure) return Alert(renamed.Error.Description);
                wallet = renamed.Value; break;
        }
        if (result.IsFailure) return Alert(result.Error.Description);
        status = result.Value;
        var id = wallet.Id.ToString("N");
        var gen = Convert.ToBase64String(status.Generation.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string E(string? value) => System.Net.WebUtility.HtmlEncode(value ?? "Unresolved");
        List<InlineKeyboardButton[]> rows = [
            [InlineKeyboardButton.WithCallbackData(status.Enabled ? "Turn Off" : "Turn On", $"pm:t:{id}:{gen}")],
            [InlineKeyboardButton.WithCallbackData("Trade minimum", $"pm:m:{id}:{gen}"), InlineKeyboardButton.WithCallbackData("Supply account", $"pm:s:{id}:{gen}")]
        ];
        if (status.ProposedAddress is not null) rows.Add([InlineKeyboardButton.WithCallbackData("Confirm account / Rebind", $"pm:b:{id}:{gen}")]);
        if (status.Profile?.PublicName is not null) rows.Add([InlineKeyboardButton.WithCallbackData("Use public name", $"pm:a:{id}:{gen}")]);
        rows.Add([InlineKeyboardButton.WithCallbackData("Refresh", $"tw:v1:p:{id}"), InlineKeyboardButton.WithCallbackData("Back", $"tw:v1:o:{id}")]);
        return Edit(new TelegramMenuMessage($"<b>Polymarket</b>\nYour alias: {E(wallet.Alias)}\nPolymarket profile: {E(status.Profile?.PublicName)}\nEntered: <code>{E(status.OriginalAddress)}</code>\nBound account: <code>{E(status.SourceAddress)}</code>\nProposed account: <code>{E(status.ProposedAddress)}</code>\nPolymarket follows the confirmed trading account above.\nTrade minimum: {status.MinimumTradeValue.ToString(System.Globalization.CultureInfo.InvariantCulture)} collateral units (gross)\n{E(status.Status)}", ParseMode.Html, new InlineKeyboardMarkup(rows)));
    }

    private async Task<TelegramCallbackResponse> HandleHyperliquidAsync(long userId, TelegramCallbackData callback, CancellationToken ct)
    {
        if (hyperliquid is null) return Alert("Hyperliquid is unavailable.");
        var result = callback.Action == TelegramCallbackAction.HyperliquidToggle && callback.Enabled is { } enabled
            ? await hyperliquid.SetAsync(userId, callback.WalletId, enabled, ct, callback.Generation)
            : await hyperliquid.GetAsync(userId, callback.WalletId, ct);
        if (result.IsFailure) return Alert(result.Error.Description);
        var status = result.Value;
        var id = callback.WalletId.ToString("N");
        var generation = Convert.ToBase64String(status.Generation.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return Edit(new TelegramMenuMessage($"<b>Hyperliquid</b>\n{System.Net.WebUtility.HtmlEncode(status.Status)}\nNative USDC perp trades, liquidations, deposits and withdrawals.\nHistory may be delayed or incomplete during recovery.", ParseMode.Html,
            new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData(status.Enabled ? "Turn Off" : "Turn On", $"hc:{id}:{(status.Enabled ? "0" : "1")}:{generation}")],
                [InlineKeyboardButton.WithCallbackData("Refresh", $"tw:v1:h:{id}"), InlineKeyboardButton.WithCallbackData("Back", $"tw:v1:o:{id}")]
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
            return Alert("This menu is no longer available.");
        }

        if (!configuredNetworks.Any(network => network.Chain == chain))
        {
            return Alert("That network is not configured for tracking.");
        }

        var result = await sender.Send(
            new SetTrackedWalletChainTrackingCommand(telegramUserId, wallet.Id, chain, enabled),
            cancellationToken);

        if (result.IsFailure)
        {
            return Alert(result.Error.Description);
        }

        return Edit(RenderNetworks(result.Value), "Updated.");
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
            Text: "Tracked wallet deleted.",
            AnswerText: "Deleted.");
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
            AnswerText: "Waiting for your reply.");
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
