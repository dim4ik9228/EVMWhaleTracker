using System.Globalization;
using System.Net;
using Application.TrackedWallets;
using Domain.Common;
using MediatR;

namespace Presentation.Telegram.Commands;

public sealed class TelegramCommandProcessor(ISender sender) : ITelegramCommandProcessor
{
    private static readonly string HelpText =
        """
        Welcome to EVMWhaleTracker.

        Available commands:
        <code>/add &lt;address&gt; [minimumValue]</code>
        <code>/list</code>
        <code>/remove &lt;address&gt;</code>

        Examples:
        <code>/add 0x52908400098527886E0F7030069857D2E4169EE7</code>
        <code>/add 0x52908400098527886e0f7030069857d2e4169ee7 1.5</code>
        <code>/remove 0x52908400098527886E0F7030069857D2E4169EE7</code>
        """;

    public async Task<TelegramCommandResponse?> ProcessAsync(TelegramCommandRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.MessageText))
        {
            return null;
        }

        if (!TryParseCommand(request.MessageText, out var command, out var arguments))
        {
            return null;
        }

        if (!request.IsPrivateChat)
        {
            return new TelegramCommandResponse("Please open this bot in a private chat to manage tracked wallets.");
        }

        return command switch
        {
            "start" => new TelegramCommandResponse(HelpText),
            "help" => new TelegramCommandResponse(HelpText),
            "add" => await HandleAddAsync(request.TelegramUserId, arguments, cancellationToken),
            "list" => await HandleListAsync(request.TelegramUserId, cancellationToken),
            "remove" => await HandleRemoveAsync(request.TelegramUserId, arguments, cancellationToken),
            _ => new TelegramCommandResponse(HelpText)
        };
    }

    private async Task<TelegramCommandResponse> HandleAddAsync(long telegramUserId, string arguments, CancellationToken cancellationToken)
    {
        if (!TryParseAddArguments(arguments, out var rawAddress, out var minimumTransactionValue, out var errorText))
        {
            return new TelegramCommandResponse(errorText);
        }

        var result = await sender.Send(
            new AddTrackedWalletCommand(
                TelegramUserId: telegramUserId,
                RawAddress: rawAddress,
                MinimumTransactionValue: minimumTransactionValue),
            cancellationToken);

        if (result.IsFailure)
        {
            return new TelegramCommandResponse(FormatError(result.Error));
        }

        var wallet = result.Value;
        var responseText =
            $"""
            Tracked wallet added successfully.

            Alias: <code>{HtmlEncode(wallet.Alias ?? "none")}</code>
            Address: <code>{HtmlEncode(wallet.Address)}</code>
            Minimum transaction value: <code>{wallet.MinimumTransactionValue.ToString(CultureInfo.InvariantCulture)}</code>
            Incoming transfers: <code>{FormatOnOff(wallet.TrackIncomingTransfers)}</code>
            Outgoing transfers: <code>{FormatOnOff(wallet.TrackOutgoingTransfers)}</code>
            Token transfers: <code>{FormatOnOff(wallet.TrackTokenTransfers)}</code>
            Enabled chains: <code>{HtmlEncode(FormatChains(wallet.EnabledChains))}</code>
            """;

        return new TelegramCommandResponse(responseText);
    }

    private async Task<TelegramCommandResponse> HandleListAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListTrackedWalletsQuery(telegramUserId), cancellationToken);
        if (result.IsFailure)
        {
            return new TelegramCommandResponse(FormatError(result.Error));
        }

        if (result.Value.Count == 0)
        {
            return new TelegramCommandResponse("You are not tracking any wallets yet.");
        }

        var lines = new List<string>
        {
            "Tracked wallets:"
        };

        for (var index = 0; index < result.Value.Count; index++)
        {
            var wallet = result.Value[index];
            lines.Add(
                $"""
                {index + 1}. <code>{HtmlEncode(wallet.Address)}</code>
                   Alias: <code>{HtmlEncode(wallet.Alias ?? "none")}</code>
                   Minimum transaction value: <code>{wallet.MinimumTransactionValue.ToString(CultureInfo.InvariantCulture)}</code>
                   Incoming: <code>{FormatOnOff(wallet.TrackIncomingTransfers)}</code>, Outgoing: <code>{FormatOnOff(wallet.TrackOutgoingTransfers)}</code>, Token: <code>{FormatOnOff(wallet.TrackTokenTransfers)}</code>
                   Enabled chains: <code>{HtmlEncode(FormatChains(wallet.EnabledChains))}</code>
                """.TrimEnd());
        }

        return new TelegramCommandResponse(string.Join(Environment.NewLine, lines));
    }

    private async Task<TelegramCommandResponse> HandleRemoveAsync(long telegramUserId, string arguments, CancellationToken cancellationToken)
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
            return new TelegramCommandResponse(FormatError(result.Error));
        }

        return new TelegramCommandResponse(
            $"""
            Tracked wallet removed successfully.

            Address: <code>{HtmlEncode(rawAddress)}</code>
            """);
    }

    private static bool TryParseCommand(string messageText, out string command, out string arguments)
    {
        command = string.Empty;
        arguments = string.Empty;

        var trimmed = messageText.Trim();
        if (!trimmed.StartsWith('/'))
        {
            return false;
        }

        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        command = NormalizeCommandToken(parts[0]);
        arguments = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        return !string.IsNullOrWhiteSpace(command);
    }

    private static string NormalizeCommandToken(string token)
    {
        var command = token.Trim();
        if (command.StartsWith('/'))
        {
            command = command[1..];
        }

        var atIndex = command.IndexOf('@');
        if (atIndex >= 0)
        {
            command = command[..atIndex];
        }

        return command.ToLowerInvariant();
    }

    private static bool TryParseAddArguments(string arguments, out string rawAddress, out decimal minimumTransactionValue, out string errorText)
    {
        rawAddress = string.Empty;
        minimumTransactionValue = 0m;
        errorText = string.Empty;

        if (string.IsNullOrWhiteSpace(arguments))
        {
            errorText = "Usage: /add &lt;address&gt; [minimumValue]";
            return false;
        }

        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 2)
        {
            errorText = "Usage: /add &lt;address&gt; [minimumValue]";
            return false;
        }

        rawAddress = parts[0];
        if (parts.Length == 2 && !TryParseDecimal(parts[1], out minimumTransactionValue))
        {
            errorText = "The minimum value must be a valid decimal number.";
            return false;
        }

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

    private static bool TryParseDecimal(string value, out decimal result)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result)
            || decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result);

    private static string FormatOnOff(bool value) => value ? "on" : "off";

    private static string FormatChains(IReadOnlyCollection<SupportedChain>? chains)
        => chains is null || chains.Count == 0
            ? "all"
            : string.Join(", ", chains.Select(chain => chain.ToString()));

    private static string FormatError(Error error)
        => HtmlEncode(error.Description);

    private static string HtmlEncode(string value)
        => WebUtility.HtmlEncode(value);
}
