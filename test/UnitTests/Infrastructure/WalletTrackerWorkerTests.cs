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

    [Theory]
    [InlineData(SupportedChain.Robinhood, NotificationOperation.Received)]
    [InlineData(SupportedChain.Robinhood, NotificationOperation.Sent)]
    [InlineData(SupportedChain.Robinhood, NotificationOperation.SelfTransfer)]
    public async Task NewChains_MatchTokenDirectionsAndUseTokenDecimalsWithoutUsd(SupportedChain chain, NotificationOperation operation)
    {
        await using var fixture = new WalletTrackerWorkerFixture(new FailingAssetPriceProvider(), enabledChains: [], minimumUsdValue: 0m);
        var worker = fixture.CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        const string other = "0x1111111111111111111111111111111111111111";
        var from = operation == NotificationOperation.Received ? other : TrackedAddress;
        var to = operation == NotificationOperation.Sent ? other : TrackedAddress;
        var block = new RawBlockData(chain, 108, "0xblock", DateTimeOffset.UtcNow, [],
            [TokenTransferLog("0xnewchain", from, to)]);
        await fixture.Channels.RawBlockDataChannel.Writer.WriteAsync(block);
        var notification = await fixture.ReadNotificationAsync();
        Assert.Equal(chain, notification.Chain);
        Assert.Equal(operation, notification.Operation);
        Assert.Equal(252.582704m, notification.Amount);
        Assert.Null(notification.UsdValue);
        Assert.False(await fixture.TryReadNotificationAsync(TimeSpan.FromMilliseconds(100)));
        await fixture.StopWorkerAsync(worker);
    }

    [Theory]
    [InlineData(SupportedChain.Robinhood)]
    public async Task NewChains_RespectExistingExplicitWalletChainList(SupportedChain chain)
    {
        await using var fixture = new WalletTrackerWorkerFixture();
        var worker = fixture.CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        await fixture.Channels.RawBlockDataChannel.Writer.WriteAsync(new RawBlockData(
            chain, 109, "0xblock", DateTimeOffset.UtcNow, [],
            [TokenTransferLog("0xrestricted", "0x1111111111111111111111111111111111111111", TrackedAddress)]));
        Assert.False(await fixture.TryReadNotificationAsync(TimeSpan.FromMilliseconds(150)));
        await fixture.StopWorkerAsync(worker);
    }

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
        Assert.Equal(TrackedAddress, notification.TrackedWalletAddress);
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
        Assert.Equal(TrackedAddress, notification.TrackedWalletAddress);
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
        await using var fixture = new WalletTrackerWorkerFixture(priceProvider: new FailingAssetPriceProvider(), minimumUsdValue: 0m);

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
        Assert.Equal(TrackedAddress, notification.TrackedWalletAddress);
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

    [Theory]
    [InlineData(false, 0, true)]
    [InlineData(false, 1, false)]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, false)]
    public async Task UnpricedTransfer_RequiresZeroMinimum(bool token, int minimum, bool shouldSend)
    {
        await using var fixture = new WalletTrackerWorkerFixture(new FailingAssetPriceProvider(), minimumUsdValue: minimum);
        using var worker = fixture.CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        await fixture.Channels.RawBlockDataChannel.Writer.WriteAsync(FilterTestBlock(token));
        fixture.Channels.RawBlockDataChannel.Writer.Complete();
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(shouldSend, fixture.Channels.NotificationPayloadChannel.Reader.TryRead(out var payload));
        if (shouldSend)
        {
            Assert.Equal(token ? 252.582704m : 2.5m, payload!.Amount);
            Assert.Null(payload.UsdValue);
        }
        Assert.False(fixture.Channels.NotificationPayloadChannel.Reader.TryRead(out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnpricedTransfer_EvaluatesEachRecipientIndependently(bool token)
    {
        await using var fixture = new WalletTrackerWorkerFixture(new FailingAssetPriceProvider(), minimumUsdValue: 1m);
        var zeroMinimumWallet = TrackedWallet.Create(TrackedAddress, 8888, minimumUsdValue: 0m);
        Assert.True(zeroMinimumWallet.IsSuccess);
        fixture.Cache.Add(zeroMinimumWallet.Value);
        using var worker = fixture.CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        await fixture.Channels.RawBlockDataChannel.Writer.WriteAsync(FilterTestBlock(token));
        fixture.Channels.RawBlockDataChannel.Writer.Complete();
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(fixture.Channels.NotificationPayloadChannel.Reader.TryRead(out var payload));
        Assert.Equal(8888, payload.TelegramUserId);
        Assert.Equal(zeroMinimumWallet.Value.Id, payload.TrackedWalletId);
        Assert.Null(payload.UsdValue);
        Assert.False(fixture.Channels.NotificationPayloadChannel.Reader.TryRead(out _));
    }

    [Theory]
    [InlineData(false, 4.99, true)]
    [InlineData(false, 5, true)]
    [InlineData(false, 5.01, false)]
    [InlineData(true, 252.582703, true)]
    [InlineData(true, 252.582704, true)]
    [InlineData(true, 252.582705, false)]
    [InlineData(false, 0, true, true)]
    [InlineData(true, 0, true, true)]
    public async Task PricedTransfer_UsesInclusiveMinimum(bool token, double minimum, bool shouldSend, bool zeroPrice = false)
    {
        await using var fixture = new WalletTrackerWorkerFixture(
            new FakeAssetPriceProvider(zeroPrice), minimumUsdValue: (decimal)minimum);
        using var worker = fixture.CreateWorker();
        await worker.StartAsync(CancellationToken.None);
        await fixture.Channels.RawBlockDataChannel.Writer.WriteAsync(FilterTestBlock(token));
        fixture.Channels.RawBlockDataChannel.Writer.Complete();
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(shouldSend, fixture.Channels.NotificationPayloadChannel.Reader.TryRead(out var payload));
        if (shouldSend)
            Assert.Equal(zeroPrice ? 0m : token ? 252.582704m : 5m, payload!.UsdValue);
        Assert.False(fixture.Channels.NotificationPayloadChannel.Reader.TryRead(out _));
    }

    private static RawBlockData FilterTestBlock(bool token)
        => new(SupportedChain.Bsc, 110, "0xfilter-block", DateTimeOffset.UtcNow,
            token ? [] : [new RawTransactionData("0xfilter-native", "0x1111111111111111111111111111111111111111", TrackedAddress, 2.5m)],
            token ? [TokenTransferLog("0xfilter-token", "0x1111111111111111111111111111111111111111", TrackedAddress)] : []);

    private sealed class WalletTrackerWorkerFixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(5));
        private readonly LoggerFactory _loggerFactory = new();

        private readonly IAssetPriceProvider _priceProvider;

        public WalletTrackerWorkerFixture(
            IAssetPriceProvider? priceProvider = null,
            bool trackTokenTransfers = true,
            SupportedChain[]? enabledChains = null,
            decimal minimumUsdValue = 1m)
        {
            Channels = new OnChainPipelineChannels();
            _priceProvider = priceProvider ?? new FakeAssetPriceProvider();

            var trackedWallet = CreateTrackedWallet(trackTokenTransfers, enabledChains, minimumUsdValue);
            Cache = new InMemoryCacheService();
            Cache.Add(trackedWallet);
        }

        public OnChainPipelineChannels Channels { get; }

        public InMemoryCacheService Cache { get; }

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

        private static TrackedWallet CreateTrackedWallet(bool trackTokenTransfers, SupportedChain[]? enabledChains, decimal minimumUsdValue)
        {
            var result = TrackedWallet.Create(
                TrackedAddress,
                7777,
                minimumUsdValue: minimumUsdValue,
                trackTokenTransfers: trackTokenTransfers,
                alias: "debabki.eth",
                enabledChains: enabledChains ?? [SupportedChain.Bsc]);
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

    private sealed class FakeAssetPriceProvider(bool zeroPrice = false) : IAssetPriceProvider
    {
        public Task<Result<AssetUsdPrice>> GetUsdPriceAsync(
            SupportedChain chain,
            string? tokenContractAddress,
            string assetSymbol,
            CancellationToken cancellationToken)
        {
            var price = zeroPrice ? 0m : string.IsNullOrWhiteSpace(tokenContractAddress) ? 2m : 1m;
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
