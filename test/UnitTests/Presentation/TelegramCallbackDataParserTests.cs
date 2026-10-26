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
}
