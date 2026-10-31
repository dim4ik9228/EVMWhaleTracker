using Application.Abstractions;
using Application.Messaging;
using Application.Swaps;
using Domain.Common;
using Domain.ValueObjects;
using Infrastructure.Blockchain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Workers;

/// <summary>
/// Matches native transactions and token logs against tracked wallets and emits structured notification payloads.
/// </summary>
public sealed class WalletTrackerWorker : BackgroundService
{
    private readonly OnChainPipelineChannels _channels;
    private readonly ITrackedWalletCacheService _trackedWalletCache;
    private readonly IChainMetadataProvider _chainMetadataProvider;
    private readonly ITokenMetadataProvider _tokenMetadataProvider;
    private readonly IAssetPriceProvider _assetPriceProvider;
    private readonly Erc20TransferLogParser _erc20TransferLogParser;
    private readonly ILogger<WalletTrackerWorker> _logger;
    private readonly ISwapCandidateIntake? _swaps;

    public WalletTrackerWorker(
        OnChainPipelineChannels channels,
        ITrackedWalletCacheService trackedWalletCache,
        IChainMetadataProvider chainMetadataProvider,
        ITokenMetadataProvider tokenMetadataProvider,
        IAssetPriceProvider assetPriceProvider,
        Erc20TransferLogParser erc20TransferLogParser,
        ILogger<WalletTrackerWorker> logger, ISwapCandidateIntake? swaps = null)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(trackedWalletCache);
        ArgumentNullException.ThrowIfNull(chainMetadataProvider);
        ArgumentNullException.ThrowIfNull(tokenMetadataProvider);
        ArgumentNullException.ThrowIfNull(assetPriceProvider);
        ArgumentNullException.ThrowIfNull(erc20TransferLogParser);
        ArgumentNullException.ThrowIfNull(logger);

