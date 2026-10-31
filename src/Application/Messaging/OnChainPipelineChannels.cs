using System.Threading.Channels;

namespace Application.Messaging;

/// <summary>
/// Bounded in-memory channels that connect ingestion, matching, and notification delivery workers.
/// </summary>
public sealed class OnChainPipelineChannels
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Domain.Common.SupportedChain, Channel<RawBlockData>> _chainInputs = new();
    /// <summary>Separate bounded source quotas prevent an unavailable enrichment store from blocking other chains.</summary>
    public Channel<RawBlockData> ForChain(Domain.Common.SupportedChain chain) => _chainInputs.GetOrAdd(chain,
        _ => Channel.CreateBounded<RawBlockData>(new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true }));
    public OnChainPipelineChannels(
        int rawBlockBufferCapacity = 512,
        int notificationBufferCapacity = 512,
        int aggregatedNotificationBufferCapacity = 512)
    {
        RawBlockDataChannel = Channel.CreateBounded<RawBlockData>(new BoundedChannelOptions(rawBlockBufferCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        NotificationPayloadChannel = Channel.CreateBounded<NotificationPayload>(new BoundedChannelOptions(notificationBufferCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        AggregatedNotificationPayloadChannel = Channel.CreateBounded<AggregatedNotificationPayload>(new BoundedChannelOptions(aggregatedNotificationBufferCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    /// <summary>
    /// Carries raw block or log batches into wallet matching.
    /// </summary>
    public Channel<RawBlockData> RawBlockDataChannel { get; }

    /// <summary>
    /// Carries structured notifications from matching workers to Telegram delivery.
    /// </summary>
    public Channel<NotificationPayload> NotificationPayloadChannel { get; }

    /// <summary>
    /// Carries transaction-level notification batches to Telegram delivery.
    /// </summary>
    public Channel<AggregatedNotificationPayload> AggregatedNotificationPayloadChannel { get; }
}
