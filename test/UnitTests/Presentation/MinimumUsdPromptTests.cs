using Presentation.Telegram.Commands.Callbacks;

namespace UnitTests.Presentation;

public sealed class MinimumUsdPromptTests
{
    [Fact]
    public void Prompt_ExplainsUnpricedTransfersForPositiveAndZeroMinimums()
    {
        Assert.Contains("A positive minimum skips transfers without a USD price", TelegramCallbackProcessorText.MinimumUsdPrompt);
        Assert.Contains("Use 0 to notify on every matched transaction, including unpriced transfers", TelegramCallbackProcessorText.MinimumUsdPrompt);
    }
}
