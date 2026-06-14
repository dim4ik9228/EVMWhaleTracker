# Multi-Network Support Implementation Plan

Status: implemented
Date: 2026-06-14

Completed: 2026-06-14

Implementation note: the shipped MVP uses one shared Infura API key and shared listener timing settings across all networks. Each network keeps only chain identity, enabled state, and HTTP/WebSocket base URLs. Native transfer tracking remains deferred/disabled.

This plan describes how to make the application track ERC-20/BEP-20 transfers on multiple EVM-compatible networks at the same time.

The preferred MVP approach is **one application process with multiple hosted listener instances**, not separate microservices yet. Each enabled network gets its own websocket subscription loop, but all listeners publish into the same matching, aggregation, and Telegram delivery pipeline.

Native transfer tracking remains deferred/disabled unless explicitly re-enabled later.

## Goals

- Track token transfers across multiple enabled chains concurrently.
- Keep one shared wallet cache, matching worker, notification aggregation worker, and Telegram dispatcher.
- Keep each chain listener independently reconnectable.
- Keep notification payloads chain-aware, preserving correct token metadata, prices, and explorer links.
- Let users keep using chain restrictions through `TrackedWallet.EnabledChains`.
- Avoid microservice complexity until scale or isolation requirements justify it.

## Non-Goals

- Do not split listeners into separate deployable services in this pass.
- Do not re-enable native block scanning as part of multi-network support.
- Do not introduce Redis, a broker, or distributed queues.
- Do not add per-user chain editing UX in this pass unless needed to test the backend.

## Current Single-Network Shape

The current runtime assumes one configured chain:

- `BlockchainIndexerOptions`
  - `Chain`
  - `HttpRpcUrl`
  - `WebSocketRpcUrl`
  - reconnect/polling settings
- `TokenTransferLogSubscriberWorker`
  - reads one `BlockchainIndexerOptions`
  - opens one websocket client
  - publishes `RawBlockData` with `_options.Chain`
- `NethereumTokenMetadataProvider`
  - owns one `Web3` client
  - rejects metadata requests for chains other than its configured `_chain`
- `Program.cs`
  - registers one token log subscriber
  - registers one wallet tracker

The downstream pipeline is already mostly multi-chain-ready because messages carry `SupportedChain`:

```text
TokenTransferLogSubscriberWorker
  -> RawBlockDataChannel
  -> WalletTrackerWorker
  -> NotificationPayloadChannel
  -> NotificationAggregationWorker
  -> AggregatedNotificationPayloadChannel
  -> NotificationDispatcherWorker
```

## Target Runtime Architecture

Run one token log subscriber per enabled network:

```text
BSC TokenTransferLogSubscriberWorker
Base TokenTransferLogSubscriberWorker
Arbitrum TokenTransferLogSubscriberWorker
Ethereum TokenTransferLogSubscriberWorker
        |
        v
Shared RawBlockDataChannel
        |
        v
WalletTrackerWorker
        |
        v
NotificationAggregationWorker
        |
        v
NotificationDispatcherWorker
```

Each subscriber:

- owns its own websocket client
- owns its own reconnect loop
- builds filters from the shared tracked-wallet cache
- publishes `RawBlockData` with the correct `SupportedChain`

The matcher and renderer remain chain-aware through existing payload fields.

## Configuration Design

Replace the single `BlockchainIndexer` runtime section with a network list.

Suggested shape:

```json
{
  "BlockchainNetworks": {
    "Networks": [
      {
        "Chain": "Bsc",
        "Enabled": true,
        "HttpRpcUrl": "https://bsc-mainnet.infura.io/v3/",
        "WebSocketRpcUrl": "wss://bsc-mainnet.infura.io/ws/v3/",
        "WebSocketReconnectDelay": "00:00:05",
        "SubscriptionRefreshInterval": "00:01:00"
      },
      {
        "Chain": "Base",
        "Enabled": true,
        "HttpRpcUrl": "https://base-mainnet.infura.io/v3/",
        "WebSocketRpcUrl": "wss://base-mainnet.infura.io/ws/v3/",
        "WebSocketReconnectDelay": "00:00:05",
        "SubscriptionRefreshInterval": "00:01:00"
      },
      {
        "Chain": "Arbitrum",
        "Enabled": false,
        "HttpRpcUrl": "",
        "WebSocketRpcUrl": "",
        "WebSocketReconnectDelay": "00:00:05",
        "SubscriptionRefreshInterval": "00:01:00"
      }
    ]
  }
}
```

Suggested contracts:

