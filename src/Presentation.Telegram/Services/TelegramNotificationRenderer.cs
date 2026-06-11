using System.Globalization;
using System.Text;
using Application.Abstractions;
using Application.Messaging;
using Telegram.Bot.Types.Enums;

namespace Presentation.Telegram.Services;

public sealed class TelegramNotificationRenderer(IChainMetadataProvider chainMetadataProvider)
    : ITelegramNotificationRenderer
{
    private static readonly HashSet<char> MarkdownV2ReservedCharacters =
    [
        '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!'
    ];

    public TelegramRenderedMessage Render(NotificationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var chainMetadata = chainMetadataProvider.Get(payload.Chain);
        var trackedWalletText = string.IsNullOrWhiteSpace(payload.TrackedWalletAlias)
            ? ShortAddress(payload.TrackedWalletAddress)
            : payload.TrackedWalletAlias;

        var trackedWalletLink = Link(
            trackedWalletText,
            chainMetadata.GetAddressUrl(payload.TrackedWalletAddress));

        var counterpartyText = string.IsNullOrWhiteSpace(payload.CounterpartyAlias)
            ? ShortAddress(payload.CounterpartyAddress)
            : payload.CounterpartyAlias;

        var counterparty = string.IsNullOrWhiteSpace(payload.CounterpartyAddress)
            ? EscapeText("unknown")
            : Link(counterpartyText, chainMetadata.GetAddressUrl(payload.CounterpartyAddress));

        var counterpartyRole = payload.Operation == NotificationOperation.Received ? "From" : "To";
        var operationText = payload.Operation switch
        {
            NotificationOperation.Received => "Received",
            NotificationOperation.Sent => "Sent",
            NotificationOperation.SelfTransfer => "Self",
            _ => "Activity"
        };

        var amount = EscapeText(FormatAmount(payload.Amount));
        var symbol = EscapeText(payload.AssetSymbol);
        var usdPart = payload.UsdValue.HasValue
            ? $" {EscapeText($"(~{FormatUsd(payload.UsdValue.Value)})")}"
            : string.Empty;

        var transactionLink = Link("Tx hash", chainMetadata.GetTransactionUrl(payload.TransactionHash));

        var text =
            $"{trackedWalletLink} \\| {EscapeText(chainMetadata.ShortName)}\n" +
            $"{EscapeText(operationText)}: {amount} {symbol}{usdPart} {EscapeText(counterpartyRole)}: {counterparty}\n" +
            transactionLink;

        return new TelegramRenderedMessage(text, ParseMode.MarkdownV2);
    }

    private static string Link(string visibleText, string url)
        => $"[{EscapeText(visibleText)}]({EscapeUrl(url)})";

    private static string EscapeText(string value)
    {
        var builder = new StringBuilder(value.Length * 2);
        foreach (var character in value)
        {
            if (character == '\\' || MarkdownV2ReservedCharacters.Contains(character))
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string EscapeUrl(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static string ShortAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return "unknown";
        }

        return address.Length <= 12
            ? address
            : $"{address[..6]}...{address[^4..]}";
    }

    private static string FormatAmount(decimal value)
        => value.ToString("0.########", CultureInfo.InvariantCulture);

    private static string FormatUsd(decimal value)
    {
        if (value < 1m)
        {
            return $"${value.ToString("0.00", CultureInfo.InvariantCulture)}";
        }

        return $"${decimal.Round(value, 0).ToString("0", CultureInfo.InvariantCulture)}";
    }
}
