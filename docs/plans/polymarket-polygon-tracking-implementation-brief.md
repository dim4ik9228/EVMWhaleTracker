# Polymarket Polygon Tracking Implementation Brief

Status: completed
Date: 2026-10-25
Summary: Complete prediction-market alerts using Polygon settlement events, automatic public account mapping, cached market metadata and durable Telegram delivery.

## Purpose and relationship to existing work

The user requested this brief after confirming the intended wallet-to-profile flow. The existing [profile and tracking plan](polymarket-profile-and-tracking-integration.md) retains the deployed profile/settings implementation and rollout history. This brief supersedes its proposed recurring Data API activity polling as the primary execution source. Preserve the existing alias resolver, account-binding UI, independent minimum and owner checks; complete the missing tracking workers instead of rebuilding profile support.

The user subsequently authorized implementation and the previously authorized local live rollout on 2026-10-25. Implementation, fresh integration, independent review and observed rollout evidence are tracked separately; configuration alone does not establish complete coverage.

## Verified account mapping and evidence

On 2026-10-25, trusted HTTPS through the user's enabled desktop VPN successfully resolved the saved main-wallet address through Gamma's public profile endpoint. The returned public name matched the saved alias and `proxyWallet` was a different valid EVM address. A subsequent Data API v2 activity query for that returned address produced three TRADE rows and a next-page cursor. No wallet deletion, re-enrollment, trading action, signature or private key was needed. Keep the real addresses and trading details out of this document.

Consequences:

- An entered EVM address can resolve automatically to the account that holds/trades Polymarket positions. Follow the returned address; preserve the entered address for the owner's other EVM/Hyperliquid tracking.
- The mapping is sufficient for public observation; it does not authenticate the Telegram user as the wallet owner. Tracking someone else's public account is supported.
- Existing explicit Confirm account / Rebind is a review step, not a requirement to discover a second address manually. Show the verified proposal automatically. A changed mapping must never redirect enabled tracking silently.
- Successful activity reads prove the account is queryable, not correct Polygon decoding, maker coverage, live push delivery or complete history.

