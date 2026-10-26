# EVMWhaleTracker

## Local Docker Setup

If you want to run the app through `docker compose up --build`, the container cannot read your host user secrets directly.

Use the helper script to generate a local `.env` file from the `Presentation.Telegram` user secrets:

```powershell
.\scripts\Export-DockerEnvFromUserSecrets.ps1
```

Then start the stack:

```powershell
docker compose up -d --build
```

The generated `.env` is ignored by git and is used only for local development.

Postgres data is stored in `.docker/postgres-data/`, so the database survives container recreation locally.

Both services use `restart: unless-stopped`. Start the whole stack so Compose waits for PostgreSQL health before starting the bot. Docker must be running; after an explicit stop, run the command above again. See the [database restart recovery record](docs/plans/postgres-restart-recovery.md) for the 2026-10-23 outage and recovery limits.

RPC URLs are configured as API-key-free base URLs. Put the key in user secrets or `.env` separately:

```env
BLOCKCHAIN_RPC_API_KEY=your-provider-key
BLOCKCHAIN_0_HTTP_RPC_URL=https://bsc-mainnet.infura.io/v3/
BLOCKCHAIN_0_WEB_SOCKET_RPC_URL=wss://bsc-mainnet.infura.io/ws/v3/
```

The app appends `BLOCKCHAIN_RPC_API_KEY` at startup for every configured Infura network. For providers that need the key in the middle of the URL, use `{ApiKey}` or `{RpcApiKey}` in the URL.

## Robinhood Chain

Robinhood Chain (4663, RH) uses the existing Infura WebSocket subscriber. Configuration slot 4 is disabled by default; slots 0-3 remain BSC, Base, Arbitrum, and Ethereum. The exporter emits these five slots and preserves their secret overrides. Local RH rollout delivered real Telegram transfer alerts.

Arc support was removed on 2026-10-25 at the user's request. There are no Arc endpoints, subscriptions, menu actions, or pricing entries. Numeric value 5042 remains reserved solely for saved-setting compatibility: an explicit saved Arc-only list stays restricted and tracks no active EVM network until the user selects one. Other saved settings are preserved. See [the removal record](docs/plans/arc-removal.md).

Wallets with an empty enabled-chain list include every runtime-enabled network; explicit saved lists retain their membership. DexScreener token pricing uses `robinhood`; tokens without a price still notify with their token amount and bypass USD thresholds. Native transfer scanning remains disabled. Normal Compose builds the production `final` stage; the debug override retains `debug`. Dotenv files and local database files are excluded from the Docker build context.

## Polymarket predictions

Profile/name lookup and Polygon prediction tracking are separate opt-ins. Enable `POLYMARKET_ENABLED=true` for public profiles and `POLYMARKET_POLYGON_ENABLED=true` for settlement ingestion/delivery. Both checked-in defaults are false. Polygon mainnet must be enabled in the Infura project; the adapter uses the existing shared RPC key unless `POLYMARKET_POLYGON_RPC_API_KEY` overrides it. Keep RPC base URLs key-free. Local API access may require the user's VPN/network route.

In a wallet's Polymarket editor, confirm the automatically resolved trading account, set the independent minimum gross collateral value, and turn tracking on. The original EVM address, other network settings and custom alias remain separate. Alerts show executed buys/sells and supported aggregate redemptions with cached market/outcome names and transaction links. Market/limit orders notify only when executed. Redemptions bypass the trade minimum; payout is not profit. Prices and gross amounts use the actual pUSD/USDC.e collateral units.

The adapter uses filtered Infura WebSocket logs and bounded reconnect history, not recurring activity polling. Default delivery waits for Polygon's finalized boundary. HTTP calls share pacing and throttle cooldown; API outages, recovery and Telegram backlog can delay alerts. Combos, perps, ambiguous generic routed redemptions and position-open/close interpretation are excluded. See the [implementation and live validation limits](docs/plans/polymarket-polygon-tracking-implementation-brief.md).

## Address Labels

Counterparty labels are stored in PostgreSQL and hydrated into memory at startup, so notification rendering does not query the database. Import the reviewed seed JSON with:

```powershell
$seedPath = (Resolve-Path .\data\address-labels).Path
docker compose run --rm --build -v "${seedPath}:/seed:ro" app import-address-labels /seed/address-labels.exchange-bridge.seed.json
```

On Ethereum cache misses, the bot can also use ENS reverse lookup through the configured Ethereum HTTP RPC endpoint. See `docs/address-labels.md` for details.

## Documentation

- AI/project context: `docs/ai/context.md`
- AI documentation conventions: `docs/ai/conventions.md`
- Development subagent workflow and model roles: `docs/ai/development-workflow.md`
- Address labels and seed import: `docs/address-labels.md`
- Tracking pipeline architecture: `docs/architecture/tracking-pipeline.md`
- Architecture decisions: `docs/decisions/`
- Implementation plans: `docs/plans/`
  - Native Hyperliquid WebSocket integration and validation limits: `docs/plans/hyperliquid-native-websocket-implementation-brief.md`
  - Arc removal: `docs/plans/arc-removal.md`
  - Historical Robinhood and Arc rollout: `docs/plans/robinhood-arc-implementation-plan.md`
  - Inline notification editing: `docs/plans/inline-notification-editing-implementation-plan.md`

## Tracking Mode

Hyperliquid/HyperCore has a separate default-off adapter. Set `HYPERLIQUID_ENABLED=true` on a planned restart and explicitly turn on Hyperliquid in an existing wallet's editor to enroll. It supports native USDC perpetual fills and deposit/withdrawal/liquidation ledger events; an EVM network selection never opts a wallet in. The deployment shares one public socket and reserves at most ten distinct addresses across all owners. Other clients on the same public IP also consume Hyperliquid's provider budget.

Core settings, source identities, recovery boundaries and pending Telegram deliveries are durable in PostgreSQL. Recovery defaults to 24 hours with bounded inclusive pagination; history limits produce visible degraded status. Ledger completeness is conservatively reported as incomplete. Disabling the global flag on restart stops Core ingestion/recovery/delivery and preserves settings for later recovery. Local live rollout on 2026-10-20 verified actual Telegram BTC alerts, planned restart recovery, minimum-USD filtering and opt-out cleanup. Local Core is enabled with no wallets automatically opted in. Live ledger events, TWAP execution coverage and externally exactly-once Telegram delivery remain unverified. See the implementation brief for the validation evidence and remaining rollout work.

The running MVP currently tracks ERC-20/BEP-20 token transfers through a WebSocket log subscription. The app subscribes to `Transfer(address,address,uint256)` logs where a tracked wallet appears in the indexed `from` or `to` topic, so quiet wallets do not cause per-block RPC requests.

Native chain transfers, such as BNB or ETH transfers, are not enabled in the current runtime configuration. The codebase still contains the native block-scanning path through `NethereumBlockchainBlockSource` and `BlockchainIndexerWorker`, so native transfer tracking can be re-enabled later if needed. That mode listens for new blocks, fetches block transactions, and matches tracked wallets against transaction `from` / `to` addresses.

### RPC debugging

When you want to inspect Infura load, set:

```env
BLOCKCHAIN_0_ENABLE_RPC_DIAGNOSTICS=true
```

With diagnostics enabled, native block scanning logs each block's transaction count and load time when that path is enabled.

Hyperliquid notifications use a linked wallet name, `HL` label and pencil editor link, compact trade details and Hypurrscan transaction links. Related fills show deduplicated numbered links; events without a valid source hash link to wallet activity. See [layout and live verification](docs/plans/hyperliquid-notification-layout.md).
