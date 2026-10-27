using Presentation.Telegram.Commands;
using Domain.Common;
using System.Globalization;
using System.Text;
using Application.Abstractions;
using Application.Messaging;
using Telegram.Bot.Types.Enums;

namespace Presentation.Telegram.Services;

/// <summary>
/// Builds compact MarkdownV2 wallet-activity notifications with explorer links.
/// </summary>
public sealed class TelegramNotificationRenderer(IChainMetadataProvider chainMetadataProvider)
    : ITelegramNotificationRenderer
{
    private const int MaxRenderedTransferLines = 10;

    private static readonly HashSet<char> MarkdownV2ReservedCharacters =
    [
        '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!'
    ];

    /// <summary>
    /// Renders the activity notification with explorer links and, when available, a Telegram edit deep link.
    /// </summary>
    public TelegramRenderedMessage Render(EnrichedTelegramNotification notification, string? botUsername = null)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var chainMetadata = chainMetadataProvider.Get(notification.Chain);

        var trackedWalletLink = Link(
            notification.TrackedWalletDisplayText,
            chainMetadata.GetAddressUrl(notification.TrackedWalletAddress));

        var transactionLink = Link(TelegramNotificationRendererText.TxHash, chainMetadata.GetTransactionUrl(notification.TransactionHash));
        var lines = new List<string>();
        foreach (var line in notification.Lines.Take(MaxRenderedTransferLines))
        {
            lines.Add(RenderLine(line, chainMetadata));
        }

        if (notification.Lines.Count > MaxRenderedTransferLines)
        {
            lines.Add(EscapeText(TelegramNotificationRendererText.AdditionalTransfers(notification.Lines.Count - MaxRenderedTransferLines)));
        }

        var header = $"{trackedWalletLink} \\| {EscapeText(chainMetadata.ShortName)}";
        if (!string.IsNullOrWhiteSpace(botUsername))
        {
            header += $" \\| {Link(TelegramNotificationRendererText.EditIcon, BuildEditDeepLink(botUsername, notification.TrackedWalletId))}";
        }

        var text =
            TelegramNotificationRendererText.Line(header) +
            TelegramNotificationRendererText.Line(string.Join('\n', lines)) +
            transactionLink;

        return new TelegramRenderedMessage(text, ParseMode.MarkdownV2);
    }

    private static string RenderLine(
        EnrichedTelegramTransferLine line,
        ChainMetadata chainMetadata)
    {
        var counterparty = string.IsNullOrWhiteSpace(line.CounterpartyAddress)
            ? EscapeText(TelegramNotificationRendererText.Unknown)
            : Link(line.CounterpartyDisplayText, chainMetadata.GetAddressUrl(line.CounterpartyAddress));

        var counterpartyRole = line.Operation == NotificationOperation.Received ? TelegramNotificationRendererText.From : TelegramNotificationRendererText.To;
        var operationText = line.Operation switch
        {
            NotificationOperation.Received => TelegramNotificationRendererText.Received,
            NotificationOperation.Sent => TelegramNotificationRendererText.Sent,
            NotificationOperation.SelfTransfer => TelegramNotificationRendererText.Self,
            _ => TelegramNotificationRendererText.Activity
        };

        var amount = EscapeText(FormatAmount(line.Amount));
        var symbol = string.IsNullOrWhiteSpace(line.TokenContractAddress)
            ? EscapeText(line.AssetSymbol)
            : Link(line.AssetSymbol, chainMetadata.GetTokenUrl(line.TokenContractAddress));
        var usdPart = line.UsdValue.HasValue
            ? $" {EscapeText($"(~{FormatUsd(line.UsdValue.Value)})")}"
            : string.Empty;

        return TelegramNotificationRendererText.TransferLine(EscapeText(operationText), amount, symbol, usdPart, EscapeText(counterpartyRole), counterparty);
    }

    private static string Link(string visibleText, string url)
        => $"[{EscapeText(visibleText)}]({EscapeUrl(url)})";

    /// <summary>
    /// Escapes visible text according to Telegram MarkdownV2 rules.
    /// </summary>
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

    private static string BuildEditDeepLink(string botUsername, Guid trackedWalletId)
    {
        var normalizedBotUsername = botUsername.Trim().TrimStart('@');
        return $"https://t.me/{Uri.EscapeDataString(normalizedBotUsername)}?start={TelegramInteractionTokens.EditWalletPrefix}{trackedWalletId:N}";
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
