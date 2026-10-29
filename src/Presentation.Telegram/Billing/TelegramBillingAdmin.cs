using Application.Billing;
using System.Net;
using System.Globalization;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Presentation.Telegram.Services;

namespace Presentation.Telegram.Billing;

/// <summary>Bounded actor sessions. Only replies to the current explicit admin prompt are consumed.</summary>
public sealed class AdminWizardStore(TimeProvider clock)
{
    private readonly Dictionary<long, AdminWizard> sessions = [];
    private readonly object gate = new();
    public AdminWizard? Get(long actor) { lock (gate) { Prune(); return sessions.GetValueOrDefault(actor); } }
    public AdminWizard Start(long actor) { lock (gate) { Prune(); if (sessions.Count >= 256) sessions.Remove(sessions.MinBy(x => x.Value.Expires).Key); var s = new AdminWizard { Token = Guid.NewGuid().ToString("N")[..8], Expires = clock.GetUtcNow().AddMinutes(10) }; sessions[actor] = s; return s; } }
    public void Remove(long actor) { lock (gate) sessions.Remove(actor); }
    private void Prune() { foreach (var key in sessions.Where(x => x.Value.Expires <= clock.GetUtcNow()).Select(x => x.Key).ToArray()) sessions.Remove(key); }
}
/// <summary>Process-local wizard state; durable operation identity lives in the confirmed preview.</summary>
public sealed class AdminWizard
{
    public string Token { get; init; } = "";
    public DateTimeOffset Expires { get; init; }
    public long Target { get; set; }
    public int Hours { get; set; }
    public Guid? Revoke { get; set; }
    public string Step { get; set; } = "";
    public int Prompt { get; set; }
    public AdminPreview? Preview { get; set; }
}
/// <summary>Private, allowlisted administration adapter; every entry and reply rechecks authorization.</summary>
public sealed class TelegramBillingAdmin(IBillingAdministration admin, AdminWizardStore store, ITelegramBotClient bot, TelegramGlobalRateLimiter? limiter = null)
{
    private int? viewMessage;
    private static InlineKeyboardButton Button(string label, AdminWizard s, string action) => InlineKeyboardButton.WithCallbackData(label, AdminUiTokens.Prefix + s.Token + ":" + action);
    private async Task Send(long actor, string text, InlineKeyboardMarkup? markup, CancellationToken ct)
        {
        if (limiter != null) await limiter.WaitAsync(actor, ct);
        if (viewMessage is { } message)
        {
            try { await bot.EditMessageText(actor, message, text, parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct); }
            catch (global::Telegram.Bot.Exceptions.ApiRequestException ex) when (ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase)) { }
        }
        else await bot.SendMessage(actor, text, parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct);
    }
    private async Task Prompt(long actor, AdminWizard s, string step, string text, CancellationToken ct)
    { s.Step = step; s.Preview = null; if (limiter != null) await limiter.WaitAsync(actor, ct); var m = await bot.SendMessage(actor, text + AdminText.CancelHint, replyMarkup: new ForceReplyMarkup { Selective = true }, cancellationToken: ct); s.Prompt = m.Id; }
    public async Task<bool> HandleAsync(Update update, CancellationToken ct)
    {
        var callback = update.CallbackQuery;
        var command = update.Message?.Text?.Split(' ', 2)[0].Split('@')[0];
        if (command == AdminUiTokens.MyId && update.Message is { From: { } from } identityMessage)
        {
            store.Remove(from.Id);
            if (limiter != null) await limiter.WaitAsync(identityMessage.Chat.Id, ct);
            await bot.SendMessage(identityMessage.Chat.Id,
                identityMessage.Chat.Type == ChatType.Private && identityMessage.Chat.Id == from.Id
                    ? AdminText.Identity(from.Id) : AdminText.IdentityPrivateOnly, cancellationToken: ct);
            return true;
        }
        var isCommand = command is AdminUiTokens.Command or AdminUiTokens.Cancel;
        var isCallback = callback?.Data?.StartsWith(AdminUiTokens.Prefix, StringComparison.Ordinal) == true;
        var actor = callback?.From.Id ?? update.Message?.From?.Id ?? 0;
        var state = store.Get(actor);
        var reply = update.Message;
        var isReply = reply?.ReplyToMessage?.Id == state?.Prompt && state?.Prompt > 0 && reply?.Text != null;
        var expiredReply = reply?.ReplyToMessage is { From.IsBot: true, Text: { } promptText } && promptText.EndsWith(AdminText.CancelHint, StringComparison.Ordinal) && !isReply;
        if (!isCommand && command?.StartsWith('/') == true) { store.Remove(actor); return false; }
        if (!isCommand && !isCallback && !isReply && !expiredReply) return false;
        var chat = callback?.Message?.Chat ?? reply?.Chat;
        if (chat?.Type != ChatType.Private || chat.Id != actor || !admin.IsOperator(actor))
        {
            store.Remove(actor);
            if (callback != null) await bot.AnswerCallbackQuery(callback.Id, AdminText.Denied, cancellationToken: ct);
            else if (chat != null) { if (limiter != null) await limiter.WaitAsync(chat.Id, ct); await bot.SendMessage(chat.Id, AdminText.Denied, cancellationToken: ct); }
            return true;
        }
        viewMessage = callback?.Message?.Id;
        if (callback != null) await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
        if (command == AdminUiTokens.Cancel) { store.Remove(actor); await Send(actor, AdminText.Cancelled, null, ct); return true; }
        if (command == AdminUiTokens.Command || callback?.Data == AdminUiTokens.Open)
        {
            state = store.Start(actor);
            await Send(actor, AdminText.Home, new InlineKeyboardMarkup(new[] { new[] { Button(AdminText.MyAccount, state, AdminUiTokens.My), Button(AdminText.Lookup, state, AdminUiTokens.Lookup) } }), ct); return true;
        }
        if (state == null || expiredReply) { await Send(actor, BillingAdminErrors.Expired.Description, null, ct); return true; }
        if (isReply)
        {
            var input = reply!.Text!.Trim(); var step = state.Step; state.Prompt = 0;
            if (input.StartsWith('/')) { store.Remove(actor); return false; }
            if (step == AdminUiTokens.Lookup)
            {
                if (long.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var target) && target > 0) { state.Target = target; await Account(actor, state, 0, ct); }
                else await Prompt(actor, state, step, BillingAdminErrors.Unknown.Description, ct);
            }
            else if (step == AdminUiTokens.Custom)
            {
                if (int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var days) && days is >= 1 and <= 365) { state.Hours = days * 24; await Prompt(actor, state, AdminUiTokens.Reason, AdminText.Reason, ct); }
                else await Prompt(actor, state, step, AdminText.CustomDays, ct);
            }
            else if (step == AdminUiTokens.Reason)
            {
                var result = await admin.PreviewAsync(actor, state.Target, state.Hours, state.Revoke, input, ct);
                if (result.IsFailure) await Prompt(actor, state, step, result.Error.Description, ct);
                else { state.Preview = result.Value; state.Step = ""; await Send(actor, AdminText.Preview(result.Value), new InlineKeyboardMarkup(new[] { new[] { Button(AdminText.Confirm, state, AdminUiTokens.Confirm + ":" + result.Value.OperationId.ToString("N")), Button(AdminText.Cancel, state, AdminUiTokens.CancelAction) } }), ct); }
            }
            return true;
        }
        var pieces = callback!.Data!.Split(':');
        if (pieces.Length < 3 || pieces[1] != state.Token) { await Send(actor, BillingAdminErrors.Expired.Description, null, ct); return true; }
        var action = pieces[2]; state.Prompt = 0;
        if (action == AdminUiTokens.CancelAction) { store.Remove(actor); await Send(actor, AdminText.Cancelled, null, ct); }
        else if (action == AdminUiTokens.My) { state.Target = actor; await Account(actor, state, 0, ct); }
        else if (action == AdminUiTokens.Lookup) await Prompt(actor, state, action, AdminText.LookupPrompt, ct);
        else if (action == AdminUiTokens.Page && pieces.Length == 4 && int.TryParse(pieces[3], out var page)) await Account(actor, state, page, ct);
        else if (action == AdminUiTokens.Grant)
        {
            state.Preview = null; state.Revoke = null;
            await Send(actor, AdminText.Duration, new InlineKeyboardMarkup(new[] { new[] { Button("1 hour", state, AdminUiTokens.Hours + ":1"), Button("1 day", state, AdminUiTokens.Hours + ":24"), Button("7 days", state, AdminUiTokens.Hours + ":168") }, new[] { Button("30 days", state, AdminUiTokens.Hours + ":720"), Button("90 days", state, AdminUiTokens.Hours + ":2160"), Button(AdminText.Custom, state, AdminUiTokens.Custom) } }), ct);
        }
        else if (action == AdminUiTokens.Hours && pieces.Length == 4 && int.TryParse(pieces[3], out var hours) && hours is 1 or 24 or 168 or 720 or 2160) { state.Hours = hours; state.Revoke = null; await Prompt(actor, state, AdminUiTokens.Reason, AdminText.Reason, ct); }
        else if (action == AdminUiTokens.Custom) { state.Revoke = null; await Prompt(actor, state, action, AdminText.CustomDays, ct); }
        else if (action == AdminUiTokens.Revoke && pieces.Length == 4 && Guid.TryParseExact(pieces[3], "N", out var grant)) { state.Revoke = grant; await Prompt(actor, state, AdminUiTokens.Reason, AdminText.RevokeReason, ct); }
        else if (action == AdminUiTokens.Confirm && state.Preview is { } preview && pieces.Length == 4 && Guid.TryParseExact(pieces[3], "N", out var operation) && operation == preview.OperationId)
        {
            var result = await admin.ConfirmAsync(actor, preview, ct);
            await Send(actor, result.IsSuccess ? AdminText.Done : result.Error.Description, null, ct);
        }
        else await Send(actor, BillingAdminErrors.Expired.Description, null, ct);
        return true;
    }
    private async Task Account(long actor, AdminWizard s, int page, CancellationToken ct)
    {
        s.Preview = null; var result = await admin.AccountAsync(actor, s.Target, page, ct);
        if (result.IsFailure) { await Send(actor, result.Error.Description, null, ct); return; }
        var a = result.Value; var rows = new List<InlineKeyboardButton[]> { new[] { Button(AdminText.Grant, s, AdminUiTokens.Grant) } };
        foreach (var g in a.Grants.Where(g => g.ReversedAt == null)) rows.Add([Button(AdminText.Revoke + " " + g.Id.ToString("N")[..8], s, AdminUiTokens.Revoke + ":" + g.Id.ToString("N"))]);
        var nav = new List<InlineKeyboardButton>();
        if (a.Page > 0) nav.Add(Button(AdminText.Previous, s, AdminUiTokens.Page + ":" + (a.Page - 1)));
        if (a.More && a.Page < 1000) nav.Add(Button(AdminText.Next, s, AdminUiTokens.Page + ":" + (a.Page + 1)));
        if (nav.Count > 0) rows.Add(nav.ToArray());
        rows.Add([Button(AdminText.Lookup, s, AdminUiTokens.Lookup), Button(AdminText.Cancel, s, AdminUiTokens.CancelAction)]);
        await Send(actor, AdminText.Account(a), new InlineKeyboardMarkup(rows), ct);
    }
}
/// <summary>Compact callback protocol, including a session binding.</summary>
public static class AdminUiTokens
{
    public const string Command = "/admin", Cancel = "/admin_cancel", MyId = "/myid", Prefix = "adm:", Open = "adm:open";
    public const string My = "my", Lookup = "lookup", Grant = "grant", Revoke = "revoke", Hours = "h", Custom = "custom", Reason = "reason", Confirm = "confirm", CancelAction = "cancel", Page = "page";
}
/// <summary>Admin-only copy; all operator-entered values are escaped at rendering.</summary>
public static class AdminText
{
    public const string Menu = "Admin", Home = "<b>Pro administration</b>\nChoose an existing bot account.", MyAccount = "My account", Lookup = "Find user", LookupPrompt = "Reply with the user's positive numeric Telegram ID. Ask them to use /myid in a private chat with this bot.", Denied = "Administration requires an operator account in a private chat.";
    public const string IdentityPrivateOnly = "Open /myid in a private chat with this bot.";
    public static string Identity(long userId) => "Your Telegram ID: " + userId.ToString(CultureInfo.InvariantCulture) + "\nShare this numeric ID with the bot administrator.";
    public const string Grant = "Grant Pro", Revoke = "Revoke", Duration = "Choose the Pro duration.", Custom = "Custom days", CustomDays = "Reply with whole days from 1 to 365.", Reason = "Reply with an audit reason (1–300 characters).", RevokeReason = "Reply with the audit reason for revoking this manual grant (1–300 characters).";
    public const string CancelHint = "\nUse /admin_cancel to cancel. Reply directly to this prompt.", Confirm = "Confirm", Cancel = "Cancel", Cancelled = "Administration cancelled. No pending action was applied.", Done = "Audited action completed.", Previous = "Previous", Next = "Next";
    private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("dd MMM yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);
    public static string Account(AdminAccount a) => $"<b>User {a.UserId}</b>\nAccess: {(a.Pro ? "Pro" : "Free")}" + (a.Until is { } until ? " through " + Utc(until) : "") + (a.Grants.Count == 0 ? "" : "\n\nManual grants · page " + (a.Page + 1) + "\n") + string.Join("\n", a.Grants.Select(g => $"{g.Id.ToString("N")[..8]} · {Utc(g.StartsAt)} → {Utc(g.EndsAt)} · {(g.ReversedAt != null ? "revoked" : "issued")}\n" + WebUtility.HtmlEncode(g.Reason is { Length: > 300 } reason ? reason[..300] : g.Reason)));
    public static string Preview(AdminPreview p) => $"<b>Confirm {(p.Revoke == null ? "Grant Pro" : "Revoke manual grant")}</b>\nUser: {p.Target}\n" + (p.Revoke is { } g ? $"Grant: {g:N}\n" : $"Duration: {p.Hours} hours\n") + Utc(p.StartsAt) + " → " + Utc(p.EndsAt) + "\nReason: " + WebUtility.HtmlEncode(p.Reason) + "\nExpires: " + Utc(p.ExpiresAt);
}
