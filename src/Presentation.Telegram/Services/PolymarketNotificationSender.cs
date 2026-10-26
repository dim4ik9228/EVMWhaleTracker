using Application.Polymarket;
using Infrastructure.Polymarket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Presentation.Telegram.Workers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Presentation.Telegram.Services;

/// <summary>Shares Telegram budgets/retry policy; the unverified source gate prevents accidental sends.</summary>
public sealed class PolymarketNotificationSender(ITelegramBotClient client, TelegramGlobalRateLimiter limiter,
    ILogger<PolymarketNotificationSender> logger, PolymarketNotificationRenderer renderer,
    ITelegramBotUsernameProvider usernames, IOptions<PolymarketOptions> options,
    IOptions<PolymarketPolygonOptions> polygon) : IPolymarketNotificationSender
{
    private readonly TelegramNotificationSendRetryPolicy _retry = new();
    public async Task<bool> SendAsync(long userId, PolymarketNotification notification,
        Func<CancellationToken, Task<bool>> stillEligible, CancellationToken ct)
    {
        if (!options.Value.Enabled || !polygon.Value.Enabled || !PolymarketCapabilities.ActivityVerified) return false;
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attempt.CancelAfter(TimeSpan.FromSeconds(30));
        var html = renderer.Render(notification, await usernames.GetBotUsernameAsync(attempt.Token));
        var eligible = true;
        var success = await _retry.SendAsync(userId, token => limiter.WaitAsync(userId, token), async token =>
        {
            if (!options.Value.Enabled || !polygon.Value.Enabled || !await stillEligible(token)) { eligible = false; return; }
            await client.SendMessage(userId, html, parseMode: ParseMode.Html,
                linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true }, cancellationToken: token);
        }, logger, attempt.Token);
        return success && eligible;
    }
}
