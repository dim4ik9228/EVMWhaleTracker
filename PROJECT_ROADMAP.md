# EVMWhaleTracker Project Roadmap

This document describes the current state of the project and the remaining work needed to turn it into a working MVP Telegram bot for tracking EVM wallet activity.

The short version: the background processing pipeline exists, but the product-facing pieces are still missing. We can ingest blocks, match them against an in-memory address cache, and dispatch Telegram notifications. What we do not yet have is persistent tracked-wallet storage, Telegram commands, startup cache hydration, or deployment wiring.

Status: the end-to-end MVP flow is now implemented and validated on a real chain.

## Current State

### Implemented

The solution already has the core Clean Architecture projects:

- `Domain`
- `Application`
- `Infrastructure`
- `Presentation.Telegram`
- `test/UnitTests`

The `Domain` project contains:

- `TrackedWallet` aggregate root.
- `Address` value object with EIP-55 checksum handling.
- `Result` and `Result<T>` primitives for expected business failures.

The `Application` project contains:

- `RawBlockData`, `RawTransactionData`, `RawLogData`, and `NotificationPayload` message contracts.
- Strongly typed in-memory channels through `OnChainPipelineChannels`.
- Cache abstractions for tracked addresses and tracked wallets.

The `Infrastructure` project contains:

- `BlockchainIndexerWorker`.
- Nethereum-backed block source.
- `WalletTrackerWorker`.
- `InMemoryCacheService`.

The `Presentation.Telegram` project contains:

- Host registration in `Program.cs`.
- Telegram bot options.
- `NotificationDispatcherWorker`.
- Global Telegram rate limiter.

The `test/UnitTests` project contains coverage for:

- Address checksum and normalization behavior.
- Tracked wallet aggregate behavior.
- In-memory cache behavior.
- Wallet tracker worker matching `FromAddress` and `ToAddress`.

### Not Yet Implemented

The project does not yet have:

- Database persistence.
- EF Core `DbContext`.
- Database migrations.
- Repository or query abstractions.
- MediatR commands and handlers.
- Telegram update receiving.
- Telegram commands such as `/start`, `/add`, `/list`, `/remove`, and `/help`.
- Startup synchronization from database into `InMemoryCacheService`.
- Runtime synchronization when users add or remove wallets.
- Dockerfile and compose/deployment setup.
- Integration tests for Telegram and blockchain infrastructure.

## Target MVP Behavior

The MVP should support this flow:

1. A Telegram user starts the bot.
2. The user sends `/add 0x...`.
3. The application normalizes and validates the address.
4. A `TrackedWallet` is saved to the database.
5. The in-memory cache is updated immediately.
6. The blockchain indexer receives new blocks.
7. The wallet tracker checks every transaction against the in-memory cache.
8. If a tracked wallet appears in `FromAddress` or `ToAddress`, a notification payload is emitted.
9. The notification dispatcher sends the alert through Telegram while respecting rate limits.

## Recommended Implementation Order

## Phase 1: Tighten the Domain Model

Goal: make the domain model represent the real rules before persistence and handlers grow around it.

Tasks:

- Add a domain overload such as `TrackedWallet.Create(Address address, long userId, ...)`.
- Keep string parsing and address normalization outside the aggregate.
- Decide whether `MinimumTransactionValue` is denominated in ETH, native chain units, or another normalized unit.
- Add optional fields only if needed for MVP:
  - `IsActive`
  - `ChainId`
  - `Name` or `Label`
  - `CreatedByTelegramChatId`

Why this matters:

The aggregate should enforce tracking rules, but it should not need to know how Telegram command text is parsed. In a MediatR setup, handlers can normalize raw user input and then pass clean value objects into the domain.

Recommended tests:

- Creating a wallet from an `Address` value object succeeds.
- Negative minimum transaction values fail.
- Invalid Telegram user ids fail.
- Updating tracking settings preserves invariants.

## Phase 2: Add Persistence

Goal: make tracked wallets survive application restarts.

Tasks:

