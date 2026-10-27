# Polymarket live checks

Follow the [common run rules](README.md). Scope is executed prediction fills and supported aggregate redemption payouts via the dedicated Polygon adapter. Perps, combos, ambiguous generic redemption routes and interpreted position opening/closing remain excluded. See the [implementation record](../../plans/polymarket-polygon-tracking-implementation-brief.md) and [contract registry](../../plans/polymarket-polygon-contract-registry.md).

## PM-01 — Profile mapping, alias and explicit opt-in

Prerequisites: RT-01; profile service and Polygon adapter globally enabled for the full case; unused public profile with recent supported fills; spare distinct-account capacity. Profile-only checks may be recorded independently when chain tracking is unavailable.

Steps:

1. Add an unused public EVM/profile address without an alias. Inspect any public name/pseudonym suggestion and accept it using the offered `use` flow; if there is no suggestion, record that assertion `Inconclusive` and use an explicit temporary alias to continue. Save the created wallet ID immediately, then rename it to the run's test alias.
2. Open Polymarket settings. Compare the entered address, proposed trading account and public profile mapping to the actual public API. A signer and trading account may differ; public association does not prove the Telegram user owns either account.
3. Confirm/rebind the displayed verified account where needed, set the independent trade minimum to zero and explicitly enable prediction tracking. Record its activation boundary and refresh status. Hyperliquid remains off.
4. Verify the test alias is preserved, original EVM address/network settings are unchanged, and the original owner baseline is intact.

Expected: a returned public name can be accepted without overriding a later custom alias; only the bound trading account is followed; tracking requires explicit opt-in. Status distinguishes resolving/starting/recovering/active/degraded truthfully. An unavailable profile mapping/provider/capacity blocks dependent checks; do not supply an arbitrary unverified account.

Evidence: public mapping and source, temporary ID, alias result, opt-in boundary and source health. Cleanup: reuse this wallet for selected cases, then common cleanup.

## PM-02 — Natural prediction trade and links

Prerequisites: PM-01; naturally executed supported BUY or SELL after activation and finality. Resting orders alone are not executions.

Steps:

1. Observe within the remaining time/alert budget. Compare one alert to public trade/Polygon evidence for the bound account: side, question, outcome, share count, execution price, gross collateral and fee when supplied.
2. Check profile/market/Polygon transaction links and pencil navigation to this test wallet. Respect actual collateral units (pUSD/USDC.e/USDC as applicable), not an invented profit figure.
3. Correlate the execution to durable raw-log/event identity when accessible. Multiple executions in one transaction are legitimate; transaction hash alone is not a duplicate key.

Expected: an executed prediction trade reaches the designated chat after the configured finality/enrichment path, with correct identity and units. Missing enrichment must not cause a guessed outcome. BUY success does not establish SELL, maker/taker, negative-risk or multi-fill coverage; record each observed subtype separately. A source execution that remains unresolved through the window is not proof of rendered delivery.

Evidence: public transaction/execution, activation and source times, actual alert, compared fields and optional raw-log/event identity. No eligible event within budget means `Inconclusive`. Cleanup: common cleanup.

## PM-03 — Independent gross-trade minimum (additional)

Prerequisites: PM-01; settled test queue and accessible source/normalized/outbox evidence.

Steps: leave the general wallet USD minimum unchanged; set only the Polymarket minimum above a future observed trade's gross collateral amount. Record a new below-minimum supported execution after the setting change. Verify canonical ingestion/normalization without eligible recipient delivery. Restore the protocol minimum to zero and observe a later eligible delivery if budget permits.

Expected: suppression uses gross collateral, not shares or profit, and is independent of general EVM settings. Source activity plus filtering evidence is necessary; silence is `Inconclusive`. Redemptions bypass this minimum and must not be used to claim threshold failure.

Evidence: both minimum values, setting boundary, source execution/gross value and recipient eligibility evidence. Cleanup: common cleanup.

## PM-04 — Planned app restart and overlap recovery (additional, explicit restart scope)

Prerequisites: PM-01; explicit permission in the request to restart the named app service; healthy PostgreSQL; read-only access to raw logs, checkpoints and outbox. No database restart, rebuild or deployment is implied.

Steps: record coverage checkpoint and canonical execution identities; restart the app once; verify acknowledged subscriptions and recovery to current finalized coverage; correlate any downtime execution to preserved raw/normalized state and recipient delivery. If no supported activity occurred during downtime, gap recovery remains `Inconclusive` even when reconnection passes.

Expected: binding/preferences survive, checkpoint advances, overlap does not duplicate canonical raw-log/account activities or durable recipient outbox work for the same event/generation. Telegram may accept a send before the worker persists its acknowledgement; a crash in that window can repeat delivery. Investigate repeated messages using durable identities and send/ack evidence, and report this ambiguity rather than promising externally exactly-once delivery. HTTP 429 triggers bounded cooldown rather than a tight retry loop; only assess throttling if it naturally occurs. Startup must not replay pre-activation activity as fresh enrollment alerts.

Evidence: restart window, checkpoint progression, subscription/health summary and exact event/delivery identities. This does not exercise an actual reorg or multi-owner race. Cleanup: common cleanup and ensure the requested app service is running.

## PM-05 — Natural supported redemption (additional)

Prerequisites: PM-01 and a naturally occurring supported direct CTF or official automatic aggregate redemption for the bound account. Never buy/redeem shares to create a test event.

Steps: independently identify the holder and supported contract path; compare its aggregate payout alert to canonical settlement evidence. Record the exact route tested. If selected alongside PM-03, only a redemption observed while the high minimum is effective can establish minimum bypass.

Expected: correct account, payout and collateral units; no invented per-outcome attribution or claim that payout equals profit. Direct redemption does not establish automatic-route coverage. Unsupported routes cannot satisfy this case. Evidence: holder/route, transaction and alert. No suitable event within budget means `Inconclusive`. Cleanup: common cleanup.
