# Notification Enrichment Implementation Plan

This plan covers the roadmap objectives:

- Minimum transaction value filtering
- ERC-20 transfer event matching
- Token metadata resolution
- USD valuation on notifications
- Telegram Markdown escaping
- Drops-style Telegram notification layout
- Transaction explorer links

Inline notification editing is intentionally out of scope for this plan.

## Target Telegram Notification

The notification should be compact, readable on mobile, and link the important entities to the correct chain explorer.

Example shape:

```text
[debabki.eth](wallet-explorer-url) | BNB
Received: 252.39 USDT (~$252) From: [0x09...1ed7](counterparty-explorer-url)
[Tx hash](transaction-explorer-url)
```

Rendered intent:

- First line: tracked wallet alias if available, otherwise shortened tracked wallet address. This text links to the tracked wallet on the active chain explorer.
- First line suffix: short chain display name, for example `ETH`, `BNB`, `Base`, `Arb`, or `Polygon`.
- Second line: operation, amount, token/native symbol, USD estimate, and counterparty.
- Counterparty: use a known/manual alias when available; otherwise use a shortened address. The visible text links to the counterparty wallet on the same chain explorer.
- Third line: transaction hash link. The visible text can be `Tx hash` or a shortened transaction hash, depending on final UX preference.

## Key Design Decisions

1. Move notification formatting out of `Infrastructure.Workers.WalletTrackerWorker`.

   The worker should detect activity and publish structured data. `Presentation.Telegram` should render Telegram-specific Markdown. This keeps blockchain processing independent from Telegram formatting rules.

2. Treat minimum transaction filtering as a USD threshold for enriched notifications.

   The existing `MinimumTransactionValue` name is ambiguous once native and ERC-20 transfers are both supported. For this feature, the recommended model is `MinimumUsdValue` or a clearly documented migration of `MinimumTransactionValue` to USD semantics. USD filtering gives one consistent threshold across ETH, BNB, USDT, WETH, and other assets.

3. Use MarkdownV2 for notification messages.

   MarkdownV2 supports the desired hidden links through `[text](url)` and requires strict escaping. A dedicated escaping helper should be used for all visible text and URLs.

4. Keep valuation failures non-fatal.

   If pricing is unavailable, the notification can still be sent with the token/native amount and omit the USD segment. If a wallet has a non-zero USD threshold and USD valuation is unavailable, skip the notification by default and log the skip reason.

5. Keep chain support explicit.

   Every raw block, transfer match, explorer URL, token metadata lookup, and notification must carry `SupportedChain`.

## Proposed Data Contracts

### Chain Metadata

Add a small chain catalog in `Application` or `Infrastructure`, depending on whether it only describes application concepts or also contains explorer URL construction.

Suggested contract:

```csharp
public sealed record ChainMetadata(
    SupportedChain Chain,
    string DisplayName,
    string ShortName,
    string NativeSymbol,
    int NativeDecimals,
    string ExplorerBaseUrl);
```

Required URL helpers:

```csharp
string GetAddressUrl(SupportedChain chain, string address);
string GetTransactionUrl(SupportedChain chain, string transactionHash);
string GetTokenUrl(SupportedChain chain, string tokenContractAddress);
```

Initial explorer examples:

- Ethereum: `https://etherscan.io`
- BSC: `https://bscscan.com`
- Base: `https://basescan.org`
- Arbitrum: `https://arbiscan.io`
- Optimism: `https://optimistic.etherscan.io`
- Polygon: `https://polygonscan.com`
- Gnosis: `https://gnosisscan.io`
- Celo: `https://celoscan.io`

### Raw Blockchain Messages

Update the raw pipeline data so each message knows its chain.

```csharp
public sealed record RawBlockData(
    SupportedChain Chain,
    ulong BlockNumber,
    string BlockHash,
    DateTimeOffset BlockTimestampUtc,
    IReadOnlyList<RawTransactionData> Transactions,
    IReadOnlyList<RawLogData> Logs);
```

For native transactions, keep `Value` as a normalized native-token amount for now. If raw wei precision becomes important, add `BigInteger ValueWei` and calculate display amounts later with decimals.

For logs, keep the current shape but document that:

- `Topics[0]` is the event signature topic.
- `Topics[1]` and `Topics[2]` are indexed addresses for standard ERC-20 transfers.
- `Data` contains the raw `uint256` transfer value.

### Structured Notification Payload

Replace or extend the current `NotificationPayload.MarkdownMessage` contract with structured fields.

Suggested contract:

