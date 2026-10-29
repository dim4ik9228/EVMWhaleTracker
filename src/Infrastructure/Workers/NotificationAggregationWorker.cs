using System.Collections.Concurrent;
using Application.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Workers;

/// <summary>
/// Coalesces multiple wallet activity lines from the same transaction into one notification payload.
/// </summary>
public sealed class NotificationAggregationWorker : BackgroundService
{
    private static readonly TimeSpan DefaultDebounceInterval = TimeSpan.FromSeconds(1);

    private readonly OnChainPipelineChannels _channels;
    private readonly TimeSpan _debounceInterval;
    private readonly ILogger<NotificationAggregationWorker> _logger;
    private readonly ConcurrentDictionary<AggregationKey, PendingNotification> _pending = new();

    public NotificationAggregationWorker(
        OnChainPipelineChannels channels,
        ILogger<NotificationAggregationWorker> logger)
        : this(channels, DefaultDebounceInterval, logger)
    {
    }

    internal NotificationAggregationWorker(
        OnChainPipelineChannels channels,
        TimeSpan debounceInterval,
        ILogger<NotificationAggregationWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(logger);
        if (debounceInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(debounceInterval), NotificationAggregationWorkerDiagnostics.DebounceIntervalMustBePositive);
        }

        _channels = channels;
        _debounceInterval = debounceInterval;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var payload in _channels.NotificationPayloadChannel.Reader.ReadAllAsync(stoppingToken))
            {
                Add(payload, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during host shutdown.
        }
        finally
        {
            await FlushAllAsync(CancellationToken.None);
        }
    }

    private void Add(NotificationPayload payload, CancellationToken cancellationToken)
    {
        var key = AggregationKey.FromPayload(payload);
        var pending = _pending.GetOrAdd(key, _ => new PendingNotification(payload, _debounceInterval));

        pending.Add(payload, _debounceInterval);

        if (pending.TryStartFlushTask())
        {
            _ = FlushWhenQuietAsync(key, pending, cancellationToken);
        }
    }

    private async Task FlushWhenQuietAsync(
        AggregationKey key,
        PendingNotification pending,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var delay = pending.GetRemainingDelay();
                if (delay <= TimeSpan.Zero)
                {
                    break;
                }

                await Task.Delay(delay, cancellationToken);
            }

            if (_pending.TryRemove(key, out var removed))
            {
                await WriteAsync(removed.ToPayload(), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Pending items are flushed by ExecuteAsync when the worker stops.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, NotificationAggregationWorkerDiagnostics.AggregationFailed, key.TransactionHash);
        }
    }

    private async Task FlushAllAsync(CancellationToken cancellationToken)
    {
        foreach (var pair in _pending.ToArray())
        {
            if (_pending.TryRemove(pair.Key, out var pending))
            {
                await WriteAsync(pending.ToPayload(), cancellationToken);
            }
        }
    }

    private async Task WriteAsync(AggregatedNotificationPayload payload, CancellationToken cancellationToken)
    {
        await _channels.AggregatedNotificationPayloadChannel.Writer.WriteAsync(payload, cancellationToken);
        _logger.LogDebug(
            NotificationAggregationWorkerDiagnostics.NotificationAggregated,
            payload.Lines.Count,
            payload.TelegramUserId,
            payload.TransactionHash);
    }

    private sealed record AggregationKey(
        long TelegramUserId,
        Domain.Common.SupportedChain Chain,
        string TrackedWalletAddress,
        string TransactionHash, Guid WalletId, long? EntitlementRevision)
    {
        public static AggregationKey FromPayload(NotificationPayload payload)
            => new(
                payload.TelegramUserId,
                payload.Chain,
                payload.TrackedWalletAddress.ToLowerInvariant(),
                payload.TransactionHash.ToLowerInvariant(), payload.TrackedWalletId, payload.EntitlementRevision);
    }

    private sealed class PendingNotification
    {
        private readonly Lock _sync = new();
        private readonly List<NotificationTransferLine> _lines = [];
        private readonly long _telegramUserId;
        private readonly long? _entitlementRevision;
        private readonly DateTimeOffset? _entitlementValidUntil;
        private readonly Guid _trackedWalletId;
        private readonly Domain.Common.SupportedChain _chain;
        private readonly string _trackedWalletAddress;
        private readonly string? _trackedWalletAlias;
        private readonly string _transactionHash;
        private readonly ulong _blockNumber;
        private readonly DateTimeOffset _blockTimestampUtc;
        private DateTimeOffset _flushAfterUtc;
        private bool _flushTaskStarted;

        public PendingNotification(NotificationPayload firstPayload, TimeSpan debounceInterval)
        {
            _telegramUserId = firstPayload.TelegramUserId;
            _entitlementRevision = firstPayload.EntitlementRevision;
            _entitlementValidUntil = firstPayload.EntitlementValidUntil;
            _trackedWalletId = firstPayload.TrackedWalletId;
            _chain = firstPayload.Chain;
            _trackedWalletAddress = firstPayload.TrackedWalletAddress;
            _trackedWalletAlias = firstPayload.TrackedWalletAlias;
            _transactionHash = firstPayload.TransactionHash;
            _blockNumber = firstPayload.BlockNumber;
            _blockTimestampUtc = firstPayload.BlockTimestampUtc;
            _flushAfterUtc = DateTimeOffset.UtcNow.Add(debounceInterval);
        }

        public void Add(NotificationPayload payload, TimeSpan debounceInterval)
        {
            lock (_sync)
            {
                _lines.Add(NotificationTransferLine.FromPayload(payload));
                _flushAfterUtc = DateTimeOffset.UtcNow.Add(debounceInterval);
            }
        }

        public bool TryStartFlushTask()
        {
            lock (_sync)
            {
                if (_flushTaskStarted)
                {
                    return false;
                }

                _flushTaskStarted = true;
                return true;
            }
        }

        public TimeSpan GetRemainingDelay()
        {
            lock (_sync)
            {
                return _flushAfterUtc - DateTimeOffset.UtcNow;
            }
        }

        public AggregatedNotificationPayload ToPayload()
        {
            lock (_sync)
            {
                return new AggregatedNotificationPayload(
                    _telegramUserId,
                    _trackedWalletId,
                    _chain,
                    _trackedWalletAddress,
                    _trackedWalletAlias,
                    _transactionHash,
                    _blockNumber,
                    _blockTimestampUtc,
                    _lines.ToArray(), _entitlementRevision, _entitlementValidUntil);
            }
        }
    }
}
