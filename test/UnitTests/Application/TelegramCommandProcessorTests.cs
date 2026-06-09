using Application.TrackedWallets;
using Domain.Common;
using MediatR;
using Presentation.Telegram.Commands;

namespace UnitTests.Application;

public sealed class TelegramCommandProcessorTests
{
    private const string LowercaseAddress = "0x52908400098527886e0f7030069857d2e4169ee7";

    [Fact]
    public async Task ProcessAsync_NonCommandMessage_ReturnsNull()
    {
        var processor = new TelegramCommandProcessor(new FakeSender());

        var response = await processor.ProcessAsync(
            new TelegramCommandRequest(7777, "alice", "hello there", IsPrivateChat: true),
            CancellationToken.None);

        Assert.Null(response);
    }

    [Fact]
    public async Task ProcessAsync_StartCommand_ReturnsHelpText()
    {
        var processor = new TelegramCommandProcessor(new FakeSender());

        var response = await processor.ProcessAsync(
            new TelegramCommandRequest(7777, "alice", "/start", IsPrivateChat: true),
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Contains("Available commands:", response!.Text);
        Assert.Contains("&lt;address&gt;", response.Text);
    }

    [Fact]
    public async Task ProcessAsync_AddCommand_SendsAddRequestAndReturnsConfirmation()
    {
        var sender = new FakeSender();
        var processor = new TelegramCommandProcessor(sender);

        var response = await processor.ProcessAsync(
            new TelegramCommandRequest(7777, "alice", $"/add@EVMWhaleTrackerBot {LowercaseAddress} 1.5", IsPrivateChat: true),
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Contains("Tracked wallet added successfully", response!.Text);
        Assert.Single(sender.SentRequests);

        var request = Assert.IsType<AddTrackedWalletCommand>(sender.SentRequests[0]);
        Assert.Equal(7777, request.TelegramUserId);
        Assert.Equal(LowercaseAddress, request.RawAddress);
        Assert.Equal(1.5m, request.MinimumTransactionValue);
    }

    [Fact]
    public async Task ProcessAsync_ListCommand_FormatsReturnedWallets()
    {
        var sender = new FakeSender
        {
            ListResult = Result<IReadOnlyList<TrackedWalletDetails>>.Success(
                [
                    new TrackedWalletDetails(
                        Guid.NewGuid(),
                        "0x52908400098527886E0F7030069857D2E4169EE7",
                        7777,
                        2m,
                        true,
                        false,
                        true,
                        DateTimeOffset.UtcNow,
                        null,
                        "MEXC Hot Wallet",
                        [SupportedChain.Bsc, SupportedChain.Base])
                ])
        };
        var processor = new TelegramCommandProcessor(sender);

        var response = await processor.ProcessAsync(
            new TelegramCommandRequest(7777, "alice", "/list", IsPrivateChat: true),
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Contains("Tracked wallets:", response!.Text);
        Assert.Contains("Alias:", response.Text);
        Assert.Contains("Minimum transaction value:", response.Text);
        Assert.Contains("Outgoing: <code>off</code>", response.Text);
        Assert.Contains("Enabled chains:", response.Text);
    }

    [Fact]
    public async Task ProcessAsync_RemoveCommand_SendsRemoveRequestAndReturnsConfirmation()
    {
        var sender = new FakeSender();
        var processor = new TelegramCommandProcessor(sender);

        var response = await processor.ProcessAsync(
            new TelegramCommandRequest(7777, "alice", $"/remove {LowercaseAddress}", IsPrivateChat: true),
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Contains("Tracked wallet removed successfully", response!.Text);
        Assert.Single(sender.SentRequests);

        var request = Assert.IsType<RemoveTrackedWalletCommand>(sender.SentRequests[0]);
        Assert.Equal(7777, request.TelegramUserId);
        Assert.Equal(LowercaseAddress, request.RawAddress);
    }

    [Fact]
    public async Task ProcessAsync_NonPrivateChat_ReturnsPrivateChatGuidance()
    {
        var processor = new TelegramCommandProcessor(new FakeSender());

        var response = await processor.ProcessAsync(
            new TelegramCommandRequest(7777, "alice", "/help", IsPrivateChat: false),
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Contains("private chat", response!.Text, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeSender : ISender
    {
        public List<object> SentRequests { get; } = [];

        public Result<TrackedWalletDetails> AddResult { get; set; } = Result<TrackedWalletDetails>.Success(
            new TrackedWalletDetails(
                Guid.NewGuid(),
                "0x52908400098527886E0F7030069857D2E4169EE7",
                7777,
                0m,
                true,
                true,
                true,
                DateTimeOffset.UtcNow,
                null,
                null,
                []));

        public Result<IReadOnlyList<TrackedWalletDetails>> ListResult { get; set; } = Result<IReadOnlyList<TrackedWalletDetails>>.Success([]);

        public Result RemoveResult { get; set; } = Result.Success();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            SentRequests.Add(request);
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            SentRequests.Add(request);

            object response = request switch
            {
                AddTrackedWalletCommand => AddResult,
                ListTrackedWalletsQuery => ListResult,
                RemoveTrackedWalletCommand => RemoveResult,
                _ => throw new NotSupportedException($"Unsupported request type {request.GetType().Name}.")
            };

            return Task.FromResult((TResponse)response);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            SentRequests.Add(request);
            return Task.FromResult<object?>(null);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
