# Address Labeling Implementation Plan

Status: proposed
Date: 2026-06-15

This plan describes how to add readable counterparty labels to notifications, such as `MEXC`, `Binance`, `OKX DEX Router`, or `vitalik.eth`, without depending on expensive explorer nametag APIs.

The MVP approach is:

- database-backed address labels as the source of truth
- in-memory cache for hot-path lookup
- nullable chain scope so wallet labels can be global while contract labels can be chain-specific
- ENS reverse lookup as a fallback for Ethereum addresses
- offline/imported seed data for exchanges, protocols, routers, bridges, and known counterparties

## Goals

- Show readable labels in Telegram notifications instead of shortened addresses when a label is known.
- Avoid database calls while processing transactions.
- Avoid paid explorer nametag APIs in the notification hot path.
- Support global labels for EOAs that are meaningful across EVM networks.
- Support chain-specific overrides for contracts and network-specific infrastructure.
- Keep labels editable and durable through PostgreSQL.
- Make initial population practical with import tooling and curated public data sources.

## Non-Goals

- Do not pay for Etherscan/BscScan/Arbiscan/Basescan Pro Plus nametag APIs.
- Do not scrape explorers from the runtime notification path.
- Do not build entity clustering or attribution heuristics in the MVP.
- Do not block notifications if label resolution fails.
- Do not add Telegram label-management UX in the first implementation pass unless needed for testing.

## Data Model

Use PostgreSQL as the source of truth and cache labels in memory.

Suggested entity:

```csharp
public sealed class AddressLabel
{
    public Guid Id { get; private set; }
    public string Address { get; private set; }
    public SupportedChain? Chain { get; private set; }
    public string Label { get; private set; }
    public AddressLabelKind Kind { get; private set; }
    public AddressLabelSource Source { get; private set; }
    public string? SourceReference { get; private set; }
    public int Priority { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
}
```

`Chain = null` means a global label.

Examples:

```text
null, 0x9bb6..., MEXC, Exchange
Bsc,  0x...,    PancakeSwap Router, DexRouter
Base, 0x...,    Aerodrome Router, DexRouter
```

Suggested enums:

```csharp
public enum AddressLabelKind
{
    Unknown = 0,
    Exchange = 1,
    ExchangeHotWallet = 2,
    ExchangeColdWallet = 3,
    DexRouter = 4,
    Bridge = 5,
    Protocol = 6,
    Treasury = 7,
    Ens = 8,
    Manual = 9
}

public enum AddressLabelSource
{
    Manual = 0,
    SeedFile = 1,
    Dune = 2,
    EnsReverse = 3,
    VerifiedContract = 4,
    OfficialProjectDocs = 5
}
```

Indexes:

```text
unique lower(Address), Chain where IsEnabled = true and Chain is not null
unique lower(Address) where IsEnabled = true and Chain is null
index lower(Address)
index Chain
index Kind
index Source
```

If EF/PostgreSQL partial unique indexes are awkward, use application-level validation first and add stricter DB indexes later.

## Resolution Rules

Lookup should be deterministic:

```text
1. chain-specific DB/cache label
2. global DB/cache label
3. ENS reverse lookup, Ethereum only
4. shortened address fallback
```

Why nullable chain:

- EOAs are usually the same identity across EVM networks, so global labels are useful.
- Contracts are chain-specific; the same address can be unrelated on another chain.
- Chain-specific labels should override global labels when both exist.

## Runtime Architecture

Add an address label cache similar to tracked wallet caching:

```text
PostgreSQL AddressLabels
        |
        v
AddressLabelCacheHydrationService
        |
        v
InMemoryAddressLabelCache
        |
        v
AddressLabelResolver
        |
        v
Notification rendering/enrichment
```

Hot path behavior:

- `WalletTrackerWorker` or notification enrichment creates structured payloads as it does now.
- Renderer/enrichment asks `IAddressLabelResolver` for tracked wallet and counterparty labels.
- Resolver reads from memory first.
- ENS fallback is cached and should never block or crash notification delivery.

## Contracts

Application abstractions:

```csharp
public interface IAddressLabelRepository
{
    Task<IReadOnlyCollection<AddressLabel>> GetEnabledAsync(CancellationToken cancellationToken);
    Task UpsertAsync(AddressLabel label, CancellationToken cancellationToken);
}

public interface IAddressLabelCacheService
{
    void ReplaceAll(IEnumerable<AddressLabelCacheEntry> labels);
    bool TryGet(SupportedChain chain, Address address, out AddressLabelCacheEntry label);
}

public interface IAddressLabelResolver
{
    Task<AddressLabelResolution> ResolveAsync(
        SupportedChain chain,
        Address address,
        CancellationToken cancellationToken);
}

public sealed record AddressLabelCacheEntry(
    Address Address,
    SupportedChain? Chain,
    string Label,
    AddressLabelKind Kind,
    AddressLabelSource Source,
    int Priority);

public sealed record AddressLabelResolution(
    Address Address,
    SupportedChain Chain,
    string DisplayText,
    bool IsLabeled,
    AddressLabelKind Kind,
    AddressLabelSource Source);
```