        _channels = channels;
        _trackedWalletCache = trackedWalletCache;
        _chainMetadataProvider = chainMetadataProvider;
        _tokenMetadataProvider = tokenMetadataProvider;
        _assetPriceProvider = assetPriceProvider;
        _erc20TransferLogParser = erc20TransferLogParser;
        _logger = logger;
        _swaps = swaps;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var readers = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var inputs = Enum.GetValues<SupportedChain>().Where(x => x != SupportedChain.Arc).Select(x => _channels.ForChain(x).Reader)
            .Append(_channels.RawBlockDataChannel.Reader);
        var tasks = inputs.Select(async reader =>
        {
        await foreach (var block in reader.ReadAllAsync(readers.Token))
        {
            foreach (var transaction in block.Transactions)
            {
                await ProcessNativeTransferAsync(transaction, block, stoppingToken);
            }

            foreach (var log in block.Logs)
            {
                await ProcessLogMatchAsync(log, block, stoppingToken);
            }
        }
        }).ToArray();
        await Task.WhenAny(tasks);
        readers.Cancel();
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) when (readers.IsCancellationRequested) { }
    }

    private async Task ProcessNativeTransferAsync(
        RawTransactionData transaction,
        RawBlockData block,
        CancellationToken cancellationToken)
    {
        var fromAddress = NormalizeAddressOrNull(transaction.FromAddress);
        var toAddress = NormalizeAddressOrNull(transaction.ToAddress);

        if (fromAddress is null && toAddress is null)
        {
            return;
        }

        var isSelfTransfer = fromAddress is not null && fromAddress.Equals(toAddress);

        if (fromAddress is not null)
        {
            await ProcessNativeAddressMatchAsync(
                trackedAddress: fromAddress,
                counterpartyAddress: toAddress,
                operation: isSelfTransfer ? NotificationOperation.SelfTransfer : NotificationOperation.Sent,
                transaction,
                block,
                cancellationToken);
        }

        if (toAddress is not null && !isSelfTransfer)
        {
            await ProcessNativeAddressMatchAsync(
                trackedAddress: toAddress,
                counterpartyAddress: fromAddress,
                operation: NotificationOperation.Received,
                transaction,
                block,
                cancellationToken);
        }
    }

    private async Task ProcessNativeAddressMatchAsync(
        Address trackedAddress,
        Address? counterpartyAddress,
        NotificationOperation operation,
        RawTransactionData transaction,
        RawBlockData block,
        CancellationToken cancellationToken)
    {
        if (!_trackedWalletCache.TryGetEntries(trackedAddress, out var entries))
        {
            return;
        }

        var chainMetadata = _chainMetadataProvider.Get(block.Chain);
        foreach (var entry in entries)
        {
            if (!ShouldNotify(entry, block.Chain, operation, isTokenTransfer: false))
            {
                continue;
            }

            var usdValue = await ResolveUsdValueAsync(
                block.Chain,
                tokenContractAddress: null,
                chainMetadata.NativeSymbol,
                transaction.Value,
                entry.MinimumUsdValue,
                cancellationToken);

            if (!usdValue.ShouldSend)
            {
                continue;
            }

            var payload = new NotificationPayload(
                TelegramUserId: entry.TelegramUserId,
                TrackedWalletId: entry.Id,
                Chain: block.Chain,
                Operation: operation,
                TrackedWalletAddress: trackedAddress.Value,
                TrackedWalletAlias: entry.Alias,
                CounterpartyAddress: counterpartyAddress?.Value,
                CounterpartyAlias: ResolveCounterpartyAlias(counterpartyAddress),
                TransactionHash: transaction.TransactionHash,
                AssetSymbol: chainMetadata.NativeSymbol,
                AssetName: chainMetadata.NativeSymbol,
                TokenContractAddress: null,
                Amount: transaction.Value,
                UsdValue: usdValue.Value,
                BlockNumber: block.BlockNumber,
                BlockTimestampUtc: block.BlockTimestampUtc, EntitlementRevision: entry.EntitlementRevision, EntitlementValidUntil: entry.EntitlementValidUntil);

            await _channels.NotificationPayloadChannel.Writer.WriteAsync(payload, cancellationToken);
        }
    }

    /// <summary>
    /// Parses ERC-20 Transfer logs and evaluates both sides of the transfer against tracked-wallet settings.
    /// </summary>
    private async Task ProcessLogMatchAsync(RawLogData log, RawBlockData block, CancellationToken cancellationToken)
    {
        if (_swaps is not null && (_swaps.IsEnabled(block.Chain) || log.Removed))
        {
            await _swaps.ObserveAsync(log, block, cancellationToken);
            return;
        }
        if (log.Removed) return;
        if (!_erc20TransferLogParser.TryParse(log, block.Chain, out var transferLog))
        {
            return;
        }

        var tokenMetadataResult = await _tokenMetadataProvider.GetAsync(
            block.Chain,
            transferLog.TokenContractAddress,
            cancellationToken);

        if (tokenMetadataResult.IsFailure)
        {
            _logger.LogDebug(
                WalletTrackerWorkerDiagnostics.MetadataUnavailable,
                transferLog.TransactionHash,
                transferLog.TokenContractAddress,
                tokenMetadataResult.Error.Description);
            return;
        }

        var amount = ConvertRawTokenAmount(transferLog.RawAmount, tokenMetadataResult.Value.Decimals);

        if (transferLog.FromAddress.Equals(transferLog.ToAddress))
        {
            await ProcessTokenAddressMatchAsync(
                transferLog.ToAddress,
                transferLog.FromAddress,
                NotificationOperation.SelfTransfer,
                transferLog,
                tokenMetadataResult.Value,
                amount,
                block,
                cancellationToken);

            return;
        }

        await ProcessTokenAddressMatchAsync(
            transferLog.ToAddress,
            transferLog.FromAddress,
            NotificationOperation.Received,
            transferLog,
            tokenMetadataResult.Value,
            amount,
            block,
            cancellationToken);

        await ProcessTokenAddressMatchAsync(
            transferLog.FromAddress,
            transferLog.ToAddress,
            NotificationOperation.Sent,
            transferLog,
            tokenMetadataResult.Value,
            amount,
            block,
            cancellationToken);
    }

    private async Task ProcessTokenAddressMatchAsync(
        Address trackedAddress,
        Address counterpartyAddress,
        NotificationOperation operation,
        Erc20TransferLog transferLog,
        TokenMetadata tokenMetadata,
        decimal amount,
        RawBlockData block,
        CancellationToken cancellationToken)
    {
        if (!_trackedWalletCache.TryGetEntries(trackedAddress, out var entries))
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (!ShouldNotify(entry, block.Chain, operation, isTokenTransfer: true))
            {
                continue;
            }

            var usdValue = await ResolveUsdValueAsync(
                block.Chain,
                transferLog.TokenContractAddress,
                tokenMetadata.Symbol,
                amount,
                entry.MinimumUsdValue,
                cancellationToken);

            if (!usdValue.ShouldSend)
            {
                continue;
            }

            var payload = new NotificationPayload(
                TelegramUserId: entry.TelegramUserId,
                TrackedWalletId: entry.Id,
                Chain: block.Chain,
                Operation: operation,
                TrackedWalletAddress: trackedAddress.Value,
                TrackedWalletAlias: entry.Alias,
                CounterpartyAddress: counterpartyAddress.Value,
                CounterpartyAlias: ResolveCounterpartyAlias(counterpartyAddress),
                TransactionHash: transferLog.TransactionHash,
                AssetSymbol: tokenMetadata.Symbol,
                AssetName: tokenMetadata.Name,
                TokenContractAddress: transferLog.TokenContractAddress,
                Amount: amount,
                UsdValue: usdValue.Value,
                BlockNumber: block.BlockNumber,
                BlockTimestampUtc: block.BlockTimestampUtc, EntitlementRevision: entry.EntitlementRevision, EntitlementValidUntil: entry.EntitlementValidUntil);

            await _channels.NotificationPayloadChannel.Writer.WriteAsync(payload, cancellationToken);
        }
    }

    private async Task<UsdResolution> ResolveUsdValueAsync(
        SupportedChain chain,
        string? tokenContractAddress,
        string assetSymbol,
        decimal amount,
        decimal minimumUsdValue,
        CancellationToken cancellationToken)
    {
        var priceResult = await _assetPriceProvider.GetUsdPriceAsync(
            chain,
            tokenContractAddress,
            assetSymbol,
            cancellationToken);

        if (priceResult.IsFailure)
        {
            _logger.LogWarning(
                minimumUsdValue > 0
                    ? WalletTrackerWorkerDiagnostics.UnpricedTransferSuppressed
                    : WalletTrackerWorkerDiagnostics.UnpricedTransferAllowed,
                assetSymbol,
                chain,
                priceResult.Error.Description);
            return new UsdResolution(minimumUsdValue == 0, null);
        }

        var value = amount * priceResult.Value.PriceUsd;
        return value >= minimumUsdValue
            ? new UsdResolution(true, value)
            : new UsdResolution(false, value);
    }

    private string? ResolveCounterpartyAlias(Address? counterpartyAddress)
    {
        if (counterpartyAddress is null)
        {
            return null;
        }

        return _trackedWalletCache.TryGetEntries(counterpartyAddress, out var entries)
            ? entries.FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.Alias))?.Alias
            : null;
    }

    private static bool ShouldNotify(
        TrackedWalletCacheEntry entry,
        SupportedChain chain,
        NotificationOperation operation,
        bool isTokenTransfer)
    {
        if (entry.EnabledChains.Count > 0 && !entry.EnabledChains.Contains(chain))
        {
            return false;
        }

        if (isTokenTransfer && !entry.TrackTokenTransfers)
        {
            return false;
        }

        return operation switch
        {
            NotificationOperation.Received => entry.TrackIncomingTransfers,
            NotificationOperation.Sent => entry.TrackOutgoingTransfers,
            NotificationOperation.SelfTransfer => entry.TrackIncomingTransfers || entry.TrackOutgoingTransfers,
            _ => false
        };
    }

    private static Address? NormalizeAddressOrNull(string? rawAddress)
    {
        var normalizedAddressResult = Address.Normalize(rawAddress);
        return normalizedAddressResult.IsSuccess ? normalizedAddressResult.Value : null;
    }

    private static decimal ConvertRawTokenAmount(System.Numerics.BigInteger rawAmount, int decimals)
    {
        var divisor = System.Numerics.BigInteger.Pow(10, decimals);
        var whole = System.Numerics.BigInteger.DivRem(rawAmount, divisor, out var remainder);
        return (decimal)whole + ((decimal)remainder / (decimal)divisor);
    }

    private sealed record UsdResolution(bool ShouldSend, decimal? Value);
}
