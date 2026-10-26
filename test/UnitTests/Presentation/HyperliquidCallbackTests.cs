using Presentation.Telegram.Commands.Callbacks;

namespace UnitTests.Presentation;

public sealed class HyperliquidCallbackTests
{
    [Fact]
    public void GenerationProtectedCallbackFitsTelegramLimitAndRoundTrips()
    {
        var wallet = Guid.NewGuid(); var generation = Guid.NewGuid();
        var token = Convert.ToBase64String(generation.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var data = $"hc:{wallet:N}:1:{token}";
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(data) <= 64);
        Assert.True(TelegramCallbackDataParser.TryParse(data, out var parsed));
        Assert.Equal(TelegramCallbackAction.HyperliquidToggle, parsed.Action);
        Assert.Equal(wallet, parsed.WalletId);
        Assert.Equal(generation, parsed.Generation);
        Assert.True(parsed.Enabled);
    }

    [Theory]
    [InlineData("hc:00000000000000000000000000000000:1:!!!!!!!!!!!!!!!!!!!!!!")]
    [InlineData("hc:00000000000000000000000000000000:1:AAAAAAAAAAAAAAAAAA    ")]
    [InlineData("hc:00000000000000000000000000000000:2:AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("tw:v1:ht:00000000000000000000000000000000:1")]
    public void InvalidOrUnversionedMutationCannotChangeEnrollment(string data)
        => Assert.False(TelegramCallbackDataParser.TryParse(data, out _));
}
