namespace Infrastructure.AddressLabels;

/// <summary>Named diagnostic messages and structured log templates for AddressLabelSeedImportExtensions.</summary>
internal static class AddressLabelSeedImportExtensionsDiagnostics
{
    public const string AddressLabelImportDryRunComplete = "Address label import dry run complete.";
    public const string AddressLabelImportComplete = "Address label import complete.";

    public static string Inserted(int count) => $"Inserted: {count}";
    public static string Updated(int count) => $"Updated: {count}";
    public static string Skipped(int count) => $"Skipped: {count}";
    public static string Invalid(int count) => $"Invalid: {count}";

    public const string Usage = "Usage: import-address-labels <json-path> [--dry-run] [--allow-candidate]";
    public const string DryRunOption = "--dry-run";
    public const string AllowCandidateOption = "--allow-candidate";
}
