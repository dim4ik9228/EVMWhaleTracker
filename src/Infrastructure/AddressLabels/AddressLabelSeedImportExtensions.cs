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
            Console.WriteLine(AddressLabelSeedImportExtensionsDiagnostics.Usage);
            Environment.ExitCode = 1;
            return;
        }

        var path = Path.GetFullPath(args[1]);
        var dryRun = args.Any(arg => string.Equals(arg, AddressLabelSeedImportExtensionsDiagnostics.DryRunOption, StringComparison.OrdinalIgnoreCase));
        var allowCandidate = args.Any(arg => string.Equals(arg, AddressLabelSeedImportExtensionsDiagnostics.AllowCandidateOption, StringComparison.OrdinalIgnoreCase));

        var report = await serviceProvider.ImportAddressLabelSeedAsync(path, dryRun, allowCandidate, cancellationToken);

        Console.WriteLine(dryRun ? AddressLabelSeedImportExtensionsDiagnostics.AddressLabelImportDryRunComplete : AddressLabelSeedImportExtensionsDiagnostics.AddressLabelImportComplete);
        Console.WriteLine(AddressLabelSeedImportExtensionsDiagnostics.Inserted(report.Inserted));
        Console.WriteLine(AddressLabelSeedImportExtensionsDiagnostics.Updated(report.Updated));
        Console.WriteLine(AddressLabelSeedImportExtensionsDiagnostics.Skipped(report.Skipped));
        Console.WriteLine(AddressLabelSeedImportExtensionsDiagnostics.Invalid(report.Invalid));

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
