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

## Documentation

- AI/project context: `docs/ai/context.md`
- AI documentation conventions: `docs/ai/conventions.md`
- Tracking pipeline architecture: `docs/architecture/tracking-pipeline.md`
- Architecture decisions: `docs/decisions/`
- Implementation plans: `docs/plans/`

## Tracking Mode

The running MVP currently tracks ERC-20/BEP-20 token transfers through a WebSocket log subscription. The app subscribes to `Transfer(address,address,uint256)` logs where a tracked wallet appears in the indexed `from` or `to` topic, so quiet wallets do not cause per-block RPC requests.

Native chain transfers, such as BNB or ETH transfers, are not enabled in the current runtime configuration. The codebase still contains the native block-scanning path through `NethereumBlockchainBlockSource` and `BlockchainIndexerWorker`, so native transfer tracking can be re-enabled later if needed. That mode listens for new blocks, fetches block transactions, and matches tracked wallets against transaction `from` / `to` addresses.

### RPC debugging

When you want to inspect Infura load, set:

```env
BLOCKCHAIN_ENABLE_RPC_DIAGNOSTICS=true
```

With diagnostics enabled, native block scanning logs each block's transaction count and load time when that path is enabled.
