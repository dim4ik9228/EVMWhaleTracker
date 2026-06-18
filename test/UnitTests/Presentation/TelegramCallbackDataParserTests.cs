using Domain.Common;
using Presentation.Telegram.Commands.Callbacks;

namespace UnitTests.Presentation;

public sealed class TelegramCallbackDataParserTests
{
    [Fact]
    public void TryParse_ParsesNetworkToggle()
    {
        var walletId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var parsed = TelegramCallbackDataParser.TryParse(
            $"tw:v1:nt:{walletId:N}:{(int)SupportedChain.Base}:1",
            out var callbackData);

        Assert.True(parsed);
        Assert.Equal(TelegramCallbackAction.NetworkToggle, callbackData.Action);
        Assert.Equal(walletId, callbackData.WalletId);
        Assert.Equal(SupportedChain.Base, callbackData.Chain);
        Assert.True(callbackData.Enabled);
    }

    [Fact]
    public void TryParse_RejectsInvalidPrefix()
    {
        var parsed = TelegramCallbackDataParser.TryParse("other:v1:n:bad", out _);

        Assert.False(parsed);
    }
}
