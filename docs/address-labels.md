# Address Labels

Address labels are durable, reviewed names for counterparties shown in Telegram notifications.

## Import

Run the importer inside Docker Compose so it uses the same database connection, network, and environment as the app:

```powershell
$seedPath = (Resolve-Path .\data\address-labels).Path
docker compose run --rm --build -v "${seedPath}:/seed:ro" app import-address-labels /seed/address-labels.exchange-bridge.seed.json
```

This starts the Compose PostgreSQL dependency if needed, runs EF migrations through the app startup path, imports the mounted seed JSON, and exits without starting the long-running bot workers.

Use `--dry-run` to validate a file without writing:

```powershell
$seedPath = (Resolve-Path .\data\address-labels).Path
docker compose run --rm --build -v "${seedPath}:/seed:ro" app import-address-labels /seed/address-labels.exchange-bridge.seed.json --dry-run
```

Reviewed seeds import by default. Candidate files generated from raw explorer dumps are rejected unless you pass `--allow-candidate`:

```powershell
$seedPath = (Resolve-Path .\data\address-labels).Path
docker compose run --rm --build -v "${seedPath}:/seed:ro" app import-address-labels /seed/generated/address-name-tags.seed.candidate.json --dry-run --allow-candidate
```

Verify imported row count directly in the Compose database:

```powershell
docker compose exec -T postgres psql -U postgres -d EvmWhaleTracker -c 'SELECT COUNT(*) AS address_label_count FROM "AddressLabels";'
```

## Resolution Flow

Notification rendering resolves display names in this order:

1. Chain-specific DB/cache label.
2. Global DB/cache label.
3. ENS reverse lookup for Ethereum addresses only.
4. Shortened address fallback.

Tracked wallet user aliases still have the highest priority for the tracked wallet title.

When a user runs `/add <address>` without a name, the Telegram command handler tries ENS reverse lookup for that address. If an ENS name is found, it is saved as the tracked wallet alias immediately. If ENS misses or fails, the bot falls back to asking the user to provide a name.

## ENS Fallback

ENS reverse lookup uses Ethereum mainnet RPC through `eth_call`; it does not use Etherscan or an ENS-specific API key. Configure an Ethereum HTTP RPC URL in `BlockchainNetworks` and keep `EnsReverseLookup:Enabled` set to `true`.

Docker Compose settings:

```env
BLOCKCHAIN_3_CHAIN=Ethereum
BLOCKCHAIN_3_HTTP_RPC_URL=https://mainnet.infura.io/v3/
BLOCKCHAIN_RPC_API_KEY=your-provider-key
ENS_REVERSE_LOOKUP_ENABLED=true
ENS_REVERSE_LOOKUP_TIMEOUT=00:00:02
```

Ethereum tracking workers do not need to be enabled for ENS fallback; the resolver only needs the Ethereum HTTP RPC URL. Successful ENS names are cached for 7 days, misses for 1 day, and transient failures for 10 minutes.

## JSON Format

```json
{
  "schemaVersion": 1,
  "reviewStatus": "reviewed",
  "scopePolicy": {
    "exchange": "global",
    "bridge": "chainScoped",
    "dexRouter": "chainScoped",
    "protocol": "chainScoped",
    "treasury": "chainScoped"
  },
  "entries": [
    {
      "address": "0xb38e8c17e38363af6ebdcb3dae12e0243582891d",
      "chain": null,
      "nameTag": "Binance Hot Wallet",
      "kind": "Exchange",
      "source": "arbiscan",
      "sourceReference": "https://arbiscan.io/address/0xb38e8c17e38363af6ebdcb3dae12e0243582891d",
      "priority": 100,
      "isEnabled": true
    }
  ]
}
```

Use `reviewStatus: "reviewed"` for files that are safe to import into the normal database. Use `reviewStatus: "candidate"` for generated review material; the runtime importer requires `--allow-candidate` for those files.

Use `chain: null` for global EOA labels such as exchange wallets. Use a supported chain name, such as `Ethereum`, `Bsc`, `Base`, or `Arbitrum`, for chain-specific infrastructure like bridge contracts. The importer rejects chain-scoped exchange entries and unscoped bridge/router/protocol/treasury entries. The current reviewed scraped nameTag seed intentionally includes only exchanges and bridges.

Prefer curated Dune exports and official protocol deployment documentation. Explorer labels are useful review hints, but the runtime path should not scrape explorer pages.

## Importing Dune CEX exports

Convert the raw arrays offline using the [converter instructions](../tools/address-label-importer/README.md#dune-cex-exports), review the seed, then use the existing `import-address-labels` command. `source: "dune"` persists as `AddressLabelSource.Dune`; absent, explorer or other source strings retain the existing `SeedFile` behavior. Matching `(address, chain)` rows are updated with the incoming label, source, reference, priority and enabled state regardless of the previous priority. Unmatched rows and tracked-wallet aliases are untouched; imports never delete labels.

For an explicitly authoritative global exchange dataset, add `--prefer-global-exchanges` to both the dry-run and applied import. This optional flag disables existing enabled chain-scoped labels at the same addresses as incoming enabled global exchange entries. It retains their IDs, labels, kinds, source metadata and priorities. This lets the global exchange name resolve on those chains. It does not affect other addresses or already disabled rows. Without the flag, chain-specific labels continue to win. The report lists `Disabled chain overrides` separately from `Updated`; dry-run predicts both without changing the database/cache.

Use a reviewed import image compatible with the running database. The general `--build` examples above build the current checkout and run startup migrations; do not use them to accidentally deploy unrelated pending schema or feature work. Back up the database before an authoritative import and compare unmatched rows afterward. An import refreshes its own process cache; an already running bot needs its normal cache hydration/restart to observe the changes.
