using System.Net;
using System.Text;
using System.Text.Json;
using Infrastructure.Polymarket;
using Microsoft.Extensions.Options;

namespace UnitTests.Infrastructure.Polymarket;

public sealed class PolymarketPolygonBudgetTests
{
    [Fact]
    public async Task QuietSchedulingHasSparseReadsAndAuditsAndPendingGetsFastRetries()
    {
        var clock = new Clock();
        var scheduler = new PolymarketPolygonWorkScheduler(new(), clock);
        var reads = 0; var audits = 0; var maintenance = 0; var pending = false;
        Task Finalize() { reads++; return Task.CompletedTask; }
        Task Maintain() { maintenance++; return Task.CompletedTask; }
        Task Audit() { audits++; return Task.CompletedTask; }
        for (var second = 0; second <= 600; second++)
        {
            clock.Now = DateTimeOffset.UnixEpoch.AddSeconds(second);
            await scheduler.TickAsync(() => Task.FromResult(pending), Finalize, Maintain, Audit);
        }
        Assert.Equal(5, reads); Assert.Equal(5, maintenance); Assert.Equal(2, audits);
        pending = true;
        for (var second = 601; second <= 610; second++)
        {
            clock.Now = DateTimeOffset.UnixEpoch.AddSeconds(second);
            await scheduler.TickAsync(() => Task.FromResult(pending), Finalize, Maintain, Audit);
        }
        Assert.Equal(10, reads); Assert.Equal(2, audits);
    }

