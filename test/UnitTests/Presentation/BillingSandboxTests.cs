using Infrastructure.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Presentation.Telegram.Billing;
using Telegram.Bot.Types;

namespace UnitTests.Presentation;

public sealed class BillingSandboxTests
{
    private static Dictionary<string, string?> Settings() => new()
    {
        ["Billing:SandboxMode"] = "true", ["Billing:OperatorIds"] = "42",
        ["Billing:SandboxInitialTelegramOffset"] = "100"
    };
    private static IConfiguration Config(Dictionary<string, string?> settings) => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    [Fact]
    public void ProviderHostsAreFixedAndProductionIsDefault()
    {
        Assert.False(new BillingOptions().SandboxMode);
        Assert.Equal("https://api.nowpayments.io/v1/", new BillingOptions().ProviderBaseAddress.AbsoluteUri);
        Assert.Equal("https://api-sandbox.nowpayments.io/v1/", new BillingOptions { SandboxMode = true }.ProviderBaseAddress.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://sandbox.nowpayments.io/payment/test", true)]
    [InlineData("https://nowpayments.io/payment/test", false)]
    [InlineData("https://sandbox.nowpayments.io.evil.example/payment", false)]
    [InlineData("http://sandbox.nowpayments.io/payment", false)]
    [InlineData("https://user@sandbox.nowpayments.io/payment", false)]
    [InlineData("https://sandbox.nowpayments.io:8443/payment", false)]
    public void SandboxCheckoutRejectsProductionAndLookalikes(string url, bool expected) => Assert.Equal(expected, NowPaymentsClient.IsSandboxCheckoutUrl(url));

    [Fact]
    public void StartupRequiresDedicatedDatabaseAndNoTracking()
    {
        var config = Config(Settings());
        BillingSandbox.Validate(config, "Host=localhost;Database=EvmWhaleTrackerBillingSandbox", 0, "BillingSandbox");
        Assert.Throws<InvalidOperationException>(() => BillingSandbox.Validate(config, "Host=localhost;Database=EvmWhaleTracker", 0, "BillingSandbox"));
        Assert.Throws<InvalidOperationException>(() => BillingSandbox.Validate(config, "Host=localhost;Database=EvmWhaleTrackerBillingSandbox", 1, "BillingSandbox"));
    }

    [Theory]
    [InlineData(true, "Production")]
    [InlineData(true, "Development")]
    [InlineData(false, "BillingSandbox")]
    public void SandboxModeAndEnvironmentMustAgreeBeforeDatabaseUse(bool mode, string environment)
    {
        var settings = Settings(); settings["Billing:SandboxMode"] = mode.ToString();
        Assert.Throws<InvalidOperationException>(() => BillingSandbox.Validate(Config(settings), "Host=localhost;Database=EvmWhaleTrackerBillingSandbox", 0, environment));
    }

    [Theory]
    [InlineData("Hyperliquid:Enabled", "true")]
    [InlineData("Polymarket:Enabled", "true")]
    [InlineData("PolymarketPolygon:Enabled", "true")]
    [InlineData("Billing:StarsSalesEnabled", "true")]
    [InlineData("Billing:EnforcementEnabled", "true")]
    [InlineData("Billing:OperatorIds", "")]
    [InlineData("Billing:SandboxInitialTelegramOffset", "0")]
    public void UnsafeSandboxConfigurationFailsBeforeDatabaseUse(string key, string value)
    {
        var settings = Settings(); settings[key] = value;
        Assert.Throws<InvalidOperationException>(() => BillingSandbox.Validate(Config(settings), "Host=localhost;Database=EvmWhaleTrackerBillingSandbox", 0, "BillingSandbox"));
    }

    [Fact]
    public async Task ProductionSimulationReturnsBeforeAnyDependenciesOrHttpAreUsed()
    {
        var client = new NowPaymentsClient(null!, Options.Create(new BillingOptions { OperatorUserIds = [42] }), null!, null!, TimeProvider.System);
        Assert.False(await client.SimulateAsync(42, Guid.NewGuid(), "success", default));
        var sandbox = new NowPaymentsClient(null!, Options.Create(new BillingOptions { SandboxMode = true, OperatorUserIds = [42] }), null!, null!, TimeProvider.System);
        Assert.False(await sandbox.SimulateAsync(43, Guid.NewGuid(), "success", default));
        Assert.False(await sandbox.SimulateAsync(42, Guid.NewGuid(), "finished", default));
    }

    [Fact]
    public async Task ProductionConsumesTestNamespaceBeforeBillingOrWalletInput()
    {
        var ui = new TelegramBilling(null!, null!, Options.Create(new BillingOptions()), null!, null!);
        Assert.True(await ui.HandleAsync(new Update { Message = new Message { Text = "/sandboxsubscription" } }, default));
        Assert.True(await ui.HandleAsync(new Update { CallbackQuery = new CallbackQuery { Data = "sbx:billing:agree" } }, default));
    }

    [Fact]
    public async Task SandboxConsumesOrdinaryCommandsAndNonOperatorsBeforeAnyDependencies()
    {
        var ui = new TelegramBilling(null!, null!, Options.Create(new BillingOptions { SandboxMode = true, OperatorUserIds = [42] }), null!, null!);
        Assert.True(await ui.HandleAsync(new Update { Message = new Message { Text = "/subscription", From = new User { Id = 42 }, Chat = new Chat { Id = 42, Type = Telegram.Bot.Types.Enums.ChatType.Private } } }, default));
        Assert.True(await ui.HandleAsync(new Update { Message = new Message { Text = "/sandboxsubscription", From = new User { Id = 43 }, Chat = new Chat { Id = 43, Type = Telegram.Bot.Types.Enums.ChatType.Private } } }, default));
    }
}
