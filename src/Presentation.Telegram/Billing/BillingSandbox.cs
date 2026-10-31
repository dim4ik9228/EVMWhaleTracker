using Infrastructure.Billing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Presentation.Telegram.Configuration;

namespace Presentation.Telegram.Billing;

/// <summary>Fail-closed startup boundary before migration or hosted workers can touch a database.</summary>
public static class BillingSandbox
{
    public const string Label = "SANDBOX / NO REAL PAYMENT\n\n";
    public const string Terms = "SANDBOX / NO REAL PAYMENT. Nonbinding test terms: simulated funds only, no purchase, settlement, refund or payout. Test orders and access periods exist only in the isolated sandbox database. Contact the configured test operator for support. Never send real funds.";
    public const string Prefix = "sbx:";
    public const string InvalidIsolation = "Sandbox requires the BillingSandbox environment, its dedicated database, tracking/enforcement/Stars off, operators and an explicit Telegram checkpoint boundary.";
    public static void Validate(IConfiguration configuration, string connectionString, int enabledNetworks, string environment)
    {
        var options = configuration.GetSection("Billing").Get<BillingOptions>() ?? new();
        var sandboxEnvironment = environment.Equals(RuntimeConfiguration.BillingSandboxEnvironment, StringComparison.OrdinalIgnoreCase);
        if (options.SandboxMode != sandboxEnvironment) throw new InvalidOperationException(InvalidIsolation);
        if (!options.SandboxMode) return;
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (!string.Equals(database, "EvmWhaleTrackerBillingSandbox", StringComparison.Ordinal)
            || enabledNetworks != 0 || configuration.GetValue<bool>("Hyperliquid:Enabled")
            || configuration.GetValue<bool>("Polymarket:Enabled") || configuration.GetValue<bool>("PolymarketPolygon:Enabled")
            || options.StarsSalesEnabled || options.EnforcementEnabled || !options.IsValid() || options.SandboxInitialTelegramOffset <= 0)
            throw new InvalidOperationException(InvalidIsolation);
    }
}
