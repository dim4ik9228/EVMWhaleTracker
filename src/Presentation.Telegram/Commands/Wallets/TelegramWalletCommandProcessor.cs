using Application.Abstractions;
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
    ITelegramBotUsernameProvider botUsernameProvider)
    : ITelegramWalletCommandProcessor
{
    private const string AliasPrompt = "Please give a name to this wallet";

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

            pendingActions.Set(telegramUserId, PendingTelegramAction.AddWalletAlias(rawAddress));
            return new TelegramCommandResponse(AliasPrompt);
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
            return new TelegramCommandResponse("You are not tracking any wallets yet.");
        }

        var botUsername = await botUsernameProvider.GetBotUsernameAsync(cancellationToken);
        var lines = new List<string>
        {
            "Your Wallets Watchlist:",
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
            $"""
            Tracked wallet removed successfully.

            Address: <code>{TelegramCommandText.HtmlEncode(rawAddress)}</code>
            """);
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
            $"""
            Added to watchlist ✅

            {FormatWatchlistLine(wallet, botUsername)}
            Minimum USD: <code>${TelegramCommandText.FormatDecimal(wallet.MinimumUsdValue)}</code>
            Networks: <code>{TelegramCommandText.HtmlEncode(TelegramCommandText.FormatChains(wallet.EnabledChains))}</code>
            """;

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
            errorText = "Usage: /add &lt;address&gt; [name]";
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
            errorText = "Usage: /remove &lt;address&gt;";
            return false;
        }

        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 1)
        {
            errorText = "Usage: /remove &lt;address&gt;";
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
            $"<b>{TelegramCommandText.HtmlEncode(displayName)}</b> · <code>{TelegramCommandText.HtmlEncode(ShortAddress(wallet.Address))}</code>";

        if (!string.IsNullOrWhiteSpace(botUsername))
        {
            line += $" | <a href=\"{BuildEditDeepLink(botUsername, wallet.Id)}\">✏️</a>";
        }

        return line;
    }

    private static string ShortAddress(string address)
        => address.Length <= 8 ? address : address[..8];

    private static string BuildEditDeepLink(string botUsername, Guid walletId)
    {
        var normalizedBotUsername = botUsername.Trim().TrimStart('@');
        return $"https://t.me/{Uri.EscapeDataString(normalizedBotUsername)}?start=edit_wallet_{walletId:N}";
    }
}
