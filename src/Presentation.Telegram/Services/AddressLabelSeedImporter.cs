using System.Text.Json;
using Application.Abstractions;
using Domain.Common;
using Domain.Entities;

namespace Presentation.Telegram.Services;

public sealed record AddressLabelImportReport(
    int Inserted,
    int Updated,
    int Skipped,
    int Invalid,
    IReadOnlyList<string> Messages);

/// <summary>
/// Imports reviewed address labels from the repository JSON seed format.
/// </summary>
public sealed class AddressLabelSeedImporter(
    IAddressLabelRepository repository,
    IAddressLabelCacheService cache)
{
    public async Task<AddressLabelImportReport> ImportAsync(
        string path,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A seed JSON path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Address label seed JSON was not found.", path);
        }

        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var entriesResult = GetEntries(document.RootElement);
        if (entriesResult.IsFailure)
        {
            return new AddressLabelImportReport(
                Inserted: 0,
                Updated: 0,
                Skipped: 0,
                Invalid: 1,
                Messages: [entriesResult.Error.Description]);
        }

        var inserted = 0;
        var updated = 0;
        var skipped = 0;
        var invalid = 0;
        var messages = new List<string>();
        var rowNumber = 0;

        foreach (var row in entriesResult.Value.EnumerateArray())
        {
            rowNumber++;
            var labelResult = CreateLabel(row);
            if (labelResult.IsFailure)
            {
                invalid++;
                messages.Add($"Entry {rowNumber}: {labelResult.Error.Description}");
                continue;
            }

            var label = labelResult.Value;
            var existing = await repository.GetByAddressAndChainAsync(label.Address, label.Chain, cancellationToken);
            if (!dryRun)
            {
                await repository.UpsertAsync(label, cancellationToken);
            }

            if (existing is null)
            {
                inserted++;
            }
            else
            {
                updated++;
            }
        }

        if (!dryRun)
        {
            await repository.SaveChangesAsync(cancellationToken);
            var labels = await repository.GetEnabledAsync(cancellationToken);
            cache.ReplaceAll(labels);
        }

        return new AddressLabelImportReport(inserted, updated, skipped, invalid, messages);
    }

    private static Result<JsonElement> GetEntries(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return Result<JsonElement>.Success(root);
        }

        if (root.ValueKind == JsonValueKind.Object &&
            (root.TryGetProperty("entries", out var entries) || root.TryGetProperty("Entries", out entries)) &&
            entries.ValueKind == JsonValueKind.Array)
        {
            return Result<JsonElement>.Success(entries);
        }

        return Result<JsonElement>.Failure(Error.Validation(
            "AddressLabelSeed.Entries",
            "Address label seed JSON must be an array or an object with an entries array."));
    }

    private static Result<AddressLabel> CreateLabel(JsonElement row)
    {
        var address = GetRequiredString(row, "address");
        if (address.IsFailure)
        {
            return Result<AddressLabel>.Failure(address.Error);
        }

        var label = GetOptionalString(row, "nameTag") ?? GetOptionalString(row, "label");
        var kindText = GetRequiredString(row, "kind");
        if (kindText.IsFailure)
        {
            return Result<AddressLabel>.Failure(kindText.Error);
        }

        var chain = GetOptionalString(row, "chain");
        SupportedChain? parsedChain = null;
        if (!string.IsNullOrWhiteSpace(chain))
        {
            if (!Enum.TryParse<SupportedChain>(chain, ignoreCase: true, out var supportedChain))
            {
                return Result<AddressLabel>.Failure(Error.Validation("AddressLabel.Chain", $"Unsupported chain '{chain}'."));
            }

            parsedChain = supportedChain;
        }

        if (!Enum.TryParse<AddressLabelKind>(kindText.Value, ignoreCase: true, out var kind))
        {
            return Result<AddressLabel>.Failure(Error.Validation("AddressLabel.Kind", $"Unsupported address label kind '{kindText.Value}'."));
        }

        var sourceReference = GetOptionalString(row, "sourceReference");
        var priority = GetOptionalInt(row, "priority") ?? 100;
        var isEnabled = GetOptionalBool(row, "isEnabled") ?? true;

        return AddressLabel.Create(
            address.Value,
            parsedChain,
            label,
            kind,
            AddressLabelSource.SeedFile,
            sourceReference,
            priority,
            isEnabled);
    }

    private static Result<string> GetRequiredString(JsonElement row, string propertyName)
    {
        var value = GetOptionalString(row, propertyName);
        return string.IsNullOrWhiteSpace(value)
            ? Result<string>.Failure(Error.Validation("AddressLabelSeed.Required", $"Missing required JSON property '{propertyName}'."))
            : Result<string>.Success(value);
    }

    private static string? GetOptionalString(JsonElement row, string propertyName)
    {
        if (TryGetProperty(row, propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        return null;
    }

    private static int? GetOptionalInt(JsonElement row, string propertyName)
        => TryGetProperty(row, propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : null;

    private static bool? GetOptionalBool(JsonElement row, string propertyName)
        => TryGetProperty(row, propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;

    private static bool TryGetProperty(JsonElement row, string propertyName, out JsonElement property)
    {
        if (row.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        var pascalCaseName = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
        return row.TryGetProperty(pascalCaseName, out property);
    }
}
