# Polymarket ingestion latency

Status: completed
Date: 2026-11-04
Summary: Bound and coalesce queued Polygon head work so raw settlement logs do not wait behind redundant finality passes. Implemented, independently reviewed and deployed locally; natural BUY/SELL latency acceptance remains pending.

## Evidence and scope

An observed frame backlog grew by about 12 frames/minute. BUY and SELL raw persistence lagged block timestamps by approximately 66 and 118 seconds respectively, while normalization took roughly two seconds and delivery succeeded on its first attempt. The single ordered processor performed a full RPC-backed finality/validation pass for every head before reaching later logs.

Preserve filtered websocket ingestion, existing finality, recovery budgets, canonical validation, source generations and owner-specific delivery. No provider, schema, subscription, polling, receipt, native scanning or deployment-setting changes belong to this fix.

## Implementation

The single channel reader takes a bounded snapshot of available frames, persists every log in arrival order (including removed logs), acknowledges barriers only after preceding persistence succeeds, then evaluates the last head in that snapshot. Lower last-arriving heads are retained for reorg handling. The snapshot never exceeds the existing queue capacity; frames arriving during persistence wait for the next snapshot so continuous ingress cannot starve head evaluation. Head finalization, coverage validation and reconnect behavior remain intact. Pruning remains bounded and runs every 64 evaluated heads.

Canonical block reads shared by followers/checkpoints are reused only inside one `ValidateAsync` call. Later validation passes, normalization commit checks and orphan checks still read canonical blocks freshly; no long-lived canonical cache is introduced.

## Acceptance and validation

Focused tests cover burst coalescing, ordered removed-log persistence, recovery barrier durability, finite draining under continuous ingress, lower reorg heads, persistence failure and cancellation. Natural BUY/SELL latency acceptance remains pending user-generated activity; no agent trades are authorized by this implementation.

Executed: `dotnet test test/UnitTests/UnitTests.csproj --filter FullyQualifiedName~Polymarket --no-restore --verbosity minimal` passed: 12 passed, 3 existing PostgreSQL-dependent billing tests skipped, 0 failed. All six new frame-processor tests passed. Existing dependency advisory warnings remain. `git diff --check` passed (line-ending warnings only). An initial test invocation exited 1 with only an advisory warning; a piped verbose diagnostic attempt was abandoned after producing no output. No live acceptance was performed.

Fresh integration traced subscription acknowledgement/reconnect, recovery barriers, ordered raw persistence/removals, normalization, canonical checks and recipient dispatch. No additional runtime defect was found in this slice. Added two regression scenarios proving that the snapshot limit retains subsequent logs/heads/barriers and a head-processing failure cannot acknowledge a later recovery barrier. The same focused command passed with 14 passed, 3 existing PostgreSQL-dependent billing tests skipped and 0 failed; all eight frame-processor tests passed. Existing package advisory warnings remain. Canonical-read reuse is confined to one validation call by inspection; no PostgreSQL-backed canonical-cache behavior test or live acceptance was executed.

Independent read-only review found no actionable issues. The coordinator built `evmwhaletracker:polymarket-latency-20261104` from the preserved payment-recovery deployment source with only the five Polygon runtime files overlaid (worker, store, frame processor, RPC and RPC diagnostics), then recreated only the app container. This avoids deploying the unrelated swap feature or its migration. A private database backup and deployment baseline preceded replacement. Polygon acknowledged all six subscriptions, the HTTP billing health check responded with no review items or pending notices, and exact comparisons preserved wallets, protocol preferences, access grants and migration history; the label count remained 5,500 with 5,499 enabled labels rehydrated. Run evidence is private under `artifacts/live-validation/polymarket-latency-20261104/`.

Initial startup observation encountered intermittent Polygon `eth_getBlockByNumber` RPC code `-32603` failures with successful reconnect/recovery. Direct latest/finalized/checkpoint probes succeeded; the failing target was not established. Added a narrow diagnostic including only a parsed numeric block height or the allowlisted `finalized` tag and RPC code; endpoint credentials and arbitrary provider error text remain excluded. No retry or finality policy changed. Focused validation passed with 16 passed, 3 existing PostgreSQL-dependent skips and 0 failures, including both diagnostic target scenarios; independent follow-up review found no actionable issues. Both image builds succeeded with existing dependency advisories.

After recovery, the first image reported zero buffered frames at 11:07:17 UTC. The final diagnostic image, deployed at 11:08:41 UTC, reported six acknowledged subscriptions, one buffered frame, zero coverage gaps and zero normalization/enrichment/delivery backlog or failures at 11:09:49 UTC. The old image had stayed saturated at 256 frames. This is a short readiness observation, not a measured trade latency guarantee or sustained-load/reorg acceptance. Natural BUY/SELL testing remains pending the user; intermittent provider errors may still cause recovery delays.