```csharp
public sealed class BlockchainNetworksOptions
{
    public IReadOnlyList<BlockchainNetworkOptions> Networks { get; init; } = [];
}

public sealed class BlockchainNetworkOptions
{
    public SupportedChain Chain { get; init; }
    public bool Enabled { get; init; } = true;
    public string? HttpRpcUrl { get; init; }
    public string? WebSocketRpcUrl { get; init; }
    public string? RpcApiKey { get; init; }
    public TimeSpan WebSocketReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan SubscriptionRefreshInterval { get; init; } = TimeSpan.FromMinutes(1);
}
```

`HttpRpcUrl` and `WebSocketRpcUrl` should be stored as API-key-free base URLs in checked-in configuration. `RpcApiKey` comes from user secrets or environment variables and is appended at runtime. Providers that need a placeholder can use `{ApiKey}` or `{RpcApiKey}` in the URL.

Keep `BlockchainIndexerOptions` only for the deferred native block-scanning path if useful, or replace it with a more clearly named `NativeBlockScannerOptions` later.

## Environment Variable Shape

For Docker, array-style environment variables can be awkward but supported:

```env
BLOCKCHAINNETWORKS__NETWORKS__0__CHAIN=Bsc
BLOCKCHAINNETWORKS__NETWORKS__0__ENABLED=true
BLOCKCHAINNETWORKS__NETWORKS__0__HTTPRPCURL=...
BLOCKCHAINNETWORKS__NETWORKS__0__WEBSOCKETRPCURL=...
BLOCKCHAINNETWORKS__NETWORKS__0__RPCAPIKEY=...

BLOCKCHAINNETWORKS__NETWORKS__1__CHAIN=Base
BLOCKCHAINNETWORKS__NETWORKS__1__ENABLED=true
BLOCKCHAINNETWORKS__NETWORKS__1__HTTPRPCURL=...
BLOCKCHAINNETWORKS__NETWORKS__1__WEBSOCKETRPCURL=...
```

If this becomes painful, add named entries instead:

```json
"BlockchainNetworks": {
  "Bsc": { "Enabled": true, "HttpRpcUrl": "", "WebSocketRpcUrl": "" },
  "Base": { "Enabled": true, "HttpRpcUrl": "", "WebSocketRpcUrl": "" }
}
```

The list form is simpler for validation and ordered registration. The named form is nicer for environment variables. Pick one during implementation and document it.

## Key Design Decisions

1. Use multiple hosted services, not microservices.

   This keeps deployment simple. Each listener is still independent as an async background task with its own websocket and reconnect behavior.

2. Keep one shared pipeline.

   All listeners write to the same `RawBlockDataChannel`. The existing `Chain` field prevents ambiguity downstream.

3. Refactor metadata provider into a multi-chain provider.

   Token metadata resolution must use the RPC client for the chain where the token event happened.

4. Keep wallet chain filtering in `WalletTrackerWorker`.

   `TrackedWallet.EnabledChains` already expresses which chains a wallet wants. Empty collection means all supported chains.

5. Keep failures isolated per listener.

   One chain websocket dropping should not stop other chains from receiving logs.

## Implementation Phases

## Phase 1: Add Multi-Network Options

Goal: introduce a first-class config model for many chain endpoints.

Tasks:

- Add `BlockchainNetworksOptions`.
- Add `BlockchainNetworkOptions`.
- Add validation rules:
  - no duplicate enabled chains
  - enabled networks must have `WebSocketRpcUrl` for token tracking
  - metadata-enabled networks must have `HttpRpcUrl` or `WebSocketRpcUrl`
  - unknown chains fail fast during startup
- Update `appsettings.json` with at least one configured sample network.
- Keep backwards compatibility temporarily if the old `BlockchainIndexer` section exists.

Acceptance criteria:

- App can load multiple network configs.
- Invalid duplicate chains fail with clear startup error.
- Existing single-chain local config can be migrated predictably.

## Phase 2: Refactor Token Log Subscriber Construction

Goal: let the same worker implementation run once per network.

Tasks:

- Change `TokenTransferLogSubscriberWorker` to accept `BlockchainNetworkOptions` directly instead of `IOptions<BlockchainIndexerOptions>`.
- Include `Chain` in log messages:
  - subscription start
  - reconnect
  - refresh
  - unsubscribe failures
- Make `SubscriptionRefreshInterval` configurable per network instead of a private constant.
- Preserve the current tracked-address snapshot and filter-building behavior.
- Ensure `ToRawBlockData` uses the per-network chain.

Suggested constructor direction:

```csharp
public TokenTransferLogSubscriberWorker(
    OnChainPipelineChannels channels,
    ITrackedAddressCacheService trackedAddressCache,
    BlockchainNetworkOptions network,
    ILogger<TokenTransferLogSubscriberWorker> logger)
```

Acceptance criteria:

- A worker instance can be created for BSC and another for Base with different websocket URLs.
- Each instance publishes `RawBlockData.Chain` correctly.
- Reconnect logs identify the affected chain.

## Phase 3: Register One Listener Per Enabled Network

Goal: build all enabled chain listeners during startup.

Tasks:

- Bind `BlockchainNetworksOptions` in `Program.cs`.
- Resolve enabled networks.
- Register one hosted service per enabled network.
- Keep one shared `WalletTrackerWorker`.
- Keep one shared `NotificationAggregationWorker`.
- Keep one shared `NotificationDispatcherWorker`.

Implementation note:

Default `AddHostedService<T>()` is not enough when each instance needs different constructor data. Use an implementation factory with `IHostedService`:

```csharp
foreach (var network in enabledNetworks)
{
    builder.Services.AddSingleton<IHostedService>(sp =>
        new TokenTransferLogSubscriberWorker(
            sp.GetRequiredService<OnChainPipelineChannels>(),
            sp.GetRequiredService<ITrackedAddressCacheService>(),
            network,
            sp.GetRequiredService<ILogger<TokenTransferLogSubscriberWorker>>()));
}
```

Acceptance criteria:

- Enabling BSC and Base creates two subscriber instances.
- Disabling a network does not create a subscriber for it.
- App still starts with a single enabled chain.

## Phase 4: Make Token Metadata Resolution Multi-Chain

Goal: resolve token metadata with the RPC client for the event chain.

Current issue:

- `NethereumTokenMetadataProvider` owns one `Web3`.
- It rejects `GetAsync(chain, tokenAddress)` if `chain != _chain`.

Target design:

- Replace it with a multi-chain provider, or refactor it internally.
- Maintain one `Web3` client per configured chain.
- Key metadata cache by `chain + token address`, which already matches current cache behavior.
- Keep static token metadata override lookup first.
- Keep failure caching.

Suggested internal shape:

```csharp
private readonly IReadOnlyDictionary<SupportedChain, Web3> _web3ByChain;
private readonly IReadOnlyList<IDisposable> _ownedTransports;
```

Lookup flow:

```text
1. StaticTokenMetadataCatalog.TryGet(chain, tokenAddress)
2. In-memory cache
3. Web3 client for chain
4. RPC symbol/name/decimals
5. cache success or short-lived failure
```

Acceptance criteria:

- BSC USDT metadata resolves through the BSC client.
- Base USDC metadata resolves through the Base client.
- Requesting metadata for an unconfigured chain returns `Result.Failure`.
- Static metadata works even if RPC is unavailable for that token.

## Phase 5: Pricing Provider Review

Goal: confirm the price provider remains correct for multi-chain token pricing.

Current state:

- `HttpAssetPriceProvider` is already chain-aware.
- Native prices use CoinGecko IDs per `SupportedChain`.
- Token prices use DexScreener chain IDs per `SupportedChain`.

Tasks:

- Ensure every enabled chain has a `DexScreenerChainIds` entry.
- Ensure every chain with native price support has a `NativeCoinGeckoIds` entry.
- Decide behavior when price mapping is missing:
  - current behavior sends without USD and logs the failure
  - keep this behavior
- Add missing mappings for newly supported chains as needed.

Acceptance criteria:

- Token notifications across enabled chains can include USD price where DexScreener supports the chain.
- Missing price support does not block notifications.

## Phase 6: Startup, Docker, And User Secrets

Goal: make local and container configuration understandable.

Tasks:

- Update `appsettings.json`.
- Update `appsettings.Development.json` if present.
- Update `.env` export script if it assumes single-chain keys.
- Update `docker-compose.yml` environment variables.
- Update README local setup docs.
- Include a clear example for enabling multiple networks.
- Keep checked-in RPC URLs API-key-free and append the API key from user secrets or `.env`.

Acceptance criteria:

- `docker compose up --build` can run with one chain.
- Adding a second chain only requires adding another network config.
- Missing optional chain config does not break the app when that chain is disabled.

## Phase 7: User-Facing Chain Settings

Goal: make sure tracked wallets can opt into the chains they care about.

Current state:

- `TrackedWallet.EnabledChains` already exists.
- Empty collection means all supported chains.
- Command flow stores enabled chains when provided by application commands.

