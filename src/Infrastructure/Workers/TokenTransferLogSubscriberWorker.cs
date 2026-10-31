using System.Globalization;
using System.Threading.Channels;
using Application.Abstractions;
using Application.Messaging;
using Domain.ValueObjects;
using Infrastructure.Blockchain;
using Infrastructure.Swaps;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nethereum.JsonRpc.Client;
using Nethereum.JsonRpc.WebSocketStreamingClient;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.RPC.Reactive.Eth.Subscriptions;

namespace Infrastructure.Workers;

/// <summary>
/// Subscribes to ERC-20/BEP-20 Transfer logs for tracked wallets and forwards matching logs into the processing pipeline.
/// </summary>
public sealed class TokenTransferLogSubscriberWorker : BackgroundService
{
    private static readonly TimeSpan EmptyWalletDelay = TimeSpan.FromSeconds(5);

    private readonly OnChainPipelineChannels _channels;
    private readonly ITrackedAddressCacheService _trackedAddressCache;
    private readonly BlockchainNetworkOptions _network;
    private readonly ILogger<TokenTransferLogSubscriberWorker> _logger;

    public TokenTransferLogSubscriberWorker(
        OnChainPipelineChannels channels,
        ITrackedAddressCacheService trackedAddressCache,
        BlockchainNetworkOptions network,
        ILogger<TokenTransferLogSubscriberWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(trackedAddressCache);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(logger);

        _channels = channels;
        _trackedAddressCache = trackedAddressCache;
        _network = network;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_network.BuildWebSocketRpcUrl()))
        {
            _logger.LogWarning(
                TokenTransferLogSubscriberWorkerDiagnostics.SubscriberDisabled,
                _network.Chain);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var (trackedAddresses, cacheVersion) = GetStableTrackedAddressSnapshot();
            if (trackedAddresses.Count == 0)
            {
                await Task.Delay(EmptyWalletDelay, stoppingToken);
                continue;
            }

            try
            {
                await SubscribeUntilRefreshOrDropAsync(trackedAddresses, cacheVersion, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (IsExpectedWebSocketDrop(ex))
            {
                _logger.LogDebug(
                    ex,
                    TokenTransferLogSubscriberWorkerDiagnostics.SubscriptionDropped,
                    _network.Chain);
                await Task.Delay(_network.WebSocketReconnectDelay, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    TokenTransferLogSubscriberWorkerDiagnostics.SubscriptionDropped,
                    _network.Chain);
                await Task.Delay(_network.WebSocketReconnectDelay, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Opens websocket subscriptions for the current cache snapshot, then refreshes periodically to pick up wallet changes.
    /// </summary>
    private async Task SubscribeUntilRefreshOrDropAsync(
        IReadOnlyCollection<Address> trackedAddresses,
        long cacheVersion,
        CancellationToken stoppingToken)
    {
        var filters = Erc20TransferLogFilterBuilder.Build(trackedAddresses);
        if (filters.Count == 0)
        {
            await Task.Delay(EmptyWalletDelay, stoppingToken);
            return;
        }

        using var client = new StreamingWebSocketClient(_network.BuildWebSocketRpcUrl());
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var connectionToken = connection.Token;
        var queue = Channel.CreateBounded<FilterLog>(new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.Wait,
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
                        log =>
                        {
                            if (!queue.Writer.TryWrite(log))
                            {
                                _logger.LogWarning(SwapDiagnostics.IngressGap, _network.Chain);
                                queue.Writer.TryComplete(new InvalidOperationException(SwapDiagnostics.IngressGap));
                                connection.Cancel();
                            }
                        },
                        exception => queue.Writer.TryComplete(exception),
                        () => { })));
            }

            await client.StartAsync();

            for (var i = 0; i < filters.Count; i++)
            {
                await subscriptions[i].SubscribeAsync(ToNethereumFilter(filters[i]), null);
            }

            _logger.LogInformation(
                TokenTransferLogSubscriberWorkerDiagnostics.SubscribedToERCTransferLogsOnForTracked,
                _network.Chain,
                trackedAddresses.Count,
                filters.Count);

            var cacheChangeTask = _trackedAddressCache.WaitForChangeAsync(cacheVersion, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var logAvailableTask = queue.Reader.WaitToReadAsync(connectionToken).AsTask();
                var completedTask = await Task.WhenAny(logAvailableTask, cacheChangeTask);
                if (completedTask == cacheChangeTask)
                {
                    await cacheChangeTask;
                    _logger.LogDebug(TokenTransferLogSubscriberWorkerDiagnostics.SubscriptionsRefreshing, _network.Chain);
                    break;
                }

                if (!await logAvailableTask)
                {
                    break;
                }

                while (queue.Reader.TryRead(out var log))
                {
                    var logKey = BuildLogKey(log);
                    if (!seenLogs.Add(logKey))
                    {
                        continue;
                    }
                    // Per-connection dedup is a bounded optimization; durable enrichment identities
                    // provide replay protection, and removed logs have their own version key.
                    if (seenLogs.Count > 4096) seenLogs.Clear();

                    var block = ToRawBlockData(log);
                    await _channels.ForChain(_network.Chain).Writer.WriteAsync(block, connectionToken);
                }
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
                    _logger.LogDebug(ex, TokenTransferLogSubscriberWorkerDiagnostics.UnsubscribeFailed, _network.Chain);
                }
            }

            try
            {
                await client.StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, TokenTransferLogSubscriberWorkerDiagnostics.WebSocketStopFailed, _network.Chain);
            }
        }
    }

    /// <summary>
    /// Adapts a pushed log into the existing raw pipeline contract consumed by WalletTrackerWorker.
    /// </summary>
    private RawBlockData ToRawBlockData(FilterLog log)
        => new(
            Chain: _network.Chain,
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
                    Data: log.Data ?? string.Empty,
                    LogIndex: log.LogIndex is null ? null : checked((ulong)log.LogIndex.Value),
                    Removed: log.Removed,
                    BlockHash: log.BlockHash,
                    ObservedAtUtc: DateTimeOffset.UtcNow)
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
        return $"{log.BlockHash}:{transactionHash}:{logIndex}:{log.Removed}";
    }

    private (IReadOnlyCollection<Domain.ValueObjects.Address> Addresses, long Version) GetStableTrackedAddressSnapshot()
    {
        while (true)
        {
            var versionBeforeSnapshot = _trackedAddressCache.Version;
            var trackedAddresses = _trackedAddressCache.Snapshot();
            var versionAfterSnapshot = _trackedAddressCache.Version;
            if (versionBeforeSnapshot == versionAfterSnapshot)
            {
                return (trackedAddresses, versionAfterSnapshot);
            }
        }
    }

    private static bool IsExpectedWebSocketDrop(Exception exception)
        => exception is RpcResponseException { Message: TokenTransferLogSubscriberWorkerDiagnostics.ClientConnectionError };

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
