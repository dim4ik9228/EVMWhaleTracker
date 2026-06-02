using Application.Messaging;
using Domain.Entities;
using Infrastructure.Caching;
using Infrastructure.Workers;
using Microsoft.Extensions.Logging;

namespace UnitTests.Infrastructure;

public sealed class WalletTrackerWorkerTests
{
    private const string TrackedAddress = "0x52908400098527886E0F7030069857D2E4169EE7";

    [Fact]
    public async Task ExecuteAsync_EmitsNotification_WhenTrackedAddressAppearsInToField()
    {
        await using var fixture = new WalletTrackerWorkerFixture();

        var worker = fixture.CreateWorker();
        await worker.StartAsync(CancellationToken.None);

        var block = new RawBlockData(
            BlockNumber: 100,
            BlockHash: "0xblock",
            BlockTimestampUtc: DateTimeOffset.UtcNow,
            Transactions:
            [
                new RawTransactionData(
                    TransactionHash: "0xtx-to",
                    FromAddress: "0x1111111111111111111111111111111111111111",
                    ToAddress: "0x52908400098527886e0f7030069857d2e4169ee7",
                    Value: 2.5m)
            ],
            Logs: []);

        await fixture.Channels.RawBlockDataChannel.Writer.WriteAsync(block);

        var notification = await fixture.ReadNotificationAsync();

        Assert.Equal(7777, notification.TelegramUserId);
        Assert.Equal("0xtx-to", notification.TransactionHash);
        Assert.Equal(TrackedAddress, notification.WalletAddress);
        Assert.Contains("incoming transfer", notification.MarkdownMessage);

        await fixture.StopWorkerAsync(worker);
    }

    [Fact]
    public async Task ExecuteAsync_EmitsNotification_WhenTrackedAddressAppearsInFromField()
    {
        await using var fixture = new WalletTrackerWorkerFixture();

        var worker = fixture.CreateWorker();
        await worker.StartAsync(CancellationToken.None);

        var block = new RawBlockData(
            BlockNumber: 101,
            BlockHash: "0xblock2",
            BlockTimestampUtc: DateTimeOffset.UtcNow,
            Transactions:
            [
                new RawTransactionData(
                    TransactionHash: "0xtx-from",
                    FromAddress: "0x52908400098527886e0f7030069857d2e4169ee7",
                    ToAddress: "0x2222222222222222222222222222222222222222",
                    Value: 3.75m)
            ],
            Logs: Array.Empty<RawLogData>());

        await fixture.Channels.RawBlockDataChannel.Writer.WriteAsync(block);

        var notification = await fixture.ReadNotificationAsync();

        Assert.Equal(7777, notification.TelegramUserId);
        Assert.Equal("0xtx-from", notification.TransactionHash);
        Assert.Equal(TrackedAddress, notification.WalletAddress);
        Assert.Contains("outgoing transfer", notification.MarkdownMessage);

        await fixture.StopWorkerAsync(worker);
    }

    private sealed class WalletTrackerWorkerFixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(5));
        private readonly LoggerFactory _loggerFactory = new();

        public WalletTrackerWorkerFixture()
        {
            Channels = new OnChainPipelineChannels();

            var trackedWallet = CreateTrackedWallet();
            Cache = new InMemoryCacheService();
            Cache.Add(trackedWallet);
        }

        public OnChainPipelineChannels Channels { get; }

        private InMemoryCacheService Cache { get; }

        public WalletTrackerWorker CreateWorker()
            => new(Channels, Cache, _loggerFactory.CreateLogger<WalletTrackerWorker>());

        public async Task<NotificationPayload> ReadNotificationAsync()
            => await Channels.NotificationPayloadChannel.Reader.ReadAsync(_timeout.Token);

        public async Task StopWorkerAsync(WalletTrackerWorker worker)
        {
            _timeout.Cancel();
            await worker.StopAsync(CancellationToken.None);
        }

        public ValueTask DisposeAsync()
        {
            _timeout.Dispose();
            _loggerFactory.Dispose();
            Cache.Dispose();
            return ValueTask.CompletedTask;
        }

        private static TrackedWallet CreateTrackedWallet()
        {
            var result = TrackedWallet.Create(TrackedAddress, 7777, minimumTransactionValue: 1m);
            Assert.True(result.IsSuccess);
            return result.Value;
        }
    }
}