- Add EF Core packages to `Infrastructure`.
- Choose the initial database provider:
  - PostgreSQL is a strong default for deployment.
  - SQL Server is also fine if that matches the target environment.
- Create an `Application` abstraction for tracked wallet persistence, for example `ITrackedWalletRepository`.
- Implement the repository in `Infrastructure`.
- Add `AppDbContext`.
- Add EF mapping for:
  - `TrackedWallet`
  - `Address` value object
- [x] Persist an optional wallet alias/display name alongside the tracked wallet.
- Add an initial migration.
- Add database configuration to `appsettings.json`.

Design notes:

- The database is the source of truth.
- The in-memory cache is only a fast lookup layer.
- Store addresses in normalized EIP-55 format.
- Add a unique constraint that prevents the same user from tracking the same address twice.

Recommended tests:

- Repository can save and read a tracked wallet.
- Duplicate wallet tracking for the same user is rejected by the application handler.
- A removed or inactive wallet is not returned as active.

## Phase 3: Add MediatR Application Use Cases

Goal: move business orchestration into the `Application` layer.

Tasks:

- Add MediatR to the `Application` project.
- Create command/query models:
  - `AddTrackedWalletCommand`
  - `RemoveTrackedWalletCommand`
  - `ListTrackedWalletsQuery`
  - `UpdateTrackedWalletSettingsCommand`
- Add handlers that return `Result` or `Result<T>`.
- Normalize raw address strings in the handler or in a small application-level parsing service.
- Save changes through repository abstractions.
- Update `InMemoryCacheService` after successful add/remove operations.
- Update the cache and stored entity after settings changes.

Recommended handler flow for `/add`:

```csharp
var addressResult = Address.Normalize(command.RawAddress);
if (addressResult.IsFailure)
{
    return Result.Failure(addressResult.Error);
}

var walletResult = TrackedWallet.Create(addressResult.Value, command.TelegramUserId, command.MinimumTransactionValue);
if (walletResult.IsFailure)
{
    return Result.Failure(walletResult.Error);
}

await repository.AddAsync(walletResult.Value, cancellationToken);
trackedWalletCache.Add(walletResult.Value);

return Result.Success();
```

Why this matters:

MediatR gives us a clean place to convert raw Telegram input into domain objects, enforce use-case rules, persist state, and synchronize the in-memory cache.

Recommended tests:

- Add command accepts lowercase input and stores normalized address.
- Add command rejects malformed addresses.
- Add command rejects duplicates.
- Remove command removes from database and cache.
- List query returns only the current user's tracked wallets.

## Phase 4: Implement Telegram Bot Commands

Goal: let real users manage tracked wallets.

Tasks:

- Add Telegram update receiving.
- Decide between polling and webhook mode:
  - Polling is simpler for local development and MVP.
  - Webhooks are better once deployed behind HTTPS.
- Implement command routing in `Presentation.Telegram`.
- Support commands:
  - `/start`
  - `/help`
  - `/add <address> [minimumValue]`
  - `/list`
  - `/remove <address>`
- Convert Telegram messages into MediatR commands and queries.
- Return friendly messages for `Result` failures.

Command behavior:

- `/add` should accept lowercase, uppercase, or checksummed addresses if they are valid hex EVM addresses.
- The handler should normalize the address before creating the aggregate.
- `/list` should show normalized tracked addresses and basic settings.
- `/remove` should normalize the provided address before lookup.

Recommended tests:

- Command parser handles valid `/add` input.
- Command parser handles missing address.
- Command parser handles optional minimum value.
- Presentation layer maps application errors into user-friendly text.

## Phase 5: Startup Cache Synchronization

Goal: make the workers useful immediately after restart.

Tasks:

- Create a startup synchronization hosted service.
- On application startup, load active tracked wallets from the database.
- Call `InMemoryCacheService.ReplaceAll(IEnumerable<TrackedWallet>)`.
- Log how many wallets were loaded.
- Decide whether to periodically resync as a drift safety net.

Why this matters:

Right now the cache starts empty. That means the workers can process blocks, but they cannot match anything unless something manually inserts addresses into the cache.

Recommended tests:

