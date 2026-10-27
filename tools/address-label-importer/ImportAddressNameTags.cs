#!/usr/bin/env dotnet run
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

var options = ImportOptions.Parse(args);
Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);

var extractor = new NameTagExtractor(options);
var result = extractor.Extract();

await File.WriteAllTextAsync(
    options.OutputPath,
    JsonSerializer.Serialize(result.Seed, SeedJsonContext.Default.CandidateSeed));

Console.WriteLine(ImportDiagnostics.Written(result.Seed.Entries.Count, options.ReviewStatus, options.OutputPath));
Console.WriteLine(ImportDiagnostics.ReadRows(result.Stats.ReadRows));
Console.WriteLine(ImportDiagnostics.EmptyTagsSkipped(result.Stats.NullNameTagRows));
Console.WriteLine(ImportDiagnostics.InvalidAddressesSkipped(result.Stats.InvalidAddressRows));
Console.WriteLine(ImportDiagnostics.NoiseExcluded(result.Stats.ExcludedRows));
Console.WriteLine(ImportDiagnostics.DuplicatesCollapsed(result.Stats.DuplicateRows));
Console.WriteLine();
Console.WriteLine(ImportDiagnostics.EntriesByChain);
foreach (var item in result.Seed.Entries.GroupBy(entry => entry.Chain).OrderBy(group => group.Key))
{
    Console.WriteLine(ImportDiagnostics.CountByLabel(item.Key ?? ImportDiagnostics.GlobalScope, item.Count()));
}

Console.WriteLine();
Console.WriteLine(ImportDiagnostics.EntriesByKind);
foreach (var item in result.Seed.Entries.GroupBy(entry => entry.Kind).OrderByDescending(group => group.Count()))
{
    Console.WriteLine(ImportDiagnostics.CountByLabel(item.Key, item.Count()));
}

Console.WriteLine();
Console.WriteLine(ImportDiagnostics.TopSourceLabels);
foreach (var item in result.Seed.Entries
    .GroupBy(entry => entry.SourceLabel)
    .OrderByDescending(group => group.Count())
    .ThenBy(group => group.Key)
    .Take(25))
{
    Console.WriteLine(ImportDiagnostics.CountByLabel(item.Key, item.Count()));
}

internal sealed record ImportOptions(
    IReadOnlyList<InputSource> Inputs,
    string OutputPath,
    IReadOnlySet<string> IncludedKinds,
    string ReviewStatus)
{
    public static ImportOptions Parse(string[] args)
    {
        var outputPath = "data/address-labels/generated/address-name-tags.seed.candidate.json";
        var inputPaths = new List<string>();
        var rawDirectory = "data/address-labels/raw";
        var includedKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reviewStatus = ImportReviewStates.Candidate;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case ImportArguments.Output:
                    outputPath = RequireValue(args, ref index, ImportArguments.Output);
                    break;
                case ImportArguments.Input:
                    inputPaths.Add(RequireValue(args, ref index, ImportArguments.Input));
                    break;
                case ImportArguments.RawDirectory:
                    rawDirectory = RequireValue(args, ref index, ImportArguments.RawDirectory);
                    break;
                case ImportArguments.Kind:
                    includedKinds.Add(RequireValue(args, ref index, ImportArguments.Kind));
                    break;
                case ImportArguments.ReviewStatus:
                    reviewStatus = RequireValue(args, ref index, ImportArguments.ReviewStatus);
                    break;
                default:
                    inputPaths.Add(args[index]);
                    break;
            }
        }

        if (!reviewStatus.Equals(ImportReviewStates.Candidate, StringComparison.OrdinalIgnoreCase) &&
            !reviewStatus.Equals(ImportReviewStates.Reviewed, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(ImportDiagnostics.InvalidReviewStatus);
        }

        if (inputPaths.Count == 0)
        {
            inputPaths.AddRange(FindRawInputs(rawDirectory));
        }

        var inputs = inputPaths.Select(InputSource.FromPath).ToArray();
        return new ImportOptions(inputs, outputPath, includedKinds, reviewStatus.ToLowerInvariant());
    }

    private static string RequireValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException(ImportDiagnostics.OptionValueRequired(optionName));
        }

        index++;
        return args[index];
    }

    private static IReadOnlyList<string> FindRawInputs(string rawDirectory)
    {
        if (!Directory.Exists(rawDirectory))
        {
            throw new DirectoryNotFoundException(ImportDiagnostics.RawDirectoryMissing(rawDirectory));
        }

        var supportedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "accounts.eth..json",
            "accounts.eth.json",
            "accounts.bsc.json",
            "accounts.base.json",
            "accounts.arbitrum.json"
        };

        var files = Directory
            .EnumerateFiles(rawDirectory, "accounts.*.json", SearchOption.TopDirectoryOnly)
            .Where(path => supportedFileNames.Contains(Path.GetFileName(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidOperationException(ImportDiagnostics.NoSupportedFiles(rawDirectory));
        }

        return files;
    }
}

