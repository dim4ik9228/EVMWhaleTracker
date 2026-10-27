# Named errors and string catalogs

Status: completed
Date: 2026-10-27
Summary: Review handwritten runtime code and replace inline error descriptions and meaningful application string tokens with named definitions owned by the appropriate layer.

## Requirements

- Expected failures use named `Error` definitions, for example `Failure(PolymarketErrors.ProviderUnavailable)`, with no call-site prose or helper accepting an arbitrary description. Parameterized failures use named factories accepting structured values, not caller-supplied sentences.
- Keep error catalogs in the layer owning the rule/contract: Domain invariants, Application use-case/port failures and Infrastructure adapter failures. Keep presentation copy, technical diagnostics and transport/configuration definitions in their owning layers. Do not introduce references from Domain/Application to Infrastructure or Presentation. See the subsequent [ownership correction](error-catalog-layer-ownership.md).
- Extract human-readable Telegram replies, prompts, buttons and formatting templates into named presentation definitions. Extract exception/options-validation messages into named diagnostics; keep log event templates consistently named within their owning diagnostic catalog.
- Replace stringly typed delivery/normalization/profile/health states and meaningful dispatch/action/callback/protocol tokens with named constants shared at the lowest appropriate layer. Preserve constant expressions needed by patterns and EF-translatable predicates.
- Review all handwritten `src/**/*.cs` and standalone `tools/**/*.cs`, not only Polymarket. Preserve file-based tool compilation. Prefer feature-oriented catalogs and named formatters over one unrelated global string bag.

## Compatibility and exclusions

- Preserve existing descriptions, error codes, persisted strings, protocol payloads, URLs, serialized property names, callbacks, formatting, HTML escaping, culture, status-prefix behavior and option defaults. In particular, existing Polymarket failures currently share `Polymarket.Settings`; named errors do not imply a code migration.
- No database schema changes, migrations, deployment/restart, live acceptance run, new protocol behavior or transport policy changes.
- Generated migrations/snapshots, JSON configuration/data, test fixtures, documentation examples and previously named schema/configuration declarations are not rewritten mechanically. Empty strings, punctuation/format separators and string formatting syntax are not business magic values. External schema/property names remain protocol definitions; extract meaningful repeated values without changing their representation.
- Preserve the uncommitted manual-live-acceptance documentation work. Do not commit or revert it as part of this request.

## Validation and workflow

Use a read-only inventory, one first-pass writer, a fresh integration writer, then a separate read-only reviewer. At most one repository writer at a time. Build the solution; inspect the remaining inline error/diagnostic/reply and semantic-state literal sites. Run relevant existing pure domain/parser/formatting checks where useful, without adding a mocked live-testing substitute or contacting providers/Telegram. Record actual commands, outcomes and any deliberately retained literal categories. Live acceptance remains explicitly invoked only.

## Completion and validation

Implemented named error catalogs for Polymarket, Hyperliquid, pricing, token metadata and seed validation, including structured seed-error factories. These were initially grouped in Domain; the subsequent [ownership correction](error-catalog-layer-ownership.md) places use-case/port errors in Application and seed-import errors in Infrastructure. Runtime callers no longer construct those errors from prose. Presentation text/formatters, diagnostic/log catalogs, persisted states, shared activity/stream/currency values, callback tokens and configuration keys now have named owners. Standalone importer definitions remain in its source file for file-based compilation. Error descriptions/codes and stored/wire values were preserved; no migration was added.

The first implementation pass was followed by a fresh integration pass and an independent read-only review with no actionable findings. A preliminary integration assignment stopped before inspecting code because the brief contained first-pass conclusions; those notes were removed and a new independent assignment performed the actual integration pass. The coordinator added the resulting conventions and checked the combined source/diff.

Executed checks:

- `dotnet build EVMWhaleTracker.slnx --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false`: passed, zero errors and five existing dependency advisory warnings.
- `dotnet test test/UnitTests/UnitTests.csproj --no-build --no-restore --filter "FullyQualifiedName~UnitTests.Domain|FullyQualifiedName~TelegramCallbackDataParserTests|FullyQualifiedName~TelegramStartPayloadParserTests|FullyQualifiedName~HyperliquidCallbackTests|FullyQualifiedName~TelegramNotificationRendererTests|FullyQualifiedName~TrackedWalletMenuRendererTests|FullyQualifiedName~HyperliquidProtocolTests|FullyQualifiedName~PolymarketNotificationRendererTests"`: 62 passed, zero failed/skipped. Includes generation-protected Polymarket callback and exact escaped/culture-sensitive rendering checks. A sandboxed attempt timed out communicating with testhost; the same focused checks completed with escalation. No provider or Telegram integration suite ran.
- `dotnet build tools/address-label-importer/ImportAddressNameTags.cs --disable-build-servers -p:UseSharedCompilation=false`: passed without executing the importer. For this and the successful tests, process-local `TEMP` and `TMP` pointed to an existing Git-ignored `artifacts/script-compile-temp/` directory. The standalone build emitted NU1900 because online vulnerability data was unavailable; earlier restricted-environment compilation attempts were unsuccessful.
- Coordinator `dotnet ef migrations has-pending-model-changes --no-build --project src/Infrastructure --startup-project src/Presentation.Telegram --context AppDbContext`: no model changes since the last migration.
- Coordinator source-policy scan across 201 handwritten runtime/tool C# files at the initial refactor: no direct literal arguments at failure/schema-failure, exception, alert or log sites; explicit `Error` construction remained in named catalogs/factories. Catalog ownership was subsequently corrected as linked above. This is a targeted source audit, not a claim that every C# string literal disappeared.
- Git whitespace and relative-document-link checks passed. Prior uncommitted live-runbook work was preserved.

Remaining literals are intentional schema/data/ABI/JSON definitions, declared configuration defaults, SQL/regex/format syntax, punctuation/empty values and syntax owned by named route/cache/identity formatters. No live checks, financial actions, deployment or restart were performed for this refactor. Live acceptance remains manually invoked.
