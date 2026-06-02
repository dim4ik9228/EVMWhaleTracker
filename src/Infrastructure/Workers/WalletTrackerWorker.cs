using Application.Abstractions;
using Application.Messaging;
using Domain.ValueObjects;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Workers;

public sealed class WalletTrackerWorker : BackgroundService
{
    private readonly OnChainPipelineChannels _channels;
    private readonly ITrackedWalletCacheService _trackedWalletCache;
    private readonly ILogger<WalletTrackerWorker> _logger;

    public WalletTrackerWorker(
        OnChainPipelineChannels channels,
        ITrackedWalletCacheService trackedWalletCache,
        ILogger<WalletTrackerWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(trackedWalletCache);
        ArgumentNullException.ThrowIfNull(logger);

        _channels = channels;
        _trackedWalletCache = trackedWalletCache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var block in _channels.RawBlockDataChannel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            foreach (var transaction in block.Transactions)
            {
                await ProcessAddressMatchAsync(
                    transaction.TransactionHash,
                    transaction.FromAddress,
                    transaction.ToAddress,
                    transaction.Value,
                    block,
                    stoppingToken).ConfigureAwait(false);
            }

            foreach (var log in block.Logs)
            {
                await ProcessLogMatchAsync(log, block, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessAddressMatchAsync(
        string transactionHash,
        string? fromAddress,
        string? toAddress,
        decimal value,
        RawBlockData block,
        CancellationToken cancellationToken)
    {
        await ProcessSingleAddressAsync(transactionHash, fromAddress, "outgoing transfer", value, block, cancellationToken)
            .ConfigureAwait(false);
        await ProcessSingleAddressAsync(transactionHash, toAddress, "incoming transfer", value, block, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ProcessSingleAddressAsync(
        string transactionHash,
        string? rawAddress,
        string directionLabel,
        decimal value,
        RawBlockData block,
        CancellationToken cancellationToken)
    {
        var normalizedAddressResult = Address.Normalize(rawAddress);
        if (!normalizedAddressResult.IsSuccess)
        {
            return;
        }

        var normalizedAddress = normalizedAddressResult.Value;
        if (!_trackedWalletCache.Contains(normalizedAddress))
        {
            return;
        }

        if (!_trackedWalletCache.TryGetTelegramUserIds(normalizedAddress, out var telegramUserIds))
        {
            _logger.LogDebug("Tracked address {Address} has no associated Telegram user ids.", normalizedAddress);
            return;
        }

        foreach (var telegramUserId in telegramUserIds)
        {
            var payload = new NotificationPayload(
                TelegramUserId: telegramUserId,
                MarkdownMessage: BuildMessage(normalizedAddress, directionLabel, value, transactionHash, block),
                WalletAddress: normalizedAddress.Value,
                TransactionHash: transactionHash);

            await _channels.NotificationPayloadChannel.Writer.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessLogMatchAsync(RawLogData log, RawBlockData block, CancellationToken cancellationToken)
    {
        var normalizedAddressResult = Address.Normalize(log.ContractAddress);
        if (!normalizedAddressResult.IsSuccess)
        {
            return;
        }

        var normalizedAddress = normalizedAddressResult.Value;
        if (!_trackedWalletCache.Contains(normalizedAddress))
        {
            return;
        }

        if (!_trackedWalletCache.TryGetTelegramUserIds(normalizedAddress, out var telegramUserIds))
        {
            return;
        }

        foreach (var telegramUserId in telegramUserIds)
        {
            var payload = new NotificationPayload(
                TelegramUserId: telegramUserId,
                MarkdownMessage: BuildLogMessage(normalizedAddress, log, block),
                WalletAddress: normalizedAddress.Value,
                TransactionHash: log.TransactionHash);

            await _channels.NotificationPayloadChannel.Writer.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildMessage(Address address, string directionLabel, decimal value, string transactionHash, RawBlockData block)
    {
        return
            $"*Wallet activity detected*\n" +
            $"Address: `{address.Value}`\n" +
            $"Direction: `{directionLabel}`\n" +
            $"Value: `{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}`\n" +
            $"Tx: `{transactionHash}`\n" +
            $"Block: `{block.BlockNumber}`";
    }

    private static string BuildLogMessage(Address address, RawLogData log, RawBlockData block)
    {
        var topics = log.Topics.Count == 0
            ? "none"
            : string.Join(", ", log.Topics.Select(topic => $"`{topic}`"));

        return
            $"*Wallet activity detected*\n" +
            $"Address: `{address.Value}`\n" +
            $"Log tx: `{log.TransactionHash}`\n" +
            $"Topics: {topics}\n" +
            $"Block: `{block.BlockNumber}`";
    }
}
