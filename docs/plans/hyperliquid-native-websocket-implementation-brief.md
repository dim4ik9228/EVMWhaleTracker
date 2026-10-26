# Native Hyperliquid WebSocket Integration Brief

Status: completed
Date: 2026-10-19
Summary: Add opt-in HyperCore wallet alerts using one shared public Hyperliquid WebSocket connection, with durable recovery and a global ten-address admission limit.

## Implementation state and validation limits (2026-10-19)

The default-off implementation is complete, including a fresh integration pass and independent review with no remaining actionable findings. The subsequent authorized live rollout is recorded below. The host registers separate Core ingestion and durable delivery workers without enabling native EVM scanning. Existing wallet entries gain an owner-checked Hyperliquid editor. PostgreSQL serializes admission, retains reservations until the old socket is closed, protects one ingestion owner, and persists source identities, recipient work and recovery boundaries. Delivery shares Telegram retry and rate-limit services with EVM notifications.

Integration fixes include waiting for all desired subscription acknowledgments before recovery, time-scoped fill identities, streamed bounded HTTP responses, idempotent socket cleanup, and configured aggregation scheduling. Paused settings release unused reservations only while holding both admission and ingestion ownership locks, so an overlapping enabled host keeps its old socket's capacity. Disabled Core skips source-bound startup validation while admission still enforces the provider's ten-address maximum. Recipient scheduling rotates through due chats, and opt-out during an in-flight permit wait cannot restore cancelled outbox work. The adapter reconnects with exponential backoff and jitter capped at 30 seconds; endpoint, heartbeat, queue/frame, aggregation, history-page and retention settings are configurable. Configuration changes take effect on host restart.

Recovery preserves inclusive timestamp boundaries and reports stalled pages, exhausted fill retention, age overruns and request/page failures. Ledger history has no verified completeness guarantee, so an enabled feed currently reports Degraded after recovery even when its socket is healthy. Health status is process-local; a standby host's editor can show Connecting while another host owns ingestion. Durable gap records are retained and are not automatically cleared by a later successful bounded recovery. Live checkpoints are conservatively advanced only by completed recovery, so a continuously connected session longer than the configured history window may report an age gap on restart even when live events were accepted.

The coordinator executed `dotnet test EVMWhaleTracker.slnx --no-restore` against the explicitly disposable loopback PostgreSQL database: 173 passed, zero skipped (41 seconds). Normal/debug Compose configuration, synthetic exporter defaults/overrides and `git diff --check` passed. Independent review verified the fixes and found no remaining actionable defects. Tests use fake provider sockets/HTTP and fake Telegram delivery; no real Telegram messages or production database mutations were used. Crash-boundary checks simulate durable commit/reopened-context and worker restarts; actual process-kill crash injection was not performed. Existing NuGet audit warnings remain for SQLitePCLRaw.lib.e_sqlite3 2.1.11 and System.Security.Cryptography.Pkcs 9.0.0.

At implementation completion on 2026-10-19, no live Core rollout had been performed; see the subsequent rollout evidence below. Acknowledgments are not end-to-end evidence. TWAP execution coverage is unverified; dedicated TWAP, spot and HIP-3 tracking remain excluded. Telegram can repeat an accepted message if a process crashes before recording delivery success. Fake-provider acceptance tests cover heartbeat loss, partial subscription rejection, consumer overload, successful inclusive multi-page recovery and restart deduplication, 10,000-fill retention exhaustion and opt-out while awaiting a delivery permit. These scenarios passed in the final full suite. Metadata requires an explicit `collateralToken: 0`; older/sample schemas omitting collateral identity fail closed rather than assuming USDC. One public metadata request verified that the current mainnet `meta` response supplies collateralToken 0 and 234 markets; this proves schema compatibility only.

## Objective and authorization

Extend the Telegram bot with useful Hyperliquid trading/account alerts while keeping normal ingestion event-driven. The user authorized implementation of this brief on 2026-10-19. Checked-in configuration remains opt-in; implementation validation does not require automatically enabling the live deployment.

Success means an explicitly enrolled wallet produces accurate, owner-specific Telegram alerts; reconnects and process restarts recover within a documented history window; capacity or recovery failures are visible rather than silently dropping wallets or claiming complete coverage.

## Proposed first release

