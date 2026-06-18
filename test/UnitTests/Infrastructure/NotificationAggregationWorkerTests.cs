using Application.Messaging;
using Domain.Common;
using Infrastructure.Workers;
using Microsoft.Extensions.Logging;

namespace UnitTests.Infrastructure;

public sealed class NotificationAggregationWorkerTests
{
    [Fact]
    public async Task ExecuteAsync_GroupsNotificationsByTransaction()
    {
        await using var fixture = new NotificationAggregationFixture();
        var worker = fixture.CreateWorker();
        await worker.StartAsync(CancellationToken.None);

        await fixture.Channels.NotificationPayloadChannel.Writer.WriteAsync(CreatePayload("0xtx", 1m));
        await fixture.Channels.NotificationPayloadChannel.Writer.WriteAsync(CreatePayload("0xtx", 2m));

        var notification = await fixture.ReadAggregatedAsync();

        Assert.Equal("0xtx", notification.TransactionHash);
        Assert.Equal(2, notification.Lines.Count);
        Assert.Equal([1m, 2m], notification.Lines.Select(line => line.Amount));

        await fixture.StopWorkerAsync(worker);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotGroupDifferentTransactions()
    {
        await using var fixture = new NotificationAggregationFixture();
        var worker = fixture.CreateWorker();
        await worker.StartAsync(CancellationToken.None);

        await fixture.Channels.NotificationPayloadChannel.Writer.WriteAsync(CreatePayload("0xtx-a", 1m));
        await fixture.Channels.NotificationPayloadChannel.Writer.WriteAsync(CreatePayload("0xtx-b", 2m));

        var first = await fixture.ReadAggregatedAsync();
        var second = await fixture.ReadAggregatedAsync();

        Assert.Equal(["0xtx-a", "0xtx-b"], new[] { first.TransactionHash, second.TransactionHash }.Order());
        Assert.Single(first.Lines);
        Assert.Single(second.Lines);

        await fixture.StopWorkerAsync(worker);
    }

    private static NotificationPayload CreatePayload(string transactionHash, decimal amount)
        => new(
            TelegramUserId: 7777,
            TrackedWalletId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Chain: SupportedChain.Bsc,
            Operation: NotificationOperation.Sent,
            TrackedWalletAddress: "0x52908400098527886E0F7030069857D2E4169EE7",
            TrackedWalletAlias: "debabki.eth",
            CounterpartyAddress: "0x1111111111111111111111111111111111111111",
            CounterpartyAlias: null,
            TransactionHash: transactionHash,
            AssetSymbol: "USDT",
            AssetName: "Tether USD",
            TokenContractAddress: "0x55d398326f99059fF775485246999027B3197955",
            Amount: amount,
            UsdValue: amount,
            BlockNumber: 100,
            BlockTimestampUtc: DateTimeOffset.UtcNow);

    private sealed class NotificationAggregationFixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(5));
        private readonly LoggerFactory _loggerFactory = new();

        public OnChainPipelineChannels Channels { get; } = new();

        public NotificationAggregationWorker CreateWorker()
            => new(
                Channels,
                TimeSpan.FromMilliseconds(50),
                _loggerFactory.CreateLogger<NotificationAggregationWorker>());

        public async Task<AggregatedNotificationPayload> ReadAggregatedAsync()
            => await Channels.AggregatedNotificationPayloadChannel.Reader.ReadAsync(_timeout.Token);

        public async Task StopWorkerAsync(NotificationAggregationWorker worker)
        {
            _timeout.Cancel();
            await worker.StopAsync(CancellationToken.None);
        }

        public ValueTask DisposeAsync()
        {
            _timeout.Dispose();
            _loggerFactory.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
