# Development Agent Workflow

Use development subagents by default for non-trivial features, fixes, and refactors. The main thread coordinates scope, architecture, integration, and the final response. These development roles are separate from the application's background workers described below.

Before implementation or review, read [current runtime context](docs/ai/context.md), [development conventions](docs/ai/conventions.md), [the subagent workflow](docs/ai/development-workflow.md), and the relevant feature plan. The active registration in `src/Presentation.Telegram/Program.cs` and the current runtime context take precedence over the historical architecture blueprint below. Native block scanning remains disabled unless the requested work explicitly changes that decision.

## Workflow

1. Convert the request into a bounded brief with requirements, acceptance criteria, exclusions, and validation. Existing user authorization is sufficient; ask only for material missing product decisions or actions requiring permission.
2. Delegate independent research and QA planning when useful. Run these read-only assignments in parallel, then reconcile their findings before dependent implementation.
3. Delegate a coherent vertical slice to `implementer-files`. Allow only one agent, including the coordinator, to edit repository files at a time.
4. For non-trivial code changes, follow with a new `implementer-integration` agent. Give it fresh context, the requirements, and access to current code and diff, without the first implementer's reasoning or self-review. It may fix integration defects within scope.
5. Use a separate fresh `reviewer` to review the resulting diff against the requirements. Route fixes through one writer, then have the reviewer verify resolutions. The integration pass does not replace independent review.
6. The coordinator checks the combined result, runs appropriate validation, updates affected docs, and reports completed behavior and material validation limits. Never claim an unexecuted check passed.
7. Trivial edits, explanations, and read-only summaries may stay in the main thread. Configuration/documentation work may use a coordinator implementation and an independent review without two coding passes. Avoid agents whose work does not improve the result.

## Roles and Configuration

The source of truth for model and reasoning assignments is [.codex/config.toml](.codex/config.toml) and the role files in `.codex/agents/`:

- `evm-researcher`: protocol, provider, blockchain, and Telegram API research.
- `qa-planner`: independent acceptance criteria and failure scenarios.
- `implementer-files`: first implementation pass across the assigned slice.
- `implementer-integration`: fresh cross-file integration and fixes.
- `reviewer`: independent read-only correctness and regression review.

Use the named custom agent when supported. If the spawning tool only accepts a model, reasoning effort, and prompt, read the role file and explicitly provide its model, effort, and instructions; a task label alone does not load a role. Use `fork_turns="none"` when supported and supply a compact self-contained brief. Role files do not change the model of an already-running coordinator.

Read-only roles must not modify files or run commands that write artifacts, even when inherited permissions permit it. Custom agent definitions do not enforce a separate sandbox. The project config caps concurrently open subagent threads at three, excluding the coordinator; respect any tighter host limit. Before each new stage, collect results and close/release completed threads using the host's supported tool when available. Do not assume a finished turn releases an open-thread slot. If the host has no release tool and capacity prevents a fresh spawn, report that limit and use the documented coordinator fallback; do not reuse an implementer's context as independent integration/review. Subagents must not delegate further. If a configured model or tool is unavailable, disclose the limitation and use an available suitable assignment; never silently claim the configured role ran.

---

# EVM On-Chain Tracking Platform: Agent Architecture (MVP)

This document outlines the architectural design, responsibilities, and interaction patterns of the background processing units ("agents/workers") for the EVM-compatible on-chain tracking Telegram bot. 

The system is built using **.NET 10**, leveraging **Clean Architecture** and light **DDD (Domain-Driven Design)** principles, and is fully containerized using **Docker**.

---

## 1. Architectural Overview & Technical Stack

To ensure high throughput and low latency for real-time blockchain event processing, the system is split into distinct architectural layers:
* **Domain**: Core entities (e.g., `TrackedWallet`, `Subscription`), value objects, and domain events.
* **Application**: CQRS handlers (MediatR), orchestration logic, and port definitions.
* **Infrastructure**: Blockchain listeners (Nethereum, WebSockets), `InMemory` storage/caching providers, and database context (EF Core).
* **Presentation / Presentation.Telegram**: The Telegram Bot interface acting as the primary user touchpoint.

### Storage & Caching Strategy (Early Stage)
* **Primary DB**: PostgreSQL / MS SQL Server (via EF Core) for persistent data (user settings, tracked wallets).
* **Caching**: Strict use of **In-Memory Cache** (`IMemoryCache` / `Channels`) for active tracking states and rate-limiting. No Redis dependency at this stage.

---

## 2. Agent Topology (Background Services)

The platform utilizes specialized background workers (`IHostedService` / `BackgroundService`) that act as isolated, concurrent agents. They communicate asynchronously via high-performance **System.Threading.Channels**.