- Mainnet public endpoint: `wss://api.hyperliquid.xyz/ws`.
- One shared connection per bot deployment, with `userFills` (`aggregateByTime=false`) and `userNonFundingLedgerUpdates` subscriptions per admitted address. No account signatures, private keys, exchange actions, or trading permissions.
- Initial market scope: native USDC-quoted perpetual markets. Cache market metadata and classify markets explicitly. Spot fills and HIP-3 markets are ignored safely in this release, retaining enough source identity for a later extension.
- Alert types: executed perp trades, liquidation account events, USDC deposits, and USDC withdrawals. A withdrawal alert reports the source ledger event; do not invent a destination-chain settlement confirmation.
- Fills produced by TWAP execution may appear in the canonical fills feed and follow normal trade handling. Dedicated TWAP status/progress and slice subscriptions are deferred; do not claim those are fully tracked.
- Existing wallet alias and minimum USD preference remain available. Hyperliquid enablement is a separate explicit wallet setting, default off for both existing and newly added wallets.
- Existing EVM tracking, Infura configuration and native EVM scanning policy remain unchanged. `HyperEvm = 999` is not HyperCore. Do not add a fake EVM chain ID or route Core events through ERC-20 log parsing.

This first cut enrolls an existing tracked-wallet entry. A separate Hyperliquid-only wallet creation/management flow, independent of the existing EVM network checklist, is deferred.

## Verified provider constraints

Checked against official documentation on 2026-10-19; recheck before implementation:

| Constraint | Published value / behavior |
| --- | --- |
| Public IP budget | 10 connections, 1,000 subscriptions, **10 distinct user addresses across user-specific subscriptions** |
| Connection/message budget | 30 new connections/minute; 2,000 client messages/minute |
| HTTP recovery budget | 1,200 aggregate request weight/minute; history responses incur additional weight |
| Quiet connection | Server closes after 60 seconds without a server message; application `ping` receives `pong` |
| Fill history | `userFillsByTime`: at most 2,000 fills/response; only latest 10,000 fills available; inclusive time bounds |
| Snapshots | Time-series subscriptions can include prior data marked `isSnapshot`; reconnect data can overlap processed events |
| Account identity | Subscribe to the actual master/subaccount address, not an agent wallet |

