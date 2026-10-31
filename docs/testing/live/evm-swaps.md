# Ethereum swap enrichment acceptance

Manual invocation only. Do not run as part of builds or implementation verification.

Prerequisites: explicitly authorized rollout, completed integration/review, generated migration applied through the normal deployment process, and read-only qualification of the configured Ethereum endpoint for complete `trace_transaction` evidence. Current qualification failed with `-32601`; keep both `SwapEnrichment:Enabled` and `ProviderQualified` false until this changes through an authorized configuration decision. Do not purchase a provider upgrade or guess alternative trace methods.

1. Record original tracked-wallet entries, chain/direction/token preferences, thresholds and feature flags privately. Use only the explicitly authorized test account. Verify Ethereum is the only enriched chain and native scanning remains unregistered.
2. Temporarily enroll an explicitly supplied public wallet with a zero minimum and both transfer directions. Observe natural token-triggered activity for at most ten minutes. Do not trade, transfer funds or request transactions from others.
3. Stop after one verified swap, one transfer fallback, or the ten-minute budget. Compare the single alert with canonical receipt, trace, pinned router ABI and factory-proven pools; record exact raw amounts, input-side pricing if present, decision reason, RPC call count and latency. Historical receipt replay or synthetic execution fixtures prove decoding only.
4. Verify the same logical transaction did not produce both swap and transfer alerts. A provider timeout/unsupported route must yield only permitted fallback legs when canonical inclusion time and baseline metadata are available within the budget; otherwise it is suppressed. Missing natural activity is inconclusive.
5. Restore temporary enrollment/settings/flags exactly. Save the run report under Git-ignored `artifacts/live-validation/`; never store credentials or credential-bearing endpoint URLs.

Low-confirmation alerts can be invalidated by later reorgs. Sent identities remain diagnostics-only after invalidation; automatic correction messages are deferred. Telegram accepted-send/crash ambiguity can repeat a delivery. Deduplication covers the configured seven-day retention horizon, not arbitrary replay. Deadline fallback may omit late transfer legs. Ingestion overflow disconnects with a possible-gap diagnostic; reconnect has no durable EVM backfill guarantee.
