# PostgreSQL restart recovery

Status: completed
Date: 2026-10-23
Summary: Restore the local bot after its database stopped, and align database/container restart policies.

## Problem and scope

The local PostgreSQL container was stopped with exit 255 and no restart policy. The app had an unless-stopped policy, retried startup 41 times and exited with code 139. Its logs show Npgsql DNS failures for postgres during startup migrations. Neither container was marked OOM-killed. The retained logs do not establish why PostgreSQL originally stopped.

Add restart: unless-stopped to PostgreSQL in Compose and apply that policy to the existing container. Preserve the database bind mount, wallet settings, secrets, Hyperliquid opt-ins and EVM configuration. Start PostgreSQL, wait for readiness, rebuild/recreate only the application, then inspect database integrity, worker health and a live Telegram command. No mocked or isolated test suite is needed for this configuration-only repair.

## Acceptance and limits

- PostgreSQL is healthy and both containers use unless-stopped.
- The deployed bot stays running without automatic restarts, loads existing wallets and responds to Telegram.
- Existing durable state is preserved; any Hyperliquid recovery gaps are reported honestly.
- Do not simulate a Docker/Desktop shutdown or kill the live database merely to test restart behavior. A restart policy applies while Docker is running; it cannot keep the bot online while the computer or Docker engine is off, and an explicitly stopped container may still need a manual start.

## Validation and outcome

PostgreSQL started with its existing data directory and completed automatic crash recovery, then reported healthy/accepting connections. Applied `docker update --restart unless-stopped` to the existing database container and added the equivalent Compose setting. Rebuilt the production app with `docker compose up -d --build --no-deps app` after database readiness; the build and deployment succeeded. Existing dependency audit warnings remain.

Normal and debug Compose configuration checks and `git diff --check` passed. Independent read-only configuration review found no actionable defects. No mock tests, isolated database tests, simulated shutdowns or live database kills were performed.

The recreated app and existing database remained running with zero automatic restarts during a bounded observation of more than one minute. Both inspect as `unless-stopped`. Logs confirm cache hydration of three wallets, Telegram receiver startup, subscriptions to all six configured EVM networks, and Hyperliquid subscription acknowledgments 2/2 with continuing pong responses. A live `/list` sent through the signed-in Telegram browser returned the three existing wallets; a private session screenshot records the response.

Before/after full-row digests matched for all three wallet rows and the one persisted Hyperliquid setting. No wallet preferences, enrollment generations or credentials were changed. Hyperliquid retains an age-exceeded gap and the known ledger-completeness warning. Restored live subscriptions do not prove all outage activity was recovered, and no new natural transaction was submitted or required for validation. The original reason PostgreSQL stopped is not established by retained logs; the missing database restart policy and resulting application startup failures are confirmed.