```csharp
public sealed record NotificationPayload(
    long TelegramUserId,
    SupportedChain Chain,
    NotificationOperation Operation,
    string TrackedWalletAddress,
    string? TrackedWalletAlias,
    string? CounterpartyAddress,
    string? CounterpartyAlias,
    string TransactionHash,
    string AssetSymbol,
    string? AssetName,
    string? TokenContractAddress,
    decimal Amount,
    decimal? UsdValue,
    ulong BlockNumber,
    DateTimeOffset BlockTimestampUtc);

public enum NotificationOperation
{
    Received,
    Sent,
    SelfTransfer
}
```

The dispatcher or a dedicated renderer should convert this into Telegram text.

### Cached Tracked Wallet Snapshot

The current cache can only return Telegram user ids for an address. Filtering needs wallet-level settings and aliases.

Add a read model:

```csharp
public sealed record TrackedWalletCacheEntry(
    Guid Id,
    long TelegramUserId,
    Address Address,
    decimal MinimumUsdValue,
    bool TrackIncomingTransfers,
    bool TrackOutgoingTransfers,
    bool TrackTokenTransfers,
    IReadOnlyCollection<SupportedChain> EnabledChains,
    string? Alias);
```

Add cache query methods:

```csharp
bool TryGetEntries(Address address, out IReadOnlyCollection<TrackedWalletCacheEntry> entries);
```

`WalletTrackerWorker` should process each matching cache entry independently because two users can track the same address with different aliases, thresholds, and direction settings.

## Implementation Phases

## Phase 1: Chain Context And Explorer Links

Goal: ensure every match can be tied to a chain and explorer URL.

Tasks:

- Add `SupportedChain Chain` to `RawBlockData`.
- Add `SupportedChain Chain` to `BlockchainIndexerOptions`, defaulting to the currently configured network.
- Update `NethereumBlockchainBlockSource` to populate `RawBlockData.Chain`.
- Add a chain metadata/explorer URL service.
- Add tests for address, transaction, and token explorer URL generation.

Acceptance criteria:

- A block produced by the indexer has a chain value.
- A notification can build wallet and transaction links for every supported chain.
- Unknown or unsupported chains fail with an explicit `Result` or configuration validation error.

## Phase 2: Cache Entries For Filtering And Aliases

Goal: make the hot-path worker able to apply user-specific settings without database calls.

Tasks:

- Add `TrackedWalletCacheEntry`.
- Update `ITrackedWalletCacheService` to return entries for a normalized address.
- Update `InMemoryCacheService` internals from address-to-user-id maps to address-to-cache-entry maps.
- Preserve `Contains` if tests or simpler flows still use it, but make matching use `TryGetEntries`.
- Include alias, enabled chains, transfer direction flags, and minimum USD value in each cache entry.
- Update startup cache hydration and add/remove/settings handlers to write full cache entries.

Acceptance criteria:

- Multiple users tracking the same wallet can receive different notification text and filtering behavior.
- Alias changes are reflected in notifications after cache refresh.
- Wallets with chain restrictions are ignored on non-enabled chains.

## Phase 3: Native Transfer Filtering

Goal: apply direction settings and threshold filtering to native transfers.

Tasks:

- Replace `ProcessSingleAddressAsync` with logic that builds a candidate native transfer match:
  - tracked address
  - counterparty address
  - operation: `Received`, `Sent`, or `SelfTransfer`
  - native amount
  - chain
  - transaction hash
- Apply `TrackIncomingTransfers` and `TrackOutgoingTransfers`.
- Apply `EnabledChains`:
  - empty collection means all chains.
  - non-empty collection must contain the block chain.
- Resolve native asset metadata from chain metadata:
  - BNB on BSC
  - ETH on Ethereum/Base/Arbitrum/Optimism
  - MATIC/POL on Polygon, depending on the final chain metadata decision
- Get native USD price before applying `MinimumUsdValue`.
- Skip matches below the threshold.

Acceptance criteria:

- Incoming native transfer is skipped when incoming tracking is off.
- Outgoing native transfer is skipped when outgoing tracking is off.
- Transfer below the wallet threshold is skipped.
- Transfer at or above the wallet threshold emits a structured notification.

## Phase 4: ERC-20 Transfer Event Matching

Goal: detect ERC-20 transfers where a tracked wallet appears in the `from` or `to` indexed event topics.

Standard ERC-20 transfer topic:

```text
0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef
```

Matching rules:

