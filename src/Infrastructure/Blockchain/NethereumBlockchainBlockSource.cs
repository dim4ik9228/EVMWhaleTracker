using System.Numerics;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Application.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.WebSocketClient;
using Nethereum.JsonRpc.WebSocketStreamingClient;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.RPC.Reactive.Eth.Subscriptions;
using Nethereum.Web3;

namespace Infrastructure.Blockchain;

/// <summary>
/// Nethereum-backed native block source. Kept available for native transfer tracking when block scanning is enabled.
/// </summary>
public sealed class NethereumBlockchainBlockSource : IBlockchainBlockSource
{
    private readonly Web3 _web3;
    private readonly string? webSocketRpcUrl;
    private readonly bool _enableRpcDiagnostics;
    private readonly ILogger<NethereumBlockchainBlockSource> _logger;
    private readonly IDisposable? _ownedTransport;

    public NethereumBlockchainBlockSource(
        IOptions<BlockchainIndexerOptions> options,
        ILogger<NethereumBlockchainBlockSource> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var settings = options.Value;
        _logger = logger;
        webSocketRpcUrl = settings.WebSocketRpcUrl;
        _enableRpcDiagnostics = settings.EnableRpcDiagnostics;
        Chain = settings.Chain;

        if (!string.IsNullOrWhiteSpace(settings.HttpRpcUrl))
        {
            _web3 = new Web3(settings.HttpRpcUrl);
        }
        else if (!string.IsNullOrWhiteSpace(settings.WebSocketRpcUrl))
        {
            var requestClient = new WebSocketClient(settings.WebSocketRpcUrl);
            _ownedTransport = requestClient;
            _web3 = new Web3(requestClient);
        }
        else
        {
            throw new InvalidOperationException("Either HttpRpcUrl or WebSocketRpcUrl must be configured for the blockchain indexer.");
        }
    }

    private Domain.Common.SupportedChain Chain { get; }

    public void Dispose()
    {
        _ownedTransport?.Dispose();
    }

    public async Task<ulong> GetLatestBlockNumberAsync(CancellationToken cancellationToken)
    {
        var blockNumber = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
        return checked((ulong)blockNumber.Value);
    }

    public async Task<RawBlockData?> GetBlockAsync(ulong blockNumber, CancellationToken cancellationToken)
    {
        var stopwatch = _enableRpcDiagnostics ? Stopwatch.StartNew() : null;
        var block = await _web3.Eth.Blocks.GetBlockWithTransactionsByNumber
            .SendRequestAsync(new HexBigInteger(new BigInteger(blockNumber)));

        if (block is null)
        {
            if (_enableRpcDiagnostics)
            {
                _logger.LogInformation("Block {BlockNumber} returned no data from RPC.", blockNumber);
            }

            return null;
        }

        var transactions = new List<RawTransactionData>(block.Transactions.Length);

        foreach (var transaction in block.Transactions)
        {
            transactions.Add(new RawTransactionData(
                TransactionHash: transaction.TransactionHash,
                FromAddress: transaction.From,
                ToAddress: transaction.To,
                Value: Web3.Convert.FromWei(transaction.Value?.Value ?? BigInteger.Zero)));
        }

        var timestamp = block.Timestamp?.Value is { } unixSeconds
            ? DateTimeOffset.FromUnixTimeSeconds((long)unixSeconds)
            : DateTimeOffset.UnixEpoch;

        var rawBlockData = new RawBlockData(
            Chain: Chain,
            BlockNumber: block.Number?.Value is { } value ? checked((ulong)value) : blockNumber,
            BlockHash: block.BlockHash ?? string.Empty,
            BlockTimestampUtc: timestamp,
            Transactions: transactions,
            Logs: Array.Empty<RawLogData>());

        if (_enableRpcDiagnostics)
        {
            stopwatch?.Stop();
            _logger.LogDebug(
                "Loaded block {BlockNumber}: transactions={TransactionCount}, elapsedMs={ElapsedMilliseconds}.",
                rawBlockData.BlockNumber,
                transactions.Count,
                stopwatch?.ElapsedMilliseconds ?? 0);
        }

        return rawBlockData;
    }

    public async IAsyncEnumerable<ulong> StreamNewBlockNumbersAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(webSocketRpcUrl))
        {
            throw new InvalidOperationException("WebSocketRpcUrl must be configured to stream new block headers.");
        }

        var client = new StreamingWebSocketClient(webSocketRpcUrl);
        var subscription = new EthNewBlockHeadersObservableSubscription(client);
        var queue = Channel.CreateUnbounded<ulong>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        using var observerRegistration = subscription.GetSubscriptionDataResponsesAsObservable().Subscribe(
            new ChannelObserver<Block>(blockHeader =>
            {
                if (blockHeader.Number?.Value is { } number)
                {
                    queue.Writer.TryWrite(checked((ulong)number));
                }
            },
            exception => queue.Writer.TryComplete(exception),
            () => queue.Writer.TryComplete()));

        try
        {
            await client.StartAsync();
            await subscription.SubscribeAsync();

            while (await queue.Reader.WaitToReadAsync(cancellationToken))
            {
                while (queue.Reader.TryRead(out var blockNumber))
                {
                    yield return blockNumber;
                }
            }
        }
        finally
        {
            try
            {
                await subscription.UnsubscribeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ignoring websocket unsubscribe failure.");
            }

            try
            {
                await client.StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ignoring websocket stop failure.");
            }

            client.Dispose();
        }
    }

    private sealed class ChannelObserver<T> : IObserver<T>
    {
        private readonly Action<T> _onNext;
        private readonly Action<Exception> _onError;
        private readonly Action _onCompleted;

        public ChannelObserver(Action<T> onNext, Action<Exception> onError, Action onCompleted)
        {
            _onNext = onNext;
            _onError = onError;
            _onCompleted = onCompleted;
        }

        public void OnCompleted() => _onCompleted();

        public void OnError(Exception error) => _onError(error);

        public void OnNext(T value) => _onNext(value);
    }
}
