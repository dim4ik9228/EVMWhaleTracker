using Microsoft.Extensions.Configuration;

namespace Presentation.Telegram.Configuration;

/// <summary>Loads settings, filtered credentials, and explicit overrides in precedence order.</summary>
public static class RuntimeConfiguration
{
    public const string BillingSandboxEnvironment = "BillingSandbox";
    /// <summary>Identifies credential settings, including confidential endpoint overrides.</summary>
    public static bool IsSecretKey(string key) =>
        key.Equals("TelegramBot:BotToken", StringComparison.OrdinalIgnoreCase)
        || key.Equals("ConnectionStrings:Default", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Billing:NowPaymentsApiKey", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Billing:NowPaymentsIpnSecret", StringComparison.OrdinalIgnoreCase)
        || System.Text.RegularExpressions.Regex.IsMatch(key,
            "^(BlockchainNetworks:RpcApiKey|BlockchainNetworks:Networks:[0-4]:RpcApiKey|PolymarketPolygon:RpcApiKey)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Treats embedded credentials as secrets while ignoring stale public endpoint settings.</summary>
    public static bool IsConfidentialEndpoint(string key, string? value)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(key,
            "^(Google:|Billing:NowPayments(Email|Password|Username|Jwt|AccessToken|RefreshToken|Dashboard.*)$)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(value)
            || !(key.EndsWith("Url", StringComparison.OrdinalIgnoreCase) || key.EndsWith("Uri", StringComparison.OrdinalIgnoreCase))) return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return true;
        if (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) return true;
        return (key.EndsWith(":HttpRpcUrl", StringComparison.OrdinalIgnoreCase)
            || key.EndsWith(":WebSocketRpcUrl", StringComparison.OrdinalIgnoreCase))
            && !System.Text.RegularExpressions.Regex.IsMatch(value,
                "^(https|wss)://(bsc-mainnet|base-mainnet|arbitrum-mainnet|mainnet|robinhood-mainnet|polygon-mainnet)\\.infura\\.io/(ws/)?v3/$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>Ignores obsolete non-secret User Secrets in every environment.</summary>
    public static void Configure<T>(ConfigurationManager configuration, string root, string environment, string[] args)
        where T : class
    {
        if (environment.Equals(BillingSandboxEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            Configure(configuration, root, environment, args, []);
            return;
        }
        var secrets = new ConfigurationBuilder().AddUserSecrets<T>(optional: true).Build();
        Configure(configuration, root, environment, args, secrets.AsEnumerable());
    }

    /// <summary>Supports isolated validation without accessing actual credentials.</summary>
    public static void Configure(ConfigurationManager configuration, string root, string environment, string[] args,
        IEnumerable<KeyValuePair<string, string?>> secrets)
    {
        var sandbox = environment.Equals(BillingSandboxEnvironment, StringComparison.OrdinalIgnoreCase);
        var filtered = (sandbox ? [] : secrets).Where(p => (IsSecretKey(p.Key) || IsConfidentialEndpoint(p.Key, p.Value))
            && !string.IsNullOrWhiteSpace(p.Value)).ToArray();
        configuration.Sources.Clear();
        configuration.SetBasePath(root)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true);
        if (!sandbox) configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
        configuration
            .AddInMemoryCollection(filtered)
            .AddEnvironmentVariables()
            .AddCommandLine(args);
    }
}
