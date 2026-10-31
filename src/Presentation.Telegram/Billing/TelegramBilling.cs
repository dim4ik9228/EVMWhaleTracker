using System.Globalization;
using System.Net;
using Telegram.Bot.Exceptions;
using Application.Billing;
using Domain.Entities;
using Infrastructure.Billing;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.Payments;
using Telegram.Bot.Types.ReplyMarkups;
using Presentation.Telegram.Services;

namespace Presentation.Telegram.Billing;

/// <summary>Private subscription and financial-update adapter. No payment activation depends on a URL or button.</summary>
public sealed class TelegramBilling(IBillingService billing, AppDbContext db, IOptions<BillingOptions> options,
    NowPaymentsClient crypto, ITelegramBotClient bot, TelegramGlobalRateLimiter? rateLimiter = null, TelegramBillingAdmin? administration = null, IBillingAdministration? adminService = null)
{
    private InlineKeyboardMarkup? SandboxMarkup(InlineKeyboardMarkup? markup)
    {
        if (options.Value.SandboxMode && markup is not null)
            foreach (var button in markup.InlineKeyboard.SelectMany(row => row))
                if (button.CallbackData is { } data && !data.StartsWith(BillingSandbox.Prefix, StringComparison.Ordinal))
                    button.CallbackData = BillingSandbox.Prefix + data;
        return markup;
    }
    private async Task<Message> SendMessageAsync(long chatId, string text, InlineKeyboardMarkup? replyMarkup = null, CancellationToken cancellationToken = default)
    {
        if (rateLimiter is not null) await rateLimiter.WaitAsync(chatId, cancellationToken);
        return await bot.SendMessage(chatId, (options.Value.SandboxMode ? BillingSandbox.Label : "") + text, replyMarkup: SandboxMarkup(replyMarkup), cancellationToken: cancellationToken);
    }
    private bool SalesEnabled => options.Value.StarsSalesEnabled || options.Value.CryptoSalesEnabled;
    private async Task ViewAsync(long user, int? messageId, string text, InlineKeyboardMarkup markup, CancellationToken ct)
    {
        if (options.Value.SandboxMode) text = BillingSandbox.Label + text;
        markup = SandboxMarkup(markup)!;
        if (rateLimiter is not null) await rateLimiter.WaitAsync(user, ct);
        if (messageId is null) await bot.SendMessage(user, text, parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct);
        else
        {
            try { await bot.EditMessageText(user, messageId.Value, text, parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct); }
            catch (ApiRequestException ex) when (ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase)) { }
        }
    }
    private static InlineKeyboardMarkup BackMarkup() => new(InlineKeyboardButton.WithCallbackData(BillingText.Back, BillingUiTokens.Open));
    public async Task<bool> HandleAsync(Update update, CancellationToken ct)
    {
        var sandboxCallback = update.CallbackQuery?.Data?.StartsWith(BillingSandbox.Prefix, StringComparison.Ordinal) == true;
        var sandboxCommand = update.Message?.Text?.StartsWith("/sandbox", StringComparison.Ordinal) == true;
        if (!options.Value.SandboxMode && (sandboxCallback || sandboxCommand)) return true;
        if (options.Value.SandboxMode)
        {
            // Consume every non-test update: none can reach billing administration or pending wallet input.
            var actor = update.CallbackQuery?.From.Id ?? update.Message?.From?.Id;
            var chat = update.CallbackQuery?.Message?.Chat ?? update.Message?.Chat;
            if (actor is null || chat?.Type != ChatType.Private || chat.Id != actor || !options.Value.IsOperator(actor.Value)) return true;
            if (sandboxCallback)
            {
                update.CallbackQuery!.Data = update.CallbackQuery.Data![BillingSandbox.Prefix.Length..];
                if (!update.CallbackQuery.Data.StartsWith(BillingUiTokens.Prefix, StringComparison.Ordinal)
                    && !update.CallbackQuery.Data.StartsWith(BillingUiTokens.CompactPrefix, StringComparison.Ordinal)) return true;
            }
            else if (sandboxCommand)
            {
                var parts = update.Message!.Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var sandboxAction = parts[0].Split('@')[0];
                if (sandboxAction == "/sandboxsimulate")
                {
                    var ok = parts.Length == 3 && Guid.TryParse(parts[1], out var order)
                        && await crypto.SimulateAsync(actor.Value, order, parts[2], ct);
                    await SendMessageAsync(actor.Value, ok ? "Provider simulation requested; authenticated status verification remains required." : "Use /sandboxsimulate <order-id> <success|failed|partially_paid> for your sandbox invoice.", cancellationToken: ct);
                    return true;
                }
                var mapped = sandboxAction switch
                {
                    "/sandboxsubscription" => BillingUiTokens.Subscription,
                    "/sandboxterms" => BillingUiTokens.Terms,
                    "/sandboxsupport" => BillingUiTokens.Support,
                    "/sandboxrecovery" => BillingUiTokens.Recovery,
                    "/sandboximport" => BillingUiTokens.Import,
                    "/sandboxattach" => BillingUiTokens.Attach,
                    _ => null
                };
                if (mapped is null) return true;
                update.Message.Text = mapped + (parts.Length > 1 ? " " + string.Join(' ', parts.Skip(1)) : "");
            }
            else return true;
        }
        if (!options.Value.SandboxMode && administration is not null && await administration.HandleAsync(update, ct)) return true;
        if (update.PreCheckoutQuery is { } q)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(7));
            var ok = Guid.TryParseExact(q.InvoicePayload, "N", out var attempt)
                && (await billing.PreCheckoutAsync(q.From.Id, attempt, q.Currency, q.TotalAmount, deadline.Token)).IsSuccess;
            await bot.AnswerPreCheckoutQuery(q.Id, ok ? null : BillingText.PaymentRejected, cancellationToken: deadline.Token); return true;
        }
        if (update.Message is { SuccessfulPayment: { } paid } message)
        {
            await billing.ReceiveAsync(new(BillingPlans.StarsProvider, BillingKeys.Paid(paid.TelegramPaymentChargeId), paid.TelegramPaymentChargeId,
                Guid.TryParseExact(paid.InvoicePayload, "N", out var id) ? id : null, message.From?.Id, paid.Currency, paid.TotalAmount), ct); return true;
        }
        if (update.Message is { RefundedPayment: { } refund } refunded)
        {
            await billing.ReceiveAsync(new(BillingPlans.StarsProvider, BillingKeys.Refund(refund.TelegramPaymentChargeId), refund.TelegramPaymentChargeId,
                Guid.TryParseExact(refund.InvoicePayload, "N", out var id) ? id : null, refunded.From?.Id, refund.Currency, refund.TotalAmount, true), ct); return true;
        }
        if (update.CallbackQuery is { Data: { } data } callback && (data.StartsWith(BillingUiTokens.Prefix, StringComparison.Ordinal) || data.StartsWith(BillingUiTokens.CompactPrefix, StringComparison.Ordinal)))
        {
            if (callback.Message?.Chat.Type != ChatType.Private || callback.Message.Chat.Id != callback.From.Id)
            { await bot.AnswerCallbackQuery(callback.Id, BillingText.PrivateOnly, cancellationToken: ct); return true; }
            await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
            var messageId = callback.Message.Id;
            if (data == BillingUiTokens.Open) await ShowAsync(callback.From.Id, ct, messageId);
            else if (data == BillingUiTokens.Pro) await ProAsync(callback.From.Id, messageId, ct);
            else if (data == BillingUiTokens.History) await HistoryAsync(callback.From.Id, messageId, ct);
            else if (data == BillingUiTokens.Help) await ViewAsync(callback.From.Id, messageId, BillingText.Help(options.Value.TermsUrl, options.Value.SupportContact), BackMarkup(), ct);
            else if (data == BillingUiTokens.Buy)
            {
                if (!SalesEnabled) await ProAsync(callback.From.Id, messageId, ct);
                else await ViewAsync(callback.From.Id, messageId, BillingText.Agreement(WebUtility.HtmlEncode(options.Value.TermsUrl)),
                    new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData(BillingText.Agree, BillingUiTokens.Agree) }, new[] { InlineKeyboardButton.WithCallbackData(BillingText.Back, BillingUiTokens.Open) } }), ct);
            }
            else if (data == BillingUiTokens.Agree)
            {
                if (!SalesEnabled) await ProAsync(callback.From.Id, messageId, ct);
                else await BuyAsync(callback.From.Id, ct);
            }
            else if (data == BillingUiTokens.Selection) await SelectionAsync(callback.From.Id, ct, messageId: messageId);
            else if (data.StartsWith(BillingUiTokens.SelectionPage, StringComparison.Ordinal) && int.TryParse(data[BillingUiTokens.SelectionPage.Length..], out var page)) await SelectionAsync(callback.From.Id, ct, page, messageId);
            else if (data.StartsWith(BillingUiTokens.CompactPrefix, StringComparison.Ordinal))
            {
                var pieces = data.Split(':');
                if (pieces.Length is 4 or 5 && pieces[1] is BillingUiTokens.SelectSlot or BillingUiTokens.SelectPolymarket
                    && long.TryParse(pieces[2], out var revision) && Guid.TryParseExact(pieces[3], "N", out var wallet)
                    && (pieces.Length == 4 || int.TryParse(pieces[4], out _)))
                {
                    var current = await billing.SummaryAsync(callback.From.Id, ct);
                    var ids = current.FreeSelection.ToList(); var pm = current.FreePolymarket;
                    if (pieces[1] == "s") { if (!ids.Remove(wallet)) ids.Add(wallet); if (pm == wallet && !ids.Contains(wallet)) pm = null; }
                    else if (pieces[1] == "p") { pm = pm == wallet ? null : wallet; if (pm == wallet && !ids.Contains(wallet)) ids.Add(wallet); }
                    var changed = await billing.SelectAsync(callback.From.Id, ids, pm, revision, ct);
                    await SelectionAsync(callback.From.Id, ct, page: pieces.Length == 5 ? int.Parse(pieces[4], CultureInfo.InvariantCulture) : 0,
                        messageId: messageId, error: changed.IsFailure ? changed.Error.Description : null);
                }
            }
            else if (data.StartsWith(BillingUiTokens.TestCrypto, StringComparison.Ordinal) && Guid.TryParseExact(data[BillingUiTokens.TestCrypto.Length..], "N", out var testOrder))
            {
                var purchase = await db.Set<PurchaseOrder>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == testOrder && x.UserId == callback.From.Id && x.Plan == BillingPlans.OperatorTestPlan, ct);
                if (purchase is null || !options.Value.CanTestCheckout(callback.From.Id) || purchase.Fulfilled || purchase.CheckoutExpiresAt <= DateTimeOffset.UtcNow)
                    await SendMessageAsync(callback.From.Id, BillingErrors.Unavailable.Description, cancellationToken: ct);
                else
                {
                    var attempt = await crypto.CheckoutAsync(callback.From.Id, testOrder, ct);
                    await SendMessageAsync(callback.From.Id, BillingText.OperatorTestPrice(purchase) + "\n" +
                        (attempt.CheckoutUrl is null ? BillingText.CreationUnknown + BillingText.AwaitingVerification(testOrder) : BillingText.CryptoQuote),
                        attempt.CheckoutUrl is null ? null : new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl(BillingText.OpenCheckout, attempt.CheckoutUrl)), ct);
                }
            }
            else if (data.StartsWith(BillingUiTokens.Crypto, StringComparison.Ordinal) && Guid.TryParseExact(data[15..], "N", out var order))
            {
                if (!options.Value.CryptoSalesEnabled) { await ProAsync(callback.From.Id, messageId, ct); return true; }
                var attempt = await crypto.CheckoutAsync(callback.From.Id, order, ct);
                if (options.Value.SandboxMode && !NowPaymentsClient.IsSandboxCheckoutUrl(attempt.CheckoutUrl)) attempt.CheckoutUrl = null;
                await SendMessageAsync(callback.From.Id, attempt.CheckoutUrl is null ? BillingText.CreationUnknown + BillingText.AwaitingVerification(order) : BillingText.CryptoQuote,
                    replyMarkup: attempt.CheckoutUrl is null ? null : new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl(BillingText.OpenCheckout, attempt.CheckoutUrl)), cancellationToken: ct);
            }
            return true;
        }
        if (update.Message is not { Text: { } text, From: { } from } m) return false;
        var command = text.Split(' ', 2)[0].Split('@')[0];
        if (command is not (BillingUiTokens.TestCheckout or BillingUiTokens.Subscription or BillingUiTokens.Terms or BillingUiTokens.Support or BillingUiTokens.SelectionCommand or BillingUiTokens.Grant or BillingUiTokens.Refund or BillingUiTokens.Preview or BillingUiTokens.Revoke or BillingUiTokens.Attach or BillingUiTokens.Import or BillingUiTokens.Recovery)) return false;
        if (m.Chat.Type != ChatType.Private || m.Chat.Id != from.Id) { await SendMessageAsync(m.Chat.Id, BillingText.PrivateOnly, cancellationToken: ct); return true; }
        if (command == BillingUiTokens.TestCheckout)
        {
            var result = await billing.CreateOperatorTestOrderAsync(from.Id, ct);
            if (result.IsFailure) await SendMessageAsync(from.Id, result.Error.Description, cancellationToken: ct);
            else await SendMessageAsync(from.Id, BillingText.OperatorTestPrice(result.Value) + BillingText.OperatorTestAgreement,
                new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData(BillingText.OperatorTestConfirm, BillingUiTokens.TestCrypto + result.Value.Id.ToString("N"))), ct);
        }
        else if (command == BillingUiTokens.Subscription) await ShowAsync(from.Id, ct);
        else if (command == BillingUiTokens.Terms) await SendMessageAsync(from.Id, string.IsNullOrWhiteSpace(options.Value.TermsUrl) ? BillingText.TermsPending : options.Value.TermsUrl, cancellationToken: ct);
        else if (command == BillingUiTokens.Support) await SendMessageAsync(from.Id, BillingText.Support(options.Value.SupportContact), cancellationToken: ct);
        else if (command == BillingUiTokens.SelectionCommand)
        {
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 || !long.TryParse(parts[1], out var revision)) await SendMessageAsync(from.Id, BillingText.SelectionHelp, cancellationToken: ct);
            else
            {
                var ids = parts[2] == "-" ? [] : parts[2].Split(',').Select(x => Guid.TryParse(x, out var id) ? id : Guid.Empty).ToArray();
                var pm = parts[3] == "-" ? (Guid?)null : Guid.TryParse(parts[3], out var id) ? id : Guid.Empty;
                var result = await billing.SelectAsync(from.Id, ids, pm, revision, ct);
                await SendMessageAsync(from.Id, result.IsSuccess ? BillingText.SelectionSaved : result.Error.Description, cancellationToken: ct);
            }
        }
        else await OperatorAsync(from.Id, text, ct);
        return true;
    }
    private async Task ShowAsync(long user, CancellationToken ct, int? messageId = null)
    {
        var s = await billing.SummaryAsync(user, ct);
        var rows = new List<InlineKeyboardButton[]>
        {
            new[] { InlineKeyboardButton.WithCallbackData(BillingText.ManageSelection, BillingUiTokens.Selection) },
            new[] { InlineKeyboardButton.WithCallbackData(BillingText.ProDetails, BillingUiTokens.Pro), InlineKeyboardButton.WithCallbackData(BillingText.Payments, BillingUiTokens.History) },
            new[] { InlineKeyboardButton.WithCallbackData(BillingText.HelpTitle, BillingUiTokens.Help) }
        };
        if (!options.Value.SandboxMode && options.Value.IsOperator(user)) rows.Add([InlineKeyboardButton.WithCallbackData(AdminText.Menu, AdminUiTokens.Open)]);
        await ViewAsync(user, messageId, BillingText.Summary(s), new InlineKeyboardMarkup(rows), ct);
    }
    private async Task ProAsync(long user, int messageId, CancellationToken ct)
    {
        var rows = new List<InlineKeyboardButton[]>();
        if (SalesEnabled) rows.Add([InlineKeyboardButton.WithCallbackData(BillingText.ContinuePurchase, BillingUiTokens.Buy)]);
        rows.Add([InlineKeyboardButton.WithCallbackData(BillingText.Back, BillingUiTokens.Open)]);
        await ViewAsync(user, messageId, BillingText.ProDetailsText(SalesEnabled), new InlineKeyboardMarkup(rows), ct);
    }
    /// <summary>Shows money movements and unresolved deposits, keeping transport and quote evidence in the operator ledger.</summary>
    internal static IQueryable<PaymentReceipt> FinancialReceipts(IQueryable<PaymentReceipt> receipts) => receipts.Where(receipt =>
        receipt.Amount > 0 && receipt.Currency != ""
        && (receipt.State == BillingStates.Applied || receipt.State == BillingStates.DuplicatePayment
            || receipt.State == BillingStates.Review || receipt.State == BillingStates.Reversed
            || receipt.State == BillingStates.ReversalPending));

    private async Task HistoryAsync(long user, int messageId, CancellationToken ct)
    {
        var receipts = await (from receipt in FinancialReceipts(db.Set<PaymentReceipt>().AsNoTracking())
            join attempt in db.Set<PaymentAttempt>() on receipt.AttemptId equals attempt.Id
            join order in db.Set<PurchaseOrder>() on attempt.OrderId equals order.Id
            where order.UserId == user
            orderby receipt.ReceivedAt descending select new { receipt.Id, receipt.Provider, receipt.Currency, receipt.Amount, receipt.State }).Take(8).ToListAsync(ct);
        var text = receipts.Count == 0 ? BillingText.NoPayments : BillingText.Receipts(string.Join("\n\n", receipts.Select(x =>
            WebUtility.HtmlEncode(string.Create(CultureInfo.InvariantCulture, $"{x.Provider} {x.Amount} {x.Currency} · {x.State}\nReceipt: {x.Id:N}")))));
        var unresolved = await (from a in db.Set<PaymentAttempt>().AsNoTracking() join o in db.Set<PurchaseOrder>() on a.OrderId equals o.Id
            where o.UserId == user && a.Provider == BillingPlans.CryptoProvider && a.ExternalId == null && a.State != BillingStates.Settled && a.State != BillingStates.Reversed
            orderby a.CreatedAt descending select o.Id).Take(4).ToListAsync(ct);
        foreach (var order in unresolved) text += BillingText.AwaitingVerification(order);
        var periods = await db.Set<AccessGrant>().AsNoTracking().Where(x => x.UserId == user && x.ReversedAt == null)
            .OrderByDescending(x => x.StartsAt).Take(4).ToListAsync(ct);
        if (periods.Count > 0) text += BillingText.AccessPeriods(periods);
        await ViewAsync(user, messageId, text, BackMarkup(), ct);
    }
    private async Task SelectionAsync(long owner, CancellationToken ct, int page = 0, int? messageId = null, string? error = null)
    {
        var summary = await billing.SummaryAsync(owner, ct);
        var wallets = await db.TrackedWallets.AsNoTracking().Where(x => x.UserId == owner).OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToListAsync(ct);
        var bindings = await db.Set<PolymarketSetting>().AsNoTracking().Where(x => wallets.Select(w => w.Id).Contains(x.WalletId) && x.SourceAddress != null && !x.NeedsAttention).Select(x => x.WalletId).ToListAsync(ct);
        var rows = new List<InlineKeyboardButton[]>();
        const int pageSize = 15;
        var pages = Math.Max(1, (wallets.Count + pageSize - 1) / pageSize); page = Math.Clamp(page, 0, pages - 1);
        foreach (var wallet in wallets.Skip(page * pageSize).Take(pageSize))
        {
            var name = wallet.Alias ?? wallet.Address.Value[..8] + "…" + wallet.Address.Value[^4..];
            var buttons = new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData((summary.FreeSelection.Contains(wallet.Id) ? "☑ " : "☐ ") + name, BillingUiTokens.Slot(summary.Revision, wallet.Id, page)) };
            if (bindings.Contains(wallet.Id)) buttons.Add(InlineKeyboardButton.WithCallbackData(summary.FreePolymarket == wallet.Id ? BillingText.PmSelected : BillingText.PmSelect, BillingUiTokens.Polymarket(summary.Revision, wallet.Id, page)));
            rows.Add(buttons.ToArray());
        }
        var navigation = new List<InlineKeyboardButton>();
        if (page > 0) navigation.Add(InlineKeyboardButton.WithCallbackData(BillingText.PreviousPage, BillingUiTokens.SelectionPage + (page - 1)));
        if (page + 1 < pages) navigation.Add(InlineKeyboardButton.WithCallbackData(BillingText.NextPage, BillingUiTokens.SelectionPage + (page + 1)));
        if (navigation.Count > 0) rows.Add(navigation.ToArray());
        rows.Add([InlineKeyboardButton.WithCallbackData(BillingText.Back, BillingUiTokens.Open)]);
        await ViewAsync(owner, messageId, (error is null ? "" : WebUtility.HtmlEncode(error) + "\n\n") + BillingText.SelectionIntro + BillingText.SelectionPage(page + 1, pages, summary.FreeSelection.Count), new InlineKeyboardMarkup(rows), ct);
    }
    private async Task BuyAsync(long user, CancellationToken ct)
    {
        var created = await billing.CreateOrderAsync(user, true, ct);
        if (created.IsFailure) { await SendMessageAsync(user, created.Error.Description, cancellationToken: ct); return; }
        var order = created.Value;
        if (options.Value.StarsSalesEnabled)
        {
            var attempt = await billing.PrepareAttemptAsync(user, order.Id, BillingPlans.StarsProvider, ct);
            if (attempt.IsSuccess)
            {
                if (rateLimiter is not null) await rateLimiter.WaitAsync(user, ct);
                await bot.SendInvoice(user, BillingText.ProTitle, BillingText.InvoiceDescription, attempt.Value.Id.ToString("N"),
                    BillingPlans.Xtr, [new LabeledPrice(BillingText.ProTitle, order.StarsPrice)], providerToken: "", startParameter: "subscription", cancellationToken: ct);
            }
        }
        if (options.Value.CryptoSalesEnabled) await SendMessageAsync(user, BillingText.CryptoOption,
            replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData(BillingText.CryptoButton, BillingUiTokens.Crypto + order.Id.ToString("N"))), cancellationToken: ct);
    }
    private async Task OperatorAsync(long actor, string text, CancellationToken ct)
    {
        if (!options.Value.IsOperator(actor)) { await SendMessageAsync(actor, BillingText.NotAuthorized, cancellationToken: ct); return; }
        var recovery = text.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (recovery[0] == BillingUiTokens.Recovery)
        {
            const int pageSize = 4;
            var page = recovery.Length == 1 ? 0 : int.TryParse(recovery[1], out var parsed) ? Math.Clamp(parsed, 0, 10000) : 0;
            var rows = await (from a in db.Set<PaymentAttempt>().AsNoTracking() join o in db.Set<PurchaseOrder>() on a.OrderId equals o.Id
                where a.Provider == BillingPlans.CryptoProvider && a.ExternalId == null && a.State != BillingStates.Creating && a.State != BillingStates.Settled && a.State != BillingStates.Reversed
                orderby a.CreatedAt, a.Id select new { a.Id, a.OrderId, a.InvoiceId, a.CreatedAt, o.UserId }).Skip(page * pageSize).Take(pageSize).ToListAsync(ct);
            var evidence = await db.Set<PaymentReceipt>().AsNoTracking().Where(x => x.Provider == BillingPlans.CryptoProvider && (x.State == BillingStates.Review || x.State == BillingStates.DuplicatePayment))
                .OrderBy(x => x.ReceivedAt).ThenBy(x => x.Id).Skip(page * pageSize).Take(pageSize).Select(x => new { x.Id, x.AttemptId, x.ChargeId, x.ReceivedAt }).ToListAsync(ct);
            await SendMessageAsync(actor, BillingText.RecoveryPage(page, rows.Select(x => BillingText.RecoveryAttempt(x.Id, x.OrderId, x.InvoiceId, x.CreatedAt, x.UserId))
                .Concat(evidence.Select(x => BillingText.RecoveryEvidence(x.Id, x.AttemptId, x.ChargeId, x.ReceivedAt)))), cancellationToken: ct); return;
        }
        if (recovery[0] == BillingUiTokens.Import)
        {
            if (recovery.Length != 3 || recovery[1].Length > 128 || recovery[2].Length > 500) { await SendMessageAsync(actor, BillingText.OperatorHelp, cancellationToken: ct); return; }
            var imported = await crypto.ImportAsync(actor, recovery[1], recovery[2], ct);
            await SendMessageAsync(actor, imported ? BillingText.OperatorDone : BillingText.PaymentRejected, cancellationToken: ct); return;
        }
        var attachment = text.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);
        if (attachment[0] == BillingUiTokens.Attach)
        {
            var attached = attachment.Length == 6 && long.TryParse(attachment[1], out var attachmentOwner) && Guid.TryParse(attachment[2], out var attempt)
                && await crypto.AttachAsync(actor, attachmentOwner, attempt, attachment[3], attachment[4], attachment[5], ct);
            await SendMessageAsync(actor, attached ? BillingText.OperatorDone : BillingText.OperatorHelp, cancellationToken: ct); return;
        }
        var parts = text.Split(' ', 5, StringSplitOptions.RemoveEmptyEntries);
        if (parts[0] == BillingUiTokens.Preview)
        {
            var owners = await db.TrackedWallets.AsNoTracking().Select(x => x.UserId).Distinct().ToListAsync(ct);
            var over = await db.TrackedWallets.GroupBy(x => x.UserId).CountAsync(x => x.Count() > BillingPlans.FreeWallets, ct);
            await SendMessageAsync(actor, BillingText.Preview(owners.Count, over), cancellationToken: ct);
            foreach (var previewOwner in owners)
            {
                var saved = await db.TrackedWallets.AsNoTracking().Where(x => x.UserId == previewOwner).OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
                var hl = await db.Set<HyperliquidSetting>().CountAsync(x => saved.Contains(x.WalletId) && x.Enabled, ct);
                var pm = await db.Set<PolymarketSetting>().Where(x => saved.Contains(x.WalletId) && x.Enabled).OrderBy(x => x.ActivatedAt).ThenBy(x => x.WalletId).Select(x => x.WalletId).ToListAsync(ct);
                if (saved.Count <= BillingPlans.FreeWallets && hl == 0 && pm.Count <= 1) continue;
                var account = await db.Set<BillingAccount>().AsNoTracking().SingleOrDefaultAsync(x => x.UserId == previewOwner, ct);
                var selected = account?.SelectionInitialized == true ? System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(account.FreeWalletIds)! : saved.Take(BillingPlans.FreeWallets).ToList();
                var selectedPm = account?.SelectionInitialized == true ? account.FreePolymarketWalletId : pm.Where(selected.Contains).Select(x => (Guid?)x).FirstOrDefault();
                await SendMessageAsync(actor, BillingText.PreviewOwner(previewOwner, saved.Count, hl, pm.Count, selected, selectedPm), cancellationToken: ct);
            }
            return;
        }
        if (parts.Length < 5 || !long.TryParse(parts[1], out var owner)) { await SendMessageAsync(actor, BillingText.OperatorHelp, cancellationToken: ct); return; }
        if (parts[0] == BillingUiTokens.Grant && int.TryParse(parts[2], out var hours) && hours is > 0 and <= 720 && parts[3] == BillingUiTokens.Confirm)
        {
            var preview = adminService is null ? null : await adminService.PreviewAsync(actor, owner, hours, null, parts[4], ct);
            var result = preview?.IsSuccess == true ? await adminService!.ConfirmAsync(actor, preview.Value, ct) : null;
            await SendMessageAsync(actor, result?.IsSuccess == true ? BillingText.OperatorDone : result?.Error.Description ?? preview?.Error.Description ?? BillingText.OperatorHelp, cancellationToken: ct);
        }
        else if (parts[0] == BillingUiTokens.Refund && parts[3] == BillingUiTokens.Confirm)
        {
            var receipt = await db.Set<PaymentReceipt>().AsNoTracking().SingleOrDefaultAsync(x => x.Provider == BillingPlans.StarsProvider && x.ChargeId == parts[2] && !x.Reversal && x.UserId == owner
                && (x.State == BillingStates.Applied || x.State == BillingStates.DuplicatePayment), ct);
            if (receipt is null) { await SendMessageAsync(actor, BillingText.PaymentRejected, cancellationToken: ct); return; }
            // An intent is durable before the HTTP call. Ambiguous failures require reconciliation, never blind repeats.
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                await BillingService.LockOwnerAsync(db, owner, ct);
                if (await db.Set<BillingAudit>().AnyAsync(x => x.Action == BillingStates.RefundRequested && x.Reference == parts[2], ct))
                { await SendMessageAsync(actor, BillingText.CreationUnknown, cancellationToken: ct); return; }
                db.Add(new BillingAudit { Id = Guid.NewGuid(), ActorId = actor, UserId = owner, Action = BillingStates.RefundRequested, Reference = parts[2], Reason = parts[4], At = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            }
            await bot.RefundStarPayment(owner, parts[2], ct);
            await billing.ReceiveAsync(new(BillingPlans.StarsProvider, BillingKeys.Refund(parts[2]), parts[2], receipt.AttemptId, owner, BillingPlans.Xtr, receipt.Amount, true), ct);
            await SendMessageAsync(actor, BillingText.OperatorDone, cancellationToken: ct);
        }
        else if (parts[0] == BillingUiTokens.Revoke && parts[3] == BillingUiTokens.Confirm && Guid.TryParse(parts[2], out var grantId))
        {
            var result = adminService is null ? null : await adminService.RevokeExpertAsync(actor, owner, grantId, parts[4], ct);
            await SendMessageAsync(actor, result?.IsSuccess == true ? BillingText.OperatorDone : result?.Error.Description ?? BillingText.OperatorHelp, cancellationToken: ct);
        }
        else await SendMessageAsync(actor, BillingText.OperatorHelp, cancellationToken: ct);
    }

}

