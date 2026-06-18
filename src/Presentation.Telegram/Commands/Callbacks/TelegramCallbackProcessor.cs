using Application.Abstractions;
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
    IReadOnlyList<BlockchainNetworkOptions> configuredNetworks)
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
