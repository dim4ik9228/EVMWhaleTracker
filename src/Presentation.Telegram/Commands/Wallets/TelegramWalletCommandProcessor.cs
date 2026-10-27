using Application.Abstractions;
using Application.Polymarket;
using Application.TrackedWallets;
using Domain.ValueObjects;
using MediatR;
using Presentation.Telegram.Commands;
using Presentation.Telegram.Commands.PendingActions;
using Presentation.Telegram.Services;

namespace Presentation.Telegram.Commands.Wallets;

/// <summary>
/// Handles tracked-wallet slash commands and keeps Telegram-specific formatting outside application handlers.
/// </summary>
public sealed class TelegramWalletCommandProcessor(
    ISender sender,
    IPendingTelegramActionStore pendingActions,
    IEnsReverseLookupProvider ensReverseLookupProvider,
    ITelegramBotUsernameProvider botUsernameProvider,
    IPolymarketProfileResolver? polymarket = null)
    : ITelegramWalletCommandProcessor
{

    /// <inheritdoc />
    public async Task<TelegramCommandResponse> HandleAddAsync(
        long telegramUserId,
        string arguments,
        CancellationToken cancellationToken)
    {
        if (!TryParseAddArguments(arguments, out var rawAddress, out var alias, out var errorText))
        {
            return new TelegramCommandResponse(errorText);
        }

        if (string.IsNullOrWhiteSpace(alias))
        {
            var addressResult = Address.Normalize(rawAddress);
            if (addressResult.IsFailure)
            {
                return new TelegramCommandResponse(TelegramCommandText.FormatError(addressResult.Error));
            }

            var ensAlias = await ensReverseLookupProvider.ResolveNameAsync(addressResult.Value, cancellationToken);
            if (!string.IsNullOrWhiteSpace(ensAlias))
            {
                return await AddWalletAsync(telegramUserId, rawAddress, ensAlias, cancellationToken);
            }

            var profile = polymarket is null ? null : await polymarket.ResolveAsync(rawAddress, cancellationToken);
            var suggestion = profile?.Profile?.PublicName;
            pendingActions.Set(telegramUserId, PendingTelegramAction.AddWalletAlias(rawAddress) with { SuggestedAlias = suggestion });
            return new TelegramCommandResponse(string.IsNullOrWhiteSpace(suggestion) ? TelegramUiText.AliasPrompt
                : TelegramWalletCommandProcessorText.ProfileNameSuggestion((profile!.Profile!.DisplayUsernamePublic ? TelegramUiText.PublicName : TelegramUiText.Pseudonym), TelegramCommandText.HtmlEncode(suggestion)));
        }

        return await AddWalletAsync(telegramUserId, rawAddress, alias, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TelegramCommandResponse> HandleListAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListTrackedWalletsQuery(telegramUserId), cancellationToken);
        if (result.IsFailure)
        {
            return new TelegramCommandResponse(TelegramCommandText.FormatError(result.Error));
        }

        if (result.Value.Count == 0)
        {
            return new TelegramCommandResponse(TelegramWalletCommandProcessorText.WatchlistEmpty);
        }

        var botUsername = await botUsernameProvider.GetBotUsernameAsync(cancellationToken);
        var lines = new List<string>
        {
            TelegramWalletCommandProcessorText.WatchlistHeading,
            string.Empty
        };

        foreach (var wallet in result.Value)
        {
            lines.Add(FormatWatchlistLine(wallet, botUsername));
        }

        return new TelegramCommandResponse(string.Join(Environment.NewLine, lines));
    }

    /// <inheritdoc />
    public async Task<TelegramCommandResponse> HandleRemoveAsync(
        long telegramUserId,
        string arguments,
        CancellationToken cancellationToken)
    {
        if (!TryParseRemoveArguments(arguments, out var rawAddress, out var errorText))
        {
            return new TelegramCommandResponse(errorText);
        }

        var result = await sender.Send(
            new RemoveTrackedWalletCommand(
                TelegramUserId: telegramUserId,
                RawAddress: rawAddress),
            cancellationToken);

        if (result.IsFailure)
        {
            return new TelegramCommandResponse(TelegramCommandText.FormatError(result.Error));
        }

        return new TelegramCommandResponse(
            TelegramWalletCommandProcessorText.WalletRemoved(TelegramCommandText.HtmlEncode(rawAddress)));
    }

    /// <inheritdoc />
    public async Task<TelegramCommandResponse> AddWalletAsync(
        long telegramUserId,
        string rawAddress,
        string alias,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new AddTrackedWalletCommand(
                TelegramUserId: telegramUserId,
                RawAddress: rawAddress,
                Alias: alias),
            cancellationToken);

        if (result.IsFailure)
        {
            return new TelegramCommandResponse(TelegramCommandText.FormatError(result.Error));
        }

        var wallet = result.Value;
        var botUsername = await botUsernameProvider.GetBotUsernameAsync(cancellationToken);
        var responseText =
            TelegramWalletCommandProcessorText.WalletAdded(FormatWatchlistLine(wallet, botUsername), TelegramCommandText.FormatDecimal(wallet.MinimumUsdValue), TelegramCommandText.HtmlEncode(TelegramCommandText.FormatChains(wallet.EnabledChains)));

        return new TelegramCommandResponse(responseText);
    }

    private static bool TryParseAddArguments(
        string arguments,
        out string rawAddress,
        out string? alias,
        out string errorText)
    {
        rawAddress = string.Empty;
        alias = null;
        errorText = string.Empty;

        if (string.IsNullOrWhiteSpace(arguments))
        {
            errorText = TelegramWalletCommandProcessorText.AddUsage;
            return false;
        }

        var parts = arguments.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        rawAddress = parts[0];
        alias = parts.Length == 2 ? parts[1].Trim() : null;

        return true;
    }

    private static bool TryParseRemoveArguments(string arguments, out string rawAddress, out string errorText)
    {
        rawAddress = string.Empty;
        errorText = string.Empty;

        if (string.IsNullOrWhiteSpace(arguments))
        {
            errorText = TelegramWalletCommandProcessorText.RemoveUsage;
            return false;
        }

        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 1)
        {
            errorText = TelegramWalletCommandProcessorText.RemoveUsage;
            return false;
        }

        rawAddress = parts[0];
        return true;
    }

    private static string FormatWatchlistLine(TrackedWalletDetails wallet, string? botUsername)
    {
        var displayName = string.IsNullOrWhiteSpace(wallet.Alias)
            ? ShortAddress(wallet.Address)
            : wallet.Alias;
        var line =
            TelegramWalletCommandProcessorText.WalletListEntry(TelegramCommandText.HtmlEncode(displayName), TelegramCommandText.HtmlEncode(ShortAddress(wallet.Address)));

        if (!string.IsNullOrWhiteSpace(botUsername))
        {
            line += TelegramWalletCommandProcessorText.HtmlLink(BuildEditDeepLink(botUsername, wallet.Id));
        }

        return line;
    }

    private static string ShortAddress(string address)
        => address.Length <= 8 ? address : address[..8];

    private static string BuildEditDeepLink(string botUsername, Guid walletId)
    {
        var normalizedBotUsername = botUsername.Trim().TrimStart('@');
        return $"https://t.me/{Uri.EscapeDataString(normalizedBotUsername)}?start={TelegramInteractionTokens.EditWalletPrefix}{walletId:N}";
    }
}
