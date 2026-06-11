# AI Documentation Conventions

Use this guide when adding AI-generated notes, implementation plans, or project memory.

## Where Things Go

- Root `agents.md`: durable agent instructions and architecture rules.
- `docs/ai/`: AI-facing context and working conventions.
- `docs/plans/`: implementation plans and feature/fix plans.
- `docs/architecture/`: long-lived system design notes.
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

## Hygiene

- Keep temporary scratch notes out of root.
- Prefer updating existing architecture or decision docs over creating duplicate explanations.
- If code behavior changes, update any affected docs in the same change.
- Avoid storing secrets, RPC keys, bot tokens, wallet private keys, or real user-sensitive data in docs.
