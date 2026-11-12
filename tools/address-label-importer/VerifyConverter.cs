#!/usr/bin/env dotnet run
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

// Exercise the real standalone CLI with disposable synthetic files, never a database.
var root = Path.GetFullPath(args.FirstOrDefault() ?? ".");
var converter = Path.Combine(root, "tools/address-label-importer/ImportAddressNameTags.cs");
var temp = Path.Combine(Path.GetTempPath(), $"dune-converter-{Guid.NewGuid():N}");
Directory.CreateDirectory(temp);
var assertions = 0;
try
{
    string Address(char digit) => "0x" + new string(digit, 40);
    object Row(char digit, string? distinct, string? cex = "Example", string? date = null, string? author = "tester")
        => new { address = Address(digit), distinct_name = distinct, cex_name = cex, added_date = date, added_by = author };
    var first = Path.Combine(temp, "arbitrary-name.json");
    var second = Path.Combine(temp, "another-file.json");
    await File.WriteAllTextAsync(first, JsonSerializer.Serialize(new object?[] {
        Row('a', "Example Deposit", date: "2024-01-01"),
        Row('b', " ", "Fallback"), Row('c', "First", date: "bad"),
        Row('d', "Tie first", date: "2025-01-01"),
        Row('e', new string('x', 65)), Row('f', null, null),
        new { address = "bad", cex_name = "Bad" }, null, 123,
        Row('1', "Literal <data> & text", author: "quoted \"author\""),
        Row('2', "Too much provenance", author: new string('z', 600)) }));
    await File.WriteAllTextAsync(second, JsonSerializer.Serialize(new object[] {
        new { address = Address('a').ToUpperInvariant().Replace("0X", "0x"), distinct_name = "Newest", cex_name = "Example", added_date = "2026-01-01", added_by = "new-author" },
        Row('a', "Older last", date: "2023-01-01"),
        Row('c', "Valid beats invalid", date: "2020-01-01"),
        Row('d', "Tie later", date: "2025-01-01"),
        Row('b', "Invalid tie later", date: "invalid") }));
    var dune = await Run(true, "--format", "dune-cex", "--input", first, "--input", second);
    var entries = dune!["entries"]!.AsArray();
    Check(entries.Count == 5, "valid unique Dune entries only");
    Check(dune["reviewStatus"]!.GetValue<string>() == "candidate", "candidate default");
    JsonNode Entry(char digit) => entries.Single(e => e!["address"]!.GetValue<string>() == Address(digit))!;
    Check(Entry('a')["nameTag"]!.GetValue<string>() == "Newest", "newest date wins across files and address casing");
    Check(Entry('b')["nameTag"]!.GetValue<string>() == "Fallback", "blank distinct name falls back and invalid date tie keeps first");
    Check(Entry('c')["nameTag"]!.GetValue<string>() == "Valid beats invalid", "valid date beats invalid");
    Check(Entry('d')["nameTag"]!.GetValue<string>() == "Tie first", "equal dates keep first row");
    Check(Entry('1')["nameTag"]!.GetValue<string>() == "Literal <data> & text", "data remains literal");
    Check(entries.All(e => e!["chain"] is null && e["chainId"] is null && e["source"]!.GetValue<string>() == "dune" && e["kind"]!.GetValue<string>() == "Exchange" && e["priority"]!.GetValue<int>() == 100 && e["isEnabled"]!.GetValue<bool>()), "global enabled exchange mapping");
    var provenance = JsonNode.Parse(Entry('a')["sourceReference"]!.GetValue<string>())!;
    Check(provenance["file"]!.GetValue<string>() == "another-file.json" && provenance["addedBy"]!.GetValue<string>() == "new-author" && provenance["addedDate"]!.GetValue<string>() == "2026-01-01", "winning row provenance");
    Check(dune["stats"]!["duplicateRows"]!.GetValue<int>() == 5, "duplicate statistics");
    Check(dune["stats"]!["invalidAddressRows"]!.GetValue<int>() == 3, "invalid and nonobject rows skipped");
    Check(dune["stats"]!["nullNameTagRows"]!.GetValue<int>() == 2, "missing and overlong labels skipped");
    Check(dune["stats"]!["excludedRows"]!.GetValue<int>() == 1, "overlong provenance skipped");
    var repeat = await Run(true, "--format", "dune-cex", "--input", first, "--input", second);
    Check(JsonNode.DeepEquals(entries, repeat!["entries"]), "repeatable entries");
    var dateEdges = Path.Combine(temp, "date-edges.json");
    await File.WriteAllTextAsync(dateEdges, JsonSerializer.Serialize(new[] {
        Row('a', "Invalid first", date: "bad"), Row('a', "Valid minimum", date: "0001-01-01"),
        Row('b', "Complete date", date: "2000-01-01"), Row('b', "Time only", date: "23:59:59"),
        Row('c', "Offset first", date: "2026-01-01T03:00:00+03:00"),
        Row('c', "UTC tie later", date: "2026-01-01T00:00:00Z"),
        Row('d', "Earlier date", date: "2026-01-01"), Row('d', "Fractional UTC", date: "2026-01-01 00:00:00.123 UTC") }));
    var dateSeed = await Run(true, "--format", "dune-cex", "--input", dateEdges);
    string DateName(char digit) => dateSeed!["entries"]!.AsArray().Single(e => e!["address"]!.GetValue<string>() == Address(digit))!["nameTag"]!.GetValue<string>();
    Check(DateName('a') == "Valid minimum", "minimum valid date beats invalid date");
    Check(DateName('b') == "Complete date", "time-only values cannot infer the current date");
    Check(DateName('c') == "Offset first", "UTC-equivalent timestamps preserve input order");
    Check(DateName('d') == "Fractional UTC", "full fractional UTC timestamps are valid dates");
    var reviewed = await Run(true, "--format", "dune-cex", "--input", first, "--review-status", "reviewed", "--kind", "Bridge");
    Check(reviewed!["reviewStatus"]!.GetValue<string>() == "reviewed" && reviewed["entries"]!.AsArray().Count == 0, "review and kind options");
    await Run(false, "--format", "unknown", "--input", first);
    await Run(false, "--format", "dune-cex");
    var malformed = Path.Combine(temp, "malformed.json");
    await File.WriteAllTextAsync(malformed, "{}");
    await Run(false, "--format", "dune-cex", "--input", malformed);
    await Run(false, "--format", "dune-cex", "--input", Path.Combine(temp, "missing.json"));
    await File.WriteAllTextAsync(malformed, "[broken");
    await Run(false, "--format", "dune-cex", "--input", malformed);

    var explorerPath = Path.Combine(temp, "accounts.base.json");
    await File.WriteAllTextAsync(explorerPath, JsonSerializer.Serialize(new[] {
        new { address = Address('a'), nameTag = (string?)"Example Exchange", label = "exchange" },
        new { address = Address('b'), nameTag = (string?)"Across Bridge", label = "bridge" },
        new { address = Address('c'), nameTag = (string?)"Fake_phishing", label = "exchange" },
        new { address = Address('d'), nameTag = (string?)null, label = "exchange" } }));
    var explorer = await Run(true, "--input", explorerPath);
    var oldEntries = explorer!["entries"]!.AsArray();
    Check(oldEntries.Count == 2, "explorer filtering unchanged");
    Check(oldEntries[0]!["chain"] is null && oldEntries[0]!["sourceReference"]!.GetValue<string>() == $"https://basescan.org/address/{Address('a')}", "explorer exchange scope and URL");
    Check(oldEntries[1]!["chain"]!.GetValue<string>() == "Base" && oldEntries[1]!["priority"]!.GetValue<int>() == 90, "explorer bridge scope");
    Console.WriteLine($"Passed {assertions} converter assertions.");

    async Task<JsonNode?> Run(bool success, params string[] arguments)
    {
        var output = Path.Combine(temp, $"output-{Guid.NewGuid():N}.json");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "run", converter, "--", "--output", output }.Concat(arguments)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        var logs = await stdout + await stderr;
        Check((process.ExitCode == 0) == success, $"CLI exit ({string.Join(' ', arguments)}): {logs}");
        if (!success) { Check(!File.Exists(output), "failed CLI does not emit seed"); return null; }
        return JsonNode.Parse(await File.ReadAllTextAsync(output));
    }
    void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        assertions++;
    }
}
finally
{
    Directory.Delete(temp, recursive: true);
}