internal sealed record InputSource(string Path, string Chain, int ChainId, string Source)
{
    public static InputSource FromPath(string path)
    {
        var fileName = System.IO.Path.GetFileName(path).ToLowerInvariant();
        return fileName switch
        {
            "accounts.eth..json" or "accounts.eth.json" => new InputSource(path, "Ethereum", 1, "etherscan"),
            "accounts.bsc.json" => new InputSource(path, "Bsc", 56, "bscscan"),
            "accounts.base.json" => new InputSource(path, "Base", 8453, "basescan"),
            "accounts.arbitrum.json" => new InputSource(path, "Arbitrum", 42161, "arbiscan"),
            _ => throw new ArgumentException(ImportDiagnostics.UnknownChain(fileName))
        };
    }
}

internal sealed class NameTagExtractor(ImportOptions options)
{
    private static readonly Regex AddressRegex = new("^0x[0-9a-fA-F]{40}$", RegexOptions.Compiled);
    private readonly Dictionary<string, CandidateEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ExtractionStats _stats = new();

    private static readonly HashSet<string> ExcludedLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "blocked",
        "burn",
        "genesis",
        "phish-hack",
        "exploit",
        "heist",
        "spam-token",
        "friend-tech-users",
        "token-contract",
        "bridged-token",
        "nft",
        "old-contract",
        "deprecated",
        "charity",
        "mev-bot",
        "proposer-fee-recipient",
        "binance-pegged",
        "erc-4337-bundler",
        "avs-operator",
        "token-sale",
        "endaoment",
        "my-crypto-heroes",
        "aura-finance",
        "donate",
        "nonprofit"
    };

    private static readonly HashSet<string> LowSignalLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "contract-deployer"
    };

    private static readonly string[] ExcludedNameTagTerms =
    [
        "fake_phishing",
        "phishing",
        "exploiter",
        "exploit",
        "hacker",
        "drainer",
        "scam",
        "null:",
        " token",
        ": token",
        "deployer",
        " dep:",
        " deposit",
        "deposit address",
        "charity",
        "old ",
        "old:",
        "deprecated"
    ];

    private static readonly string[] ExchangeTerms =
    [
        "binance",
        "bingx",
        "coinbase",
        "kraken",
        "okx",
        "bybit",
        "kucoin",
        "gate.io",
        "gateio",
        "mexc",
        "bitget",
        "htx",
        "huobi",
        "crypto.com",
        "cryptocom",
        "bitfinex",
        "bitstamp",
        "upbit",
        "bithumb",
        "gemini",
        "robinhood",
        "wintermute",
        "jump trading",
        "amber group",
        "flow traders"
    ];

    private static readonly string[] BridgeTerms =
    [
        "bridge",
        "stargate",
        "layerzero",
        "across",
        "synapse",
        "multichain",
        "celer",
        "hop protocol",
        "arbitrum one:",
        "optimism:",
        "polygon bridge",
        "wormhole"
    ];

    private static readonly string[] DexRouterTerms =
    [
        "router",
        "swaprouter",
        "swap router",
        "uniswap",
        "pancakeswap",
        "sushiswap",
        "1inch",
        "0x:",
        "paraswap",
        "kyberswap",
        "camelot",
        "trader joe",
        "aerodrome",
        "velodrome",
        "balancer",
        "curve.fi",
        "curve:"
    ];

    private static readonly string[] TreasuryTerms =
    [
        "treasury",
        "foundation",
        "multisig",
        "dao:"
    ];

    public ExtractionResult Extract()
    {
        foreach (var input in options.Inputs)
        {
            ExtractFile(input);
        }

        var seed = new CandidateSeed(
            SchemaVersion: 1,
            ReviewStatus: options.ReviewStatus,
            ScopePolicy: new SeedScopePolicy(
                Exchange: "global",
                Bridge: "chainScoped",
                DexRouter: "chainScoped",
                Protocol: "chainScoped",
                Treasury: "chainScoped"),
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Inputs: options.Inputs,
            Filters: new CandidateFilters(
                ExcludedLabels.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                LowSignalLabels.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                ExcludedNameTagTerms,
                "Only rows with non-empty nameTag are eligible. Output keeps nameTag as display text and preserves scraped label only as sourceLabel metadata."),
            Stats: _stats,
            Entries: _entries.Values
                .Where(entry => options.IncludedKinds.Count == 0 || options.IncludedKinds.Contains(entry.Kind))
                .OrderByDescending(entry => entry.Priority)
                .ThenBy(entry => entry.Chain ?? string.Empty)
                .ThenBy(entry => entry.NameTag, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Address, StringComparer.OrdinalIgnoreCase)
                .ToArray());

        return new ExtractionResult(seed, _stats);
    }

    private void ExtractFile(InputSource input)
    {
        if (!File.Exists(input.Path))
        {
            throw new FileNotFoundException(ImportDiagnostics.InputFileMissing, input.Path);
        }

        using var stream = File.OpenRead(input.Path);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(ImportDiagnostics.InputArrayRequired(input.Path));
        }

        foreach (var row in document.RootElement.EnumerateArray())
        {
            _stats.ReadRows++;
            ProcessRow(input, row);
        }
    }

    private void ProcessRow(InputSource input, JsonElement row)
    {
        var address = GetString(row, "address");
        if (address is null || !AddressRegex.IsMatch(address))
        {
            _stats.InvalidAddressRows++;
            return;
        }

        var nameTag = CleanNameTag(GetString(row, "nameTag"));
        if (nameTag is null)
        {
            _stats.NullNameTagRows++;
            return;
        }

        var sourceLabel = CleanSourceLabel(GetString(row, "label")) ?? "unknown";
        if (ShouldExclude(sourceLabel, nameTag))
        {
            _stats.ExcludedRows++;
            return;
        }

        var kind = Classify(sourceLabel, nameTag);
        if (kind == CandidateKind.Unknown)
        {
            _stats.ExcludedRows++;
            return;
        }

        var priority = kind switch
        {
            CandidateKind.Exchange => 100,
            CandidateKind.Bridge => 90,
            CandidateKind.DexRouter => 85,
            CandidateKind.Treasury => 80,
            CandidateKind.Protocol => 70,
            _ => 50
        };

        var isGlobalExchange = kind == CandidateKind.Exchange;
        var candidate = new CandidateEntry(
            Address: address.ToLowerInvariant(),
            Chain: isGlobalExchange ? null : input.Chain,
            ChainId: isGlobalExchange ? null : input.ChainId,
            NameTag: nameTag,
            SourceLabel: sourceLabel,
            Kind: kind.ToString(),
            Source: input.Source,
            SourceReference: BuildSourceReference(input.Source, address),
            Priority: priority,
            IsEnabled: true);

        var key = isGlobalExchange
            ? $"global:{candidate.Address}"
            : $"{input.ChainId}:{candidate.Address}";
        if (_entries.TryGetValue(key, out var existing))
        {
            _stats.DuplicateRows++;
            if (candidate.Priority > existing.Priority ||
                candidate.Priority == existing.Priority && candidate.NameTag.Length < existing.NameTag.Length)
            {
                _entries[key] = candidate;
            }

            return;
        }

        _entries[key] = candidate;
    }

    private static bool ShouldExclude(string label, string nameTag)
    {
        if (ExcludedLabels.Contains(label))
        {
            return true;
        }

        var normalized = nameTag.ToLowerInvariant();
        return ExcludedNameTagTerms.Any(normalized.Contains);
    }

    private static CandidateKind Classify(string label, string nameTag)
    {
        var text = $"{label} {nameTag}".ToLowerInvariant();

        if (IsExchange(label, nameTag))
        {
            return CandidateKind.Exchange;
        }

        if (BridgeTerms.Any(text.Contains) || label.Equals("bridge", StringComparison.OrdinalIgnoreCase))
        {
            return CandidateKind.Bridge;
        }

        if (DexRouterTerms.Any(text.Contains) || label.Equals("defi", StringComparison.OrdinalIgnoreCase))
        {
            return CandidateKind.DexRouter;
        }

        if (TreasuryTerms.Any(text.Contains))
        {
            return CandidateKind.Treasury;
        }

        if (LowSignalLabels.Contains(label))
        {
            return CandidateKind.Unknown;
        }

        if (nameTag.Contains(':'))
        {
            return CandidateKind.Protocol;
        }

        return CandidateKind.Unknown;
    }

    private static bool IsExchange(string label, string nameTag)
    {
        if (label.Equals("exchange", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (ExchangeTerms.Any(term => label.Equals(term, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var normalizedNameTag = nameTag.ToLowerInvariant();
        if (normalizedNameTag.Contains("charity") ||
            normalizedNameTag.Contains("token") ||
            normalizedNameTag.Contains("deployer"))
        {
            return false;
        }

        return ExchangeTerms.Any(term =>
            normalizedNameTag.StartsWith($"{term} ", StringComparison.OrdinalIgnoreCase) ||
            normalizedNameTag.StartsWith($"{term}:", StringComparison.OrdinalIgnoreCase) ||
            normalizedNameTag.Equals(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetString(JsonElement row, string propertyName)
    {
        return row.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? CleanNameTag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value
            .Replace('\u2026', '.')
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();

        return cleaned.Length is > 0 and <= 96 ? cleaned : null;
    }

    private static string? CleanSourceLabel(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static string BuildSourceReference(string source, string address)
    {
        var host = source switch
        {
            "etherscan" => "etherscan.io",
            "bscscan" => "bscscan.com",
            "basescan" => "basescan.org",
            "arbiscan" => "arbiscan.io",
            _ => source
        };

        return $"https://{host}/address/{address.ToLowerInvariant()}";
    }
}

internal sealed record ExtractionResult(CandidateSeed Seed, ExtractionStats Stats);

internal sealed class ExtractionStats
{
    public int ReadRows { get; set; }
    public int NullNameTagRows { get; set; }
    public int InvalidAddressRows { get; set; }
    public int ExcludedRows { get; set; }
    public int DuplicateRows { get; set; }
}

internal sealed record CandidateSeed(
    int SchemaVersion,
    string ReviewStatus,
    SeedScopePolicy ScopePolicy,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<InputSource> Inputs,
    CandidateFilters Filters,
    ExtractionStats Stats,
    IReadOnlyList<CandidateEntry> Entries);

internal sealed record SeedScopePolicy(
    string Exchange,
    string Bridge,
    string DexRouter,
    string Protocol,
    string Treasury);

internal sealed record CandidateFilters(
    IReadOnlyList<string> ExcludedLabels,
    IReadOnlyList<string> LowSignalLabels,
    IReadOnlyList<string> ExcludedNameTagTerms,
    string Notes);

internal sealed record CandidateEntry(
    string Address,
    string? Chain,
    int? ChainId,
    string NameTag,
    string SourceLabel,
    string Kind,
    string Source,
    string SourceReference,
    int Priority,
    bool IsEnabled);

internal enum CandidateKind
{
    Unknown,
    Exchange,
    DexRouter,
    Bridge,
    Protocol,
    Treasury
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CandidateSeed))]
internal sealed partial class SeedJsonContext : JsonSerializerContext;

/// <summary>Console and validation text for the standalone import tool.</summary>
internal static class ImportDiagnostics
{
    public const string GlobalScope = "Global";
    public static string CountByLabel(string? label, int count) => $"  {label}: {count:N0}";
    public static string Written(int count, string reviewStatus, string path) => $"Wrote {count:N0} {reviewStatus} entries to {path}";
    public static string ReadRows(int count) => $"Read rows: {count:N0}";
    public static string EmptyTagsSkipped(int count) => $"Null/empty nameTags skipped: {count:N0}";
    public static string InvalidAddressesSkipped(int count) => $"Invalid address rows skipped: {count:N0}";
    public static string NoiseExcluded(int count) => $"Excluded by noise rules: {count:N0}";
    public static string DuplicatesCollapsed(int count) => $"Duplicate rows collapsed: {count:N0}";
    public const string EntriesByChain = "Entries by chain:";
    public const string EntriesByKind = "Entries by kind:";
    public const string TopSourceLabels = "Top source labels:";
    public const string InvalidReviewStatus = "--review-status must be candidate or reviewed.";
    public static string OptionValueRequired(string option) => $"{option} requires a value.";
    public static string RawDirectoryMissing(string path) => $"Raw address-label directory was not found: {path}";
    public static string NoSupportedFiles(string path) => $"No supported account JSON files were found in {path}.";
    public static string UnknownChain(string fileName) => $"Cannot infer chain from file name '{fileName}'.";
    public const string InputFileMissing = "Input file was not found.";
    public static string InputArrayRequired(string path) => $"{path} must contain a JSON array.";
}

/// <summary>Supported standalone importer command-line switches.</summary>
internal static class ImportArguments
{
    public const string Output = "--output";
    public const string Input = "--input";
    public const string RawDirectory = "--raw-dir";
    public const string Kind = "--kind";
    public const string ReviewStatus = "--review-status";
}

/// <summary>Stable review states emitted in seed documents.</summary>
internal static class ImportReviewStates
{
    public const string Candidate = "candidate";
    public const string Reviewed = "reviewed";
}
