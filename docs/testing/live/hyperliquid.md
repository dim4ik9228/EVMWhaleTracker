# Hyperliquid live checks

Follow the [common run rules](README.md). Current scope is native USDC perpetual fills and supported deposit/withdrawal/liquidation ledger events. Spot, HIP-3 and TWAP coverage must not be inferred from ordinary perp success. See the [implementation record](../../plans/hyperliquid-native-websocket-implementation-brief.md) and [layout record](../../plans/hyperliquid-notification-layout.md).

## HL-01 — Explicit enrollment and source status

Prerequisites: RT-01; Hyperliquid globally enabled; a free distinct-address slot; unused public account with recent supported perp activity.

Steps:

1. Add the temporary wallet with a zero minimum USD. Save its ID and enrollment time. Confirm Hyperliquid starts off and Polymarket remains off.
2. Enable Hyperliquid in this wallet's editor and refresh status within the run budget. Check source health where accessible.
3. Confirm original wallets and network preferences did not change.

Expected: only explicit opt-in enrolls the source. No capacity rejection while a slot is actually available. The source connects and reports truthful recovery/health. A known incomplete ledger history may produce `Degraded` while live fills work; do not require blanket `Active` or complete history.

Evidence: temporary ID, opt-in boundary, before/after preference and sanitized health. Capacity or disabled global feature means `Blocked`, not permission to evict/reconfigure.

Cleanup: continue to selected cases with this same wallet, then common cleanup.

## HL-02 — Natural perp alert and links

Prerequisites: HL-01; a supported natural fill after activation.

Steps:

1. Observe at most the remaining time/alert budget. Compare a delivered fill to public Hyperliquid data/Hypurrscan: wallet, market, position effect, size, execution price and supplied realized PnL.
2. Check linked header, `HL`, Hypurrscan wallet/transaction links and pencil navigation to this test wallet. Aggregated fills use summed size and size-weighted price; compare the matching group rather than one fill alone.
3. Check that exact-zero PnL is omitted and any displayed nonzero PnL agrees with source data. Only assess cases actually observed. Missing/zero source hashes must use wallet activity, not invented transaction links.

Expected: a truthful supported alert in the designated chat, escaped names and working links. A single opening fill does not establish close/reduce/flip/liquidation coverage. Repeated identical canonical executions should be investigated; a brief observation cannot prove universal exactly-once delivery.

Evidence: source fill identity/time, Telegram message/time, compared facts and links. No supported event or insufficient correlation means `Inconclusive` for the affected assertion.

Cleanup: common cleanup; prove no enabled temporary enrollment remains. A post-opt-out source event is needed to establish opt-out suppression separately.

## HL-03 — Minimum USD suppression (additional)

Prerequisites: HL-01; delivery queue for this wallet is settled; source/store evidence accessible.

Steps: set the temporary wallet's minimum USD above an independently observed future fill's notional. Observe a new below-minimum supported fill after the setting takes effect. Verify ingestion plus filtering/outbox evidence and absence of its recipient alert within the bounded window. Restore zero and, if budget permits, correlate a later eligible fill to delivery.

Expected: below-minimum activity produces no eligible recipient delivery; later qualifying activity can notify. Silence without a matching source event/filter evidence is `Inconclusive`. Apply the normal aggregation rules when assessing size/notional.

Evidence: setting boundary, event identity/notional, recipient eligibility/outbox result and optional restored delivery. Cleanup: common cleanup.

## HL-04 — Planned app restart and recovery (additional, explicit restart scope)

Prerequisites: HL-01; request explicitly permits restarting the named app service; healthy PostgreSQL; accessible durable event/checkpoint/delivery evidence. No database restart, rebuild or deployment is implied.

Steps: record source checkpoint and observed canonical event IDs; restart the named app once; verify reconnection and bounded recovery; compare recovered/new events and recipient deliveries against public source data. If no supported event occurred during downtime, assess reconnection only and mark gap-recovery behavior `Inconclusive`.

Expected: preferences survive; overlap does not create duplicate canonical events or eligible outbox rows for the same recipient/generation; source progress resumes. Disclosed history gaps remain visible. Telegram send/ack ambiguity is not a guarantee of externally exactly-once delivery.

Evidence: restart times, before/after checkpoints, event/delivery identities and health. Cleanup: common cleanup and ensure the requested app service is running.

## HL-05 — Natural ledger event (additional)

Prerequisites: HL-01 and a supported natural deposit, withdrawal or liquidation after activation. Never initiate funds movement or liquidation for this case.

Steps: correlate an observed ledger alert to public source evidence; inspect event type, units, supplied facts and honest transaction/activity links.

Expected: the supported event is accurately described; unavailable facts/history remain explicit. No such event within budget means `Inconclusive`, not passed based on a trade alert. Evidence: source identity and rendered alert. Cleanup: common cleanup.
