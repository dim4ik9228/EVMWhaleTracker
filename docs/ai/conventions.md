# AI Documentation Conventions

Use this guide when adding AI-generated notes, implementation plans, or project memory.

## Where Things Go

- Root `agents.md`: durable agent instructions and architecture rules.
- `docs/ai/`: AI-facing context and working conventions.
- `docs/plans/`: implementation plans and feature/fix plans.
- `docs/architecture/`: long-lived system design notes.
- `docs/testing/live/`: reusable, manually invoked live acceptance cases and a report template; private execution reports go under Git-ignored `artifacts/live-validation/`.
- `docs/decisions/`: accepted architecture decisions.

## Plan Files

Create one plan file per meaningful feature or fix. Use a descriptive kebab-case name, for example:

```text
docs/plans/token-transfer-subscriptions.md
docs/plans/notification-enrichment-implementation-plan.md
```

At the top of each plan, include:

- status: proposed, in-progress, completed, or superseded
- date
- short summary

When a plan is implemented, update the status instead of leaving the result ambiguous.

## Decision Records

Use `docs/decisions/NNNN-short-title.md` for decisions that explain why the project chose one approach over another.

Keep each decision compact:

- status
- context
- decision
- consequences

## XML Code Comments

- Add concise XML comments to public contracts, records, options, domain models, value objects, and hosted workers.
- Explain intent, data flow, and non-obvious protocol details; do not restate obvious property names.
- Use normal implementation comments only for tricky private logic where XML comments would not help callers.
- Do not add XML comments to generated EF migrations or routine test fixtures.

## EF Core Migrations

- Treat EF Core migrations as generated artifacts.
- Create migrations with `dotnet ef migrations add`, not by hand-writing migration files.
- Use this command shape from the repository root:

```powershell
dotnet ef migrations add MigrationName `
  --project src/Infrastructure `
  --startup-project src/Presentation.Telegram `
  --context AppDbContext
```

- Manual edits to generated migrations should be rare, narrowly scoped, and called out explicitly.
- After adding a migration, run `dotnet test EVMWhaleTracker.slnx`.

## Hygiene

- Keep temporary scratch notes out of root.
- Prefer updating existing architecture or decision docs over creating duplicate explanations.
- If code behavior changes, update any affected docs in the same change.
- Avoid storing secrets, RPC keys, bot tokens, wallet private keys, or real user-sensitive data in docs.

## Named Errors and Runtime Strings

- Define named errors in the layer owning the rule or contract. `Domain.Common.Errors` contains entity/value-object invariants; Application owns use-case/port errors such as `PolymarketErrors.ProviderUnavailable`, `HyperliquidErrors`, `AssetPriceErrors` and `TokenMetadataErrors`; Infrastructure owns adapter-specific errors such as seed-file/schema validation. The common `Error`/`Result` primitives can remain in Domain without putting every error catalog there. Do not pass descriptions to `Failure`, `InvalidSeed` or equivalent wrappers; named factories accept structured values, not arbitrary prose. See [error ownership](../plans/error-catalog-layer-ownership.md).
- Keep Telegram copy/templates in presentation text catalogs and exception/log templates in the owning diagnostic catalog. Use concise semantic names and descriptive formatter parameters; preserve escaping and culture at existing boundaries.
- Share persisted state and protocol/callback tokens through the appropriate named catalogs. Constants used in EF queries or patterns must remain constant expressions. Preserve stored/wire values, error codes, option names and callback bytes when refactoring.
- Generated EF artifacts, schema/data declarations, JSON field names, declared configuration defaults, SQL/regex/format syntax, empty values and punctuation do not require mechanical extraction. Named route/cache/identity formatters may own their literal syntax. See [the refactor scope](../plans/named-errors-and-string-catalogs.md).