/// <summary>Billing presentation copy, isolated from provider and entitlement rules.</summary>
public static class BillingText
{
    public const string OperatorTestAgreement = "\nReal payment for 30 days of Pro. USDT on BSC only; Stars unavailable. Continue only after confirming the amount.";
    public const string OperatorTestConfirm = "Confirm real crypto test checkout";
    public static string OperatorTestPrice(PurchaseOrder order) => "Operator crypto test - USD " + order.UsdPrice.ToString("0.00", CultureInfo.InvariantCulture) + "\nOrder: " + order.Id.ToString("N");

    public const string PaymentRejected = "This payment is unavailable or does not match the purchase. Contact /paysupport.";
    public const string PrivateOnly = "Open /subscription in a private chat with this bot.";
    public const string ProTitle = "Pro · 30 days";
    public const string InvoiceDescription = "50 wallet entries, Polymarket within that allowance, and up to 10 Hyperliquid entries subject to shared capacity. This purchase always credits the original order owner.";
    public const string CryptoOption = "Alternatively: USD 29 via USDT on BNB Smart Chain. The checkout shows the exact USDT quote. A forwarded checkout credits this order's original owner.";
    public const string CryptoButton = "USDT on BNB Smart Chain · USD 29";
    public const string CryptoQuote = "Pay only USDT on BNB Smart Chain. Check the exact payable amount in the hosted checkout; fees are absorbed by the merchant.";
    public const string OpenCheckout = "Open hosted checkout";
    public const string CreationUnknown = "Payment has not yet been verified. Please contact /paysupport before making another transfer.";
    public const string Renew = "Renew Pro", Upgrade = "Upgrade to Pro";
    public const string ManageSelection = "Manage Free selection", Back = "Subscription", PmSelected = "☑ Polymarket", PmSelect = "Select Polymarket", Agree = "I agree · continue";
    public const string SelectionIntro = "Choose up to ten Free entries and one Polymarket binding. Changes apply immediately. Saved preferences are preserved. Selecting Polymarket explicitly enables the chosen binding, subject to source capacity.";
    public static string Agreement(string terms) => "Pro is a one-time 30-day purchase with manual renewal. Forwarded invoices credit the original order owner. Read the terms before continuing: " + terms + " · /paysupport";
    public const string TermsPending = "Purchases are unavailable until merchant terms and support are configured.";
    public const string SelectionHelp = "/free_selection REVISION wallet-id,wallet-id PM-wallet-id (use - for none). IDs must belong to you.";
    public const string SelectionSaved = "Free selection saved.";
    public const string EvmPaused = "Paused by plan selection or while access is being refreshed. Open /subscription; settings remain editable.";
    public const string PreviousPage = "Previous", NextPage = "Next";
    public static string SelectionPage(int page, int pages, int selected) => $"\nPage {page}/{pages} · selected {selected}/10.";
    public const string NotAuthorized = "Operator access is required.";
    public static string AwaitingVerification(Guid order) => $"\n\nPayment has not yet been verified. Contact /paysupport with your order reference: {order:N}.";
    public static string RecoveryPage(int page, IEnumerable<string> rows) => $"Recovery page {page} · /billing_recovery {page + 1}\n" + string.Join("\n", rows);
    public static string RecoveryAttempt(Guid attempt, Guid order, string? invoice, DateTimeOffset created, long owner) =>
        $"{(invoice is null ? "Ambiguous creation" : "Unknown payment identity")} · age {Math.Max(0, (DateTimeOffset.UtcNow - created).TotalHours):F1}h · {created:u}\nOwner {owner} · Attempt {attempt:N} · Order {order:N} · Invoice {invoice ?? "unknown"}";
    public static string RecoveryEvidence(Guid receipt, Guid? attempt, string payment, DateTimeOffset received) => $"Received review evidence · age {Math.Max(0, (DateTimeOffset.UtcNow - received).TotalHours):F1}h · {received:u} · Receipt {receipt:N} · Attempt {attempt} · Payment {payment}";
    public const string OperatorHelp = "/billing_recovery [page]; /billing_import PAYMENT REASON; /billing_grant USER HOURS confirm REASON; /billing_revoke USER GRANT confirm REASON; /billing_refund USER CHARGE confirm REASON; /billing_attach USER ATTEMPT INVOICE PAYMENT REASON";
    public const string OperatorDone = "Audited action completed.";
    public static string Support(string contact) => "Payment support and refund requests: " + (string.IsNullOrWhiteSpace(contact) ? "not configured; purchases are disabled." : contact) + " Include your purchase receipt. Never send secrets or private keys.";
    public static string Receipts(string receipts) => "Recent purchase receipts (include the receipt ID in /paysupport):\n" + receipts;
    public static string Preview(int owners, int over) => $"Enforcement preview: {owners} owners; {over} exceed ten saved entries. No preferences or access grants changed.";
    public static string PreviewOwner(long owner, int saved, int hyperliquid, int polymarket, IReadOnlyList<Guid> selected, Guid? selectedPm) =>
        $"Owner {owner}: saved {saved}; desired Hyperliquid {hyperliquid} (Free pauses all); desired Polymarket {polymarket}.\nProposed Free entries: {string.Join(",", selected)}\nProposed Polymarket: {selectedPm?.ToString() ?? "—"}";
    public const string ProDetails = "Pro details", Payments = "Payments", HelpTitle = "Help", ContinuePurchase = "Continue to terms";
    public const string NoPayments = "<b>Payments</b>\n\nNo payment receipts yet.";
    public static string AccessPeriods(IEnumerable<AccessGrant> periods) => "\n\n<b>Access periods (UTC)</b>\n"
        + string.Join("\n", periods.Select(x => x.StartsAt.UtcDateTime.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture)
            + " → " + x.EndsAt.UtcDateTime.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture)));
    public const string SalesUnavailable = "Purchases are currently unavailable.";
    public static string ProDetailsText(bool salesEnabled) =>
        "<b>Pro · 30 days</b>\n\n50 wallet entries\nPolymarket within the 50-entry allowance\nUp to 10 Hyperliquid entries\n\n1,450 Stars or USD 29 via USDT on BNB Smart Chain (USDTBSC).\nOne-time purchase · manual renewal.\n\nHyperliquid and Polymarket admission depends on shared source capacity.\nFree includes 10 entries, one Polymarket entry and no Hyperliquid. All supported EVM chains, aliases and filters are included."
        + (salesEnabled ? "" : "\n\n" + SalesUnavailable);
    public static string Help(string terms, string contact) => "<b>Subscription help</b>\n\n"
        + WebUtility.HtmlEncode(Support(contact)) + "\n\nTerms: " + WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(terms) ? TermsPending : terms);
    public static string Summary(BillingSummary s)
    {
        var lines = new List<string> { "<b>Subscription · " + (s.Pro ? "Pro" : "Free") + "</b>", "",
            string.Create(CultureInfo.InvariantCulture, $"Wallets: {s.Saved}/{(s.Pro ? 50 : 10)}") };
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? paidUntil = null;
        if (s.Pro)
        {
            // A later grant after a refund-created gap is not part of the current paid period.
            foreach (var period in s.Periods.Where(x => x.ReversedAt is null).OrderBy(x => x.StartsAt))
                if (period.Contains(now) || paidUntil is { } end && period.StartsAt <= end && period.EndsAt > end)
                    paidUntil = period.EndsAt > paidUntil.GetValueOrDefault() ? period.EndsAt : paidUntil;
            if (s.Periods.Count == 0) paidUntil = s.PaidUntil;
        }
        if (paidUntil is { } until) lines.Add("Paid through: " + until.UtcDateTime.ToString("dd MMM yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture));
        if (!s.Pro && s.FreePolymarket is not null) lines.Add("Free Polymarket: selected");
        if (s.Saved > (s.Pro ? 50 : 10) && s.Effective < s.Saved) lines.Add($"Paused wallets: {s.Saved - s.Effective}");
        lines.Add("\nManage wallets: /list");
        return string.Join("\n", lines);
    }
}
