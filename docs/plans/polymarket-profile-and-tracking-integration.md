# Polymarket Profile Resolution and Wallet Tracking

Status: completed
Date: 2026-10-23
Summary: Add public Polymarket profile suggestions and independently enabled wallet alerts for executed buys, sells and redemptions, with explicit trading-wallet mapping and durable delivery.

Tracking follow-up (2026-10-25): [the completed Polygon settlement brief and live rollout](polymarket-polygon-tracking-implementation-brief.md) supersede this document's activity-polling design and historical blocked/pending statements. The deployed profile/settings work remains in place; supported chain tracking is implemented and live-tested within the follow-up's explicit coverage and validation limits.

## Goal and scope

The user requested a plan covering both Polymarket name resolution and tracking alongside the existing Hyperliquid integration. Implementation explicitly authorized by the user on 2026-10-24. Deployment/live mutation remains a separate coordinated validation step. Existing EVM and Hyperliquid behavior, wallet records and custom aliases must remain intact.

First release:

- Resolve a public Polymarket profile from an EVM address and offer its public name as a wallet alias suggestion.
- Show the associated Polymarket profile separately from the owner's chosen alias.
- Enable Polymarket independently for each tracked-wallet entry.
- Alert on executed BUY and SELL activity and REDEEM activity, including automatic redemptions represented by the provider.
- Render compact alerts with market/outcome details and verified profile, market and transaction links.
- Recover supported history after interruptions, deduplicate source events, persist pending deliveries, and display delayed or incomplete coverage honestly.

Excluded initially: trading/copy trading, signatures or private keys, unfilled orders/cancellations, portfolio/PnL dashboards, combo markets, split/merge/conversion alerts, rewards/rebates, deposits/withdrawals, arbitrary username search, and changes to EVM native scanning. Account funding is not inferred from an ordinary trade. A sell is not automatically labeled a position close or realized profit.

## Verified API basis and implementation gate

Official documentation inspected on 2026-10-23:

