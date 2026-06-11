# AI Project Context

This file gives future AI-assisted sessions a short, durable project snapshot. Keep it current when major architecture or product decisions change.

## Product

EVMWhaleTracker is a .NET 10 Telegram bot for tracking on-chain wallet activity on EVM-compatible chains. The current MVP is focused on ERC-20/BEP-20 token transfer notifications for tracked wallets.

## Current Runtime Shape

- PostgreSQL stores user and tracked-wallet configuration.
- `TrackedWalletCacheHydrationService` loads tracked wallets into an in-memory cache.
- `TokenTransferLogSubscriberWorker` opens websocket log subscriptions for ERC-20 `Transfer` events where tracked wallets appear in indexed `from` or `to` topics.
- `WalletTrackerWorker` consumes pipeline messages, applies tracked-wallet settings, resolves token metadata and USD value, and writes structured notification payloads.
- `NotificationDispatcherWorker` sends Telegram messages with rate limiting.

Native transfer tracking is currently disabled at runtime. The native block-scanning code remains available through `NethereumBlockchainBlockSource` and `BlockchainIndexerWorker`, but those services are not registered in the active host configuration.

## Important Constraints

- Do not reintroduce per-transaction receipt fetching for token detection.
- Do not poll `eth_getLogs` per block for quiet wallets.
- Prefer websocket log subscriptions for token transfers.
- Keep caching in-memory for the MVP; do not add Redis.
- Preserve the Domain/Application/Infrastructure/Presentation layering.
- Expected business-flow failures should use the `Result` pattern instead of exceptions.

## Current Tradeoffs

- Token tracking is cheap and event-driven because ERC-20 transfers emit indexed logs.
- Native transfer tracking requires block scanning with standard EVM RPC, so it is disabled for now to reduce API usage.
- Native tracking can be re-enabled later if API budget or product requirements justify it.
