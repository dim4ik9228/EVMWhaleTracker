using System.Net;
using System.Text;
using Application.Billing;
using Domain.Common;
using Domain.Entities;
using Infrastructure.Billing;
using Microsoft.Extensions.Options;
using Presentation.Telegram.Billing;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Exceptions;

namespace UnitTests.Presentation;

public sealed class SubscriptionLayoutTests
{
    [Fact]
    public void OverviewHidesAbsentDetailsAndSeparatesTierAllowances()
    {
        var free = BillingText.Summary(Summary());
        Assert.Contains("Wallets: 3/10", free);
        Assert.Contains("/list", free);
        Assert.DoesNotContain("Paid through", free);
        Assert.DoesNotContain("capacity", free);
        Assert.DoesNotContain("receipt", free);
        Assert.DoesNotContain("Polymarket", free);
        var pro = BillingText.Summary(Summary() with { Pro = true, PaidUntil = DateTimeOffset.Parse("2026-11-28T12:00:00Z") });
        Assert.Contains("Wallets: 3/50", pro);
        Assert.Contains("28 Nov 2026", pro);
        Assert.DoesNotContain("Free selection", pro);
    }

    [Theory]
    [InlineData(BillingUiTokens.Open)]
    [InlineData(BillingUiTokens.Pro)]
    [InlineData(BillingUiTokens.Help)]
    [InlineData(BillingUiTokens.Buy)]
    [InlineData(BillingUiTokens.Agree)]
    [InlineData("billing:crypto:00000000000000000000000000000001")]
    public async Task ReadOnlyAndDisabledSalesCallbacksReuseMessageWithoutFinancialCalls(string data)
    {
        var handler = new CaptureHandler();
        var ui = new TelegramBilling(new ReadOnlyBilling(), null!, Options.Create(new BillingOptions()), null!,
            new TelegramBotClient("123456:fixture-token", new HttpClient(handler)));
        await ui.HandleAsync(new Update { CallbackQuery = new CallbackQuery { Id = "fixture", From = new User { Id = 12, FirstName = "Test" },
            Data = data, Message = new Message { Id = 42, Chat = new Chat { Id = 12, Type = ChatType.Private } } } }, default);
        Assert.Equal(new[] { "answerCallbackQuery", "editMessageText" }, handler.Methods);
        Assert.Contains("42", handler.Bodies.Last());
        Assert.DoesNotContain("billing:agree", handler.Bodies.Last());
        if (data is not (BillingUiTokens.Open or BillingUiTokens.Help)) Assert.Contains(BillingText.SalesUnavailable, handler.Bodies.Last());
    }

    [Fact]
    public async Task SubscriptionCommandOpensOneFreshOverview()
    {
        var handler = new CaptureHandler();
        var ui = new TelegramBilling(new ReadOnlyBilling(), null!, Options.Create(new BillingOptions()), null!, new TelegramBotClient("123456:fixture-token", new HttpClient(handler)));
        await ui.HandleAsync(new Update { Message = new Message { Id = 7, Text = "/subscription", From = new User { Id = 12, FirstName = "Test" },
            Chat = new Chat { Id = 12, Type = ChatType.Private } } }, default);
        Assert.Equal(new[] { "sendMessage" }, handler.Methods);
    }

    [Fact]
    public async Task ForeignChatCallbackIsRejectedBeforeReadingBilling()
    {
        var handler = new CaptureHandler();
        var ui = new TelegramBilling(null!, null!, Options.Create(new BillingOptions()), null!, new TelegramBotClient("123456:fixture-token", new HttpClient(handler)));
        await ui.HandleAsync(new Update { CallbackQuery = new CallbackQuery { Id = "fixture", From = new User { Id = 12, FirstName = "Test" },
            Data = BillingUiTokens.Open, Message = new Message { Id = 42, Chat = new Chat { Id = 13, Type = ChatType.Private } } } }, default);
        Assert.Equal(new[] { "answerCallbackQuery" }, handler.Methods);
    }

    [Fact]
    public void HelpEscapesConfiguredText()
    {
        var text = BillingText.Help("<terms>", "<support>&");
        Assert.Contains("&lt;terms&gt;", text);
        Assert.Contains("&lt;support&gt;&amp;", text);
    }

