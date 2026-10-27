# Manual live acceptance runbooks

Status: completed
Date: 2026-10-27
Summary: Store reusable, explicitly invoked checks of the running bot separately from implementation plans and automated tests.

## Requirements and acceptance

- Provide runtime, Hyperliquid and Polymarket cases with stable IDs, steps, expected behavior, evidence and cleanup.
- Run only when the user explicitly requests live validation. A build, code change, deployment or request to write cases does not trigger execution.
- Allow a small targeted run through a natural-language prompt; do not read or execute every suite by default.
- Use natural public activity and the actual Telegram UI. Never trade, move funds or fabricate events to satisfy a case.
- Preserve existing wallets and preferences, bound observation and alert volume, and record unavailable scenarios honestly.
- Separate committed case definitions from private local run reports. A prior successful rollout is historical evidence, not a new test result.

## Design

The [live acceptance index](../testing/live/README.md) defines selection, prerequisites, execution budgets and reporting. Each feature file contains independent cases; the [report template](../testing/live/report-template.md) captures only selected cases and evidence. No executable runner, CI hook, background monitor or application dependency is introduced.

Explicit deployment/restart, outage, reorg and multi-user exercises are outside the default smoke scope. Read-only runtime checks can establish prerequisites without expanding the requested run.

## Validation

Definitions were checked against the current settings, ingestion and delivery paths and prior rollout records, followed by an independent documentation review. Relative links and Git whitespace checks are validated during delivery. No live cases are executed as part of authoring this documentation.