Tasks:

- Confirm Telegram commands can display enabled chains correctly.
- Decide whether `/add` should keep defaulting to all chains or current configured chains.
- Consider a follow-up UX for chain selection:
  - command arguments
  - inline notification edit menu
  - settings command

Recommended MVP behavior:

- Keep empty `EnabledChains` as "all enabled runtime networks".
- Let advanced chain editing remain a follow-up.

Acceptance criteria:

- A wallet restricted to BSC does not receive Base notifications.
- A wallet with no chain restrictions receives notifications from all enabled runtime networks.

## Phase 8: Tests

Add focused tests before manual multi-chain testing.

Configuration tests:

- Options binding supports multiple networks.
- Duplicate enabled chain config fails validation.
- Disabled networks are ignored.

Subscriber tests:

- A subscriber created for `Bsc` publishes `RawBlockData.Chain = Bsc`.
- A subscriber created for `Base` publishes `RawBlockData.Chain = Base`.
- Reconnect delay comes from the network config.

Metadata provider tests:

- Static metadata lookup works per chain.
- Multi-chain provider selects the correct `Web3` client per chain, using a wrapper/fake if needed.
- Unconfigured chain returns failure.
- Failure caching remains chain-specific.

Wallet tracker tests:

- BSC-only tracked wallet ignores Base token transfer.
- Unrestricted tracked wallet accepts BSC and Base transfers.
- Aggregation does not group notifications across different chains even if transaction hashes match.

Renderer tests:

- Explorer links use the correct chain explorer for each network.
- Token links use the correct chain token URL.

End-to-end local test:

- Enable BSC and Base.
- Track one wallet.
- Send or replay one token transfer from each chain.
- Confirm each notification has the correct chain label and explorer links.

## Phase 9: Observability And Failure Isolation

Goal: make multi-chain runtime behavior easy to debug.

Tasks:

- Include `{Chain}` in all listener logs.
- Log listener lifecycle events:
  - starting
  - no tracked wallets
  - subscribed
  - refresh
  - reconnect
  - stopped
- Consider lightweight counters later:
  - logs received per chain
  - logs deduplicated per chain
  - notifications matched per chain
  - websocket reconnects per chain

Acceptance criteria:

- If Base websocket drops, logs clearly show Base reconnecting.
- BSC listener keeps running while Base reconnects.

## Phase 10: Backward Compatibility And Migration

Goal: make the config migration smooth.

Options:

1. Hard switch to `BlockchainNetworks`.
   - simpler code
   - requires local `.env` and user secrets update

2. Transitional compatibility.
   - if `BlockchainNetworks` is empty, build one network from old `BlockchainIndexer`
   - smoother local migration
   - slightly more startup code

Recommended path:

- Implement transitional compatibility for one release cycle.
- Log a warning when old `BlockchainIndexer` keys are used.
- Remove the fallback later once docs and Docker config are updated.

Acceptance criteria:

- Existing single-chain setup still starts.
- New multi-chain setup starts without old keys.
- Startup log clearly states which chains are enabled.

## Microservice Extraction Criteria

Do not extract listeners into separate services yet.

Consider microservices later if one or more of these become true:

- one chain listener needs separate deployment or scaling
- RPC providers require separate rate-limit isolation
- one chain is unstable enough that it should restart independently
- the in-memory channel becomes a bottleneck
- a durable broker becomes necessary
- multiple app instances need to coordinate delivery

Future split would look like:

```text
Chain Listener Service(s)
  -> durable queue or broker
  -> Wallet Matching Service
  -> Notification Service
```

That is not needed for the current MVP.

## Suggested Implementation Order

1. Add `BlockchainNetworksOptions` and validation.
2. Refactor `TokenTransferLogSubscriberWorker` to accept per-network options.
3. Register one subscriber per enabled network.
4. Refactor `NethereumTokenMetadataProvider` into a multi-chain provider.
5. Update appsettings, Docker env export, and README.
6. Add multi-chain filtering and metadata tests.
7. Run `dotnet test`.
8. Run local Docker with one chain.
9. Run local Docker with two chains.
10. Manually verify one token transfer per enabled chain.

## Open Questions

- Default unrestricted wallet tracking currently means all enabled runtime networks.
- Telegram chain-selection UX remains a follow-up.
- Old `BlockchainIndexer` config fallback is kept temporarily for migration.
- Initial production network set is controlled by `.env`; local verification passed with BSC, Base, and Arbitrum enabled.
- Per-chain debounce settings are not needed for MVP and can be reconsidered later.
