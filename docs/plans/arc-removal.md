# Arc removal

Status: completed
Date: 2026-10-25
Summary: Remove Arc from active tracking at the user's request; preserve Robinhood and existing wallet settings.

## Requirements and implementation

- Remove Arc metadata, pricing mapping, appsettings slot 5, Compose bindings, environment example and exporter slot. Retain slots 0-4 unchanged.
- Exclude retired Arc from runtime configuration resolution and Telegram callbacks; no Arc listener can be registered through the resolver, including stale secret overrides.
- Reserve enum value 5042 solely for compatibility with numeric persisted JSON and stale configuration binding. Never reuse it. Saved explicit lists are not rewritten or filtered into empty lists; Arc-only lists remain restricted and receive no active-chain EVM alerts until changed by the user. Empty lists keep their existing all-runtime-chains meaning.
- Compare chain sets rather than counts when collapsing a selection to the all-chain convention, so a list containing a retired value cannot accidentally become unrestricted. Exclude Arc when materializing the active domain chain set.
- Preserve unrelated settings, user ownership, caches, bounded channels, cancellation, token WebSocket detection and the disabled native scanning policy. No EF schema change or migration is required.
- Adapt existing active-network test cases to supported networks; retain saved-value compatibility cases. Do not execute the test suite or add mock validation, per user preference.

## Validation and limits

- `dotnet build EVMWhaleTracker.slnx --no-restore -m:1 -p:UseSharedCompilation=false --verbosity minimal` passed with zero errors and five existing dependency advisory warnings (Pkcs and SQLitePCLRaw).
- `docker compose --env-file .env.example config --quiet` passed.
- `docker compose --env-file .env.example -f docker-compose.yml -f docker-compose.debug.yml config --quiet` passed.
- Test suite deliberately not run per user preference.

Fresh integration found no runtime defects. Independent review identified an outdated all-chain test fixture; it was corrected to exclude the retired value, and the reviewer verified resolution with no remaining actionable findings.

The coordinator removed local `BLOCKCHAIN_5_*` environment entries and deployed with `docker compose up -d --build --no-deps app`. Release publication succeeded. Startup logs confirmed exactly five active EVM subscriptions (BSC, Base, Arbitrum, Ethereum and Robinhood), no Arc subscription, and the existing Hyperliquid connection. The app is running and PostgreSQL is healthy. All three original wallet rows and the Hyperliquid setting matched predeployment row digests exactly. No migration or live wallet mutation was needed. No real Telegram messages were sent for this change. Historical Arc diagnostics remain in the original rollout plan; they are no longer an open product blocker. Polymarket changes are outside scope.
