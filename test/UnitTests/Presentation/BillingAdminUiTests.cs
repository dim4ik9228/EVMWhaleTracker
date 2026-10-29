using Application.Billing;
using Domain.Common;
using Presentation.Telegram.Billing;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using System.Net;
using System.Text;

namespace UnitTests.Presentation;

public sealed class BillingAdminUiTests
{
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Admin : IBillingAdministration
    {
        public int Reads, Confirms;
        public bool Authorized = true;
        public bool IsOperator(long actor) => Authorized && actor == 12;
        public Task<Result<AdminAccount>> AccountAsync(long actor, long target, int page, CancellationToken ct) { Reads++; return Task.FromResult(Result<AdminAccount>.Success(new(target, 0, false, null, [], 0, false))); }
        public Task<Result<AdminPreview>> PreviewAsync(long actor, long target, int hours, Guid? revoke, string reason, CancellationToken ct) => throw new InvalidOperationException();
        public Task<Result> ConfirmAsync(long actor, AdminPreview preview, CancellationToken ct) { Confirms++; return Task.FromResult(Result.Success()); }
        public Task<Result> RevokeExpertAsync(long actor, long target, Guid grant, string reason, CancellationToken ct) => throw new InvalidOperationException();
    }
    private sealed class Capture : HttpMessageHandler
    {
        public List<string> Bodies = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            var json = request.RequestUri!.Segments.Last() == "answerCallbackQuery" ? "{\"ok\":true,\"result\":true}" : "{\"ok\":true,\"result\":{\"message_id\":42,\"date\":1793059200,\"chat\":{\"id\":12,\"type\":\"private\"},\"text\":\"fixture\"}}";
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
    private static Update Callback(string data, ChatType type = ChatType.Private, long chat = 12) => new() { CallbackQuery = new() { Id = "fixture", Data = data, From = new() { Id = 12, FirstName = "test" }, Message = new() { Id = 1, Chat = new() { Id = chat, Type = type } } } };
    [Theory]
    [InlineData(ChatType.Private, 13, true)]
    [InlineData(ChatType.Group, -100, false)]
    public async Task MyIdIsPrivateAndAvailableWithoutOperatorAccess(ChatType type, long chat, bool identityShown)
    {
        var admin = new Admin { Authorized = false }; var capture = new Capture();
        var ui = new TelegramBillingAdmin(admin, new(TimeProvider.System), new TelegramBotClient("123456:fixture-token", new HttpClient(capture)));
        var update = new Update { Message = new() { Id = 1, Text = AdminUiTokens.MyId, From = new() { Id = 13, FirstName = "test" }, Chat = new() { Id = chat, Type = type } } };
        Assert.True(await ui.HandleAsync(update, default));
        var body = Assert.Single(capture.Bodies);
        Assert.Contains(identityShown ? "Your Telegram ID: 13" : AdminText.IdentityPrivateOnly, body);
        Assert.Equal(0, admin.Reads); Assert.Equal(0, admin.Confirms);
        Assert.Contains(AdminUiTokens.MyId, AdminText.LookupPrompt);
    }
    [Theory]
    [InlineData(ChatType.Group, 12)]
    [InlineData(ChatType.Private, 13)]
    public async Task PrivateGuardRejectsForgedCallbacksWithoutReads(ChatType type, long chat)
    {
        var admin = new Admin(); var capture = new Capture(); var ui = new TelegramBillingAdmin(admin, new(TimeProvider.System), new TelegramBotClient("123456:fixture-token", new HttpClient(capture)));
        Assert.True(await ui.HandleAsync(Callback(AdminUiTokens.Open, type, chat), default)); Assert.Equal(0, admin.Reads); Assert.Equal(0, admin.Confirms);
    }
    [Fact]
    public async Task LostExpiredAndCancelledPromptRepliesAreConsumedBeforeWalletFlows()
    {
        var clock = new Clock(); var store = new AdminWizardStore(clock); var admin = new Admin(); var capture = new Capture(); var ui = new TelegramBillingAdmin(admin, store, new TelegramBotClient("123456:fixture-token", new HttpClient(capture)));
        var s = store.Start(12); s.Prompt = 42; s.Step = AdminUiTokens.Lookup;
        clock.Now = clock.Now.AddMinutes(10);
        var reply = new Update { Message = new() { Id = 2, Text = "123", From = new() { Id = 12, FirstName = "test" }, Chat = new() { Id = 12, Type = ChatType.Private }, ReplyToMessage = new() { Id = 42, From = new() { Id = 55, IsBot = true, FirstName = "bot" }, Text = AdminText.LookupPrompt + AdminText.CancelHint } } };
        Assert.True(await ui.HandleAsync(reply, default)); Assert.Equal(0, admin.Reads);
        store.Start(12); Assert.True(await ui.HandleAsync(reply, default)); Assert.Equal(0, admin.Reads);
        reply.Message.ReplyToMessage = null; Assert.False(await ui.HandleAsync(reply, default));
        reply.Message.Text = "/list"; Assert.False(await ui.HandleAsync(reply, default)); Assert.Null(store.Get(12));
    }
    [Fact]
    public async Task OldConfirmationCannotConfirmAnotherPreviewAndAuthorizationIsRechecked()
    {
        var clock = new Clock(); var store = new AdminWizardStore(clock); var admin = new Admin(); var capture = new Capture(); var ui = new TelegramBillingAdmin(admin, store, new TelegramBotClient("123456:fixture-token", new HttpClient(capture)));
        var s = store.Start(12); var old = Guid.NewGuid();
        s.Preview = new(Guid.NewGuid(), 12, 13, 0, 24, null, "reason", clock.Now, clock.Now.AddDays(1), clock.Now.AddMinutes(10), clock.Now);
        var prefix = AdminUiTokens.Prefix + s.Token + ":" + AdminUiTokens.Confirm + ":";
        Assert.True(Encoding.UTF8.GetByteCount(prefix + s.Preview.OperationId.ToString("N")) <= 64);
        await ui.HandleAsync(Callback(prefix + old.ToString("N")), default); Assert.Equal(0, admin.Confirms);
        admin.Authorized = false; await ui.HandleAsync(Callback(prefix + s.Preview.OperationId.ToString("N")), default); Assert.Equal(0, admin.Confirms); Assert.Null(store.Get(12));
    }
    [Fact]
    public async Task CancelDropsPreviewAndOldConfirmCannotMutate()
    {
        var clock = new Clock(); var store = new AdminWizardStore(clock); var admin = new Admin(); var capture = new Capture(); var ui = new TelegramBillingAdmin(admin, store, new TelegramBotClient("123456:fixture-token", new HttpClient(capture)));
        var s = store.Start(12); s.Preview = new(Guid.NewGuid(), 12, 13, 0, 24, null, "reason", clock.Now, clock.Now.AddDays(1), clock.Now.AddMinutes(10), clock.Now);
        var confirm = AdminUiTokens.Prefix + s.Token + ":" + AdminUiTokens.Confirm + ":" + s.Preview.OperationId.ToString("N");
        await ui.HandleAsync(Callback(AdminUiTokens.Prefix + s.Token + ":" + AdminUiTokens.CancelAction), default);
        await ui.HandleAsync(Callback(confirm), default); Assert.Equal(0, admin.Confirms); Assert.Null(store.Get(12));
    }
    [Fact]
    public void StoreIsBoundedAndReasonIsEscaped()
    {
        var store = new AdminWizardStore(TimeProvider.System); store.Start(1); for (var i = 2; i <= 257; i++) store.Start(i); Assert.Null(store.Get(1));
        var now = DateTimeOffset.UtcNow; var html = AdminText.Preview(new(Guid.NewGuid(), 12, 13, 0, 1, null, "<b>&", now, now.AddHours(1), now.AddMinutes(10), now));
        Assert.Contains("&lt;b&gt;&amp;", html);
    }
}