Implementation note:

- Cache keys should normalize addresses with the existing `Address` value object.
- Keep two dictionaries:
  - `(chain, address)` for chain-specific labels
  - `address` for global labels
- `TryGet` checks chain-specific first, then global.

## ENS Fallback

ENS should be a fallback, not the primary label system.

Behavior:

- Only run ENS reverse lookup for Ethereum mainnet initially.
- Cache successful ENS names for 7 days.
- Cache misses for 24 hours.
- Cache transient failures for 5-10 minutes.
- Validate returned names when possible.
- If an ENS name exists, use it as display text and source `EnsReverse`.

Important caveat:

- ENS is useful for public identities and some DAOs.
- ENS will not cover most exchange hot/cold wallets or BSC/Base/Arbitrum router addresses.

## Initial Label Data Strategy

Recommended source order for initial population:

1. Dune curated labels

   Best first source. Dune has cross-chain label datasets, owner/custodian mappings, and targeted CEX-related tables. Use it offline to export a curated seed file, not as a runtime dependency.

   Suggested seed queries:

   ```sql
   -- CEX/entity labels from Dune labels
   SELECT
     blockchain,
     address,
     name,
     label_type,
     model_name
   FROM labels.addresses
   WHERE blockchain IN ('ethereum', 'bnb', 'base', 'arbitrum')
     AND label_type IN ('cex', 'dex', 'bridge');
   ```

   ```sql
   -- If available in the Dune workspace, use targeted CEX tables too.
   SELECT *
   FROM cex.addresses
   WHERE blockchain IN ('ethereum', 'bnb', 'base', 'arbitrum');
   ```

   Import rules:

   - Exchange EOAs can usually become global labels if the same address is seen on multiple EVM chains with the same entity.
   - Contract/router labels should be chain-specific.
   - Prefer short labels in notifications: `MEXC`, `Binance`, `OKX DEX Router`.
   - Preserve source metadata in `SourceReference` for review.

2. Manual high-value exchange hot/cold wallets

   Build a short reviewed seed list for entities that appear in real notifications:

   - Binance
   - Coinbase
   - Kraken
   - OKX
   - MEXC
   - Bybit
   - KuCoin
   - Gate.io
   - Bitget
   - HTX

   This can come from:

   - Dune labels
   - explorer UI inspection during development
   - official proof-of-reserves or wallet disclosure pages when exchanges publish them
   - user-confirmed labels from real notifications

3. Protocol/router/bridge contract lists

   Use official docs or verified deployment repositories where possible:

   - Uniswap routers
   - PancakeSwap routers
   - Aerodrome routers
   - Stargate/LayerZero bridges
   - Across bridge
   - Synapse bridge
   - OKX/1inch/0x routers if their addresses are published

   These should usually be chain-specific labels.

4. ENS reverse lookup

   Use as runtime fallback for Ethereum EOAs and public identities.

5. Explorer page labels

   Use only as manual research aid, not automated runtime scraping.

   Rationale:

   - HTML is brittle.
   - Cloudflare/rate limits can break automation.
   - Terms of service may be restrictive.
   - A manually reviewed import avoids making notification delivery dependent on explorer pages.

## Seed File Format

Add a seed/import file that can be reviewed in git without secrets:

```csv
address,chain,label,kind,source,source_reference,priority,is_enabled
0x9bb6...,,"MEXC",Exchange,Dune,"labels.addresses:bnb",100,true
0x...,Bsc,"PancakeSwap Router",DexRouter,OfficialProjectDocs,"https://...",100,true
```

Recommended location:

```text
data/address-labels/address-labels.seed.csv
```

If the seed list grows large, keep it out of EF migrations and use an import command/tool instead.

## Import Tool

Add a small import command/script:

```powershell
dotnet run --project src/Presentation.Telegram -- import-address-labels data/address-labels/address-labels.seed.csv
```

Alternative if command parsing is not ready:

```powershell
dotnet run --project tools/AddressLabelImporter
```

Import behavior:

- Normalize address.
- Parse optional chain.
- Upsert by `(address, chain)`.
- Validate label is non-empty and short enough for Telegram.
- Preserve source and source reference.
- Disable labels if `is_enabled = false`.
- Report counts: inserted, updated, skipped, invalid.

## Notification Rendering Changes

Current counterparty rendering:

```text
From: [0x9Bb6...AF37](https://bscscan.com/address/...)
```

Target:

```text
From: [MEXC](https://bscscan.com/address/...)
```

