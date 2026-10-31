using Application.Abstractions;
using Application.Messaging;
using Domain.Common;
using Domain.ValueObjects;

namespace Presentation.Telegram.Services;

/// <summary>
/// Enriches raw notification payloads with alias, address-label, ENS, and short-address display names.
/// </summary>
public sealed class TelegramNotificationEnricher(IAddressLabelResolver addressLabelResolver)
    : ITelegramNotificationEnricher
{
    public Task<EnrichedTelegramNotification> EnrichAsync(
        NotificationPayload payload,
        CancellationToken cancellationToken)
        => EnrichAsync(AggregatedNotificationPayload.FromSingle(payload), cancellationToken);

    public async Task<EnrichedTelegramNotification> EnrichAsync(
        AggregatedNotificationPayload payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var trackedWalletText = await ResolveTrackedWalletTextAsync(
            payload.Chain,
            payload.TrackedWalletAddress,
            payload.TrackedWalletAlias,
            cancellationToken);

        var lines = new List<EnrichedTelegramTransferLine>(payload.Lines.Count);
        foreach (var line in payload.Lines)
        {
            lines.Add(new EnrichedTelegramTransferLine(
                line.Operation,
                line.CounterpartyAddress,
                await ResolveCounterpartyTextAsync(
                    payload.Chain,
                    line.CounterpartyAddress,
                    line.CounterpartyAlias,
                    cancellationToken),
                line.AssetSymbol,
                line.AssetName,
                line.TokenContractAddress,
                line.Amount,
                line.UsdValue));
        }

        return new EnrichedTelegramNotification(
            payload.TelegramUserId,
            payload.TrackedWalletId,
            payload.Chain,
            payload.TrackedWalletAddress,
            trackedWalletText,
            payload.TransactionHash,
            payload.BlockNumber,
            payload.BlockTimestampUtc,
            lines, payload.Swap);
    }

    private async Task<string> ResolveTrackedWalletTextAsync(
        SupportedChain chain,
        string address,
        string? alias,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(alias))
        {
            return alias;
        }

        var addressResult = Address.Normalize(address);
        return addressResult.IsSuccess
            ? (await addressLabelResolver.ResolveAsync(chain, addressResult.Value, cancellationToken)).DisplayText
            : ShortAddress(address);
    }

    private async Task<string> ResolveCounterpartyTextAsync(
        SupportedChain chain,
        string? address,
        string? alias,
        CancellationToken cancellationToken)
    {
        var addressResult = Address.Normalize(address);
        if (addressResult.IsSuccess)
        {
            var resolution = await addressLabelResolver.ResolveAsync(chain, addressResult.Value, cancellationToken);
            if (resolution.IsLabeled)
            {
                return resolution.DisplayText;
            }
        }

        return string.IsNullOrWhiteSpace(alias)
            ? ShortAddress(address)
            : alias;
    }

    private static string ShortAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return TelegramNotificationRendererText.Unknown;
        }

        return address.Length <= 12
            ? address
            : $"{address[..6]}...{address[^4..]}";
    }
}