Sources: [limits](https://hyperliquid.gitbook.io/hyperliquid-docs/for-developers/api/rate-limits-and-user-limits), [heartbeats](https://hyperliquid.gitbook.io/hyperliquid-docs/for-developers/api/websocket/timeouts-and-heartbeats), [subscriptions](https://hyperliquid.gitbook.io/hyperliquid-docs/for-developers/api/websocket/subscriptions), [connection/recovery](https://hyperliquid.gitbook.io/hyperliquid-docs/for-developers/api/websocket), [fill history](https://hyperliquid.gitbook.io/hyperliquid-docs/for-developers/api/info-endpoint#retrieve-a-users-fills-by-time). Ledger history has its own documented paging rules; do not assume the fill-history retention guarantee applies to ledger events.

One connection and two feeds for ten addresses use twenty subscriptions. The address cap is shared across all Telegram users and other clients using the same egress IP, not ten per customer or per connection.

## Wallet settings, ownership and admission

1. Add a distinct `Hyperliquid` action to the wallet editor, with On/Off and status. Do not put it in the existing EVM network checklist. Existing empty `EnabledChains` means all runtime EVM networks and must never opt a wallet into Core.
2. Persist a per-wallet Hyperliquid preference and activation generation/time. A compact one-to-one settings record linked to `TrackedWallet` is preferred; keep protocol-specific settings out of EVM chain JSON.
3. Enforce capacity atomically using PostgreSQL reservations keyed by normalized source address. Count the union of opted-in addresses, not tracked-wallet rows. Reserve the slot and persist the opt-in in one transaction. Business failures return `Result`.
4. Several users following the same address consume one slot. Each keeps their own alias, threshold, activation time and permissions. Disabling/deleting one owner retains the stream for remaining owners.
5. Reject an eleventh distinct address with a clear capacity message and no partial opt-in. Repeated enables are idempotent. Do not reveal other users, addresses or aliases in capacity/status responses.
6. Release a slot only when its last owner disables/deletes and old subscriptions are confirmed removed. If unsubscribe outcome is unknown, close the shared socket and rebuild the admitted set before reusing capacity. Account for pending removals during concurrent enables.
7. Use a dedicated settings-change signal/version. Current `InMemoryCacheService` signals address membership changes; an opt-in on an already cached EVM address must still reconcile Core subscriptions immediately.
8. Run one active Hyperliquid ingestion owner, protected by a PostgreSQL lease/advisory lock. This prevents duplicate ingestion during overlapping container restarts; additional processes remain standby. Deployment must reserve the public-IP budget against unrelated clients too.
9. On startup, never silently take the first ten records if persisted state exceeds configured capacity. Mark Core unavailable with an operator-visible reason and preserve settings; EVM tracking continues.

## Event handling and Telegram behavior

- A new subscriber/adapter normalizes provider payloads into typed HyperCore activity contracts with source, address, market, event identity, event time and exact decimal quantities. Keep the adapter behind an Application port so another provider can replace it later.
- Reuse Telegram transport, retry policy and global/per-chat rate limiting. Extend delivery through a source-aware envelope or common rendered-message sender; do not invent EVM block numbers/hashes to satisfy current `NotificationPayload`/transaction aggregation contracts. No independent limiter that doubles the per-chat allowance across sources.
- Render compact alerts showing wallet alias, `Hyperliquid`, action/market, size, execution price, approximate USD notional and source time. Include realized PnL only when supplied; do not label it total lifetime or net PnL. Use a wallet link when an individual event lacks a valid explorer transaction link.
- Interpret signed starting position and fill direction for opens, increases, reductions, closes and flips. A buy alone does not mean opening a long. Validate behavior against fixtures before using interpreted wording; use neutral trade wording if evidence is insufficient.
- The ledger liquidation event is the canonical liquidation-account alert. Suppress an ordinary trade alert for a liquidation fill only when the tracked account is identified as the liquidated user; a counterparty/liquidator's fill must not be mislabeled as that wallet being liquidated. Retain the execution details. Deduplicate each stream independently; correlate cross-stream details only with reliable identifiers. If reliable correlation is unavailable, render the ledger facts without invented execution price/notional or a fabricated exactly-once cross-stream guarantee.
- Ledger scope is exactly `deposit`, `withdraw` and `liquidation`. Internal transfers, spot transfers, funding, vault activity, staking and balances are outside the first alert scope.
- Proposed threshold policy: inclusive `>=`, applied to each fill before aggregation, matching the current EVM per-event policy. Fill notional is `abs(size * executionPrice)`; native USDC quotes are displayed as an approximate USD estimate, not a guaranteed peg or collateral requirement. Deposit/withdrawal estimates use the supplied USDC amount. Liquidations without a documented comparable USD value bypass the threshold and display available facts without a fabricated value.
- Aggregate eligible ordinary fills for a short configurable window (proposed one second), keyed by owner, wallet, market, order ID and position effect. Use size-weighted execution price and preserve original event IDs. Never combine unrelated orders, different position effects, different users, EVM transfers or ledger events. Individually sub-threshold fills do not become eligible by batching.
- Recheck current ownership, activation generation and preferences before fan-out and before sending queued alerts. An opt-out cancels unsent Core alerts for that owner. A new follower receives only activity after their activation, even if another follower already kept the address subscribed.
- Resolve aliases only within the receiving user's scope. Do not reuse the existing cross-owner counterparty-alias selection for the new source. Escape provider text and aliases for the actual Telegram parse mode.

## Durability, reconnects and recovery

Use PostgreSQL for a bounded durable event inbox, per-address/per-stream checkpoints and pending recipient deliveries. In-memory channels provide bounded scheduling/wakeup, not the only copy of accepted events. Retention/pruning must bound database growth while retaining source identities for the supported recovery horizon.

1. Start a reader and application heartbeat loop independent of Telegram work. Proposed defaults: ping every 20 seconds, reconnect on missing pong/transport failure, exponential backoff with jitter capped at 30 seconds. Confirm subscription acknowledgments; connected does not mean every wallet is subscribed.
2. Persist accepted source events and recipient work before advancing checkpoints. Use stable event identities scoped by source, address and event kind, adding market and provider execution identifiers as required. Transaction hash alone is invalid: one order can have many fills, and TWAP hashes may be zero. Use a deterministic canonical identity for ledger records whose documented schema lacks a unique event ID; test same-time distinct events.
3. On first-ever opt-in, record an activation boundary. Historical snapshot entries seed deduplication/baseline without alerts. Do not discard new live events arriving during initialization or replay old activity when a second user opts in.
4. On reconnect/restart, subscribe first and retain the live overlap, then fetch bounded history from the persisted boundary. Reconcile snapshots, HTTP results and buffered live data before advancing completeness state. Preserve timestamp-boundary event identities because paging bounds are inclusive; never skip a page boundary by blindly adding one millisecond.
5. HTTP is allowed only for initialization metadata and bounded recovery/reconciliation, never recurring quiet-wallet discovery or one request per fill. Share a weighted rate limiter across Core HTTP calls, honor throttling, cap request concurrency and recovery pages, and avoid retry storms.
6. History exhaustion, a stalled/full page, retention overrun or a recovery request budget reached produces an explicit gap/degraded status with the affected interval. Keep live ingestion running where safe; do not mark the missing interval recovered. Use the documented ledger endpoint separately and surface its completeness limits.
7. Queues, frame sizes, batch sizes and retention have explicit bounds. On overload, preserve durable work, signal degraded health and recover source gaps deliberately; do not silently drop events or block heartbeat processing behind Telegram.
8. Durable recipient work survives restart. Blocked chats and exhausted Telegram retries are isolated and recorded. Source processing is idempotent, but Telegram acceptance followed by a process crash before recording success can still cause a repeated message; do not promise externally exactly-once delivery.

## Configuration and operations

Add separate `Hyperliquid` options to appsettings/Compose/exporter with safe checked-in defaults: `Enabled=false`, mainnet WebSocket/HTTP base URLs, `MaxUniqueWallets=10` (validated 1..10 for this adapter), heartbeat/reconnect settings, aggregation window, queue/frame limits, recovery budgets and retention. No Infura API key is used for this adapter. Do not automatically enable it during this planning task.

Expose structured health/logs for connection and pong age, acknowledged versus desired subscriptions, unique-address capacity, reconnects, recovery progress/gaps, duplicate events, inbox/outbox backlog and Telegram delivery failures. Wallet status distinguishes Off, Connecting, Live, Recovering and Degraded; public copy explains delayed/incomplete tracking without exposing implementation internals or other owners. A quiet wallet with healthy pong responses is not by itself a failed feed.

Disabling the global flag closes Core subscriptions and stops Core HTTP recovery and unsent Core deliveries, preserving settings/history for rollback. Re-enable follows persisted recovery boundaries and reports gaps; intentional feature downtime is not falsely treated as covered. EVM workers remain independently operable. Running locally needs outbound network access; reliable 24/7 operation can use the same Docker stack on an always-on host, without a public inbound HTTPS receiver.

## Implementation slices and affected areas

| Slice | Work |
| --- | --- |
| Domain/Application | Explicit opt-in/settings, admission reservation and owner isolation, source-aware event/delivery contracts and ports, commands/queries returning Result |
| Infrastructure | Public WS/HTTP adapter, singleton ownership lease, subscription reconciliation, bounded inbox/checkpoints/outbox, recovery, metadata cache and health |
| Telegram | Hyperliquid editor/status, capacity/validation responses, source-aware rendering, shared delivery rate limits, opt-out cancellation |
| Host/configuration | Independent registration in `Program.cs`, defaults/options/Compose/exporter, safe disable/restart behavior |
| Persistence/tests/docs | Generated EF migrations, deterministic protocol fixtures, PostgreSQL concurrency/durability checks, current context/README and rollout evidence |

Relevant existing seams: `TrackedWallet`, `ITrackedWalletRepository`, `InMemoryCacheService`, `OnChainPipelineChannels`, `NotificationPayload`, `NotificationAggregationWorker`, `TrackedWalletMenuRenderer`, `TelegramNotificationRenderer`, `NotificationDispatcherWorker`, and `Program.cs`. Keep Clean Architecture and in-memory caching; no Redis or new microservice is required.

## Acceptance criteria and validation

1. Default-off configuration produces zero Core traffic. Enabling Core works independently of enabled EVM networks; disabling it leaves EVM behavior unchanged.
2. Existing unrestricted EVM wallets remain opted out. Owner-checked edits persist immediately and refresh subscriptions even when address membership is unchanged.
3. One fake socket carries the union of admitted wallets. Shared addresses consume one slot; concurrent final-slot requests admit at most one new address. Eleventh-wallet rejection, stale/repeated callbacks, unsubscribe uncertainty and overlapping host startup are covered.
4. Representative opens/increases/reductions/closes/flips, partial fills, TWAP-origin fills when present, liquidation victim-versus-counterparty records and deposit/withdrawal records render accurately. Prove any claimed TWAP execution coverage before rollout rather than assuming it from the fills subscription. Spot/HIP-3/unknown event types cannot be misclassified. Decimal threshold boundaries and source identities are verified.
5. Initial snapshots do not create historical spam; interleaved new events still deliver. Reconnect/restart snapshots and paginated HTTP overlaps generate no duplicate source work. Same-time different fills/ledger records remain distinct.
6. Bounded recovery reports insufficient history and page/budget exhaustion honestly. Transport drops, silent heartbeat failures, partial subscription rejection, malformed frames and slow consumers do not stop unrelated EVM workers.
7. Aliases, settings and recipients stay owner-specific. Deleting/turning off a wallet cancels queued work for that owner only; a new follower does not receive earlier activity. Shared Telegram rate limits apply across EVM and Core traffic.
8. Crash tests cover commit/checkpoint boundaries and recipient work recovery. A blocked Telegram chat does not block source ingestion or another recipient. Retention cannot prune still-pending work or make claimed recovery coverage invalid.
9. Generate EF migrations through the documented `dotnet ef` workflow; run focused tests and `dotnet test EVMWhaleTracker.slnx`. Validate normal/debug Compose and exporter with synthetic inputs, never printing secrets. Do not repeat unrelated live tests merely for ceremony.
10. A later explicitly authorized rollout uses a temporary, consenting/public watch address without submitting transactions. Observe actual pushed activity and Telegram output, test reconnect/restart and opt-out, inspect bounded recovery/idle usage, remove temporary settings, and record real evidence. A subscription acknowledgment alone is not end-to-end success. Do not spend an extra address/connection budget unknowingly while another process is active.

## Delivery workflow and future expansion

Coordinator prepares/reconciles research and QA, then delegates one coherent slice to `implementer-files` (GPT-6.1 Sol low), followed by a fresh `implementer-integration` (medium) and an independent `reviewer` (medium). QA planner uses Sol low; protocol researcher uses Sol medium. One repository writer at a time, no subagent delegation; follow the existing development workflow and capacity fallback.

After the first release, consider spot trading, HIP-3 market/quote handling, dedicated TWAP alerts, position/leverage views, Hyperliquid-only wallet management and larger address coverage. A provider replacement or self-operated indexer should plug into the source ports while preserving stored preferences and Telegram behavior. Additional sockets do not remove the public-IP wallet cap. Scaling infrastructure and third-party subscriptions are separate decisions, not part of this brief.


## Live rollout and Telegram validation — 2026-10-20

The user authorized local deployment and live testing through the signed-in Telegram browser, specifically @EVMWhaleTrackerBot. No mocks or isolated test suite were run during this rollout.

- Restored the stopped PostgreSQL container after the workstation interruption, stopped the restarting app, and took a private custom-format pg_dump backup outside the repository/build context. Built the production final image and recreated only the app, preserving existing EVM configuration. Startup applied AddHyperliquidDurableTracking. The local ignored .env now sets HYPERLIQUID_ENABLED=true; checked-in defaults remain false.
- Added one temporary public wallet selected from a public BTC recent-trades response through Telegram /add, named HYPERLIQUID LIVE TEST, opened its editor and enabled Hyperliquid using the actual callback. Only this temporary row had its unrelated EVM transfer flags disabled. Existing wallets were never enrolled in Core.
- Observed one admitted address with two acknowledged subscriptions and pong responses. Recovery completed at 14:27:02 UTC; actual BTC activity at 14:27:06 UTC and later appeared in Telegram. One verified message had size 0.00425 BTC, execution price 82996, approximate USD 352.733 and supplied realized PnL 0. Subsequent increase/reduce alerts also arrived. These later events were observed after HTTP recovery ended; the bot used the live WebSocket rather than recurring history requests. A screenshot of actual messages is retained privately with the session artifacts.
- Performed a planned app restart at approximately 14:27:47 UTC. Enrollment generation stayed unchanged. The worker opened one replacement socket, re-acknowledged two subscriptions, recovered the persisted interval and continued delivering fresh activity. Health reported 49 duplicate source records discarded after overlap; the database contained zero duplicated event/wallet/generation jobs. This verifies worker restart/recovery, not externally exactly-once Telegram delivery or a separately induced transport outage.
- Changed the temporary wallet minimum USD to 1000000000 through Telegram. Fourteen later actual fills were persisted with zero corresponding recipient jobs. Before cleanup, 50 eligible source-recipient jobs had reached Sent; this is a job count, not a count of distinct Telegram messages because related fills can aggregate. A bounded 20-second active-feed observation showed zero Core HTTP sends.
- Turned Hyperliquid Off through Telegram, confirmed Off, then removed only the temporary wallet through /remove. Final database checks showed the same three original wallet rows and the same full-row digest as before deployment, zero Core settings, zero reservations, zero pending deliveries, and 94 retained source events. A subsequent bounded 30-second log observation showed zero Core HTTP sends. The app remained running with zero automatic restarts and Hyperliquid enabled; zero enrolled wallets correctly means no Core wallet socket is needed.

Expected limitation: Telegram shows Degraded while live alerts work because ledger recovery completeness is not guaranteed. No natural deposit, withdrawal or liquidation was observed in this bounded trade test. Dedicated TWAP coverage, independent transport interruption, global disable/re-enable with pending work, and actual process-kill crash injection were not live-tested. No transactions or trades were submitted. Existing dependency audit warnings remain unchanged.
