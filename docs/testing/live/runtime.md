# Runtime and EVM checks

Follow [selection, budgets, baseline and cleanup](README.md). These cases exercise the current token-log pipeline. Native EVM transfers are disabled and are not a smoke-test expectation.

## RT-01 — Running deployment and bot response

Prerequisites: access to the designated deployment and test owner's bot chat.

Steps:

1. Read service status without restarting anything. If locally hosted, inspect Compose service health and sanitized recent startup/error lines. Do not print environment variables, keyed RPC URLs or unrestricted container inspection output.
2. Verify the bot identity, send `/list` in its designated chat and inspect the response.
3. Note enabled networks/source health if observable and compare the list to the baseline.

Expected: the app is running, PostgreSQL is healthy where directly observable, and the correct bot answers with the owner's wallet list. Existing wallets/preferences are preserved. An API or chain health claim needs corresponding evidence; a `/list` reply alone does not prove ingestion works.

Evidence: environment/revision confidence, service health summary, command response and baseline comparison. Mark inaccessible infrastructure checks `Blocked` individually.

Cleanup: no wallet mutations; the `/list` message needs no deletion.

## RT-02 — Temporary wallet and editor isolation

Prerequisites: RT-01 and an unused valid public address selected under the common rules.

Steps:

1. Add the temporary address with an explicit test alias. Save its wallet ID. Do not enable either protocol adapter.
2. Open its editor; change the test alias, minimum USD and explicit network membership. Refresh and confirm the saved values. Keep at least one network selected.
3. Check `/list` and the original owner baseline. Remove the temporary wallet through the bot and check the list again.

Expected: only the created wallet changes; refreshing preserves values; the test wallet disappears after removal; original wallets and preferences remain identical. No unrelated protocol is opted in.

Evidence: created ID, editor values before/after refresh, removal result and baseline comparison. Optional ENS/Polymarket alias suggestions are outside this case because an explicit alias was supplied.

Cleanup: remove this run's temporary wallet if any step failed. No persistent history deletion.

## RT-03 — Natural token-transfer delivery (additional)

Prerequisites: RT-01; an unused active public wallet on one runtime-enabled EVM network. Requires a real ERC-20/BEP-20 transfer after enrollment; do not send funds to generate it.

Steps:

1. Add one temporary wallet with a zero minimum, appropriate direction(s) and only the chosen EVM network enabled. Keep Hyperliquid/Polymarket off.
2. Observe within the remaining run budget. Compare a natural token-transfer alert to the chain explorer: chain, transaction, direction, token, amount and counterparty.
3. Check wallet/token/transaction links and the pencil link, if shown; the editor must open this temporary wallet.

Expected: a matching transfer reaches the correct recipient with correct token units and chain links. At the zero minimum used in this case, missing USD prices retain amount-only alerts without fabricating value. A positive EVM minimum suppresses unpriced transfers; validating that suppression requires an independently observed qualifying event and processing evidence, not silence alone. A transaction may contain multiple transfer lines; do not mistake them for duplicate transactions.

Evidence: public transaction, enrollment time, alert facts and destination editor. No suitable event within budget means `Inconclusive`.

Cleanup: common cleanup and baseline comparison.
