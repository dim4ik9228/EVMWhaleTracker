# EVMWhaleTracker

## Local Docker Setup

If you want to run the app through `docker compose up --build`, the container cannot read your host user secrets directly.

Use the helper script to generate a local `.env` file from the `Presentation.Telegram` user secrets:

```powershell
.\scripts\Export-DockerEnvFromUserSecrets.ps1
```

Then start the stack:

```powershell
docker compose up --build
```

The generated `.env` is ignored by git and is used only for local development.

Postgres data is stored in `.docker/postgres-data/`, so the database survives container recreation locally.

RPC URLs are configured as API-key-free base URLs. Put the key in user secrets or `.env` separately:

```env
BLOCKCHAIN_RPC_API_KEY=your-provider-key
BLOCKCHAIN_0_HTTP_RPC_URL=https://bsc-mainnet.infura.io/v3/
BLOCKCHAIN_0_WEB_SOCKET_RPC_URL=wss://bsc-mainnet.infura.io/ws/v3/
```

The app appends `BLOCKCHAIN_RPC_API_KEY` at startup for every configured Infura network. For providers that need the key in the middle of the URL, use `{ApiKey}` or `{RpcApiKey}` in the URL.

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
- Address labels and seed import: `docs/address-labels.md`
- Tracking pipeline architecture: `docs/architecture/tracking-pipeline.md`
- Architecture decisions: `docs/decisions/`
- Implementation plans: `docs/plans/`

## Tracking Mode

The running MVP currently tracks ERC-20/BEP-20 token transfers through a WebSocket log subscription. The app subscribes to `Transfer(address,address,uint256)` logs where a tracked wallet appears in the indexed `from` or `to` topic, so quiet wallets do not cause per-block RPC requests.

Native chain transfers, such as BNB or ETH transfers, are not enabled in the current runtime configuration. The codebase still contains the native block-scanning path through `NethereumBlockchainBlockSource` and `BlockchainIndexerWorker`, so native transfer tracking can be re-enabled later if needed. That mode listens for new blocks, fetches block transactions, and matches tracked wallets against transaction `from` / `to` addresses.

### RPC debugging

When you want to inspect Infura load, set:

```env
BLOCKCHAIN_0_ENABLE_RPC_DIAGNOSTICS=true
```

With diagnostics enabled, native block scanning logs each block's transaction count and load time when that path is enabled.
