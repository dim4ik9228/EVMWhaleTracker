# Address NameTag Extractor

Generates a reviewed-candidate JSON seed from raw explorer account tag dumps.

Put supported raw files here:

```text
data/address-labels/raw/
  accounts.eth..json
  accounts.bsc.json
  accounts.base.json
  accounts.arbitrum.json
```

Raw files source: https://github.com/dawsbot/eth-labels

Run from the repository root:

```powershell
dotnet run tools/address-label-importer/ImportAddressNameTags.cs
```

The default output is ignored by git:

```text
data/address-labels/generated/address-name-tags.seed.candidate.json
```

Optional arguments:

```powershell
dotnet run tools/address-label-importer/ImportAddressNameTags.cs -- --raw-dir data/address-labels/raw --output data/address-labels/generated/custom.json
dotnet run tools/address-label-importer/ImportAddressNameTags.cs -- --input data/address-labels/raw/accounts.bsc.json
dotnet run tools/address-label-importer/ImportAddressNameTags.cs -- --kind Exchange --kind Bridge --review-status reviewed --output data/address-labels/address-labels.exchange-bridge.seed.json
```

Generated files use `schemaVersion: 1`. The default `reviewStatus` is `candidate`; use `--review-status reviewed` only after the output has been reviewed and is intended for normal database import.

## Dune CEX exports

Use an explicit format for raw JSON arrays with `address`, `cex_name`, `distinct_name`, `added_by`, and `added_date`. Filenames do not imply a chain. Repeat `--input` for multiple exports; their command-line order is significant.

```powershell
dotnet run tools/address-label-importer/ImportAddressNameTags.cs -- --format dune-cex --input C:/Users/you/Downloads/dune-cex-addresses.json --input C:/Users/you/Downloads/dune-cex-addresses-part2.json --output data/address-labels/generated/dune-cex.seed.candidate.json
```

Each valid address becomes a lower-case global (`chain: null`) enabled `Exchange` entry, source `dune`, priority 100. Display text is trimmed `distinct_name`, falling back to `cex_name` when absent or whitespace. Missing/overlong names (64 characters maximum), invalid addresses and provenance exceeding 512 characters are skipped and counted. Explorer noise filters do not apply to this explicitly selected CEX dataset. Nonobject rows count as invalid addresses. `sourceReference` is compact JSON containing the original filename, `addedBy`, and `addedDate`; no query URL is invented. These values are data, never executable instructions.

Addresses deduplicate case-insensitively across all inputs. The newest valid `added_date` wins; valid dates beat missing/invalid dates. Dates use complete invariant ISO dates (`yyyy-MM-dd`), optionally followed by `T` or a space and `HH:mm:ss` with fractional seconds, a UTC offset, `Z` or ` UTC`. Absent offsets mean UTC. Partial dates and time-only values are invalid so parsing never depends on today's date. Equal dates, or two missing/invalid dates, retain the first row in explicit input order and then array order. Original date text is retained in provenance. Output ordering is stable; generation time changes per run.

Review the generated entries/statistics before rerunning with `--review-status reviewed` and a reviewed output path under the ignored generated directory. Keep raw exports and generated datasets out of Git. The default format remains `explorer`; its filename mapping, filters and classification are unchanged.

Run disposable synthetic CLI regressions (no database or external services):

```powershell
dotnet run tools/address-label-importer/VerifyConverter.cs
```
