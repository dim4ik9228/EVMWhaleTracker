using System.Text;
using Application.Billing;
using Application.Messaging;
using Application.Swaps;
using Domain.Common;
using Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Presentation.Telegram.Services;
using Presentation.Telegram.Workers;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace UnitTests.Presentation;

public sealed class NotificationDispatcherBillingTests
{
    [Fact]
    public async Task SwapDeliveryGuardRechecksAfterTelegramRetryPermitAndCancelsStaleWork()
    {
        var guard = new RetryGuard(); var handler = new RateLimitedHandler();
        var bot = new TelegramBotClient(new TelegramBotClientOptions("123456:fixture-token") { RetryCount = 0 }, new HttpClient(handler));
        await using var limiter = new TelegramGlobalRateLimiter(); var channels = new OnChainPipelineChannels();
        using var worker = new NotificationDispatcherWorker(channels, bot, limiter, new Enricher(), new Renderer(), new Username(),
            NullLogger<NotificationDispatcherWorker>.Instance, swapGuard: guard);
        var payload = new AggregatedNotificationPayload(1, Guid.NewGuid(), SupportedChain.Ethereum, "0x0000000000000000000000000000000000000001", null,
            "0xfixture", 1, DateTimeOffset.UtcNow, [], SwapDecisionId: Guid.NewGuid());
        await worker.StartAsync(default);
        try
        {
            await channels.AggregatedNotificationPayloadChannel.Writer.WriteAsync(payload);
            Assert.False(await guard.Completed.Task.WaitAsync(TimeSpan.FromSeconds(6)));
            Assert.Equal(2, guard.Checks); Assert.Equal(1, handler.Calls);
        }
        finally { await worker.StopAsync(default); }
    }
    private sealed class RetryGuard : ISwapDeliveryGuard
    {
        public int Checks; public TaskCompletionSource<bool> Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> AllowsAsync(AggregatedNotificationPayload payload, CancellationToken ct) => Task.FromResult(++Checks == 1);
        public Task CompleteAsync(AggregatedNotificationPayload payload, bool sent, CancellationToken ct) { Completed.TrySetResult(sent); return Task.CompletedTask; }
    }
    private sealed class RateLimitedHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)
            { Content = new StringContent("{\"ok\":false,\"error_code\":429,\"description\":\"fixture\",\"parameters\":{\"retry_after\":1}}", Encoding.UTF8, "application/json") });
        }
    }
    [Fact]
    public async Task EnforcementDisabledSendsQueuedNotificationPastCapturedProjectionDeadline()
    {
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bot = new TelegramBotClient("123456:fixture-token", new HttpClient(new SendHandler(delivered)));
        await using var limiter = new TelegramGlobalRateLimiter();
        var channels = new OnChainPipelineChannels();
        var policy = new EntitlementPolicy(Options.Create(new BillingOptions { EnforcementEnabled = false }), TimeProvider.System);
        using var worker = new NotificationDispatcherWorker(channels, bot, limiter, new Enricher(), new Renderer(), new Username(),
            NullLogger<NotificationDispatcherWorker>.Instance, policy);
        var payload = new AggregatedNotificationPayload(1, Guid.NewGuid(), SupportedChain.Ethereum, "0x0000000000000000000000000000000000000001", null,
            "0xfixture", 1, DateTimeOffset.UtcNow.AddMinutes(-3), [], EntitlementRevision: 1, EntitlementValidUntil: DateTimeOffset.UtcNow.AddMinutes(-2));
        await worker.StartAsync(default);
        try
        {
            await channels.AggregatedNotificationPayloadChannel.Writer.WriteAsync(payload);
            await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { await worker.StopAsync(default); }
    }

    private sealed class SendHandler(TaskCompletionSource delivered) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.EndsWith("/sendMessage", request.RequestUri!.AbsolutePath);
            delivered.TrySetResult();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":1,\"date\":1793059200,\"chat\":{\"id\":1,\"type\":\"private\"},\"text\":\"fixture\"}}", Encoding.UTF8, "application/json")
            });
        }
    }
    private sealed class Enricher : ITelegramNotificationEnricher
    {
        public Task<EnrichedTelegramNotification> EnrichAsync(NotificationPayload payload, CancellationToken ct) => EnrichAsync(AggregatedNotificationPayload.FromSingle(payload), ct);
        public Task<EnrichedTelegramNotification> EnrichAsync(AggregatedNotificationPayload payload, CancellationToken ct) => Task.FromResult(new EnrichedTelegramNotification(
            payload.TelegramUserId, payload.TrackedWalletId, payload.Chain, payload.TrackedWalletAddress, "fixture", payload.TransactionHash,
            payload.BlockNumber, payload.BlockTimestampUtc, []));
    }
    private sealed class Renderer : ITelegramNotificationRenderer
    {
        public TelegramRenderedMessage Render(EnrichedTelegramNotification notification, string? botUsername = null) => new("fixture", ParseMode.Html);
    }
    private sealed class Username : ITelegramBotUsernameProvider
    {
        public Task<string?> GetBotUsernameAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    }
}
