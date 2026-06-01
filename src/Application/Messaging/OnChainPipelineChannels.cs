using System.Threading.Channels;

namespace Application.Messaging;

public sealed class OnChainPipelineChannels
{
    public OnChainPipelineChannels(
        int rawBlockBufferCapacity = 512,
        int notificationBufferCapacity = 512)
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
    }

    public Channel<RawBlockData> RawBlockDataChannel { get; }

    public Channel<NotificationPayload> NotificationPayloadChannel { get; }
}