[ Blockchain Network ]
│ (WebSockets / JSON-RPC)
▼
┌────────────────────────────────────────────────────────┐
│ 1. Blockchain Indexer Agent (Ingestion)                │
└───────────────────────┬────────────────────────────────┘
│ (In-Memory Channel)
▼
┌────────────────────────────────────────────────────────┐
│ 2. Wallet Tracking & Matching Agent (Processing)      │
└───────────────────────┬────────────────────────────────┘
│ (In-Memory Channel)
▼
┌────────────────────────────────────────────────────────┐
│ 3. Notification Dispatcher Agent (Delivery)            │
└───────────────────────┬────────────────────────────────┘
▼
[ Telegram Bot API ]

## 3. Detailed Agent Specifications

### 3.1. Blockchain Indexer Agent (`BlockchainIndexerWorker`)
* **Responsibility**: Maintain a persistent connection to EVM node endpoints, ingest new blocks, and stream raw transactions/logs.
* **Technical Details**:
    * Utilizes **Nethereum** with streaming WebSockets (`EthNewBlockHeadersObservable` or `EthLogsObservable`) for real-time ingestion.
    * Falls back to high-frequency polling via HTTP JSON-RPC if the WebSocket connection drops.
    * Pushes raw block/transaction data into an internal, bounded `Channel<RawBlockData>` to prevent memory bloat.

### 3.2. Wallet Tracking & Matching Agent (`WalletTrackerWorker`)
* **Responsibility**: Correlate incoming on-chain transactions with the list of user-tracked wallets.
* **Technical Details**:
    * Consumes data from `Channel<RawBlockData>`.
    * Fetches the list of active tracked addresses from the **InMemory Cache** (synchronized periodically with the database).
    * Performs fast $O(1)$ lookups against the cached hash set of addresses.
    * Upon detecting a match (From/To address or specific Topic in smart contract logs), it generates a `WalletActivityDetected` domain event and pushes a formatted payload to the `Channel<NotificationPayload>`.

### 3.3. Notification Dispatcher Agent (`NotificationDispatcherWorker`)
* **Responsibility**: Handle outbound communication with the Telegram Bot API, ensuring message delivery and rate-limit compliance.
* **Technical Details**:
    * Consumes payloads from `Channel<NotificationPayload>`.
    * Utilizes the `Telegram.Bot` library to send formatted Markdown messages to users.
    * Implements strict **SemaphoreSlim** or token-bucket rate limiting to respect Telegram's API limits (max 30 messages/second globally, 1 message/second per chat).

---

## 4. Domain & State Management (DDD Light)

While the domain logic is simple, DDD principles protect the consistency of tracking aggregates:

* **Tracking Aggregate**: The `TrackedWallet` root aggregate manages rules (e.g., "only track tx > 1 ETH", "track specific ERC-20 transfers").
* **Functional Error Handling (The `Result` Pattern)**: 
    * To prevent the performance overhead of exceptions in high-throughput data pipelines, the Domain and Application layers strictly use a custom `Result` or `Result<T>` class for flow control.
    * Domain methods and MediatR handlers must return explicit success or failure states (e.g., `Result.Success()` or `Result.Failure(Error.WalletAlreadyTracked)`) instead of throwing exceptions for expected business violations.
* **In-Memory State Synchronization**: 
    * A scoped synchronization service updates the `IMemoryCache` lookup tables whenever a user adds or removes a wallet via the Telegram interface.
    * Thread-safe collections (`ConcurrentDictionary` or optimized snapshot arrays) ensure the `WalletTrackerWorker` never blocks while reading target addresses.

---

## 5. Dockerization (`Dockerfile` Blueprint)

The multi-stage `Dockerfile` is optimized for .NET 10 AOT (if applicable) or standard structured builds:

```dockerfile
FROM [mcr.microsoft.com/dotnet/sdk:10.0](https://mcr.microsoft.com/dotnet/sdk:10.0) AS build
WORKDIR /src

COPY ["Presentation.Telegram/Presentation.Telegram.csproj", "Presentation.Telegram/"]
COPY ["Infrastructure/Infrastructure.csproj", "Infrastructure/"]
COPY ["Application/Application.csproj", "Application/"]
COPY ["Domain/Domain.csproj", "Domain/"]
RUN dotnet restore "Presentation.Telegram/Presentation.Telegram.csproj"

COPY . .
WORKDIR "/src/Presentation.Telegram"
RUN dotnet publish "Presentation.Telegram.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM [mcr.microsoft.com/dotnet/runtime:10.0](https://mcr.microsoft.com/dotnet/runtime:10.0) AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "YourPlatform.Presentation.Telegram.dll"]
```

## 6. Development Roadmap & MVP Scope
Phase 1: Setup Clean Architecture project structure in .NET 10. Implement basic Telegram commands (/add 0x..., /list, /remove).

Phase 2: Introduce BlockchainIndexerWorker connecting to a single EVM RPC (e.g., Base or Ethereum Mainnet).

Phase 3: Implement In-Memory matching logic and the NotificationDispatcherWorker.

Phase 4: Dockerize the application and deploy the initial MVP bot.
