# Error catalog layer ownership

Status: completed
Date: 2026-10-27
Summary: Correct the ownership of use-case and adapter errors introduced by the named-string refactor.

## Requirements

- Keep common `Error`/`Result` primitives and genuine entity/value-object invariant errors in Domain.
- Move Polymarket tracking-use-case failures into `Application.Polymarket.PolymarketErrors` and Hyperliquid tracking-use-case failures into `Application.Hyperliquid.HyperliquidErrors`.
- Move price/metadata lookup contract failures beside their Application abstraction contracts as `AssetPriceErrors` and `TokenMetadataErrors`.
- Move seed-file/schema/import-policy errors into an Infrastructure-owned `AddressLabelSeedErrors` catalog; those are not entity invariants. Keep existing AddressLabel entity invariant errors in Domain.
- Update all runtime/test references. Preserve every existing error code and description and structured factory interpolation behavior. Do not leave obsolete Domain forwarding aliases that retain the misplaced ownership.
- Correct conventions/context and the earlier refactor record so they no longer require all errors to live in Domain. Named catalogs belong to the layer owning the rule/contract; no inline failure prose is reintroduced.

## Boundaries and acceptance

No tracking, persistence, schema, protocol, message, dependency direction, live test, deployment or restart changes. Preserve all existing uncommitted work. Domain must not reference Application/Infrastructure/Presentation. No Polymarket/Hyperliquid/provider/seed-import error groups remain in `Domain.Common.Errors`.

Validate the solution build, relevant pure regression checks and exact code/description preservation by source comparison. Use one implementation writer, a fresh integration pass and independent read-only review. These are relocation changes, so tests that merely duplicate catalog literals are unnecessary. Keep this brief requirements-only until independent review is complete.

## Completion and validation

Moved all five error groups and updated their Infrastructure callers. Application now owns `PolymarketErrors`, `HyperliquidErrors`, `AssetPriceErrors` and `TokenMetadataErrors`; Infrastructure owns internal `AddressLabelSeedErrors`. `Domain.Common.Errors` again contains only the original Address, TrackedWallet and AddressLabel invariant groups. Common Error/Result primitives remain in Domain. Conventions, runtime context and the earlier refactor record now describe rule/contract ownership rather than a mandatory Domain catalog.

Executed validation:

- Implementation and fresh integration solution builds passed using `dotnet build EVMWhaleTracker.slnx --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false`: zero errors, five existing dependency advisory warnings.
- Integration ran 14 focused regressions covering seed imports, pricing, address-label invariants and Hyperliquid callbacks: all passed, zero failures/skips. The initial sandbox testhost attempt stalled; a retry with escalation and local process TEMP/TMP succeeded. No live/production database checks ran.
- Coordinator compared every moved member declaration/factory against the pre-move working-tree baseline: all 28 matched exactly after indentation removal. Domain's invariant catalog matches pre-refactor HEAD; no obsolete group references or reverse-layer references remain. Source comparison is the primary text/code compatibility evidence, not tests mirroring constant values.
- Independent read-only review found no actionable ownership/caller/dependency issue. Its documentation read accidentally included the earlier feature's historical completion narrative; the reviewer disclosed this and based the correction review on current source, original definitions and project dependencies rather than that narrative. This correction's requirements brief remained free of implementer conclusions until review finished.
- Git whitespace and relative documentation-link checks passed; no migration files changed. Existing uncommitted refactor/runbook work was preserved. No deployment, restart, provider call or Telegram/live acceptance check occurred.
