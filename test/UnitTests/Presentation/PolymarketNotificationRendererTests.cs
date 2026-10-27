using System.Globalization;
using Application.Polymarket;
using Presentation.Telegram.Services;

namespace UnitTests.Presentation;

public sealed class PolymarketNotificationRendererTests
{
    [Fact]
    public void Render_PreservesEscapingDecimalCultureAndTransactionLineBreak()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fi-FI");
            var activity = new PolymarketActivity("event", "invalid", new DateTimeOffset(2026, 10, 27, 12, 34, 56, TimeSpan.Zero),
                "BUY", "42", "A < B", "Yes & no", 2.5m, 0.4m, 1m, "pUSD", "market-slug", "0x" + new string('a', 64), 0.01m);
            var result = new PolymarketNotificationRenderer().Render(new(Guid.Empty, "A & B", activity), null);

            Assert.Equal("<b>A &amp; B</b> | Polymarket\n🟢 Bought Yes &amp; no · 2.5 shares\nA &lt; B\n0.4 pUSD/share · 1 pUSD gross · 0.01 pUSD fee\n"
                + "<a href=\"https://polymarket.com/market/market-slug\">Market</a>\n"
                + "<a href=\"https://polygonscan.com/tx/0x" + new string('a', 64) + "\">Transaction</a>\n2026-10-27 12.34.56 UTC", result);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
