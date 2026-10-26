using System.Text.Json;
using Application.Hyperliquid;
using Infrastructure.Hyperliquid;

namespace UnitTests.Infrastructure;

public sealed class HyperliquidProtocolTests
{
    [Fact]
    public void DisabledCoreDoesNotValidateSourceBoundsAtHostStartup()
    {
        var options = new HyperliquidOptions { QueueCapacity = 0, MaxUniqueWallets = 99 };
        Assert.False(options.IsValid());
        Assert.True(options.IsValidForStartup());
        options.Enabled = true;
        Assert.False(options.IsValidForStartup());
    }

    private const string Address = "0x0000000000000000000000000000000000000001";
    private static readonly HashSet<string> Markets = ["BTC", "ETH"];

    [Theory]
    [InlineData("0", "B", "2", "Open long")]
    [InlineData("0", "A", "2", "Open short")]
    [InlineData("2", "B", "1", "Increase position")]
    [InlineData("-2", "A", "1", "Increase position")]
    [InlineData("2", "A", "1", "Reduce position")]
    [InlineData("-2", "B", "1", "Reduce position")]
    [InlineData("2", "A", "2", "Close position")]
    [InlineData("-2", "B", "3", "Flip position")]
    public void SignedPositionDeterminesEffect(string start, string side, string size, string expected)
    {
        var row = Fill(start, side, size);
        Assert.Equal(expected, HyperliquidProtocol.Parse(Address, "userFills", row, Markets)!.Action);
    }

    [Fact]
    public void ZeroHashPartialFillsKeepDistinctExecutionIdentitiesAndExactDecimals()
    {
        var first = HyperliquidProtocol.Parse(Address, "userFills", Fill("0", "B", "0.00000001", tid: 101), Markets)!;
        var second = HyperliquidProtocol.Parse(Address, "userFills", Fill("0", "B", "0.00000001", tid: 102), Markets)!;
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(0.00000123456789m, first.Usd);
        Assert.Equal(first.Id, HyperliquidProtocol.Parse(Address.ToUpperInvariant(), "userFills", Fill("0", "B", "0.00000001", tid: 101), Markets)!.Id);
    }

    [Fact]
    public void ReusedTradeAndOrderIdsInDifferentBlocksRemainDistinct()
    {
        var first = HyperliquidProtocol.Parse(Address, "userFills", Fill(time: 100), Markets)!;
        var later = HyperliquidProtocol.Parse(Address, "userFills", Fill(time: 200), Markets)!;
        Assert.NotEqual(first.Id, later.Id);
    }

    [Fact]
    public void LiquidationFillOnlySuppressesIdentifiedVictim()
    {
        using var victim = JsonDocument.Parse(Fill("1", "A", "1").GetRawText().Replace("\"hash\":\"0x0\"", $"\"hash\":\"0x0\",\"liquidation\":{{\"liquidatedUser\":\"{Address}\",\"method\":\"market\"}}"));
        Assert.True(HyperliquidProtocol.Parse(Address, "userFills", victim.RootElement, Markets)!.LiquidatedVictim);
        Assert.False(HyperliquidProtocol.Parse("0x0000000000000000000000000000000000000002", "userFills", victim.RootElement, Markets)!.LiquidatedVictim);
    }

    [Theory]
    [InlineData("@1")]
    [InlineData("xyz:BTC")]
    [InlineData("UNKNOWN")]
    public void UnsupportedMarketsAreExcluded(string coin) => Assert.Null(HyperliquidProtocol.Parse(Address, "userFills", Fill("0", "B", "1", coin: coin), Markets));

    [Fact]
    public void LedgerIdentityIncludesSameTimeDistinctFactsAndIgnoresPropertyOrder()
    {
        using var a = JsonDocument.Parse("""{"time":100,"hash":"0x0","delta":{"type":"deposit","usdc":"10"}}""");
        using var b = JsonDocument.Parse("""{"delta":{"usdc":"10","type":"deposit"},"hash":"0x0","time":100}""");
        using var c = JsonDocument.Parse("""{"time":100,"hash":"0x0","delta":{"type":"deposit","usdc":"11"}}""");
        var parsed = HyperliquidProtocol.Parse(Address, "userNonFundingLedgerUpdates", a.RootElement, Markets)!;
        Assert.Equal(parsed.Id, HyperliquidProtocol.Parse(Address, "userNonFundingLedgerUpdates", b.RootElement, Markets)!.Id);
        Assert.NotEqual(parsed.Id, HyperliquidProtocol.Parse(Address, "userNonFundingLedgerUpdates", c.RootElement, Markets)!.Id);
    }

    [Theory]
    [InlineData("internalTransfer")]
    [InlineData("accountClassTransfer")]
    [InlineData("spotTransfer")]
    [InlineData("vaultDeposit")]
    [InlineData("funding")]
    public void LedgerWhitelistRejectsOtherActivity(string type)
    {
        using var row = JsonDocument.Parse($$$"""{"time":100,"delta":{"type":"{{{type}}}","usdc":"10"}}""");
        Assert.Null(HyperliquidProtocol.Parse(Address, "userNonFundingLedgerUpdates", row.RootElement, Markets));
    }

    [Fact]
    public void LiquidationBypassesComparableUsdAndEscapesFactsAndAlias()
    {
        using var row = JsonDocument.Parse("""{"time":100,"hash":"x","delta":{"type":"liquidation","accountValue":"12","liquidatedPositions":[{"coin":"<BTC>","szi":"1"}]}}""");
        var a = HyperliquidProtocol.Parse(Address, "userNonFundingLedgerUpdates", row.RootElement, Markets)!;
        Assert.Null(a.Usd);
        var html = new global::Presentation.Telegram.Services.HyperliquidNotificationRenderer().Render(new(Guid.Empty, "<victim>", Address, [a]));
        Assert.Contains("&lt;victim&gt;", html);
        Assert.Contains("&lt;BTC&gt;", html);
        Assert.DoesNotContain("Approx. USD", html);
    }

    internal static JsonElement Fill(string start = "0", string side = "B", string size = "1", long tid = 100, string coin = "BTC", long time = 100)
    {
        using var json = JsonDocument.Parse($$$"""{"coin":"{{{coin}}}","px":"123.456789","sz":"{{{size}}}","side":"{{{side}}}","startPosition":"{{{start}}}","time":{{{time}}},"oid":77,"tid":{{{tid}}},"hash":"0x0","closedPnl":"0"}""");
        return json.RootElement.Clone();
    }
}
