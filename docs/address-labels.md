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

Verify imported row count directly in the Compose database:

```powershell
docker compose exec -T postgres psql -U postgres -d EvmWhaleTracker -c 'SELECT COUNT(*) AS address_label_count FROM "AddressLabels";'
```

## JSON Format

```json
{
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

Use `chain: null` for global EOA labels such as exchange wallets. Use a supported chain name, such as `Ethereum`, `Bsc`, `Base`, or `Arbitrum`, for chain-specific infrastructure like bridge contracts. The current scraped nameTag seed intentionally includes only exchanges and bridges.

Prefer curated Dune exports and official protocol deployment documentation. Explorer labels are useful review hints, but the runtime path should not scrape explorer pages.
