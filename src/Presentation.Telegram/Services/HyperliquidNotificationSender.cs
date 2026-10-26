using Application.Hyperliquid;
using Microsoft.Extensions.Logging;
using Presentation.Telegram.Workers;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types;

namespace Presentation.Telegram.Services;

/// <summary>Core rendering uses the existing Telegram retry policy and shared cross-source rate budgets.</summary>
public sealed class HyperliquidNotificationSender(ITelegramBotClient client, TelegramGlobalRateLimiter limiter,
    ILogger<HyperliquidNotificationSender> logger, HyperliquidNotificationRenderer renderer,
    ITelegramBotUsernameProvider usernameProvider) : IHyperliquidNotificationSender
{
    private readonly TelegramNotificationSendRetryPolicy _retry = new();
    public async Task<bool> SendAsync(long userId, HyperliquidNotification notification, Func<CancellationToken, Task<bool>> stillEligible, CancellationToken ct)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attempt.CancelAfter(TimeSpan.FromSeconds(30));
        var html = renderer.Render(notification, await usernameProvider.GetBotUsernameAsync(attempt.Token));
        var eligible = true;
        var success = await _retry.SendAsync(userId,
        token => limiter.WaitAsync(userId, token), async token =>
        {
            if (!await stillEligible(token)) { eligible = false; return; }
            await client.SendMessage(userId, html, parseMode: ParseMode.Html,
                linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true }, cancellationToken: token);
        }, logger, attempt.Token);
        return success && eligible;
    }
}
