using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.AddressLabels;

public static class AddressLabelSeedImportExtensions
{
    public static async Task RunAddressLabelImportAsync(
        this IServiceProvider serviceProvider,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length < 2)
        {
            Console.WriteLine("Usage: import-address-labels <json-path> [--dry-run] [--allow-candidate]");
            Environment.ExitCode = 1;
            return;
        }

        var path = Path.GetFullPath(args[1]);
        var dryRun = args.Any(arg => string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase));
        var allowCandidate = args.Any(arg => string.Equals(arg, "--allow-candidate", StringComparison.OrdinalIgnoreCase));

        var report = await serviceProvider.ImportAddressLabelSeedAsync(path, dryRun, allowCandidate, cancellationToken);

        Console.WriteLine(dryRun ? "Address label import dry run complete." : "Address label import complete.");
        Console.WriteLine($"Inserted: {report.Inserted}");
        Console.WriteLine($"Updated: {report.Updated}");
        Console.WriteLine($"Skipped: {report.Skipped}");
        Console.WriteLine($"Invalid: {report.Invalid}");

        foreach (var message in report.Messages)
        {
            Console.WriteLine(message);
        }

        Environment.ExitCode = report.Invalid == 0 ? 0 : 1;
    }

    public static async Task<AddressLabelImportReport> ImportAddressLabelSeedAsync(
        this IServiceProvider serviceProvider,
        string path,
        bool dryRun,
        bool allowCandidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        using var scope = serviceProvider.CreateScope();
        var importer = scope.ServiceProvider.GetRequiredService<AddressLabelSeedImporter>();
        return await importer.ImportAsync(path, dryRun, allowCandidate, cancellationToken);
    }
}