References: [profile lookup](https://docs.polymarket.com/api-reference/profiles/get-public-profile-by-wallet-address), [activity](https://docs.polymarket.com/api-reference/feeds/list-account-activity), [trades](https://docs.polymarket.com/api-reference/feeds/list-trades).

## First-release behavior

Track public prediction-market settlement on Polygon (137):

1. Executed BUY and SELL activity for ordinary binary and supported negative-risk markets, with maker and taker coverage.
2. Redemptions, including automatically executed redemptions where the account beneficiary and payout are attributable.
3. Market question, outcome/selection, shares, price per share, cash value/currency, account/market/transaction links and source time.

Illustrative alert, not a claim about a currently available market or real trade:

> Main Wallet | Polymarket | ✏️
> 🟢 Bought YES — Democratic Party
> Which party will win the Senate in 2026?
> 100 shares at 66¢ each · ~$66 total
> Market · Transaction

Distinguish an event title from its constituent market question and outcome. Do not append YES to a selection solely because it is a named candidate or party. Use the verified token-to-market/outcome mapping. Price is per share; it is not the total order value or guaranteed profit. Display cents/$ only where collateral units and the USD estimate basis are established; otherwise display the actual quote currency. Redemption payout is not profit.

Initial labels are Bought, Sold and Redeemed. Opened/Increased/Reduced/Closed labels are optional follow-up work requiring a reliable balance baseline and complete intervening position movements. A first observed buy is not proof of a newly opened position. No unsupported leverage, entry price, PnL or liquidation claims.

Excluded: Polymarket perps, combos, unfilled/cancelled orders, copy trading, signatures/credentials, portfolio/PnL dashboards, standalone split/merge/conversion/transfer/funding/reward alerts, and market-resolution alerts. Relevant lifecycle/balance events may still be consumed internally to decode and attribute the supported actions without mislabeling excluded movements as trades. Perps require a separate backend/account/feed investigation; the Polygon prediction contracts do not establish perps coverage.

## Source architecture

Use a dedicated Polymarket Polygon adapter behind Application ports. Reuse the configured Infura service/key with separately configured Polygon HTTP/WSS base endpoints, validating `eth_chainId=137`. Do not require ordinary Polygon ERC-20 tracking to be enabled and do not add Polymarket to the generic EVM chain checklist. Do not introduce a new paid provider or expose secrets. Global Polymarket tracking off stops these source requests/subscriptions and delivery independently of other protocols.

Primary detection: address- and topic-filtered WebSocket log subscriptions against verified Polymarket exchange/position/module contracts. Use required indexed wallet roles for the confirmed trading account. Share source work for multiple followers, with a dedicated PostgreSQL ingestion-owner lock and bounded subscription capacity. Changes to wallet settings update the desired source set promptly. Filters and subscription acknowledgments must be verified on the actual provider; a connected socket alone is insufficient.

Enrichment: cache Gamma market/outcome metadata by contract system and token/position ID, and profile information by normalized address. Coalesce concurrent misses and use bounded positive/negative caches. Do not call metadata endpoints per fill. API unavailability may delay readable enrichment, but must not discard durable settlement events. After a bounded enrichment wait, send verified quantities with safe identifiers where meaning remains unambiguous; otherwise retain pending/degraded work rather than invent market/outcome labels.

The Data API can assist public-history comparison and optional bounded position baselines; it is not the primary deduplication ledger. Do not create a second alert from an API row representing a settlement already detected on-chain.

## Contract and decoding milestone

Implement against the deployed contracts, not a remembered ABI. Polymarket documents both CTF (`Gamma version=v1`) and the newer PositionManager system (`version=v2`) on Polygon. Recheck proxy addresses, implementations, ABI revisions, decimals and activation blocks before wiring subscriptions. The first decoding milestone must produce a small committed contract registry with source links, verified chain/address/event signatures and role/amount interpretation for each supported family.

Sources: [current contract registry](https://docs.polymarket.com/resources/contracts), [on-chain position data](https://docs.polymarket.com/resources/onchain-position-data). They are a starting point, not an executed provider check.

Primary-source research identified the following implementation starting points (verify against deployed bytecode/ABIs and bounded historical logs):

- Current CTF/Protocol V2 `OrderFilled(bytes32,address,address,uint8,uint256,uint256,uint256,uint256,bytes32,bytes32)` indexes orderHash, maker and taker. Historical CTF uses a different asset-ID signature; match by emitter and ABI activation range, not the event name alone. See [current CTF interface](https://github.com/Polymarket/ctf-exchange-v2/blob/main/src/exchange/interfaces/ITrading.sol) and [historical interface](https://github.com/Polymarket/ctf-exchange/blob/main/src/exchange/interfaces/ITrading.sol).
- In the documented current settlement implementations, the economic taker's own fill has `maker=takerOrder.maker` and `taker=exchange`. Prefer each followed account's own maker-topic fill as the canonical execution; counterparty-topic matches and `OrdersMatched` are context, not additional alerts. This covers economic makers and takers without naively double-counting them. Verify the rule for every enabled ABI generation. See [CTF trading implementation](https://raw.githubusercontent.com/Polymarket/ctf-exchange-v2/main/src/exchange/mixins/Trading.sol) and [Protocol V2 exchange](https://raw.githubusercontent.com/Polymarket/polymarket-v2-external/main/src/exchange/Exchange.sol).
- Protocol V2 `PositionRedeemed` indexes initiator, positionId and recipient; recipient is a payout-recipient filter, not proof of the prior position holder, while initiator may be a router. Correlate prior holder-to-module/router transfers, including cases where the holder differs from the payout recipient. CTF `PayoutRedemption` indexes redeemer, collateral and parent collection; conditionId is in data. Routed/automatic paths still need verified holder/payout correlation. See [BaseModule](https://raw.githubusercontent.com/Polymarket/polymarket-v2-external/main/src/modules/abstract/BaseModule.sol) and [Conditional Tokens](https://raw.githubusercontent.com/gnosis/conditional-tokens-contracts/master/contracts/ConditionalTokens.sol).
- Use a dedicated rich log envelope. The existing generic token subscriber loses log-index/removal information and has in-memory deduplication; reusing that envelope unchanged cannot satisfy this brief's identity/reorg requirements.

Requirements for that milestone:

- Identify canonical exchange fills for each participant and correct BUY/SELL direction; distinguish collateral from outcome assets and actual gross/net/fee quantities.
- Do not assume an exchange event's `taker` field always names the end user. Validate matching paths, exchange-as-counterparty cases and complementary-outcome matching against actual decoded transactions.
- Determine whether fill and summary/matched events duplicate the same economic execution. Select one canonical representation per account; never count both a per-order fill and a summary as separate trades. Self-trades require an explicit neutral/suppression policy, not an invented directional change.
- Attribute redemption holder, beneficiary, outcome token quantities and collateral payout from the supported contracts, including router/automatic paths and explicit holder-not-equal-to-payout-recipient examples. If a payout is only attributable in aggregate, report an aggregate redemption rather than assigning the whole amount to each outcome.
- Handle TransferSingle/TransferBatch and lifecycle context where necessary, but never infer a buy from an arbitrary incoming ERC-1155 transfer, mint, split or merge.
- Unknown contract versions, ambiguous matches, or unsupported combo positions produce explicit coverage gaps/counters, not fabricated trades.

Use read-only public historical transactions to exercise these cases. If an event cannot be wallet-filtered directly, propose the narrow contract/event correlation filter and request budget before implementing a global exchange firehose. Unsupported volume is a capacity/design problem to resolve, not grounds for silently dropping wallets. This milestone ends with concrete supported-path evidence and bounded implementation tasks; the activity API's absent execution ID is not itself a reason to abandon the Polygon source.

## Canonical identity, finality and recovery

Persist each raw log with chain ID, emitting contract, block number/hash, transaction hash/index, log index, topics/data and removal/canonical status. Derive recipient-independent activity IDs from canonical source logs plus participant/sub-action where a single log represents several outcomes. A transaction hash alone is insufficient. Store the contributing log identities and decoder version so correlation is reproducible.

Use durable inbox/outbox transactions before advancing coverage. The existing timestamp/cursor-only checkpoint schema needs a generated additive EF migration for block coverage, block hashes, filter/registry version and pending scan progress; do not pretend a timestamp is an exact block checkpoint. Reuse compatible existing tables/contracts where appropriate, without depending on Hyperliquid-specific entities.

- Subscribe and buffer/persist live overlap before bounded historical recovery. Query `eth_getLogs` only for filtered initialization/reconnect/recovery intervals, with adaptive bounded block ranges, page/request budgets and observable resume state.
- This explicitly permits Polymarket-specific bounded log recovery. It does not permit per-block quiet-wallet polling, full-block transaction scanning or per-transaction receipt fetching, and leaves native EVM scanning disabled.
- Define a configurable finality policy using supported safe/finalized heads or a verified confirmation depth. Show pending status only if deliberately supported; default notifications wait for the selected canonical boundary. Specify and measure the latency trade-off.
- Process removed logs and validate checkpoint block hashes on reconnect. Rewind to a retained common ancestor, invalidate orphaned normalized events and cancel unsent deliveries. If a previously sent alert is orphaned beyond the finality policy, record it and send a clearly linked correction under the normal limiter; never silently reuse its identity as a different trade.
- Bound recovery age/range/requests and retained block ancestry; retain dedup identities longer than the replay horizon. Gaps remain visible when limits are exceeded. Successful reconnection does not erase an uncovered interval.
- Track successful covered block range separately from latest activity time. Quiet accounts can have healthy coverage. One hot account must not starve the others.

## Wallet behavior and delivery

Reuse the deployed owner-bound settings and minimum. Enrollment and rebind capture an activation generation and precise activation boundary; a new follower receives future activity only, including while another user already follows the source. Do not accidentally exclude post-activation logs in the same block or replay pre-activation history. Document the enrollment boundary policy and validate it against source timestamps/block ordering.

- Original wallet address, custom alias, EVM chains, Hyperliquid preference and other owners' settings stay unchanged.
- BUY/SELL minimum is inclusive and evaluated against each canonical activity before any presentation aggregation. Shares are not dollars. Unknown valuation follows an explicit policy and never becomes invented zero. Redemptions bypass the trade minimum.
- Persist per-recipient deliveries with owner, wallet, source, activity and generation uniqueness. Recheck binding, owner, generation, opt-in, global flag and current threshold before enqueue/fan-out and immediately before each send attempt, including after limiter waits.
- Opt-out/removal/rebind cancels only that owner's obsolete unsent work. Different owners can use different aliases/minimums while sharing ingestion. Preserve same-owner duplicate-source prevention.
- Reuse shared Telegram global/per-chat limiter and retry policy. Blocked chats do not halt source processing or other recipients. Telegram accepted-send/crash ambiguity can cause a duplicate; do not promise external exactly-once delivery.
- Initially send individual canonical activities. Optional grouping later must retain IDs and never merge different owners, outcomes, markets, side or generations.
- Render HTML-safe, length-bounded provider/user text; profile header, market question, Polygon transaction and owner-checked edit links. Validate URL hosts and canonical slug/ID construction; no arbitrary provider URLs. Unknown metadata uses safe text and never guessed YES/NO.

## Configuration and health

Add tracking-specific configuration separate from already deployed profile enablement: default-off chain tracking, Polygon HTTP/WSS endpoints, registry version, supported contract families, finality, subscription/address capacity, queue/frame/response limits, recovery/log-range budgets, retention and enrichment deadlines. Keep endpoints/key handling consistent with Infura conventions; do not reuse the retired Arc slot or renumber other EVM settings.

Source health includes desired/acknowledged subscriptions, verified chain ID, latest head/covered canonical block, recovery lag/gaps, malformed/unsupported/correlated/duplicate events, enrichment backlog, recipient backlog and terminal failures. User status distinguishes Off, Resolving, Starting, Recovering, Active and Degraded with plain explanations. Unknown family support cannot display blanket complete coverage.

Replace the hardcoded capability refusal only when the adapter is implemented, registered and its supported paths have passed review and validation. Enabling a settings flag alone is not implementation. Allow independently functioning profile lookup while tracking is disabled or the Polygon connection is unhealthy.

## Implementation sequence and acceptance

1. Verify Polygon chain/provider filters and current ABI families; record canonical-role decoding from public historical transactions. Confirm rate/subscription/recovery constraints and choose a bounded capacity/finality policy.
2. Implement the chain contracts, registry, normalization, raw-log identities, additive generated migration and durable checkpoint/reorg handling.
3. Implement shared ingestion/recovery and enrichment scheduling, recipient outbox, owner/generation cancellation and settings/health integration.
4. Finish the compact renderer/links and feature-specific configuration. Preserve the deployed profile flow and other protocol workers.
5. Run a fresh `implementer-integration` pass and independent `reviewer` per the repository workflow. One writer at a time; preserve unrelated dirty work.
6. Build, validate Compose/exporter and migrations, then execute explicitly authorized live rollout with a temporary natural-activity public wallet. Preserve original records and remove temporary enrollment. User prefers live validation over mocked tests; document unexecuted scenarios honestly and follow the applicable user instruction when it overrides repository test guidance.

Acceptance must demonstrate: real maker and taker buys/sells; multiple executions in one transaction preserved without summary double counting; correct negative-risk interpretation; supported redemption attribution; correct token/share/price/collateral units; unsupported-family visibility; API enrichment outages retaining source work; restart/overlap dedup; reorg/orphan behavior to the extent actually exercised; owner isolation and stale-menu protection; opt-out/rebind during queued delivery; original EVM/Hyperliquid operation unchanged. Historical read-only examples establish decoding facts, not natural live alerts. Do not place trades or force financial actions to satisfy acceptance; record scenarios unavailable during the observation window as unverified.

The user's main account may verify automatic mapping read-only. Do not delete/re-add it or change its preferences as routine validation. Real alert screenshots and sanitized event-to-outbox evidence must distinguish observed behavior from planned or unexecuted checks.

## Deployment and rollback

Keep checked-in tracking defaults off. Before rollout, snapshot wallet/settings baselines and take a database backup outside the Docker build context. Deploy additive migrations, verify unaffected sources, then enable only the designated test enrollment. Measure settlement-to-Telegram latency including finality and enrichment; do not promise instant fills.

Rollback disables Polymarket chain acquisition/delivery without disabling profile lookup or other protocols. Preserve durable state, report intentional downtime as uncovered where applicable, and recover within configured bounds on re-enable. Do not downgrade/drop live tables. A local VPN/network route may be necessary for API enrichment; 24/7 availability still requires the host and Docker to remain running.

## Completion and live rollout (2026-10-25)

Implemented and deployed the bounded prediction-market adapter, additive migration `20261025181509_AddPolymarketPolygonSettlement`, cached enrichment and durable delivery. The [contract registry and evidence](polymarket-polygon-contract-registry.md) define supported current/historical fills and direct/official automatic aggregate redemptions. Ambiguous generic routed redemptions, combos, perps and interpreted position-open/close labels remain excluded. Automatic redemption source interpretation is implemented but not historically/live exercised; do not present that path as live-verified.

Fresh implementation and integration passes completed. Independent review identified key-bearing HTTP logging, canonical resurrection, effective-filter checkpoint identity and removal-versus-normalization races. These were fixed. Configured development/review agents then hit their usage limit; the coordinator completed fixes and an available fallback agent independently verified the fixes, pacing/cooldown, retention and status changes without remaining actionable findings in those follow-ups. Default finality uses the provider's verified finalized tag; a read-only probe observed a two-block head lag, which is not a Telegram latency guarantee.

Executed validation:

- Full solution and release container builds passed. Existing dependency advisory warnings remain. EF reported no pending model changes after generated migration; normal/debug Compose, exporter syntax/synthetic export and whitespace checks passed. Mocked tests/test suites were not executed per the user's live-validation preference.
- The user enabled Polygon access in the existing Infura project after HTTP 401; actual HTTP chain 137, deployed code, filtered WSS acknowledgments and bounded historical decoding were verified. No alternative provider or TLS bypass was used.
- A private database backup and original wallet/protocol preference baselines were captured before rollout. Local profile and Polygon flags are enabled, checked-in defaults remain off, and no original wallet was opted in automatically.
- A temporary active public account produced real Telegram prediction BUY alerts with question, outcome, shares, price, gross collateral, fee and working profile/market/Polygon transaction links. One independently compared execution matched the API and persisted raw log exactly: 18.5 Under shares at 0.78 pUSD/share, gross 14.43 pUSD, transaction `0xca35246d78930956f8cc54a3e3688d9b2b9ce76d1f14a6c31e5d6b1d8dabaaf9`. At least 56 Sent deliveries were observed before temporary enrollment cleanup; this is a lower bound, not a final message count.
- Initial live recovery encountered provider HTTP 429. Recovery no longer shrinks ranges on throttling: only explicit range/response-size errors do so. All Polygon HTTP calls share serialized pacing, heavy log queries impose at least one second before the next request, and Retry-After establishes a shared cooldown. RPC request logging is disabled and diagnostics omit endpoint credentials.
- A second temporary enrollment with a 1,000,000-collateral-unit minimum ingested natural activity without recipient deliveries. It reached Active with six acknowledged subscriptions, then a deliberate bot restart recovered to Active and advanced the checkpoint. A database check found no duplicate canonical normalized activity per raw log/account. No natural multi-fill-in-one-transaction or live reorg was demonstrated by this observation.
- Both temporary enrollments were removed. Original three-wallet rows, Hyperliquid settings and pre-existing Polymarket preference fields matched their predeployment digests exactly; no enabled test sources or queued deliveries remained. Other five EVM subscriptions and Hyperliquid reconnected; Arc stayed removed.

Live SELL/redemption delivery, automatic/routed holder-correlation edge cases, actual reorg/correction execution, multi-owner concurrency, and API outage behavior were not naturally exercised. Historical SELL/direct-redemption evidence and independent code review do not replace those live checks. End-to-end latency was not benchmarked; the first active account created a Telegram backlog and some alerts were delayed. No trades were placed and no historical records were presented as natural live alerts.
