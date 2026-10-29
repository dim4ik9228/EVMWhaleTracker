# Unpriced EVM transfer filtering

- Status: completed
- Date: 2026-10-28
- Summary: make a positive EVM USD minimum require a resolved USD valuation.

## Requirements

The user confirmed that unpriced EVM transfers should be suppressed when a wallet has a positive minimum USD value, and requested deployment. With minimum zero, retain amount-only notifications when pricing fails. With a resolved price, keep inclusive `value >= minimum` behavior, including zero-valued assets at minimum zero. Apply independently per tracked recipient to both existing token/native worker paths, without enabling native scanning. Do not infer value from a token symbol, change pricing providers or introduce general spam/liquidity detection. Hyperliquid and Polymarket policies remain unchanged.

Update named diagnostic copy so it describes the actual decision, and explain the policy in the existing Telegram minimum-value prompt. No schema, wallet preference, protocol enrollment, billing flag or financial changes. Preserve all existing uncommitted work.

## Validation and delivery

Focused worker regressions must cover price failure at zero/positive minimum, token and native paths, recipient isolation and successful-price threshold boundaries. Check affected prompt tests. Use the repository implementation/integration/review workflow, then rebuild the local app and verify health, deployed image, existing wallet/protocol state and billing flags. Deployment alone is not evidence of a future natural transfer being suppressed; do not claim a natural-event live acceptance check unless observed with source evidence. Record private rollout evidence under Git-ignored artifacts. Update current context and relevant docs to supersede the old fail-open policy.

## Result

Implemented the shared EVM valuation decision, named allow/suppress diagnostics and an explanatory minimum-USD prompt. Existing billing entitlement fields remain intact. First implementation, fresh integration inspection and independent review completed; no review findings remained.

Coordinator validation: `dotnet test test/UnitTests/UnitTests.csproj --no-restore --filter 'FullyQualifiedName~WalletTrackerWorkerTests|FullyQualifiedName~MinimumUsdPromptTests' --logger 'console;verbosity=minimal'` passed **27 tests, zero failures/skips**. Tests await completed worker input processing before asserting suppression, and cover native/token price failures, per-recipient isolation, inclusive priced boundaries and zero valuation. Sandbox package-assets attempts did not execute tests; the escalated run passed. Existing dependency advisory warnings remain. No migrations were added; `git diff --check` passed with existing CRLF conversion warnings.

Deployed locally on 2026-10-28 using `docker compose up -d --build --no-deps app`. Verified a changed image, running app with zero restarts, responding HTTP billing health, six startup connection/listener markers and no observed startup failure markers. Original wallet/protocol settings and billing sales/enforcement flags were exactly preserved. Private settings, image, flags, health and build/startup evidence are under Git-ignored `artifacts/live-validation/` with the current run pointer `current-usd-filter.txt`.

No new natural transfer suppression was live-validated and no wallet minimum was changed. Unpriced legitimate transfers are also suppressed at positive minima during provider outages. Priced spam is outside this fix; zero-minimum wallets continue receiving unpriced alerts. Hyperliquid/Polymarket behavior and disabled native scanning are unchanged.