Rules:

- Always keep the explorer link.
- If label exists, visible text is the label.
- If no label exists, visible text remains shortened address.
- Labels must be MarkdownV2-escaped.
- Aggregated notifications should use labels for every counterparty line.

Tracked wallet display:

- Keep tracked wallet alias as highest priority for the tracked wallet title.
- Address labels may be used as fallback only if there is no user-defined alias.

Counterparty display:

- Use address label first.
- ENS fallback second.
- Short address last.

## Implementation Phases

## Phase 1: Domain And Persistence

Tasks:

- Add `AddressLabel` entity.
- Add `AddressLabelKind` and `AddressLabelSource`.
- Add EF configuration and migration.
- Add repository abstraction and EF implementation.
- Add validation:
  - valid address
  - non-empty label
  - optional chain must be supported
  - label length limit, for example 64 chars

Acceptance criteria:

- Labels can be stored globally or for a specific chain.
- Duplicate active labels for the same `(address, chain)` are rejected.

## Phase 2: In-Memory Cache

Tasks:

- Add `AddressLabelCacheEntry`.
- Add `IAddressLabelCacheService`.
- Add `AddressLabelCacheHydrationService`.
- Hydrate labels at startup.
- Add cache update helper for future import/admin commands.

Acceptance criteria:

- Startup loads all enabled labels into memory.
- Resolver can find chain-specific labels.
- Resolver can fall back to global labels.
- Transaction processing does not query the database for labels.

## Phase 3: Resolver

Tasks:

- Add `IAddressLabelResolver`.
- Implement resolver order:
  - chain-specific cache
  - global cache
  - ENS reverse provider
  - shortened address fallback
- Add result object with display text and source.

Acceptance criteria:

- Chain-specific label overrides global label.
- Global label applies across BSC, Base, Arbitrum, Ethereum, and future EVM networks.
- Missing label returns a safe shortened address result.

## Phase 4: ENS Reverse Provider

Tasks:

- Add ENS reverse provider using Ethereum RPC.
- Cache success, miss, and transient failure.
- Disable provider if Ethereum RPC is not configured.
- Add options for enabling/disabling ENS fallback.

Acceptance criteria:

- Ethereum address with ENS reverse record resolves to name.
- Non-Ethereum chains do not trigger ENS lookup.
- ENS failure does not delay or break notifications.

## Phase 5: Notification Integration

Tasks:

- Inject label resolver into Telegram notification rendering/enrichment.
- Resolve tracked wallet fallback label when no alias exists.
- Resolve counterparty labels.
- Ensure MarkdownV2 escaping covers labels.
- Keep address links unchanged.

Acceptance criteria:

- Known labeled counterparty renders as `[MEXC](explorer-url)`.
- Unknown counterparty renders as shortened address.
- User-defined tracked wallet alias remains preferred over DB label.
- Aggregated notifications render labels on every line.

## Phase 6: Seed Import

Tasks:

- Add CSV seed file format.
- Add import command/tool.
- Add dry-run mode if cheap.
- Add a small initial seed list:
  - exchanges observed in testing
  - common routers/bridges encountered in notifications
- Document how to export Dune labels into the seed CSV.

Acceptance criteria:

- A CSV file can insert/update labels.
- Bad rows are reported without aborting the whole import.
- Imported labels appear in notifications after cache refresh or app restart.

## Phase 7: Tests

Add focused tests:

- Address label entity validation.
- Repository upsert behavior.
- Cache chain-specific lookup.
- Cache global fallback lookup.
- Chain-specific label beats global label.
- Resolver returns shortened address when no label exists.
- ENS provider caches misses/failures.
- Renderer uses labels and escapes MarkdownV2 punctuation.
- Aggregated notification lines use labels independently.

## Phase 8: Documentation

Tasks:

- Update README with label import basics.
- Add seed CSV format docs.
- Add source-quality guidance:
  - prefer Dune/imported curated data
  - prefer official protocol deployment docs for contract/router labels
  - use explorer labels only as manual review hints
  - avoid runtime scraping

## Open Questions

- Should label imports require manual review before becoming enabled?
- Should labels have priority beyond chain-specific vs global?
- Should Telegram admin commands for `/label` and `/unlabel` be part of the next phase?
- Should ENS fallback be enabled by default only when Ethereum is configured?
- Should the app expose `/labels search mexc` for debugging?

## Recommended MVP Cut

Implement only:

1. DB entity, repository, migration.
2. Startup in-memory cache.
3. Cache-backed resolver with global and chain-specific labels.
4. Notification rendering integration.
5. CSV import tool.
6. Small manually reviewed seed file.

Then add ENS fallback as the second pass if the DB/static labels work cleanly.

This keeps the first implementation useful, cheap, and low-risk while preserving the path to richer automatic labeling later.