    [Fact]
    public void OverviewShowsOnlyTheCurrentContiguousPaidPeriod()
    {
        var now = DateTimeOffset.UtcNow;
        var currentEnd = now.AddDays(2);
        var periods = new[]
        {
            new AccessGrant { StartsAt = now.AddDays(-2), EndsAt = now.AddDays(1) },
            new AccessGrant { StartsAt = now.AddDays(1), EndsAt = currentEnd },
            new AccessGrant { StartsAt = now.AddDays(5), EndsAt = now.AddDays(8) }
        };
        var summary = Summary() with { Pro = true, PaidUntil = periods[2].EndsAt, Periods = periods };
        Assert.Contains(currentEnd.UtcDateTime.ToString("dd MMM yyyy HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture), BillingText.Summary(summary));
        Assert.DoesNotContain("Paid through", BillingText.Summary(summary with { Pro = false }));
    }

    [Theory]
    [InlineData(BillingUiTokens.Pro)]
    [InlineData(BillingUiTokens.Buy)]
    public async Task SalesEnabledDetailsAndTermsDoNotCreatePayment(string data)
    {
        var handler = new CaptureHandler();
        var ui = new TelegramBilling(new ReadOnlyBilling(), null!, Options.Create(new BillingOptions { StarsSalesEnabled = true }), null!,
            new TelegramBotClient("123456:fixture-token", new HttpClient(handler)));
        await ui.HandleAsync(Callback(data), default);
        Assert.Equal(new[] { "answerCallbackQuery", "editMessageText" }, handler.Methods);
        Assert.Contains(data == BillingUiTokens.Buy ? BillingUiTokens.Agree : BillingUiTokens.Buy, handler.Bodies.Last());
    }

    [Fact]
    public async Task RepeatedViewIgnoresOnlyTheTelegramUnchangedMessageError()
    {
        var handler = new CaptureHandler { EditError = "Bad Request: message is not modified" };
        var ui = new TelegramBilling(new ReadOnlyBilling(), null!, Options.Create(new BillingOptions()), null!,
            new TelegramBotClient("123456:fixture-token", new HttpClient(handler)));
        await ui.HandleAsync(Callback(BillingUiTokens.Open), default);
        handler.EditError = "Bad Request: message to edit not found";
        await Assert.ThrowsAsync<ApiRequestException>(() => ui.HandleAsync(Callback(BillingUiTokens.Open), default));
    }

    [Fact]
    public void SelectionCallbacksCarryTheirPageWithinTelegramLimit()
    {
        var wallet = Guid.NewGuid();
        Assert.EndsWith(":3", BillingUiTokens.Slot(long.MaxValue, wallet, 3));
        Assert.EndsWith(":3", BillingUiTokens.Polymarket(long.MaxValue, wallet, 3));
        Assert.True(Encoding.UTF8.GetByteCount(BillingUiTokens.Slot(long.MaxValue, wallet, 3)) <= 64);
    }

    private static Update Callback(string data) => new() { CallbackQuery = new CallbackQuery { Id = "fixture", From = new User { Id = 12, FirstName = "Test" },
        Data = data, Message = new Message { Id = 42, Chat = new Chat { Id = 12, Type = ChatType.Private } } } };

    private static BillingSummary Summary() => new(12, 1, false, 3, 3, null, [], null, [], 10, 20);
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<string> Methods { get; } = [];
        public List<string> Bodies { get; } = [];
        public string? EditError { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var method = request.RequestUri!.Segments.Last(); Methods.Add(method);
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            if (method == "editMessageText" && EditError is not null)
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { ok = false, error_code = 400, description = EditError }), Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(method == "answerCallbackQuery"
                ? "{\"ok\":true,\"result\":true}"
                : "{\"ok\":true,\"result\":{\"message_id\":42,\"date\":1793059200,\"chat\":{\"id\":12,\"type\":\"private\"},\"text\":\"fixture\"}}", Encoding.UTF8, "application/json") };
        }
    }
    private sealed class ReadOnlyBilling : IBillingService
    {
        public Task<BillingSummary> SummaryAsync(long userId, CancellationToken ct) => Task.FromResult(Summary());
        public Task<Result> AddWalletAsync(TrackedWallet wallet, CancellationToken ct) => throw new InvalidOperationException();
        public Task<Result> CanEnableAsync(long userId, Guid walletId, TrackingMode mode, CancellationToken ct) => throw new InvalidOperationException();
        public Task<Result> SelectAsync(long userId, IReadOnlyCollection<Guid> wallets, Guid? pm, long revision, CancellationToken ct) => throw new InvalidOperationException();
        public Task<Result<PurchaseOrder>> CreateOrderAsync(long userId, bool renewal, CancellationToken ct) => throw new InvalidOperationException();
        public Task<Result<PaymentAttempt>> PrepareAttemptAsync(long userId, Guid orderId, string provider, CancellationToken ct) => throw new InvalidOperationException();
        public Task<Result> PreCheckoutAsync(long userId, Guid attemptId, string currency, long total, CancellationToken ct) => throw new InvalidOperationException();
        public Task ReceiveAsync(FinancialReceipt receipt, CancellationToken ct) => throw new InvalidOperationException();
        public Task RefreshAsync(long userId, CancellationToken ct) => throw new InvalidOperationException();
    }
}
