# Runtime configuration and credentials

Ordinary runtime settings live in `src/Presentation.Telegram/appsettings.json`. All five supported EVM networks (BSC, Base, Arbitrum, Ethereum and Robinhood) are enabled there. Polygon is the separate Polymarket adapter. Hyperliquid and Polymarket defaults remain off; preserve local enablement during migration. Native block scanning remains unregistered.

Configuration precedence, lowest to highest: appsettings.json, appsettings.{Environment}.json, optional appsettings.Local.json, credential-only User Secrets, environment variables, command-line arguments. Public/non-secret User Secrets are ignored, including obsolete billing switches and chain enablement. Development logging applies only in Development. Both Compose environment names agree.

Use ignored `src/Presentation.Telegram/appsettings.Local.json` for deployment settings, including private operator IDs, support contact, terms/callback URLs, feature flags and limits. Colon-delimited JSON keys such as `"Billing:OperatorIds": "12345"` and `"BlockchainNetworks:Networks:1:Enabled": "true"` work as native configuration paths. Network indices are stable: 0 BSC, 1 Base, 2 Arbitrum, 3 Ethereum, 4 Robinhood. Preserve complete identities when changing arrays; configuration providers merge array indices.

Compose mounts that file read-only in Production and Development. The application allows it to be absent outside Docker; Compose deliberately refuses a missing source instead of creating a directory. For a new installation create it with `'{}' | Set-Content src/Presentation.Telegram/appsettings.Local.json` before starting. For an existing deployment migrate first. The file is excluded from Git, the Docker build context, output copying and publishing.

## Migrate an existing deployment

Capture Docker inspect privately before replacing the old dotenv. Store snapshots and preserved credentials below ignored `artifacts/live-validation/`; never print them, use `docker compose config` on real secrets in terminal output, or copy them into documentation. Compare wallet/protocol state before and after rollout separately.

Run from the repository root, using the private captured app inspect file:

```powershell
.\scripts\Convert-DockerConfiguration.ps1 -SnapshotPath artifacts/live-validation/app-inspect.json -PreservedSecretsPath artifacts/live-validation/preserved-configuration-secrets.json -EnableAllEvmChains
.\scripts\Export-DockerEnvFromUserSecrets.ps1 -PreservedSecretsPath artifacts/live-validation/preserved-configuration-secrets.json
```

The converter accepts a Docker inspect array/object or raw Config.Env string array. It preserves effective flags, limits, public endpoints and operator IDs in local JSON, and credentials in a separate private file. Unknown/reordered network identities fail for manual reconciliation. Empty URL/credential placeholders are omitted so configured fallbacks survive. Retired NOWPayments dashboard fields and Google credentials are discarded. Billing sales/enforcement values are preserved from the running snapshot, never stale User Secrets.

The exporter reads the configured UserSecretsId (or an explicit fixture `-SecretsPath`). It emits canonical .NET environment names, for example `TelegramBot__BotToken`, `Billing__NowPaymentsApiKey`, `Billing__NowPaymentsIpnSecret`, `BlockchainNetworks__RpcApiKey`, `BlockchainNetworks__Networks__0__RpcApiKey` and `PolymarketPolygon__RpcApiKey`. Per-network overrides never become the shared key. Credential-bearing endpoint overrides remain in dotenv; known public Infura base URLs stay in settings. Custom RPC endpoints are treated conservatively as confidential, including tokens in paths. Query/userinfo-bearing URLs are confidential too.

Captured database connection and endpoint overrides win over stale User Secrets. New token/API/IPN key values override captured equivalents. A conflicting database password fails before replacing dotenv; reconcile it explicitly instead of rotating an existing PostgreSQL volume accidentally. Development connection host/port differences are allowed while the captured Docker connection remains authoritative. POSTGRES_PASSWORD is derived from that exact captured connection. The database volume must be retained; changing an environment variable does not rotate an initialized PostgreSQL password.

The private preserved map retains public endpoint values too, so a stale credential-bearing User Secrets URL cannot replace a captured public endpoint. Public endpoints remain in local settings and are omitted from dotenv.

Dotenv double quoting preserves dollars, hashes, spaces, apostrophes, quotes and backslashes. Every emitted value rejects CR/LF/NUL before replacing an existing file. No placeholders are emitted for missing optional keys, and no settings/credentials are printed. Do not put ordinary switches in dotenv: env_file passes canonical keys through directly and explicit environment overrides intentionally remain possible.

After review, recreate only the app with `docker compose up -d --build --no-deps app`. Do not recreate/reset PostgreSQL for this settings refactor. Check health, five EVM subscriptions, Hyperliquid/Polymarket startup, unchanged billing flags and the saved wallet/protocol baseline. Startup verification does not establish natural-event or payment acceptance.

## Fixture validation

`scripts/Test-ConfigurationSeparation.ps1` uses synthetic snapshots/credentials under ignored artifacts and resolves normal/debug Compose without reading the real dotenv. Focused .NET tests cover Production/Development precedence, optional local settings, ignored nonsecrets, array keys and shared/per-network RPC fallback. Existing dependency audit warnings are separate from these checks.
