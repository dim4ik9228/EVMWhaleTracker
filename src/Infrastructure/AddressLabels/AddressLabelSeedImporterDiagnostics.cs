namespace Infrastructure.AddressLabels;

/// <summary>Named diagnostic messages and structured log templates for AddressLabelSeedImporter.</summary>
internal static class AddressLabelSeedImporterDiagnostics
{
    public const string SeedPathRequired = "A seed JSON path is required.";
    public const string SeedFileMissing = "Address label seed JSON was not found.";

    public static string EntryFailure(int rowNumber, string description) => $"Entry {rowNumber}: {description}";
}
