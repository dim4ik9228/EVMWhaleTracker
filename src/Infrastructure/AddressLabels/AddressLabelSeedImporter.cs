using System.Text.Json;
using Application.Abstractions;
using Domain.Common;
using Domain.Entities;

namespace Infrastructure.AddressLabels;

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
    private const int SupportedSchemaVersion = 1;
    private const string ReviewedStatus = "reviewed";
    private const string CandidateStatus = "candidate";

    public async Task<AddressLabelImportReport> ImportAsync(
        string path,
        bool dryRun,
        bool allowCandidate,
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

        var entriesResult = ValidateAndGetEntries(document.RootElement, allowCandidate);
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

    private static Result<JsonElement> ValidateAndGetEntries(JsonElement root, bool allowCandidate)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return InvalidSeed("Address label seed JSON must be an object with schemaVersion, reviewStatus, scopePolicy, and entries.");
        }

        var schemaVersion = GetOptionalInt(root, "schemaVersion");
        if (schemaVersion != SupportedSchemaVersion)
        {
            return InvalidSeed($"Address label seed schemaVersion must be {SupportedSchemaVersion}.");
        }

        var reviewStatus = GetOptionalString(root, "reviewStatus");
        if (string.IsNullOrWhiteSpace(reviewStatus))
        {
            return InvalidSeed("Address label seed reviewStatus is required.");
        }

        if (reviewStatus.Equals(CandidateStatus, StringComparison.OrdinalIgnoreCase) && !allowCandidate)
        {
            return InvalidSeed("Candidate address label seeds require --allow-candidate.");
        }

        if (!reviewStatus.Equals(ReviewedStatus, StringComparison.OrdinalIgnoreCase) &&
            !reviewStatus.Equals(CandidateStatus, StringComparison.OrdinalIgnoreCase))
        {
            return InvalidSeed("Address label seed reviewStatus must be 'reviewed' or 'candidate'.");
        }

        if (!TryGetProperty(root, "scopePolicy", out var scopePolicy) || scopePolicy.ValueKind != JsonValueKind.Object)
        {
            return InvalidSeed("Address label seed scopePolicy object is required.");
        }

        if (!TryGetProperty(root, "entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return InvalidSeed("Address label seed entries array is required.");
        }

        return Result<JsonElement>.Success(entries);
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

        var scopeResult = ValidateScope(kind, parsedChain);
        if (scopeResult.IsFailure)
        {
            return Result<AddressLabel>.Failure(scopeResult.Error);
        }

        var sourceReference = GetOptionalString(row, "sourceReference");
        if (string.IsNullOrWhiteSpace(sourceReference))
        {
            return Result<AddressLabel>.Failure(Error.Validation("AddressLabel.SourceReference", "Imported seed entries must include sourceReference."));
        }

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

    private static Result ValidateScope(AddressLabelKind kind, SupportedChain? chain)
    {
        if (kind is AddressLabelKind.Exchange or AddressLabelKind.ExchangeHotWallet or AddressLabelKind.ExchangeColdWallet &&
            chain is not null)
        {
            return Result.Failure(Error.Validation("AddressLabel.Chain", "Exchange seed entries must be global and omit chain."));
        }

        if (kind is AddressLabelKind.Bridge or AddressLabelKind.DexRouter or AddressLabelKind.Protocol or AddressLabelKind.Treasury &&
            chain is null)
        {
            return Result.Failure(Error.Validation("AddressLabel.Chain", $"{kind} seed entries must include chain."));
        }

        return Result.Success();
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

    private static Result<JsonElement> InvalidSeed(string description)
        => Result<JsonElement>.Failure(Error.Validation("AddressLabelSeed.Schema", description));
}