- Startup sync loads active wallets into cache.
- Startup sync ignores inactive or removed wallets.
- Repeated sync replaces stale cache state.

## Phase 6: Improve Wallet Matching

Goal: make notifications accurate enough for MVP use.

Current behavior:

- `WalletTrackerWorker` matches transaction `FromAddress`.
- `WalletTrackerWorker` matches transaction `ToAddress`.
- It also checks log contract addresses.

Needed improvements:

- Apply `MinimumTransactionValue`.
- Respect `TrackIncomingTransfers`.
- Respect `TrackOutgoingTransfers`.
- Respect `TrackTokenTransfers`.
- Add ERC-20 transfer log matching by checking the `Transfer(address,address,uint256)` topic.
- Match token transfer `from` and `to` addresses inside log topics, not only the contract address.
- Resolve the specific token metadata for each transfer:
  - token symbol
  - token contract address
  - token decimals
- Include the USD value of each transfer alongside the native/token amount.
- Include chain/network information in notifications.
- Keep block number available for debugging and test runs, but make it optional in the final user-facing layout.
- Surface the tracked wallet alias in the notification when one exists.
- Prefer known exchange/hot-wallet labels from chain scans when available, while still allowing manual aliases to win.
- Add an explorer link for the transaction or token transfer if the chain supports a public explorer.

Design note:

Native transfers and token transfers need slightly different matching logic. Native transfers use transaction `FromAddress`, `ToAddress`, and `Value`. ERC-20 transfers use event logs.

Recommended tests:

- Incoming transfer is ignored when `TrackIncomingTransfers` is false.
- Outgoing transfer is ignored when `TrackOutgoingTransfers` is false.
- Transaction below minimum value is ignored.
- ERC-20 transfer log emits a notification when the tracked address appears in indexed topics.

## Phase 7: Redesign Notification Payload

Goal: make Telegram notifications feel useful at a glance, like a compact drops-style alert.

Tasks:

- Redesign the Telegram message template around a compact title, value summary, and metadata footer.
- Show the wallet alias first when available, with the address as a secondary detail.
- Show the chain/network name prominently so cross-chain activity is obvious.
- Show the specific token/currency name and amount in the main body.
- Show the USD estimate in the same message, ideally close to the amount line.
- Include the transaction status or confirmation depth when available.
- Attach a price source and timestamp to the USD estimate if the pricing data can vary.
- Include gas fee / gas used information when available.
- Keep block number behind a debug/test toggle or lower-visibility line.
- Include a transaction hash and explorer link for quick verification.
- Keep the layout readable on mobile Telegram clients.

Recommended tests:

- Notification renders correctly for native transfers.
- Notification renders correctly for ERC-20 transfers.
- Alias appears when manually configured.
- Auto-detected wallet labels can appear without overriding a manual alias.
- Block number can be omitted from the public layout without breaking the message.

## Phase 8: Harden Notification Dispatch

Goal: make Telegram delivery reliable without overwhelming the API.

Already implemented:

- Global 30 messages/second limiter.
- Basic per-chat limiter.

Needed improvements:

- Move rate-limiter configuration into `appsettings.json`.
- Add retry handling for transient Telegram failures.
- Respect Telegram `RetryAfter` responses when available.
- Decide whether failed notifications should be dropped, retried in memory, or persisted.
- Escape Telegram Markdown correctly.
- Consider switching to MarkdownV2 or plain text if formatting errors become noisy.

Recommended tests:

- Rate limiter does not allow more than configured global throughput.
- Dispatcher skips invalid Telegram user ids.
- Dispatcher handles Telegram API exceptions without stopping the worker.

## Phase 9: Configuration And Secrets

Goal: make local and deployed configuration predictable.

Tasks:

- Replace placeholder values in `appsettings.json` with safe defaults or empty values.
- Use user secrets for local development:
  - Telegram bot token
  - RPC HTTP URL
  - RPC WebSocket URL
  - database connection string
- Add environment variable support for Docker deployment.
- Validate required options at startup.

Recommended settings:

```json
{
  "TelegramBot": {
    "BotToken": ""
  },
  "BlockchainIndexer": {
    "HttpRpcUrl": "",
    "WebSocketRpcUrl": "",
    "PreferWebSocket": true,
    "StartBlockNumber": null,
    "PollInterval": "00:00:02",
    "WebSocketReconnectDelay": "00:00:05"
  },
  "ConnectionStrings": {
    "Default": ""
  }
}
```

## Phase 10: Dockerize The Application

Goal: make the MVP deployable.

Tasks:

- Add a root `Dockerfile`.
- Add `.dockerignore`.
- Add `docker-compose.yml` for:
  - Telegram presentation service
  - database
- Wire environment variables into the container.
- Ensure the app runs migrations or document how migrations are applied.
- Add health checks if useful.

Important note:

The Dockerfile in `agents.md` is a blueprint, but the current repo paths include `src/Presentation.Telegram`, `src/Infrastructure`, `src/Application`, and `src/Domain`. The Dockerfile should match the actual folder layout.

## Phase 11: Integration Testing And Observability

Goal: catch failures that unit tests cannot see.

Tasks:

- Add integration tests for repository and database behavior.
- Add worker tests with realistic block data.
- Add structured logging around:
  - block ingestion
  - websocket reconnects
  - cache sync
  - wallet matches
  - notification dispatch
- Add basic metrics if desired:
  - blocks processed
  - transactions scanned
  - matches found
  - notifications sent
  - Telegram failures

## Suggested Next Concrete Step

The best next implementation step is Phase 1 plus Phase 2:

1. Add `TrackedWallet.Create(Address address, ...)`.
2. Update tests to use the value-object overload.
3. Add EF Core persistence with `AppDbContext`.
4. Add repository abstractions.
5. Add a startup cache synchronization service.

That sequence gives the project a real source of truth. Once the database and cache sync exist, Telegram commands become straightforward because `/add`, `/remove`, and `/list` will have somewhere real to store and retrieve data.

## MVP Completion Checklist

- [x] Domain aggregate and address value object
- [x] Result pattern
- [x] Typed channels
- [x] Blockchain indexer worker
- [x] Wallet tracker worker
- [x] Notification dispatcher worker
- [x] In-memory tracked address cache
- [x] Unit tests for core domain/cache/worker behavior
- [x] `TrackedWallet.Create(Address address, ...)` overload
- [x] EF Core database context
- [x] Tracked wallet persistence
- [x] Database migrations
- [x] MediatR commands and handlers
- [x] Telegram command receiving and routing
- [x] Startup cache synchronization
- [x] Runtime cache updates from add/remove commands
- [x] Wallet aliases
- [x] Minimum transaction value filtering
- [x] ERC-20 transfer event matching
- [x] Token metadata resolution
- [x] USD valuation on notifications
- [x] Telegram Markdown escaping
- [x] Dockerfile
- [x] Docker Compose setup
- [ ] Deployment configuration
- [x] End-to-end MVP flow validated on a live chain
- [ ] Support multiple chains simultaneously
- [x] Redesign Telegram notifications to match a drops-style bot message layout
- [ ] Auto-detected wallet labels from chain scans
- [x] Transaction explorer links in notifications
- [ ] Inline notification edit menu for alias, minimum value, and chain preferences

## Phase 12: Inline Notification Editing

Goal: turn notifications into actionable controls before release.

Tasks:

- Add an inline keyboard to the Telegram notification with a pencil/edit action.
- Open a compact settings menu from the notification instead of forcing users to use text commands.
- Allow users to edit:
  - wallet alias
  - minimum transaction value
  - included chains/networks
  - transfer direction preferences
- Persist changes immediately and refresh the in-memory cache after confirmation.
- Keep the command-based flow available as a fallback, but make the notification-driven editor the primary UX.
- Add callback-query handling and validation for the edit flow.

Recommended tests:

- Edit menu opens from the notification.
- Alias updates persist and are reflected in future notifications.
- Minimum value updates persist and affect matching.
- Chain preference updates persist and affect matching.
- Invalid edits return a friendly error and leave the old settings intact.