- `Topics.Count >= 3`
- `Topics[0]` equals the standard transfer signature topic, case-insensitive.
- `Topics[1]` decodes to the `from` address.
- `Topics[2]` decodes to the `to` address.
- `Data` decodes to the raw token amount as `uint256`.
- `ContractAddress` is the token contract address.

Tasks:

- Add an `Erc20TransferLogParser`.
- Normalize decoded addresses through `Address.Normalize`.
- For each decoded `from` and `to` address, get matching cache entries.
- Apply `TrackTokenTransfers`.
- Apply incoming/outgoing direction settings.
- Apply enabled chain restrictions.
- Resolve token metadata before formatting.
- Convert raw token amount using token decimals.
- Resolve USD valuation and apply minimum USD threshold.

Acceptance criteria:

- A tracked address in `Topics[2]` produces a `Received` token transfer notification.
- A tracked address in `Topics[1]` produces a `Sent` token transfer notification.
- Non-transfer logs are ignored.
- Malformed topics or data are ignored with debug logging, not exceptions.
- Token transfer matching does not match only on `ContractAddress`; it must match decoded `from` or `to`.

## Phase 5: Token Metadata Resolution

Goal: display token amounts correctly and avoid raw integer values in notifications.

Add an application abstraction:

```csharp
public interface ITokenMetadataProvider
{
    Task<Result<TokenMetadata>> GetAsync(
        SupportedChain chain,
        string tokenContractAddress,
        CancellationToken cancellationToken);
}

public sealed record TokenMetadata(
    SupportedChain Chain,
    string ContractAddress,
    string Symbol,
    string? Name,
    int Decimals);
```

Infrastructure implementation options:

- Query ERC-20 `symbol()`, `name()`, and `decimals()` using Nethereum.
- Cache successful results in `IMemoryCache`.
- Cache failed lookups briefly to avoid repeated RPC pressure.
- Provide a small static override list for tokens with non-standard metadata responses if needed.

Important behavior:

- If `symbol()` fails, use `TOKEN`.
- If `decimals()` fails, default to `18` only as a fallback and log a warning.
- Always include token contract address in structured payload for debugging and future token links.

Acceptance criteria:

- USDT-like 6-decimal transfers display human amounts correctly.
- Metadata lookup is cached.
- Metadata RPC failure does not crash the wallet tracker.

## Phase 6: USD Valuation

Goal: include `~$value` in notifications and support USD threshold filtering.

Add an application abstraction:

```csharp
public interface IAssetPriceProvider
{
    Task<Result<AssetUsdPrice>> GetUsdPriceAsync(
        SupportedChain chain,
        string? tokenContractAddress,
        string assetSymbol,
        CancellationToken cancellationToken);
}

public sealed record AssetUsdPrice(
    decimal PriceUsd,
    DateTimeOffset QuotedAtUtc,
    string Source);
```

Provider strategy:

- MVP option: use a configurable HTTP pricing provider such as CoinGecko, DexScreener, or a chain-specific explorer API.
- Keep the provider behind an interface so it can be swapped later.
- Cache prices briefly, for example 30 to 120 seconds.
- Native assets use `tokenContractAddress = null`.
- ERC-20 assets use the token contract address where the pricing API supports it.

Filtering behavior:

- Calculate `usdValue = amount * priceUsd`.
- If `MinimumUsdValue <= 0`, do not require a price to send the notification.
- If `MinimumUsdValue > 0` and price resolution fails, skip the notification and log the reason.
- If `usdValue < MinimumUsdValue`, skip the notification.

Formatting behavior:

- Show whole dollars for values under normal whale-alert usage, for example `~$252`.
- Preserve cents for small values if needed, for example `~$0.42`.
- Use invariant culture internally and a deterministic formatter for display.

Acceptance criteria:

- Native transfer notifications can include USD value.
- ERC-20 transfer notifications can include USD value.
- Transfers below minimum USD threshold are skipped.
- Pricing outages do not stop workers.

## Phase 7: Telegram MarkdownV2 Rendering

Goal: generate the desired compact message safely.

Add a renderer in `Presentation.Telegram`, for example:

```csharp
public interface ITelegramNotificationRenderer
{
    TelegramRenderedMessage Render(NotificationPayload payload);
}

public sealed record TelegramRenderedMessage(
    string Text,
    ParseMode ParseMode);
```

Use `ParseMode.MarkdownV2` for notifications.

Escaping requirements:

- Escape all MarkdownV2 reserved characters in visible text:
  - `_`, `*`, `[`, `]`, `(`, `)`, `~`, `` ` ``, `>`, `#`, `+`, `-`, `=`, `|`, `{`, `}`, `.`, `!`
