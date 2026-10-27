using Domain.Common;
using Presentation.Telegram.Commands.Callbacks;

namespace UnitTests.Presentation;

public sealed class TelegramCallbackDataParserTests
{
    [Theory]
    [InlineData(SupportedChain.Base)]
    [InlineData(SupportedChain.Robinhood)]
    public void TryParse_ParsesNetworkToggle(SupportedChain chain)
    {
        var walletId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var parsed = TelegramCallbackDataParser.TryParse(
            $"tw:v1:nt:{walletId:N}:{(int)chain}:1",
            out var callbackData);

        Assert.True(parsed);
        Assert.Equal(TelegramCallbackAction.NetworkToggle, callbackData.Action);
        Assert.Equal(walletId, callbackData.WalletId);
        Assert.Equal(chain, callbackData.Chain);
        Assert.True(callbackData.Enabled);
    }

    [Fact]
    public void TryParse_RejectsInvalidPrefix()
    {
        var parsed = TelegramCallbackDataParser.TryParse("other:v1:n:bad", out _);

        Assert.False(parsed);
    }
    [Theory]
    [InlineData("t", TelegramCallbackAction.PolymarketToggle)]
    [InlineData("b", TelegramCallbackAction.PolymarketBind)]
    [InlineData("m", TelegramCallbackAction.PolymarketMinimum)]
    [InlineData("a", TelegramCallbackAction.PolymarketAlias)]
    [InlineData("s", TelegramCallbackAction.PolymarketSource)]
    public void TryParse_PreservesPolymarketGenerationCallbacks(string action, TelegramCallbackAction expected)
    {
        var wallet = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var generation = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var token = Convert.ToBase64String(generation.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var data = $"pm:{action}:{wallet:N}:{token}";

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(data) <= 64);
        Assert.True(TelegramCallbackDataParser.TryParse(data, out var parsed));
        Assert.Equal(expected, parsed.Action);
        Assert.Equal(wallet, parsed.WalletId);
        Assert.Equal(generation, parsed.Generation);
    }
}
