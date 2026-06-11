using Application.Messaging;
using Application.Abstractions;
using Domain.Common;
using Domain.Entities;
using Infrastructure.Caching;
using Infrastructure.Blockchain;
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
            Chain: SupportedChain.Bsc,
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
        Assert.Equal(NotificationOperation.Received, notification.Operation);
        Assert.Equal(2.5m, notification.Amount);
        Assert.Equal(5m, notification.UsdValue);
        Assert.Equal("BNB", notification.AssetSymbol);

        await fixture.StopWorkerAsync(worker);
    }

    [Fact]
    public async Task ExecuteAsync_EmitsNotification_WhenTrackedAddressAppearsInFromField()
    {
        await using var fixture = new WalletTrackerWorkerFixture();

        var worker = fixture.CreateWorker();
        await worker.StartAsync(CancellationToken.None);

        var block = new RawBlockData(
            Chain: SupportedChain.Bsc,
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
        Assert.Equal(NotificationOperation.Sent, notification.Operation);
        Assert.Equal("0x2222222222222222222222222222222222222222", notification.CounterpartyAddress);

        await fixture.StopWorkerAsync(worker);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotEmitNotification_WhenNativeTransferBelowMinimumUsdValue()
    {
        await using var fixture = new WalletTrackerWorkerFixture();

        var worker = fixture.CreateWorker();
        await worker.StartAsync(CancellationToken.None);

        var block = new RawBlockData(
            Chain: SupportedChain.Bsc,
            BlockNumber: 102,
            BlockHash: "0xblock3",
            BlockTimestampUtc: DateTimeOffset.UtcNow,
            Transactions:
            [
                new RawTransactionData(
                    TransactionHash: "0xtx-small",
                    FromAddress: "0x1111111111111111111111111111111111111111",
                    ToAddress: "0x52908400098527886e0f7030069857d2e4169ee7",
                    Value: 0.25m)
            ],
            Logs: []);

        await fixture.Channels.RawBlockDataChannel.Writer.WriteAsync(block);

        Assert.False(await fixture.TryReadNotificationAsync(TimeSpan.FromMilliseconds(250)));

        await fixture.StopWorkerAsync(worker);
    }

    [Fact]
    public async Task ExecuteAsync_EmitsNotification_WhenUsdPriceResolutionFails()
    {
        await using var fixture = new WalletTrackerWorkerFixture(priceProvider: new FailingAssetPriceProvider());

        var worker = fixture.CreateWorker();
        await worker.StartAsync(CancellationToken.None);

        var block = new RawBlockData(
            Chain: SupportedChain.Bsc,
            BlockNumber: 103,
            BlockHash: "0xblock-price-failure",
            BlockTimestampUtc: DateTimeOffset.UtcNow,
            Transactions:
            [
                new RawTransactionData(
                    TransactionHash: "0xtx-price-failure",
                    FromAddress: "0x1111111111111111111111111111111111111111",
                    ToAddress: "0x52908400098527886e0f7030069857d2e4169ee7",
                    Value: 2.5m)
            ],
            Logs: []);

        await fixture.Channels.RawBlockDataChannel.Writer.WriteAsync(block);

        var notification = await fixture.ReadNotificationAsync();

        Assert.Equal(NotificationOperation.Received, notification.Operation);
        Assert.Equal("0xtx-price-failure", notification.TransactionHash);
        Assert.Null(notification.UsdValue);

        await fixture.StopWorkerAsync(worker);
    }

    [Fact]
    public async Task ExecuteAsync_EmitsNotification_WhenTrackedAddressAppearsInErc20TransferToTopic()
    {
        await using var fixture = new WalletTrackerWorkerFixture();

        var worker = fixture.CreateWorker();
        await worker.StartAsync(CancellationToken.None);

        var block = new RawBlockData(
            Chain: SupportedChain.Bsc,
            BlockNumber: 104,
            BlockHash: "0xblock4",
            BlockTimestampUtc: DateTimeOffset.UtcNow,
            Transactions: [],
            Logs:
            [
                new RawLogData(
                    TransactionHash: "0xtoken-tx",
                    ContractAddress: "0x55d398326f99059fF775485246999027B3197955",
                    Topics:
                    [
                        Erc20TransferLogParser.TransferEventTopic,
                        TopicAddress("0x1111111111111111111111111111111111111111"),
                        TopicAddress("0x52908400098527886e0f7030069857d2e4169ee7")
                    ],
                    Data: "0x000000000000000000000000000000000000000000000000000000000f0e1b30")
            ]);

        await fixture.Channels.RawBlockDataChannel.Writer.WriteAsync(block);

        var notification = await fixture.ReadNotificationAsync();

        Assert.Equal(7777, notification.TelegramUserId);
        Assert.Equal("0xtoken-tx", notification.TransactionHash);
        Assert.Equal(NotificationOperation.Received, notification.Operation);
        Assert.Equal("USDT", notification.AssetSymbol);
        Assert.Equal(252.582704m, notification.Amount);
        Assert.Equal(252.582704m, notification.UsdValue);
        Assert.Equal("0x1111111111111111111111111111111111111111", notification.CounterpartyAddress);

        await fixture.StopWorkerAsync(worker);
    }

    [Fact]
    public async Task ExecuteAsync_EmitsNotification_WhenTrackedAddressAppearsInErc20TransferFromTopic()
    {
        await using var fixture = new WalletTrackerWorkerFixture();

        var worker = fixture.CreateWorker();
        await worker.StartAsync(CancellationToken.None);

        var block = new RawBlockData(
            Chain: SupportedChain.Bsc,
            BlockNumber: 105,
            BlockHash: "0xblock5",
            BlockTimestampUtc: DateTimeOffset.UtcNow,
            Transactions: [],
            Logs:
            [
                TokenTransferLog(
                    transactionHash: "0xtoken-outgoing",
                    fromAddress: "0x52908400098527886e0f7030069857d2e4169ee7",
                    toAddress: "0x2222222222222222222222222222222222222222")
            ]);

        await fixture.Channels.RawBlockDataChannel.Writer.WriteAsync(block);

        var notification = await fixture.ReadNotificationAsync();

        Assert.Equal("0xtoken-outgoing", notification.TransactionHash);
        Assert.Equal(NotificationOperation.Sent, notification.Operation);
        Assert.Equal("USDT", notification.AssetSymbol);
        Assert.Equal("0x2222222222222222222222222222222222222222", notification.CounterpartyAddress);

        await fixture.StopWorkerAsync(worker);
    }

    [Fact]
    public async Task ExecuteAsync_EmitsSingleSelfTransferNotification_WhenTrackedAddressAppearsInBothErc20TransferTopics()
    {
        await using var fixture = new WalletTrackerWorkerFixture();

        var worker = fixture.CreateWorker();
        await worker.StartAsync(CancellationToken.None);

        var block = new RawBlockData(
            Chain: SupportedChain.Bsc,
            BlockNumber: 106,
            BlockHash: "0xblock6",
            BlockTimestampUtc: DateTimeOffset.UtcNow,
            Transactions: [],
            Logs:
            [
                TokenTransferLog(
                    transactionHash: "0xtoken-self",
                    fromAddress: "0x52908400098527886e0f7030069857d2e4169ee7",
                    toAddress: "0x52908400098527886e0f7030069857d2e4169ee7")
            ]);

        await fixture.Channels.RawBlockDataChannel.Writer.WriteAsync(block);

        var notification = await fixture.ReadNotificationAsync();

        Assert.Equal("0xtoken-self", notification.TransactionHash);
        Assert.Equal(NotificationOperation.SelfTransfer, notification.Operation);
        Assert.Equal(TrackedAddress, notification.WalletAddress);
        Assert.Equal(TrackedAddress, notification.CounterpartyAddress);
        Assert.False(await fixture.TryReadNotificationAsync(TimeSpan.FromMilliseconds(250)));

        await fixture.StopWorkerAsync(worker);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotEmitNotification_WhenTokenTransfersAreDisabled()
    {
        await using var fixture = new WalletTrackerWorkerFixture(trackTokenTransfers: false);

        var worker = fixture.CreateWorker();
        await worker.StartAsync(CancellationToken.None);

        var block = new RawBlockData(
            Chain: SupportedChain.Bsc,
            BlockNumber: 107,
            BlockHash: "0xblock7",
            BlockTimestampUtc: DateTimeOffset.UtcNow,
            Transactions: [],
            Logs:
            [
                TokenTransferLog(
                    transactionHash: "0xtoken-disabled",
                    fromAddress: "0x1111111111111111111111111111111111111111",
                    toAddress: "0x52908400098527886e0f7030069857d2e4169ee7")
            ]);

        await fixture.Channels.RawBlockDataChannel.Writer.WriteAsync(block);

        Assert.False(await fixture.TryReadNotificationAsync(TimeSpan.FromMilliseconds(250)));

        await fixture.StopWorkerAsync(worker);
    }

    private sealed class WalletTrackerWorkerFixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(5));
        private readonly LoggerFactory _loggerFactory = new();

        private readonly IAssetPriceProvider _priceProvider;

        public WalletTrackerWorkerFixture(
            IAssetPriceProvider? priceProvider = null,
            bool trackTokenTransfers = true)
        {
            Channels = new OnChainPipelineChannels();
            _priceProvider = priceProvider ?? new FakeAssetPriceProvider();

            var trackedWallet = CreateTrackedWallet(trackTokenTransfers);
            Cache = new InMemoryCacheService();
            Cache.Add(trackedWallet);
        }

        public OnChainPipelineChannels Channels { get; }

        private InMemoryCacheService Cache { get; }

        public WalletTrackerWorker CreateWorker()
            => new(
                Channels,
                Cache,
                new StaticChainMetadataProvider(),
                new FakeTokenMetadataProvider(),
                _priceProvider,
                new Erc20TransferLogParser(),
                _loggerFactory.CreateLogger<WalletTrackerWorker>());

        public async Task<NotificationPayload> ReadNotificationAsync()
            => await Channels.NotificationPayloadChannel.Reader.ReadAsync(_timeout.Token);

        public async Task<bool> TryReadNotificationAsync(TimeSpan timeout)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            try
            {
                _ = await Channels.NotificationPayloadChannel.Reader.ReadAsync(timeoutCts.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

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

        private static TrackedWallet CreateTrackedWallet(bool trackTokenTransfers)
        {
            var result = TrackedWallet.Create(
                TrackedAddress,
                7777,
                minimumTransactionValue: 1m,
                trackTokenTransfers: trackTokenTransfers,
                alias: "debabki.eth",
                enabledChains: [SupportedChain.Bsc]);
            Assert.True(result.IsSuccess);
            return result.Value;
        }
    }

    private static string TopicAddress(string address)
        => $"0x{new string('0', 24)}{address[2..].ToLowerInvariant()}";

    private static RawLogData TokenTransferLog(
        string transactionHash,
        string fromAddress,
        string toAddress)
        => new(
            TransactionHash: transactionHash,
            ContractAddress: "0x55d398326f99059fF775485246999027B3197955",
            Topics:
            [
                Erc20TransferLogParser.TransferEventTopic,
                TopicAddress(fromAddress),
                TopicAddress(toAddress)
            ],
            Data: "0x000000000000000000000000000000000000000000000000000000000f0e1b30");

    private sealed class FakeTokenMetadataProvider : ITokenMetadataProvider
    {
        public Task<Result<TokenMetadata>> GetAsync(
            SupportedChain chain,
            string tokenContractAddress,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<TokenMetadata>.Success(
                new TokenMetadata(chain, tokenContractAddress, "USDT", "Tether USD", 6)));
    }

    private sealed class FakeAssetPriceProvider : IAssetPriceProvider
    {
        public Task<Result<AssetUsdPrice>> GetUsdPriceAsync(
            SupportedChain chain,
            string? tokenContractAddress,
            string assetSymbol,
            CancellationToken cancellationToken)
        {
            var price = string.IsNullOrWhiteSpace(tokenContractAddress) ? 2m : 1m;
            return Task.FromResult(Result<AssetUsdPrice>.Success(
                new AssetUsdPrice(price, DateTimeOffset.UtcNow, "Test")));
        }
    }

    private sealed class FailingAssetPriceProvider : IAssetPriceProvider
    {
        public Task<Result<AssetUsdPrice>> GetUsdPriceAsync(
            SupportedChain chain,
            string? tokenContractAddress,
            string assetSymbol,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<AssetUsdPrice>.Failure(
                Error.Validation("AssetPrice.TestFailure", "Price lookup failed.")));
    }
}
