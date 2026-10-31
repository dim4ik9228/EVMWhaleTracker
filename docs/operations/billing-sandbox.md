# Isolated NOWPayments sandbox run

This runbook is for the explicitly authorized temporary reuse of the existing bot. Sandbox is default off. The sandbox provider is unmaintained; no real funds or Stars may be spent. Normal runtime and sandbox must never receive updates concurrently: database advisory locks do not coordinate across these databases.

## Prepare without stopping normal runtime

1. Privately record the normal image ID, containers, configuration hashes, tunnel route, wallet/protocol/financial row baseline, receiver checkpoint and absence of outstanding financial attempts. Retain the original image and all original volumes/configuration. Store evidence under ignored `artifacts/live-validation/`.
2. Run focused tests and `scripts/Test-BillingSandboxConfiguration.ps1`.
3. Copy `config/billing-sandbox.example.json` to `src/Presentation.Telegram/appsettings.BillingSandbox.json`. Supply the explicitly selected operator ID, current HTTPS callback and `/billing/sandbox/terms` URL. Leave tracking, enforcement and Stars off. Keep `SandboxInitialTelegramOffset` zero until the handover boundary is captured; startup deliberately fails at zero.
4. Run `scripts/Export-BillingSandboxEnv.ps1` against User Secrets containing `TelegramBot:BotToken`, `BillingSandbox:NowPaymentsApiKey` and `BillingSandbox:NowPaymentsIpnSecret`. It maps only those three credentials and generates a new isolated DB password into ignored `.env.billing-sandbox`. It never changes `.env`, imports production merchant keys or copies the production connection string. A second export refuses to overwrite the password; preserve it for retained sandbox DB data. `-SecretsPath` supports synthetic fixtures.
5. Build the separate app image, then start only `billing-sandbox-db` with the dedicated Compose file and `--env-file .env.billing-sandbox`. It has a separate named volume and no host DB port. Inspect configuration without exposing expanded secrets (`docker compose ... config --quiet`). The app binds loopback 8081. The `BillingSandbox` environment skips normal Local settings and User Secrets entirely; only its sandbox JSON and explicit environment/command-line overrides are loaded. Startup requires this environment and sandbox mode together, before migration. Use the same Compose project/network as the tunnel when container routing is required.

   ```powershell
   docker compose --env-file .env.billing-sandbox -f docker-compose.billing-sandbox.yml build billing-sandbox-app
   docker compose --env-file .env.billing-sandbox -f docker-compose.billing-sandbox.yml up -d billing-sandbox-db
   ```

## Exclusive receiver handover

1. Stop only the normal app receiver after preparation. Capture its latest committed `BillingCheckpoint` key `TelegramUpdates` and put that numeric next-update offset into sandbox JSON. Do not edit the normal checkpoint. Record start time/downtime. Normal financial updates must not be discarded; abort if the baseline no longer supports an isolated no-payment run.
2. A fresh sandbox database initializes its checkpoint from `SandboxInitialTelegramOffset`; an existing sandbox database preserves its own durable checkpoint. Do not silently reuse an old boundary. No production rows are copied. Start `billing-sandbox-app` with the dedicated Compose file. Verify startup and exact DB name `EvmWhaleTrackerBillingSandbox`.
3. Temporarily route the existing tunnel to `billing-sandbox-app:8080` (or loopback8081 for a host tunnel). Allow exactly POST `/billing/nowpayments/ipn` and GET `/billing/sandbox/terms`; keep public health/other routes blocked. Preserve the saved normal tunnel configuration for restoration.
4. Send `/sandboxsubscription` privately as the operator. All views, checkout messages and outbox notices carry `SANDBOX / NO REAL PAYMENT`. Ordinary `/subscription`, admin and wallet commands are consumed by sandbox. Sandbox callbacks carry `sbx:`; normal runtime consumes this namespace and `/sandbox*` commands, preventing later replay into purchases or pending wallet input. Use only test commands during the run.

Sandbox deliberately skips Stars accounting and transaction-history reconciliation. If any fetched batch contains a real successful-payment/refund update, the receiver rejects the entire batch before dispatch or checkpoint advancement and retries at the same offset. The financial event remains pending at Telegram. Abort the test on the named pending-payment diagnostic, stop sandbox, and restore normal runtime so its receiver can durably account for the event. Preserve observed evidence privately when available; normal transaction history/operator recovery remains available if needed. The zero-outstanding-financial-attempt baseline and disabled normal sales remain mandatory. Do not fabricate a replacement event or silently adjust the production checkpoint.

## Provider cases (15-minute budget)

Open Pro → terms → agree → crypto for one hosted invoice per case. Before opening a URL, verify HTTPS host exactly `sandbox.nowpayments.io`; the adapter suppresses all other sandbox checkout URLs. Never send funds. Copy the order ID from isolated ledger/private recovery evidence; `/sandboxrecovery` lists unresolved identities. Invoke:

```text
/sandboxsimulate <order-id> success
/sandboxsimulate <order-id> failed
/sandboxsimulate <order-id> partially_paid
```

Each submits the documented `/invoice-payment` with the real invoice ID and case. It does not fabricate an IPN or grant. All grants still require ordinary API-key status verification of invoice/order/currency/network, frozen quote and paid amount. Capture actual provider payment/invoice IDs and signed callback evidence privately. Repeated status/IPN processing must grant at most once. Failed/partial cases must grant none. For delayed known-ID recovery, use `/sandboximport <payment-id> <audit-reason>` and, only when genuine binding evidence is needed, `/sandboxattach` with the existing billing attachment syntax. Those controls are private/operator-only and run normal verification.

The coordinator's 2026-10-29 provider probe observed `case=success` reach `finished` while `actually_paid=0` for a positive `pay_amount`. This incompatible evidence must produce review/no grant; report the positive-grant scenario blocked if reproduced. Do not substitute `outcome_amount` or manufacture a paid amount. Sandbox success cannot establish production settlement/payout. Refund simulation, expiry and Stars are outside this bounded run unless separately documented and authorized.

The completed live run reproduced that limitation and received genuine signed callbacks for all three cases. Invoice creation also requires accepting string-valued `estimated_amount`; the adapter handles numeric and string amounts. The ordinary one-hour unresolved-order guard remains active in sandbox. For independent cases, stop its receiver, save its final checkpoint, rename the existing sandbox database to a retained scenario-specific name, and create a new empty `EvmWhaleTrackerBillingSandbox` database in the dedicated sandbox PostgreSQL service. Set the new initial offset from the stopped sandbox receiver, then restart it. Preserve every scenario database and audit; never alter the production database, erase financial rows or reset ambiguous attempts to force retries. Later callbacks for an archived scenario may reach the current empty test database and remain unmatched review evidence; correlate by actual order/payment identity.

## Restore even after failure

Stop sandbox app first; retain its database/volume/audit. Restore the exact saved tunnel route/configuration and normal app image/configuration, then start the normal receiver. Do not rewrite or drain the normal financial checkpoint. Test namespace updates are consumed safely; legitimate normal payment updates retain the normal financial handling path. Confirm only one receiver is running, all five EVM subscriptions restored, normal wallet/protocol/financial rows match the baseline, sales/enforcement remain false and original tunnel callback routing is healthy. Record end time/downtime and precise passed/failed/blocked/not-run cases privately. Stop sandbox DB if no longer needed; do not delete either volume.
