using System.Globalization;
using System.Text;
using Application.Abstractions;
using Application.Messaging;
using Domain.Common;
using Domain.ValueObjects;
using Telegram.Bot.Types.Enums;

namespace Presentation.Telegram.Services;

/// <summary>
/// Builds compact MarkdownV2 wallet-activity notifications with explorer links.
/// </summary>
public sealed class TelegramNotificationRenderer(
    IChainMetadataProvider chainMetadataProvider,
    IAddressLabelResolver addressLabelResolver)
    : ITelegramNotificationRenderer
{
    private const int MaxRenderedTransferLines = 10;

    private static readonly HashSet<char> MarkdownV2ReservedCharacters =
    [
        '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!'
    ];

    public TelegramRenderedMessage Render(NotificationPayload payload)
        => Render(AggregatedNotificationPayload.FromSingle(payload));

    public TelegramRenderedMessage Render(AggregatedNotificationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var chainMetadata = chainMetadataProvider.Get(payload.Chain);
        var trackedWalletText = ResolveTrackedWalletText(
            payload.Chain,
            payload.TrackedWalletAddress,
            payload.TrackedWalletAlias);

        var trackedWalletLink = Link(
            trackedWalletText,
            chainMetadata.GetAddressUrl(payload.TrackedWalletAddress));

        var transactionLink = Link("Tx hash", chainMetadata.GetTransactionUrl(payload.TransactionHash));
        var lines = payload.Lines
            .Take(MaxRenderedTransferLines)
            .Select(line => RenderLine(payload.Chain, line, chainMetadata))
            .ToList();

        if (payload.Lines.Count > MaxRenderedTransferLines)
        {
            lines.Add(EscapeText($"...and {payload.Lines.Count - MaxRenderedTransferLines} more transfers"));
        }

        var text =
            $"{trackedWalletLink} \\| {EscapeText(chainMetadata.ShortName)}\n" +
            $"{string.Join('\n', lines)}\n" +
            transactionLink;

        return new TelegramRenderedMessage(text, ParseMode.MarkdownV2);
    }

    private string RenderLine(SupportedChain chain, NotificationTransferLine line, ChainMetadata chainMetadata)
    {
        var counterpartyText = ResolveCounterpartyText(chain, line.CounterpartyAddress, line.CounterpartyAlias);
        var counterparty = string.IsNullOrWhiteSpace(line.CounterpartyAddress)
            ? EscapeText("unknown")
            : Link(counterpartyText, chainMetadata.GetAddressUrl(line.CounterpartyAddress));

        var counterpartyRole = line.Operation == NotificationOperation.Received ? "From" : "To";
        var operationText = line.Operation switch
        {
            NotificationOperation.Received => "Received",
            NotificationOperation.Sent => "Sent",
            NotificationOperation.SelfTransfer => "Self",
            _ => "Activity"
        };

        var amount = EscapeText(FormatAmount(line.Amount));
        var symbol = string.IsNullOrWhiteSpace(line.TokenContractAddress)
            ? EscapeText(line.AssetSymbol)
            : Link(line.AssetSymbol, chainMetadata.GetTokenUrl(line.TokenContractAddress));
        var usdPart = line.UsdValue.HasValue
            ? $" {EscapeText($"(~{FormatUsd(line.UsdValue.Value)})")}"
            : string.Empty;

        return $"{EscapeText(operationText)}: {amount} {symbol}{usdPart} {EscapeText(counterpartyRole)}: {counterparty}";
    }

    private string ResolveTrackedWalletText(SupportedChain chain, string address, string? alias)
    {
        if (!string.IsNullOrWhiteSpace(alias))
        {
            return alias;
        }

        var addressResult = Address.Normalize(address);
        return addressResult.IsSuccess
            ? addressLabelResolver.Resolve(chain, addressResult.Value).DisplayText
            : ShortAddress(address);
    }

    private string ResolveCounterpartyText(SupportedChain chain, string? address, string? alias)
    {
        var addressResult = Address.Normalize(address);
        if (addressResult.IsSuccess)
        {
            var resolution = addressLabelResolver.Resolve(chain, addressResult.Value);
            if (resolution.IsLabeled)
            {
                return resolution.DisplayText;
            }
        }

        return string.IsNullOrWhiteSpace(alias)
            ? ShortAddress(address)
            : alias;
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
