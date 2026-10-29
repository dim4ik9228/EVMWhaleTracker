using Infrastructure.Blockchain;
using Microsoft.Extensions.Configuration;
using Presentation.Telegram.Configuration;

namespace UnitTests.Presentation;

public sealed class RuntimeConfigurationTests
{
    [Theory]
    [InlineData("Production", "Information")]
    [InlineData("Development", "Debug")]
    public void SettingsAndExplicitOverridesBeatObsoleteUserSecrets(string environment, string level)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "appsettings.json"),
                """{"Billing":{"EnforcementEnabled":false},"Serilog":{"Level":"Information"},"BlockchainNetworks":{"RpcApiKey":"","Networks":[{"Chain":"Bsc","Enabled":true,"HttpRpcUrl":"https://bsc-mainnet.infura.io/v3/","WebSocketRpcUrl":"wss://bsc-mainnet.infura.io/ws/v3/"},{"Chain":"Base","Enabled":true,"HttpRpcUrl":"https://base-mainnet.infura.io/v3/","WebSocketRpcUrl":"wss://base-mainnet.infura.io/ws/v3/"}]}}""");
            File.WriteAllText(Path.Combine(root, "appsettings.Development.json"), """{"Serilog":{"Level":"Debug"}}""");
            File.WriteAllText(Path.Combine(root, "appsettings.Local.json"),
                """{"Hyperliquid:Enabled":"true","Billing:OperatorIds":"12345"}""");
            using var config = new ConfigurationManager();
            RuntimeConfiguration.Configure(config, root, environment,
                ["--Billing:ProjectionFreshnessSeconds=70"],
                new Dictionary<string, string?>
                {
                    ["Billing:EnforcementEnabled"] = "true",
                    ["Hyperliquid:Enabled"] = "false",
                    ["BlockchainNetworks:RpcApiKey"] = "shared",
                    ["BlockchainNetworks:Networks:0:RpcApiKey"] = "specific",
                    ["BlockchainNetworks:Networks:1:Enabled"] = "false",
                    ["TelegramBot:BotToken"] = "fixture",
                    ["Billing:NowPaymentsDashboardUrl"] = "https://dashboard.example/?token=retired",
                    ["Billing:ProjectionFreshnessSeconds"] = "90"
                });
            Assert.False(config.GetValue<bool>("Billing:EnforcementEnabled"));
            Assert.Equal("true", config["Hyperliquid:Enabled"]);
            Assert.Equal("12345", config["Billing:OperatorIds"]);
            Assert.Equal("70", config["Billing:ProjectionFreshnessSeconds"]);
            Assert.Equal(level, config["Serilog:Level"]);
            var networks = BlockchainNetworkOptionsResolver.Resolve(config);
            Assert.Equal(2, networks.Count);
            Assert.EndsWith("/specific", networks[0].BuildHttpRpcUrl());
            Assert.EndsWith("/shared", networks[1].BuildHttpRpcUrl());
            Assert.Equal("fixture", config["TelegramBot:BotToken"]);
            Assert.Null(config["Billing:NowPaymentsDashboardUrl"]);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("https://bsc-mainnet.infura.io/v3/", false)]
    [InlineData("https://bsc-mainnet.infura.io/v3/private-token", true)]
    [InlineData("https://user:password@rpc.example/", true)]
    [InlineData("https://rpc.example/?key=private", true)]
    public void RpcEndpointCredentialsAreConfidential(string value, bool expected) =>
        Assert.Equal(expected, RuntimeConfiguration.IsConfidentialEndpoint("BlockchainNetworks:Networks:0:HttpRpcUrl", value));

    [Fact]
    public void BlankUserSecretCredentialsDoNotReplaceConfiguredValues()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "appsettings.json"), """{"BlockchainNetworks":{"RpcApiKey":"configured","Networks":[{"HttpRpcUrl":"https://bsc-mainnet.infura.io/v3/"}]}}""");
            using var config = new ConfigurationManager();
            RuntimeConfiguration.Configure(config, root, "Production", [],
                new Dictionary<string, string?>
                {
                    ["BlockchainNetworks:RpcApiKey"] = " ",
                    ["BlockchainNetworks:Networks:0:HttpRpcUrl"] = " "
                });
            Assert.Equal("configured", config["BlockchainNetworks:RpcApiKey"]);
            Assert.Equal("https://bsc-mainnet.infura.io/v3/", config["BlockchainNetworks:Networks:0:HttpRpcUrl"]);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void EnvironmentOverridesSecretsAndCommandLineOverridesEnvironment()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("TelegramBot__BotToken");
        try
        {
            File.WriteAllText(Path.Combine(root, "appsettings.json"), "{}");
            Environment.SetEnvironmentVariable("TelegramBot__BotToken", "environment-fixture");
            using var config = new ConfigurationManager();
            var secrets = new Dictionary<string, string?> { ["TelegramBot:BotToken"] = "secrets-fixture" };
            RuntimeConfiguration.Configure(config, root, "Production", [], secrets);
            Assert.Equal("environment-fixture", config["TelegramBot:BotToken"]);
            RuntimeConfiguration.Configure(config, root, "Production", ["--TelegramBot:BotToken=cli-fixture"], secrets);
            Assert.Equal("cli-fixture", config["TelegramBot:BotToken"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TelegramBot__BotToken", previous);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LocalFileIsOptional()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "appsettings.json"), "{}");
            using var config = new ConfigurationManager();
            RuntimeConfiguration.Configure(config, root, "Production", [], []);
        }
        finally { Directory.Delete(root, true); }
    }
}
