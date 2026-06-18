using Presentation.Telegram.Commands.StartPayloads;

namespace UnitTests.Presentation;

public sealed class TelegramStartPayloadParserTests
{
    [Fact]
    public void TryParseEditWalletPayload_ParsesGuidNFormat()
    {
        var walletId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var parsed = TelegramStartPayloadParser.TryParseEditWalletPayload(
            $"edit_wallet_{walletId:N}",
            out var parsedWalletId);

        Assert.True(parsed);
        Assert.Equal(walletId, parsedWalletId);
    }

    [Fact]
    public void TryParseEditWalletPayload_RejectsUnknownPayload()
    {
        var parsed = TelegramStartPayloadParser.TryParseEditWalletPayload("help", out var walletId);

        Assert.False(parsed);
        Assert.Equal(Guid.Empty, walletId);
    }
}