    [Theory]
    [InlineData(402, "{}")]
    [InlineData(429, "{}")]
    [InlineData(200, "{\"error\":{\"code\":402}}")]
    [InlineData(200, "{\"error\":{\"code\":-33000}}")]
    [InlineData(200, "{\"error\":{\"code\":429}}")]
    [InlineData(200, "{\"error\":{\"code\":-33200}}")]
    public async Task QuotaStopsRequestsAcrossCallsWithoutMisreportingChain(int status, string body)
    {
        var handler = new Handler(status, body);
        using var http = new HttpClient(handler);
        var rpc = new PolymarketPolygonRpc(http, Options.Create(new PolymarketPolygonOptions { Enabled = true }));
        var first = await Assert.ThrowsAsync<PolymarketProviderQuotaException>(() => rpc.VerifyChainAsync(default));
        Assert.True(first.RetryAfter > TimeSpan.Zero);
        await Assert.ThrowsAsync<PolymarketProviderQuotaException>(() => rpc.HeadAsync(default));
        Assert.Equal(1, handler.Calls); Assert.Equal(1, rpc.RequestCount);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rpc.HeadAsync(cancelled.Token));
    }

    [Fact]
    public void WebSocketQuotaIsRecognizedBeforeChainValidationAndRangeLimitIsSeparate()
    {
        using var quota = JsonDocument.Parse("{\"id\":0,\"error\":{\"code\":402,\"message\":\"secret\"}}");
        var error = Assert.Throws<PolymarketProviderQuotaException>(() => PolymarketProviderQuotaException.ThrowIfQuota(quota.RootElement, 3600));
        Assert.DoesNotContain("secret", error.Message);
        using var range = JsonDocument.Parse("{\"error\":{\"code\":-32005}}");
        PolymarketProviderQuotaException.ThrowIfQuota(range.RootElement, 3600);
        Assert.Equal(TimeSpan.FromHours(1), PolymarketProviderQuotaException.UntilReset(new DateTimeOffset(2026, 1, 1, 23, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task LocalDailyCapRefusesBeforeSending()
    {
        var handler = new Handler(200, "{\"result\":\"0x89\"}");
        using var http = new HttpClient(handler);
        var rpc = new PolymarketPolygonRpc(http, Options.Create(new PolymarketPolygonOptions
        { Enabled = true, DailyHttpRequestLimit = 1 }));
        await rpc.VerifyChainAsync(default);
        await Assert.ThrowsAsync<PolymarketProviderQuotaException>(() => rpc.HeadAsync(default));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DelayedHttp429CannotShortenSharedDailySocketCooldown()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelayedThrottle(entered, release);
        using var http = new HttpClient(handler);
        var options = new PolymarketPolygonOptions { Enabled = true };
        var rpc = new PolymarketPolygonRpc(http, Options.Create(options));
        var inFlight = rpc.HeadAsync(default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Simulate a daily socket pause with one hour remaining until UTC reset.
        options.ExtendCooldown(PolymarketProviderQuotaException.UntilReset(
            new DateTimeOffset(2026, 1, 1, 23, 0, 0, TimeSpan.Zero)));
        var dailyDeadline = Interlocked.Read(ref options.CooldownUntilTicks);
        release.SetResult();
        var failure = await Assert.ThrowsAsync<PolymarketProviderQuotaException>(() => inFlight);
        Assert.Equal(dailyDeadline, Interlocked.Read(ref options.CooldownUntilTicks));
        Assert.True(failure.RetryAfter > TimeSpan.FromMinutes(59));
        await Assert.ThrowsAsync<PolymarketProviderQuotaException>(() => rpc.HeadAsync(default));
        await Assert.ThrowsAsync<PolymarketProviderQuotaException>(() => rpc.VerifyChainAsync(default));
        Assert.Equal(1, handler.Calls); Assert.Equal(1, rpc.RequestCount);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rpc.HeadAsync(cancelled.Token));
    }

    [Fact]
    public void UnknownSocketRpcErrorIsNeitherDailyQuotaNorChainMismatch()
    {
        using var response = JsonDocument.Parse("{\"jsonrpc\":\"2.0\",\"id\":0,\"error\":{\"code\":-32000,\"message\":\"internal error secret\"}}");
        PolymarketProviderQuotaException.ThrowIfQuota(response.RootElement, 3600);
        var failure = Assert.Throws<InvalidDataException>(() => PolymarketPolygonWorker.ThrowIfSocketRpcError(response.RootElement));
        Assert.Contains("-32000", failure.Message);
        Assert.DoesNotContain("mismatch", failure.Message);
        Assert.DoesNotContain("secret", failure.Message);
        Assert.Equal(PolymarketPolygonWorkerDiagnostics.PolygonSocketRpcRejected(-32000), failure.Message);
    }

    [Fact]
    public async Task Headerless429BackoffIsProgressiveBoundedAndResetByHealthyRpc()
    {
        using var http = new HttpClient(new SequenceHandler(call => call == 8
            ? new(HttpStatusCode.OK) { Content = new StringContent("{\"result\":\"0x89\"}") }
            : new(HttpStatusCode.TooManyRequests)));
        var options = new PolymarketPolygonOptions { Enabled = true, RpcMinimumIntervalMilliseconds = 0 };
        var rpc = new PolymarketPolygonRpc(http, Options.Create(options));
        foreach (var seconds in new[] { 10, 20, 40, 80, 160, 300, 300 })
        {
            // Simulate expiry without waiting minutes in this request-sequence regression.
            Interlocked.Exchange(ref options.CooldownUntilTicks, 0);
            var failure = await Assert.ThrowsAsync<PolymarketProviderQuotaException>(() => rpc.HeadAsync(default));
            Assert.InRange(failure.RetryAfter.TotalSeconds, seconds - 1, seconds + 1);
        }
        Interlocked.Exchange(ref options.CooldownUntilTicks, 0);
        await rpc.VerifyChainAsync(default);
        var reset = await Assert.ThrowsAsync<PolymarketProviderQuotaException>(() => rpc.HeadAsync(default));
        Assert.InRange(reset.RetryAfter.TotalSeconds, 9, 11);
        Assert.Equal(9, rpc.RequestCount);
    }

    [Fact]
    public async Task ExplicitRetryAfterRemainsAuthoritativeAfterProgressive429s()
    {
        using var http = new HttpClient(new SequenceHandler(call =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            if (call == 3) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
            return response;
        }));
        var options = new PolymarketPolygonOptions { Enabled = true, RpcMinimumIntervalMilliseconds = 0 };
        var rpc = new PolymarketPolygonRpc(http, Options.Create(options));
        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<PolymarketProviderQuotaException>(() => rpc.HeadAsync(default));
            Interlocked.Exchange(ref options.CooldownUntilTicks, 0);
        }
        var specified = await Assert.ThrowsAsync<PolymarketProviderQuotaException>(() => rpc.HeadAsync(default));
        Assert.InRange(specified.RetryAfter.TotalSeconds, 1, 3);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Handler(int status, string body) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
                { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
    private sealed class DelayedThrottle(TaskCompletionSource entered, TaskCompletionSource release) : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; entered.SetResult();
            await release.Task.WaitAsync(ct);
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return response;
        }
    }
    private sealed class SequenceHandler(Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        private int _calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(response(++_calls));
    }
}