- Escape or encode URLs before inserting them into link targets.
- Do not concatenate unescaped aliases, symbols, chain names, or addresses into Markdown.
- Add unit tests with aliases containing punctuation, brackets, underscores, dots, and pipe characters.

Renderer helpers:

```csharp
string EscapeText(string value);
string EscapeUrl(string value);
string Link(string visibleText, string url);
string ShortAddress(string address);
string ShortTransactionHash(string transactionHash);
```

Template:

```text
{trackedWalletLink} | {chainShortName}
{operation}: {amount} {assetSymbol}{usdPart} {counterpartyRole}: {counterpartyLink}
{txHashLink}
```

Rules:

- `operation` is `Received`, `Sent`, or `Self`.
- `counterpartyRole` is `From` for received transfers and `To` for sent transfers.
- If counterparty alias exists, use it as visible text; otherwise use shortened address.
- If tracked wallet alias exists, use it as visible text; otherwise use shortened address.
- `txHashLink` visible text defaults to `Tx hash`.
- The chain separator remains visible as `|` after escaping.

Acceptance criteria:

- The rendered message parses successfully with Telegram MarkdownV2.
- Aliases with special Markdown characters do not break delivery.
- Address and transaction links are hidden behind readable text.
- The final layout matches the target compact three-line structure.

## Phase 8: Dispatcher Changes

Goal: let the dispatcher send rendered messages with the correct parse mode.

Tasks:

- Inject `ITelegramNotificationRenderer` into `NotificationDispatcherWorker`.
- Change the dispatcher to render structured `NotificationPayload` just before sending.
- Use `renderedMessage.ParseMode` instead of hard-coded `ParseMode.Markdown`.
- Keep the existing global and per-chat rate limits.
- Continue logging wallet address and transaction hash after sending.
- Add retry handling for Telegram `RetryAfter` as a follow-up if not implemented in this pass.

Acceptance criteria:

- Notification dispatcher sends MarkdownV2 messages.
- Existing command responses can continue using HTML independently.
- Telegram formatting failure is logged with enough context to debug the payload.

## Phase 9: Tests

Add focused coverage at each layer.

Domain/Application:

- `TrackedWallet` rejects negative minimum USD values.
- Cache entries preserve alias, chain settings, direction settings, and threshold.

Infrastructure:

- Native incoming transfer emits a payload when enabled.
- Native outgoing transfer emits a payload when enabled.
- Native transfer below minimum USD threshold is skipped.
- Incoming/outgoing flags are respected.
- Chain restrictions are respected.
- ERC-20 transfer log parser decodes `from`, `to`, and raw value.
- ERC-20 logs with tracked `to` emit received payloads.
- ERC-20 logs with tracked `from` emit sent payloads.
- Non-transfer and malformed logs are ignored.
- Token metadata provider caches successful lookups.

Presentation.Telegram:

- MarkdownV2 escaping handles aliases like `debabki.eth`, `my_wallet`, `[vault]`, `a|b`, and `name(1)`.
- Renderer uses alias links when alias exists.
- Renderer falls back to shortened addresses.
- Renderer omits USD part when valuation is missing and no threshold requires it.
- Renderer includes transaction explorer link.

End-to-end worker test:

- Given a block with a USDT transfer to a tracked wallet alias on BSC, assert the notification renders like:

```text
[debabki.eth](...) | BNB
Received: 252.39 USDT (~$252) From: [0x09...1ed7](...)
[Tx hash](...)
```

## Suggested Implementation Order

1. Add chain metadata and explorer URL generation.
2. Add full tracked-wallet cache entries.
3. Convert `NotificationPayload` from pre-rendered Markdown to structured data.
4. Implement native transfer filtering with USD threshold support.
5. Implement ERC-20 transfer log parsing.
6. Add token metadata provider with in-memory caching.
7. Add USD price provider with short-lived caching.
8. Add Telegram MarkdownV2 renderer and escaping tests.
9. Update dispatcher to render and send MarkdownV2.
10. Update roadmap checkboxes after tests pass.

## Open Decisions Before Coding

- Whether to rename `MinimumTransactionValue` to `MinimumUsdValue` now, or keep the old name with documented USD semantics until a later migration.
- Which pricing provider to use for MVP.
- Whether BNB Chain should display as `BNB`, `BSC`, or `BNB Chain` in the first line.
- Whether the transaction link text should always be `Tx hash` or a shortened transaction hash.
- Whether token metadata and price lookups should be disabled by configuration for local tests.

