using Application.Messaging;
using Infrastructure.Blockchain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Workers;

public sealed class BlockchainIndexerWorker : BackgroundService
{
    private readonly OnChainPipelineChannels _channels;
    private readonly IBlockchainBlockSource _blockSource;
    private readonly BlockchainIndexerOptions _options;
    private readonly ILogger<BlockchainIndexerWorker> _logger;

    public BlockchainIndexerWorker(
        OnChainPipelineChannels channels,
        IBlockchainBlockSource blockSource,
        IOptions<BlockchainIndexerOptions> options,
        ILogger<BlockchainIndexerWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(blockSource);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _channels = channels;
        _blockSource = blockSource;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextBlockToProcess = await InitializeCursorAsync(stoppingToken).ConfigureAwait(false);
        var useWebSocket = CanUseWebSocket();
        var inWebSocketFallback = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (useWebSocket)
            {
                try
                {
                    nextBlockToProcess = await StreamUntilDropAsync(nextBlockToProcess, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Blockchain websocket stream dropped. Falling back to RPC polling.");
                    useWebSocket = false;
                    inWebSocketFallback = true;
                }
            }

            nextBlockToProcess = await PollOnceAsync(nextBlockToProcess, stoppingToken).ConfigureAwait(false);

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await Task.Delay(inWebSocketFallback ? _options.WebSocketReconnectDelay : _options.PollInterval, stoppingToken)
                .ConfigureAwait(false);

            useWebSocket = CanUseWebSocket();
            if (useWebSocket)
            {
                inWebSocketFallback = false;
            }
        }
    }

    private bool CanUseWebSocket()
        => _options.PreferWebSocket && !string.IsNullOrWhiteSpace(_options.WebSocketRpcUrl);

    private async Task<ulong> InitializeCursorAsync(CancellationToken cancellationToken)
    {
        if (_options.StartBlockNumber.HasValue)
        {
            return await CatchUpFromAsync(_options.StartBlockNumber.Value, cancellationToken).ConfigureAwait(false);
        }

        var latestBlock = await _blockSource.GetLatestBlockNumberAsync(cancellationToken).ConfigureAwait(false);
        return latestBlock + 1;
    }

    private async Task<ulong> StreamUntilDropAsync(ulong nextBlockToProcess, CancellationToken cancellationToken)
    {
        await foreach (var headBlockNumber in _blockSource.StreamNewBlockNumbersAsync(cancellationToken).ConfigureAwait(false))
        {
            if (headBlockNumber + 1 <= nextBlockToProcess)
            {
                continue;
            }

            nextBlockToProcess = await PublishRangeAsync(nextBlockToProcess, headBlockNumber, cancellationToken).ConfigureAwait(false);
        }

        return nextBlockToProcess;
    }

    private async Task<ulong> PollOnceAsync(ulong nextBlockToProcess, CancellationToken cancellationToken)
    {
        var latestBlockNumber = await _blockSource.GetLatestBlockNumberAsync(cancellationToken).ConfigureAwait(false);
        if (nextBlockToProcess > latestBlockNumber)
        {
            return nextBlockToProcess;
        }

        return await PublishRangeAsync(nextBlockToProcess, latestBlockNumber, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ulong> CatchUpFromAsync(ulong startBlockNumber, CancellationToken cancellationToken)
    {
        var latestBlockNumber = await _blockSource.GetLatestBlockNumberAsync(cancellationToken).ConfigureAwait(false);
        if (startBlockNumber > latestBlockNumber)
        {
            return startBlockNumber;
        }

        return await PublishRangeAsync(startBlockNumber, latestBlockNumber, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ulong> PublishRangeAsync(ulong startBlockNumber, ulong endBlockNumber, CancellationToken cancellationToken)
    {
        var currentBlockNumber = startBlockNumber;
        while (currentBlockNumber <= endBlockNumber)
        {
            var block = await _blockSource.GetBlockAsync(currentBlockNumber, cancellationToken).ConfigureAwait(false);
            if (block is not null)
            {
                await _channels.RawBlockDataChannel.Writer.WriteAsync(block, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("Block {BlockNumber} could not be loaded and was skipped.", currentBlockNumber);
            }

            currentBlockNumber++;
        }

        return endBlockNumber + 1;
    }
}
