using System.Globalization;
using System.Threading.Channels;
using Application.Abstractions;
using Application.Messaging;
using Infrastructure.Blockchain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.JsonRpc.WebSocketStreamingClient;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.RPC.Reactive.Eth.Subscriptions;

namespace Infrastructure.Workers;

public sealed class TokenTransferLogSubscriberWorker : BackgroundService
{
    private static readonly TimeSpan EmptyWalletDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SubscriptionRefreshInterval = TimeSpan.FromMinutes(1);

    private readonly OnChainPipelineChannels _channels;
    private readonly ITrackedAddressCacheService _trackedAddressCache;
    private readonly BlockchainIndexerOptions _options;
    private readonly ILogger<TokenTransferLogSubscriberWorker> _logger;

    public TokenTransferLogSubscriberWorker(
        OnChainPipelineChannels channels,
        ITrackedAddressCacheService trackedAddressCache,
        IOptions<BlockchainIndexerOptions> options,
        ILogger<TokenTransferLogSubscriberWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(trackedAddressCache);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _channels = channels;
        _trackedAddressCache = trackedAddressCache;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.WebSocketRpcUrl))
        {
            _logger.LogWarning("Token transfer log subscriber is disabled because BlockchainIndexer:WebSocketRpcUrl is not configured.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var trackedAddresses = _trackedAddressCache.Snapshot();
            if (trackedAddresses.Count == 0)
            {
                await Task.Delay(EmptyWalletDelay, stoppingToken);
                continue;
            }

            try
            {
                await SubscribeUntilRefreshOrDropAsync(trackedAddresses, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Token transfer log websocket subscription dropped. Reconnecting after delay.");
                await Task.Delay(_options.WebSocketReconnectDelay, stoppingToken);
            }
        }
    }

    private async Task SubscribeUntilRefreshOrDropAsync(
        IReadOnlyCollection<Domain.ValueObjects.Address> trackedAddresses,
        CancellationToken stoppingToken)
    {
        var filters = Erc20TransferLogFilterBuilder.Build(trackedAddresses);
        if (filters.Count == 0)
        {
            await Task.Delay(EmptyWalletDelay, stoppingToken);
            return;
        }

        using var client = new StreamingWebSocketClient(_options.WebSocketRpcUrl);
        var queue = Channel.CreateUnbounded<FilterLog>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        var subscriptions = new List<EthLogsObservableSubscription>(filters.Count);
        var observerRegistrations = new List<IDisposable>(filters.Count);
        var seenLogs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var filter in filters)
            {
                var subscription = new EthLogsObservableSubscription(client);
                subscriptions.Add(subscription);
                observerRegistrations.Add(subscription.GetSubscriptionDataResponsesAsObservable().Subscribe(
                    new ChannelObserver<FilterLog>(
                        log => queue.Writer.TryWrite(log),
                        exception => queue.Writer.TryComplete(exception),
                        () => { })));
            }

            await client.StartAsync();

            for (var i = 0; i < filters.Count; i++)
            {
                await subscriptions[i].SubscribeAsync(ToNethereumFilter(filters[i]), null);
            }

            _logger.LogInformation(
                "Subscribed to ERC-20 transfer logs for {TrackedAddressCount} tracked wallets using {FilterCount} websocket filters.",
                trackedAddresses.Count,
                filters.Count);

            using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            refreshCts.CancelAfter(SubscriptionRefreshInterval);

            try
            {
                while (await queue.Reader.WaitToReadAsync(refreshCts.Token))
                {
                    while (queue.Reader.TryRead(out var log))
                    {
                        var logKey = BuildLogKey(log);
                        if (!seenLogs.Add(logKey))
                        {
                            continue;
                        }

                        var block = ToRawBlockData(log);
                        await _channels.RawBlockDataChannel.Writer.WriteAsync(block, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogDebug("Refreshing ERC-20 transfer log subscriptions.");
            }
        }
        finally
        {
            foreach (var observerRegistration in observerRegistrations)
            {
                observerRegistration.Dispose();
            }

            foreach (var subscription in subscriptions)
            {
                try
                {
                    await subscription.UnsubscribeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Ignoring token transfer log unsubscribe failure.");
                }
            }

            try
            {
                await client.StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ignoring token transfer log websocket stop failure.");
            }
        }
    }

    private RawBlockData ToRawBlockData(FilterLog log)
        => new(
            Chain: _options.Chain,
            BlockNumber: log.BlockNumber?.Value is { } blockNumber ? checked((ulong)blockNumber) : 0,
            BlockHash: log.BlockHash ?? string.Empty,
            BlockTimestampUtc: DateTimeOffset.UtcNow,
            Transactions: Array.Empty<RawTransactionData>(),
            Logs:
            [
                new RawLogData(
                    TransactionHash: log.TransactionHash ?? string.Empty,
                    ContractAddress: log.Address ?? string.Empty,
                    Topics: log.Topics?.Select(topic => topic?.ToString() ?? string.Empty).ToArray() ?? Array.Empty<string>(),
                    Data: log.Data ?? string.Empty)
            ]);

    private static NewFilterInput ToNethereumFilter(Erc20TransferLogFilter filter)
        => new()
        {
            Topics = filter.Topics.ToArray()
        };

    private static string BuildLogKey(FilterLog log)
    {
        var transactionHash = log.TransactionHash ?? string.Empty;
        var logIndex = log.LogIndex?.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        return $"{transactionHash}:{logIndex}";
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
