# Robinhood and Arc Implementation Plan

Status: completed
Date: 2026-10-19
Summary: Add Robinhood Chain and Arc mainnet ERC-20 transfer tracking through the existing Infura WebSocket pipeline.

## Subsequent removal (2026-10-25)

The user requested Arc removal. Arc's active source/configuration support has been removed; value 5042 remains only as a persisted-settings compatibility tombstone. The provider-delivery issue below is historical and no longer an open rollout blocker. See [Arc removal](arc-removal.md). The remainder of this document preserves the original implementation and live evidence.

## Scope

This is a small EVM network extension. Reuse the current subscriber, matching, aggregation, and Telegram delivery workers. Infura remains the RPC provider. Native block scanning remains disabled; HTTP is used for on-demand token metadata, not recurring transfer discovery.

Solana is out of the current expansion scope. Hyperliquid/HyperCore and further HyperEVM work are deferred for later exploration. Starknet, Sui, and TON are excluded. Existing chain definitions and stored numeric identifiers are preserved; removing catalog entries is not part of this change.

## Verified Network Details

The signed-in Infura dashboard was inspected on 2026-10-19. The selected key's Active Endpoints page lists both mainnets with HTTPS and WebSocket endpoints. Both networks are already active there; no dashboard configuration change was made or is presently required. Endpoint visibility establishes availability in the account, not a successful RPC/subscription test.

| Setting | Robinhood Chain | Arc |
| --- | --- | --- |
| Enum member | `Robinhood = 4663` | `Arc = 5042` |
| Display / short name | Robinhood Chain / RH | Arc / Arc |
| Native currency metadata | ETH, 18 decimals | USDC, 18 decimals |
| Explorer base URL | `https://robinhoodchain.blockscout.com` | `https://explorer.arc.io` |
| HTTP base URL | `https://robinhood-mainnet.infura.io/v3/` | `https://arc-mainnet.infura.io/v3/` |
| WebSocket base URL | `wss://robinhood-mainnet.infura.io/ws/v3/` | `wss://arc-mainnet.infura.io/ws/v3/` |
| Configuration index | 4 | 5 |

