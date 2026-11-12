# Dune CEX Label Import

Status: completed
Date: 2026-10-31

Add offline conversion of the owner's two Dune CEX JSON exports and import their reviewed labels into the existing database while preserving unrelated data. Implementation, fresh integration, independent review and the authorized database import are complete. Initial implementation-stage results below are superseded by the final acceptance record.

## Requirements and decisions

- Explicit `--format dune-cex`, repeatable `--input`, no filename-based chain inference; explorer remains the default unchanged schema.
- Trim `distinct_name`, falling back to `cex_name`; emit global enabled Exchange labels, Dune source and priority 100. Respect the domain's 64-character label and 512-character source-reference limits.
- Preserve original filename, contributor and date as compact JSON provenance without inventing query links. Raw inputs are untrusted data.
- Normalize/deduplicate addresses case-insensitively. Newest valid invariant-culture UTC date wins, with valid dates ahead of invalid/missing dates; ties retain first command-line file/array row. Stable sorting makes entry output repeatable.
- Existing upserts overwrite matching global labels regardless of old priority and preserve all other rows and tracked-wallet aliases. Runtime source parsing recognizes Dune while preserving prior SeedFile behavior for explorer/unspecified sources.
- Optional `--prefer-global-exchanges` disables enabled chain-specific overrides matching incoming enabled global exchanges. Keep existing row IDs/metadata, report disabled overrides separately, and predict the same change without writes in dry-run. Default resolution/import behavior stays unchanged.
- No schema migration, runtime scraping, blockchain detection changes, cache design changes, bot feature deployment or infrastructure mutation in this implementation slice.

## Validation

`dotnet run tools/address-label-importer/VerifyConverter.cs` passed 32 assertions against the real CLI: arbitrary filenames, repeated inputs, name fallback, literal data, scope/source/priority/enabled mapping, date/casing duplicate conflicts, provenance, malformed/non-array/missing inputs, invalid rows, overlong names/provenance, deterministic entries, review/kind flags and explorer scope/filter/URL regression.

`dotnet test test/UnitTests/UnitTests.csproj --no-restore --filter 'FullyQualifiedName~AddressLabel' --verbosity normal` passed 23 tests with zero skipped. New coverage verifies dry-run immutability, authoritative update over higher-priority old labels, source compatibility, repeated import counts, global cache fallback, optional override disabling and preservation of old scoped/unmatched IDs and attributes. Existing NuGet vulnerability warnings remain. A preceding minimal test invocation exited 1 without a diagnostic beyond a package warning; the normal-verbosity rerun built and passed.

The actual two Downloads exports converted successfully to the ignored `data/address-labels/generated/dune-cex.seed.candidate.json`: 4,957 rows and global Exchange entries; zero invalid, excluded, missing-name or duplicate rows. This was offline conversion only, with no database writes.

## Coordinator acceptance still required

Use an isolated import-only image based on the reviewed running release, adding only importer changes; do not deploy pending optional Swaps work or migrations. Back up first, generate/review the private dataset, dry-run and apply with the same `--prefer-global-exchanges` option. Compare all unmatched original rows and tracked aliases, verify the overlapping scoped record is retained disabled, and restart the same bot image for cache hydration. Raw Downloads and generated data remain outside Git. No live import or restart has been performed by the implementation agent.

## Final validation and applied import

Fresh integration corrected complete-date parsing/ranking so deduplication cannot depend on today's date and valid minimum dates outrank invalid dates; invalid non-object seed entries now return normal import failures. Independent review identified that address/scope upsert could disable a historical duplicate rather than the intended active override. The fix adds an exact-ID repository operation and an idempotent domain disable transition that preserves every other property. The reviewer verified the resolution. Final focused suite: **26 passed, zero failed/skipped**; converter harness: **37 assertions passed**. The actual exports produced 4,957 unique global enabled Exchange entries with zero rejected rows; every generated label/scope/source/priority was independently compared to the inputs. Existing dependency advisory warnings remain.

The coordinator backed up the database and ran an isolated import-only image from compatible base `2472447` plus the six label runtime files, excluding optional swap code/migrations and payment workers. An initial dry-run invocation failed before connecting because Docker's raw env-file parsing retained Compose quotes; using the existing container's parsed environment in a private ignored file resolved this without changing credentials. The successful dry run predicted 4,603 additions, 354 updates and one disabled scoped override, with zero invalid/skipped entries. Exact before/after label and migration comparisons confirmed no dry-run writes.

Apply produced those same counts: **897 → 5,500 stored records, 5,499 enabled**. All 4,957 incoming global records were checked against the reviewed seed, including Dune source and provenance. Every original ID survived. The 354 matching global rows retained address/scope/creation metadata while imported mutable fields won; the conflicting Ethereum bridge row retained its metadata and became disabled so the global HTX label resolves. All 542 unrelated original rows were byte-for-property identical. Tracked wallets, access grants and Hyperliquid/Polymarket settings were unchanged; migration history was unchanged.

The same existing bot image was restarted only to refresh its startup-only cache; logs confirmed **5,499 labels hydrated** and billing health was reachable with zero review/notice backlog. Final enabled coverage includes Bitget 36, Bybit 50 and MEXC 27 addresses. No natural transfer notification test was selected or claimed. Inputs/generated data, full backup and preservation evidence remain Git-ignored; the reviewed seed is `data/address-labels/generated/dune-cex.seed.reviewed.json`, and the private run is referenced by `artifacts/live-validation/current-dune-import.txt`.