- [Gamma public profile](https://docs.polymarket.com/api-reference/profiles/get-public-profile-by-wallet-address): `GET /public-profile?address=...` accepts a user or proxy wallet address and returns nullable `name`, `pseudonym`, `proxyWallet` and `displayUsernamePublic` fields. A missing profile is a normal lookup outcome.
- [Data API v2 activity](https://docs.polymarket.com/api-reference/feeds/list-account-activity): `GET /v2/activity` is user-anchored, has cursor pagination, timestamp bounds, BUY/SELL filtering and multiple activity kinds. The documented maximum page size is 1,000. Follow `next_cursor`; do not reuse legacy offset paging. Deposit/withdrawal records require a separate flag and are outside this release.
- [Public Data API reference](https://data-api.polymarket.com/v2/docs): public data routes require no account credentials; wallet endpoints use the trading/proxy wallet. Missing numeric values are unavailable, not zero.
- [Authenticated account stream](https://docs.polymarket.com/trading/realtime-order-updates): follows the authenticated account; it is not evidence of arbitrary public-wallet subscriptions.
- [Public real-time data](https://docs.polymarket.com/market-data/realtime-data): market-data streaming must not be assumed to identify every participant wallet or provide a complete wallet history.
- [Rate limits](https://docs.polymarket.com/api-reference/rate-limits): recheck applicable endpoint and shared-IP budgets before selecting production concurrency and capacity.

Before implementation, verify live public responses for a named profile, a missing profile, an active trader, and redemption history. Confirm current wallet types/mapping, exact activity schema, collateral/value units, maker and taker inclusion, pagination behavior, historical retention, indexing delay, and stable event identity. Record sanitized evidence and source links. Documentation alone does not prove working end-to-end ingestion.

Resolve these technical gates before committing to delivery guarantees:

1. Determine an event key that preserves distinct executions/redemption outcomes while deduplicating replay. Transaction hash and timestamp alone are insufficient. If the API lacks a stable execution/log identifier, demonstrate whether a canonical composite is unambiguous; otherwise select a richer public source or narrow the promised coverage explicitly. Never quietly collapse indistinguishable legitimate events.
2. Verify that user activity includes both maker and taker executions. An endpoint's taker-only default must not silently omit maker activity.
3. Verify how recent indexing corrections and late-arriving records appear. A completed historical query is not proof that all records have already been indexed.
4. Verify real profile and market URL shapes against public pages and transaction links against the actual settlement network. Do not carry Hypurrscan links into Polymarket alerts.

If a required contract fails verification, record the blocker in this plan before implementation proceeds with an alternative. Do not add paid providers, credentials or a new RPC service without a concrete need and the appropriate authorization.

## Alias and identity behavior

### Suggested names

Preserve explicit aliases as authoritative. For `/add <address>` without an alias, retain the existing ENS behavior; when ENS does not resolve, offer a public Polymarket display name, then a clearly identified pseudonym fallback, then the existing manual-name flow. Let the owner edit the suggestion. A Polymarket timeout must not prevent adding the wallet.

Store provider identity separately from owner-selected `TrackedWallet.Alias`. Include the resolved address, trading-wallet address, profile name/pseudonym, lookup time and provenance. A later profile rename updates provider information without silently renaming the owner's wallet. Respect `displayUsernamePublic`; do not present a nonpublic name as public. Escape provider text and aliases for Telegram HTML and apply length/control-character limits.

Use bounded positive and negative caching with stale-on-error behavior: proposed successful lookup TTL 24 hours, missing-profile TTL 1 hour, and request timeout 5 seconds. Make these configurable. Avoid a profile request per trade or per notification; deduplicate concurrent requests. Failed lookup and confirmed absence are distinct states.

Wallet details should show `Your alias` and `Polymarket profile` separately, with an explicit action to use the current public name. Existing aliases are not bulk-rewritten. Do not introduce globally shared user aliases or alter unrelated counterparty-label logic.

### Trading-wallet mapping

Keep the original tracked address and its EVM/Hyperliquid subscriptions unchanged. Persist the Polymarket source address separately. When a profile maps the entered address to a different trading wallet, show both addresses and explain which account Polymarket tracking will follow before the owner enables it.

Accept an actual trading-wallet address directly when verified. Do not guess a proxy by transfer relationships or assume every externally owned address has a Polymarket account. If mapping cannot be verified, show a clear unresolved status and allow the owner to supply the profile's trading-wallet address through an owner-checked flow.

A changed provider mapping must not silently redirect an active subscription. Pause or mark the binding as needing attention and require an explicit rebind with a new activation generation. Multiple original addresses may map to one source: share ingestion, but prevent accidental duplicate enrollment for the same owner/source or explain the conflict through `Result`.

## Tracking experience and notification policy

Add a separate `Polymarket` action in the wallet editor, outside the EVM network checklist, mirroring Hyperliquid's independent opt-in. Global and per-wallet defaults are off. Empty EVM network selections never enable Polymarket.

The editor exposes On/Off, mapped account, coverage status, and a Polymarket-specific minimum trade value. Propose a default minimum of zero for the new source, shown before enablement. Keep it separate from the existing EVM/Hyperliquid minimum so small reductions are not unexpectedly hidden by another source's threshold.

- BUY/SELL: apply inclusive `>=` to each provider execution's documented cash/notional value before presentation grouping. Shares are not dollars. Display quote currency honestly; use approximate USD only when its conversion basis is established.
- REDEEM: report supplied payout and outcome, including explicit zero payout when present. Redemption alerts bypass the trade minimum so zero-payout or small settlements remain visible. Do not label a payout as profit.
- Missing value: do not invent zero; deliver with available facts and indicate unavailable value, consistent with the bot's handling of unknown valuation.
- First release sends individual activities. Later aggregation must retain source IDs and avoid grouping different outcomes, markets, owners or action types.
- Future-only activation: new followers do not receive historical floods. Restart recovery applies to already active subscriptions within the supported recovery window.

Illustrative layout (fictional values):

> Main Wallet | Polymarket | ✏️
> 🟢 Bought YES · 100 shares
> Will the event occur?
> 42¢ per share · ~$42
> Market · Transaction

Use the owner's alias linked to the verified public profile or account page, the existing owner-checked pencil navigation, the market title linked to its market, and a transaction link only for a valid supplied hash/network. If links cannot be verified, render plain text or a verified broader account link. Missing titles/outcome labels fall back to safe identifiers without claiming YES/NO. Arbitrary provider URLs are not passed through unchecked.

## Architecture and ingestion

Preserve Clean Architecture and `Result` business failures:

- Domain: source binding/settings, activation generation, durable source activity and recipient delivery state as appropriate.
- Application: public profile resolver, Polymarket activity provider/store/settings ports and typed notification contracts. Keep formatting in Presentation.
- Infrastructure: bounded HTTP clients, cache, normalized protocol parser, PostgreSQL persistence, polling/recovery worker and delivery worker.
- Presentation.Telegram: alias suggestions, profile display/rebind flow, editor actions, compact renderer and sender.

Relevant existing seams: `TelegramWalletCommandProcessor.HandleAddAsync`, `AddTrackedWalletCommandHandler`, alias update commands, `AppDbContext`, and `Program.cs`. Reuse `TelegramGlobalRateLimiter` and `TelegramNotificationSendRetryPolicy`. Follow Hyperliquid's durable inbox/outbox and generation patterns, but do not make Polymarket depend on Hyperliquid-specific contracts or copy its ten-address provider limit.

Polymarket is a protocol source, not a fabricated EVM chain. Polygon already exists in `SupportedChain`; standard ERC-20 Transfer parsing cannot represent complete prediction-market activity. This release adds no Polygon RPC ingestion and does not require enabling Polygon transfer tracking.

### Proposed public ingestion transport

Start with bounded recurring Data API v2 polling per distinct enabled source address. This is an explicit Polymarket-specific design decision; it does not relax EVM's no-per-block-log-polling rule or Hyperliquid's recovery-only HTTP rule. Do not promise WebSocket latency.

Proposed starting interval: 10 seconds per distinct address, subject to the verified rate budget, measured indexing delay and configured capacity. Share polling across followers, use a deployment-wide budget and bounded concurrency, jitter schedules, honor retry/backoff, and keep hot wallets from starving quiet ones. Budget all pages, retries, profile/metadata calls and recovery work, not just first-page requests. Reject excess enrollment clearly if capacity cannot meet the declared service interval.

Use PostgreSQL ingestion ownership with a dedicated advisory-lock namespace so overlapping app instances do not independently double the workload. Settings changes wake scheduling promptly; inactive sources stop polling. Telegram failures must not block source acquisition.

Polling cadence, provider indexing delay and Telegram backlog all contribute to end-to-end delay. Report measured delay during rollout rather than an unsupported latency promise. A future verified public wallet stream may replace the adapter while retaining the same durable contracts.

## Persistence, recovery and isolation

Persist bindings/settings, activation time/generation, source identities and normalized activities, per-source scan boundaries, retained gap intervals, and per-recipient delivery state. Durable events and eligible deliveries commit before advancing recovery state. In-memory queues only schedule persisted work.

- Validate timestamps/units and keep exact decimal quantities. Exclude unsupported event kinds and combos explicitly; record counts without presenting them as covered.
- Poll a sliding overlap to accommodate late indexing, with periodic deeper bounded reconciliation. Proposed initial windows: 5-minute recent overlap and 24-hour recovery horizon, subject to live evidence; neither is a completeness guarantee.
- Page to exhaustion within a fixed scan window. Retain boundary identities; do not advance by adding a timestamp unit or truncate a full page. Cursor expiry/stall, budget exhaustion and retention overruns produce a visible gap and resumable recovery state.
- Do not use the latest activity time as proof of an exhaustive scan. Track successful scan time and covered query interval separately from latest event time. Empty results alone do not prove permanent completeness.
- Commit activation boundaries consistently with initial history scanning; catch activity arriving during setup. A new owner joining an already polled source gets only post-activation activity.
- Recheck owner, binding, generation and current settings before fan-out and immediately before Telegram send. Opt-out/removal cancels unsent deliveries only for that owner. A rebind cannot send old-account work to the new generation.
- Failures for one wallet/chat do not halt other sources or users. Alias and threshold resolution is recipient-specific. Owner-check every callback and deep link.
- Prune bounded data only after considering pending deliveries and dedup/recovery horizons. Keep identity retention longer than the maximum replay range; never prune pending work solely because it is old.
- If the source supports revisions to an already observed record, define revision handling during the provider gate rather than silently resending or discarding changes.

Telegram acceptance followed by a crash before success persistence can cause a repeated alert. Do not promise external exactly-once delivery. Retained gaps are not cleared merely by a later successful request.

## Operations and rollout

Add separately validated `Polymarket` configuration for enablement, public endpoints, timeouts, poll interval, capacity, HTTP budget/concurrency, page/response limits, overlap/reconciliation/recovery windows, retention and cache TTLs. Integrate appsettings, normal/debug Compose and any secrets-export configuration without replacing existing local settings. No trading credentials are needed.

Health: Off, Resolving, Starting, Active, Recovering, Delayed/Degraded, or Needs attention, with readable explanations. Track last successful scan, latest activity time, lag, throttling, gaps, duplicate counts and delivery backlog. A quiet wallet is not an unhealthy wallet. Do not expose another owner's aliases, chat IDs or settings in status/logs.

Global disable stops Polymarket network acquisition and pending sends while preserving state. Re-enable performs bounded recovery and reports uncovered downtime. Existing EVM/Hyperliquid workers remain independent. Startup migrations must be compatible with rollback; disabling the feature is the first rollback option, with no destructive database downgrade.

## Implementation sequence

1. **Provider proof and bounded brief:** resolve API gates, settle source event identity and rate/capacity limits, capture sanitized public examples, and update this plan with measured constraints.
2. **Profiles and identity:** add the resolver/cache, profile metadata and owner-safe alias suggestions, plus explicit source binding and rename/rebind behavior. Generate EF migrations using the established tooling if persistence changes.
3. **Tracking slice:** settings/editor, shared source scheduling, durable normalization/checkpoints/inbox/outbox, threshold policy, cancellation and recovery.
4. **Presentation and operations:** compact links/rendering, common Telegram transport limits, configuration, health, retention, documentation and rollback.
5. **Fresh integration and independent review:** follow `docs/ai/development-workflow.md`; one repository writer at a time, named implementation roles, fresh integration context and a separate reviewer. Resolve findings before rollout.
6. **Live rollout:** deploy the reviewed default-off build, enable a dedicated public test-wallet enrollment, observe natural activity, verify delivery and cleanup, then report the exact coverage demonstrated.

## Validation and acceptance

The user's preference is live Telegram evidence rather than isolated mocked testing. Use a solution build, configuration validation, static review, read-only live public API checks, and controlled Telegram enrollment as primary evidence. Do not require the user or agent to place trades. If generated migrations invoke the repository's test requirement, reconcile it explicitly with the user's testing instruction before execution; never claim unexecuted tests passed.

Acceptance checklist:

- Explicit/custom aliases and all original tracked-wallet settings survive deployment unchanged; ENS fallback continues working.
- Named, unnamed, missing, private-name and temporarily unavailable profiles all leave `/add` usable. Cached provider renames do not overwrite aliases.
- Entered and mapped addresses are visible; direct trading-wallet enrollment works; unresolved/changed mappings do not silently track another account.
- Real maker and taker BUY/SELL records are represented correctly, with distinct same-transaction executions preserved and repeated scans deduplicated.
- Natural redemption records display payout without invented profit; unsupported/combo activity is not mislabeled.
- Threshold boundaries and missing values follow the documented policy; excluded activities can be distinguished from ingestion failures operationally.
- Restart recovery, overlap and multi-page scans preserve all verified eligible events within the supported window; gaps remain explicit where completeness cannot be established.
- New enrollment avoids old-history floods; opt-out/removal/rebind prevents queued delivery under the wrong generation. Multiple followers share source acquisition while retaining separate aliases and filters.
- A real public activity can be matched from provider record to persisted inbox/delivery and the actual Telegram message, with working links and HTML-safe text.
- EVM and Hyperliquid subscriptions and delivery continue; original wallet/settings baselines match after test cleanup.

Run live validation using a separate, clearly labeled temporary enrollment, never by altering original wallet preferences. Retain only sanitized evidence in documentation. Observe natural activity and record unavailable scenarios as unverified; historical API examples prove parsing facts, not live delivery. Restart checks must account for the shared bot's temporary interruption; no destructive production-database failure injection.

## Completion record to fill during implementation

Keep this plan `proposed` until work begins. Record chosen APIs/limits, event identity proof, actual commands, review outcomes, deployed configuration, live Telegram evidence, cleanup, measured latency and outstanding coverage limits. Mark completed only after the agreed scope is implemented and its validation limits are documented.


## 2026-10-24 implementation gate and bounded implementation

The user explicitly authorized implementation on 2026-10-24 and requested live evidence instead of mocked tests. The provider proof stage failed before tracking implementation: trusted local HTTPS requests to both Gamma and the Data API failed certificate hostname validation; diagnostic evidence supplied by the read-only researcher showed an ISP restriction page rather than JSON. Runtime TLS validation remains intact and redirects are disabled. No regional restriction bypass is implemented.

The official [v2 OpenAPI schema](https://data-api.polymarket.com/v2/openapi.json) documents `data` plus cursor pagination, inclusive second-based `start`/`end`, `proxy_wallet`, `timestamp`, `condition_id`, `type`, `size`, `usdc_size`, `transaction_hash`, `price`, `token_id`, `side`, `outcome_index`, and optional `is_combo`. It exposes no execution ID, log index or sequence ID. An internal timestamp/sequence ordering statement does not provide that identity to consumers. Maker coverage for account activity and correction behavior remain unproven. The separate trades route documents `taker_only=false`, but cannot resolve execution identity and tiny-trade coverage for this release. These are blockers, not coverage guarantees.

Coordinator direction: implement the independent profile/binding/settings/presentation slice while keeping activity strictly fail-closed. No fabricated composite IDs, polling worker, ingestion or activity sends are enabled. `PolymarketCapabilities.ActivityVerified` is false and Turn On returns a visible Result failure. A configuration change cannot bypass this guard. Durable activity/checkpoint/delivery schema and typed contracts are reserved for the verified follow-up; they do not establish implemented acquisition/recovery/outbox processing.

Implemented scope: default-off bounded public profile resolver, positive/negative cache with stale-on-error, ENS-first editable alias suggestions, separately persisted public profile and confirmed source binding, explicit owner-checked rebind and name-use actions, independent default-zero USDC minimum, mapping-change attention state, safe renderer/shared transport seam, configuration/export/Compose wiring and generated schema migration. Existing aliases, EVM networks and Hyperliquid settings are untouched. Profile/network validation and live Telegram delivery remain unverified due to the gate. The plan remains in-progress; complete tracking and live rollout are outstanding.

Fresh integration corrected missing-profile invalidation, manual-account refresh identity, stale confirmation generations, concurrent rebind handling, owner/state refresh under the settings lock, threshold prompt generations, HTTP read failures, request budgeting and configuration arithmetic bounds.

Executed checks: `dotnet build EVMWhaleTracker.slnx --no-restore -m:1` passed with zero errors and five existing dependency advisory warnings; generated `AddPolymarketProfileBinding` with EF tooling and generated its SQL without applying it; `dotnet ef migrations has-pending-model-changes --no-build` reported no pending changes. Normal/debug Compose configuration, exporter PowerShell syntax and whitespace checks passed. No mocked tests, deployment, live database mutation or Telegram messages were performed. The running deployment remains unchanged. Independent review outcome is recorded after review completes.

Independent review found two defects: malformed non-null provider identities could invalidate a valid mapping, and a delayed initial lookup could overwrite a binding created during that request. Both were fixed, the solution rebuilt successfully, and the reviewer independently verified resolution with no remaining actionable findings within the partial scope. Full tracking, trusted live profile responses and end-to-end Telegram validation remain outstanding; this is not a completed implementation of the full plan.

## Authorized live profile rollout (2026-10-24)

After the user enabled a desktop VPN and requested deployment/testing, trusted HTTPS succeeded from Windows and Docker. Gamma resolved a real public profile, Data API v2 status returned Polygon chain 137 with serving-lag metadata, and an activity query returned real rows with cursor pagination. The sampled activity schema still exposes no stable execution identifier; source completeness/deduplication design remains outstanding. The earlier local network-access blocker is resolved while the VPN is active.

Started Docker Desktop, saved a private PostgreSQL backup outside the build context, set only local `POLYMARKET_ENABLED=true`, and executed `docker compose up -d --build app`. Release publication and migration `20261023214750_AddPolymarketProfileBinding` succeeded. Checked-in enablement remains false. The deployed app has zero automatic restarts; the six existing EVM log subscriptions and Hyperliquid's two acknowledged subscriptions reconnected. Hyperliquid retains its pre-existing history-incompleteness warning.

Live Telegram validation used one temporary public wallet: `/add` produced the actual public-name suggestion; a custom alias was retained; explicit source confirmation persisted the correct account with tracking off; the independent minimum changed to 12.34 USDC while EVM minimum stayed zero; Use public name changed only the temporary alias; Turn On displayed the expected unavailable-tracking alert. Removed the temporary entry through Telegram and verified its settings were deleted. Original three-wallet and Hyperliquid-settings row digests matched the predeployment baselines exactly. No trades were placed, historical alerts replayed, or mocked tests executed.

This rollout validates deployed profile/name and wallet controls only. There is still no Polymarket activity acquisition/recovery worker and no live buy/sell/redemption alert coverage. The full plan remains in-progress.