Chain IDs and explorers are supported by [Robinhood's network documentation](https://docs.robinhood.com/chain/connecting/) and [Arc's network documentation](https://docs.arc.io/arc/references/connect-to-arc). Infura URLs above come from the live account dashboard; API keys are intentionally omitted. Mainnet and testnet must not be confused. No testnet enum entries are needed for this slice.

## Implementation

1. **Add chain identity and metadata.** Extend `src/Domain/Common/SupportedChain.cs` and `src/Infrastructure/Blockchain/StaticChainMetadataProvider.cs`. Keep every existing enum number unchanged. Use readable, distinct RH and Arc labels in menus and notifications. Keep generic ERC-20 metadata resolution; token decimals come from token metadata, never from the chain's native currency decimals. Arc's native USDC metadata does not establish the behavior or decimals of an ERC-20 interface.
2. **Wire configuration end to end.** Append entries at indices 4/5 in `src/Presentation.Telegram/appsettings.json`, `docker-compose.yml`, and `.env.example`. Extend `scripts/Export-DockerEnvFromUserSecrets.ps1` to emit indices 0 through 5, including the currently omitted index 3 for Ethereum. Preserve existing indices 0 BSC, 1 Base, 2 Arbitrum, 3 Ethereum and the shared `BLOCKCHAIN_RPC_API_KEY`. Keep new checked-in entries disabled until the validation/rollout step enables them through local configuration. Store only key-free base URLs in tracked files.
3. **Reuse current tracking and Telegram flows.** `Program.cs` already registers one `TokenTransferLogSubscriberWorker` per enabled network. Reuse its two directional, address-filtered `Transfer` subscriptions and the existing shared pipeline. Verify new networks appear in settings only when runtime-enabled, callbacks parse their numeric IDs, and notifications generate correct explorer links. Modify workers or menu logic only if a focused test exposes a new-network compatibility issue.
4. **Handle price coverage honestly.** Inspect `src/Infrastructure/Pricing/AssetPriceOptions.cs` and the existing DexScreener response shape. Add token-pricing chain mappings only after confirming the provider's identifiers/coverage for the new networks. Do not invent chain slugs, assume every stablecoin is worth exactly $1, or add another pricing provider in this slice. If pricing is unavailable, preserve current behavior: send the token amount without USD and bypass the USD threshold, as the existing implementation does. Record any missing coverage. No native pricing feature is required while native tracking is disabled.
5. **Validate and roll out.** Run the focused checks below, then enable one new network at a time in local configuration. Keep the same application process, existing queues, caches, persistence, and rate limits. Update README/runtime context and mark this plan completed only after the implemented scope and actual validation results are recorded.

No database schema migration is expected: enabled chains are stored as numeric JSON and the new values are additive. Confirm against the EF model before implementation; generate any unexpectedly required migration using the documented EF tooling.

## Wallet Compatibility

- Existing `EnabledChains = []` means all runtime-enabled networks. Those wallets will also track Robinhood and Arc once the networks are enabled. This is the existing policy, not a backfill of historical activity.
- Explicit saved lists keep exactly their existing membership. A list that previously represented all-but-one network must not be rewritten as unrestricted or silently gain the new chains.
- Settings persistence, cache refresh, and disabling the final permitted chain retain their existing behavior. Do not change all-chain semantics in this feature.
- Chain identity must remain part of token metadata, pricing, notification aggregation, and label lookup. The same token address on two networks must not share metadata or prices accidentally.

## Acceptance and Validation

- Both networks resolve from configuration with the existing shared key convention; disabled networks remain absent from runtime menus. Existing network configuration continues to resolve.
- Focused tests cover enum/config values, chain labels/explorer links, new callback IDs, settings round-trips, and unrestricted versus explicit wallet chain lists.
- Feed representative incoming/outgoing/self-transfer logs for each new network through the existing matching and notification path. Confirm direction, amounts/decimals, chain restrictions, aggregation isolation, and missing-price behavior. Keep test scope limited to the network addition.
- Validate the exporter using synthetic secret inputs and verify all six slots are represented. Do not print real secrets or run it in a way that overwrites the user's `.env` merely to test formatting. Validate Compose interpolation without emitting resolved credentials.
- Run `dotnet test EVMWhaleTracker.slnx`. Record actual failures or environment limits instead of claiming an unexecuted check passed.
- During implementation, use a bounded read-only live check: `eth_chainId` must return 4663/5042 and both filtered `eth_subscribe` requests must be accepted. Unsubscribe after the check. No broad log subscriptions, block-header streams, historical scans, or transaction submission.
- Verify a representative pushed event when an authorized watched address naturally has activity. Synthetic tests cover behavior if no suitable live event arrives; subscription acceptance and synthetic tests must not be reported as a fully observed live transfer.
- Confirm quiet wallets do not trigger recurring `eth_getLogs`, block fetching, or receipt fetching. Observe a fixed short idle period and inspect request categories/credit usage, allowing startup, subscription and metadata costs. Reconnects must retain the existing bounded delay and must not fall back to HTTP scanning.

## Development Handoff

Use the configured `implementer-files` (GPT-6.1 Sol, low), then a fresh `implementer-integration` (GPT-6.1 Sol, medium), followed by an independent `reviewer` (GPT-6.1 Sol, medium). Give each this plan and repository conventions. Keep one writer at a time. The user subsequently authorized implementation. Code/configuration, synthetic tests and bounded live subscription validation are complete. The subsequently authorized local deployment and live test results are recorded below.

## Implementation Results (2026-10-19)

- Added chain identities/metadata and disabled configuration slots 4/5; existing numeric identifiers and slots remain intact. Exporter now includes Ethereum slot 3 and both new chains.
- Reused current subscriber, matcher, menus, callbacks, and notification renderer without runtime worker changes. EF stores numeric chain lists as JSON, so no schema migration is needed.
- Coordinator verified DexScreener API search responses report `chainId` values `robinhood` and `arc` on 2026-10-19; token mappings added. References: [API reference](https://docs.dexscreener.com/api/reference), [Robinhood market page](https://dexscreener.com/robinhood), [Arc market page](https://dexscreener.com/arc). Native pricing is unchanged. Token coverage is not guaranteed; unavailable prices preserve amount-only notifications and bypass USD thresholds.
- Added synthetic configuration/key, menu, callback, persistence, incoming/outgoing/self-transfer, chain-restriction, explorer, aggregation, and pricing-isolation checks. No deployment or real Telegram messages are part of this implementation pass.

- Coordinator live probe: Robinhood `eth_chainId` returned 4663 and Arc returned 5042. Each accepted both exact Transfer-topic incoming/outgoing address filters, idled for five seconds, and returned true for both unsubscriptions. This verifies live subscription acceptance, not a real pushed transfer or the whole bot idle-credit audit.
- Synthetic exporter validation passed: all six slots, Ethereum enabled override, and disabled defaults for slots 4/5. `docker compose --env-file .env.example config --quiet` passed without exposing resolved secrets.
- Coordinator ran `dotnet test EVMWhaleTracker.slnx --no-restore -m:1 -p:UseSharedCompilation=false --verbosity minimal` outside the sandbox after sandbox testhost attempts stalled: all 116 tests passed, zero failed/skipped. This includes the fresh integration pass's historical explicit-list persistence regressions. The existing SQLitePCLRaw NU1903 warning remains.
- Implementation-pass limits are superseded by the live rollout evidence below. Checked-in defaults remain disabled; no recurring polling was added.


## Live Rollout (2026-10-19)

- User explicitly authorized deployment and live Telegram testing. Built and started the local Docker app and PostgreSQL stack with the existing persistent database and credentials. Both new chains are enabled only in local `.env`; all four previously enabled chains remain enabled. This is a local Docker deployment, not a remote-server rollout.
- Normal Compose now explicitly targets the production `final` image stage; the debug override still targets `debug`. `.dockerignore` excludes dotenv secrets and `.docker/` database/scratch files. Rebuilt the final image successfully and verified `/vsdbg` is absent. Independent configuration review found no actionable issues; normal/debug Compose validation passed.
- Telegram live checks passed: `/list`, temporary wallet creation, edit deep link, RH/Arc menu labels and toggles, saved chain membership, rejection of clearing every network, deletion confirmation, and command-based removal. Original three database rows were byte-for-byte unchanged using a pre/post digest. All four temporary test entries were removed.
- Real Robinhood WebSocket events reached Telegram with incoming/outgoing token amounts, USD prices, aggregation and correct explorer links. Cross-checked transaction `0x515715ffc96bc50b7551b181e7da52bc1fd8c80be9cb00fa478488c3020a5b6e`: receipt status successful, WETH raw amount `99000000000000000` matches the displayed `0.099` for both mint-to-watched-address and outgoing transfer. Live screenshots are local ignored artifacts under `artifacts/live-validation/`.
- Restarted the deployed app after cleanup. PostgreSQL remained healthy, the cache rehydrated three original wallets, and all six chains re-established two directional filters. `/list` responded after restart, and an existing Ethereum wallet also delivered a natural transfer notification. This verifies process restart recovery, not a separately induced WebSocket disconnect.

### Arc delivery blocker

Arc HTTP and WebSocket chain IDs both identify mainnet 5042. The bot and independent raw .NET ClientWebSocket probes accepted exact-address subscriptions but received no events, including both OR-array and scalar-topic forms. In the final synchronized comparison, the recipient subscription was acknowledged before recording the start block. During blocks `0x1985866` through `0x1985893` (strictly after that start), the raw socket delivered **0** events while a single HTTP request using the identical recipient filter returned **111** events. Emitters included 41 ERC-20 USDC interface events from `0x3600000000000000000000000000000000000000`, 51 native system events, and 19 other token events. This is stronger evidence than an idle subscription test and is not explained solely by native system logs.

The discrepancy points to provider delivery/filtering/routing; the exact cause remains unconfirmed. [Infura documents Arc WebSocket support](https://docs.infura.io/concepts/websockets/) and its [Arc reference](https://docs.infura.io/reference/arc/) publishes the endpoint. No alternative provider or HTTP polling fallback was introduced. Arc remains configured and enabled locally, but its live notification delivery must not be represented as working until a pushed event is observed. No blockchain transactions were submitted and no funds were spent.

[Arc indexing documentation](https://docs.arc.io/integrate/infrastructure/indexing-events) distinguishes native USDC system logs (18 decimals) from its ERC-20 interface (6 decimals). Native system-log support is outside this token-only extension; generic ERC-20 metadata resolution remains unchanged.

### Idle usage observation

After cleanup/restart, compared all four pages of Infura's one-hour method counters over 82 seconds, starting at 10:49:06 UTC. `eth_getLogs`, `eth_getBlockByNumber`, `eth_getTransactionReceipt`, and `eth_blockNumber` totals did not increase. Only two subscriptions and one connection per chain appeared, consistent with delayed restart accounting. Manual diagnostics account for the block/log/receipt methods already present. This supports absence of recurring scanning during this short window; dashboard aggregation delay and existing-wallet activity prevent treating it as a precise credit-cost benchmark or long-term guarantee.

The dashboard also reported six Robinhood `logs` entries in the aggregated 400-status category during the test period. Its breakdown did not identify exact status codes or causes. Real RH delivery succeeded, but the dashboard was not uniformly error-free; this requires more provider detail before attributing the failures.

### Validation limits

The earlier 116 passing tests remain the code-validation baseline; this rollout changed deployment configuration and documentation only. Docker restore also reported existing `System.Security.Cryptography.Pkcs` advisories. No dependency upgrade was attempted. Full live delivery on Arc, exactly-once behavior across reconnects, and a deliberately interrupted WebSocket recovery test remain unverified. Diagnostic HTTP block/receipt/log queries were bounded manual checks, separate from the bot runtime; they must not be confused with recurring application polling.
