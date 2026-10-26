# Polymarket Polygon contract registry and validation record

Status: in-progress
Date: 2026-10-25
Summary: Pinned source families, public read-only decoding evidence and bounded first-release coverage.

## Registry

Chain ID is 137. Runtime registry version is `polygon-2026-10-25.1`; addresses and signatures are committed in `PolymarketContractRegistry`. Sources are the [official deployed registry](https://docs.polymarket.com/resources/contracts), [position model](https://docs.polymarket.com/resources/onchain-position-data), [current CTF source](https://github.com/Polymarket/ctf-exchange-v2/tree/ccc0596074f4dfd62c944fbca4de252893b82b4b), [historical CTF source](https://github.com/Polymarket/ctf-exchange/tree/ed5c7708b7be3aa98bf5f0c6602b57cc498e2ef4), and [Protocol V2 source](https://github.com/Polymarket/polymarket-v2-external/tree/741f8bbe88c3f29a1c55d1248a3dd411f622f111).

| Family | Exchange | Fill ABI | Collateral |
| --- | --- | --- | --- |
| CTF current | `0xe111180000d2663c0091e4f400237545b87b996b` | Current | pUSD |
| CTF negative risk current | `0xe2222d279d744050d28e00520010520000310f59` | Current | pUSD |
| Position V2 | `0xe3333700ca9d93003f00f0f71f8515005f6c00aa` | Current | pUSD |
| CTF historical | `0x4bfb41d5b3570defd03c39a9a4d8de6bd8b8982e` | Historical | USDC.e |
| CTF negative risk historical | `0xc5d563a36ae78145c45a50134d48a1215220f80a` | Historical | USDC.e |

Current event: `OrderFilled(bytes32,address,address,uint8,uint256,uint256,uint256,uint256,bytes32,bytes32)`, topic `0xd543adfd945773f1a62f74f0ee55a5e3b9b1a28262980ba90b1a89f2ea84d8ee`. Indexed fields are order hash, maker, taker; data is side, token ID, maker amount, taker amount, fee, builder and metadata. BUY is side 0; SELL is side 1. Shares/collateral use six decimals. Gross cash divided by shares is execution price; current collateral fee is displayed separately. Thresholds use inclusive gross collateral units, without claiming an independently priced USD valuation. Historical BUY fee units differ, so its fee is not presented as a cash fee.

Historical event: `OrderFilled(bytes32,address,address,uint256,uint256,uint256,uint256,uint256)`, topic `0xd0a08e8c493f9c94f29311604c9de1b4e8c8d4c06bd0c789af57f2d65bfec0f6`. Data is maker asset, taker asset, maker amount, taker amount and fee. Exactly one asset must be collateral ID zero.

Only the followed account's own maker-topic fill is canonical. Current and historical exchange code emits that representation for economic takers as well. Counterparty-topic matches and `OrdersMatched` are not additional activity. Different log indices retain different executions in the same transaction. Own maker=taker context suppresses that account's transaction as a neutral self-trade. V2 module IDs other than binary 1 and negative risk 2 are explicitly unsupported, including combos 3.

## Public read-only evidence

The research role verified actual configured Infura HTTP `eth_chainId=0x89`, deployed exchange code, current/historical collateral getters, pUSD decimals 6, and a real WSS subscription acknowledgment with an exchange-address array and indexed maker-wallet filter. Polygon initially returned HTTP 401 until the user enabled the network in Infura. No provider was silently substituted. `finalized` head worked; `safe` was rejected. Runtime defaults to `FinalityMode=Finalized`; unsupported finalized responses pause normalization rather than silently weakening finality. Explicit `FinalityMode=Confirmations` uses the separately configured confirmation depth.

| Public transaction | Observed settlement |
| --- | --- |
| `0xa9b049ee404e7a44c0d57060637a47b8c0b7b997d15eb0724657dafc55238d6e` | E222 negative-risk own-maker log `0x4e7`: economic taker BUY 41.860466 shares, gross 18 pUSD, collateral fee 0.089990. Counterparty log `0x4e5` is the other participant's SELL and is not a second BUY. |
| `0xb15b92ad7aab080e40a934337a9532ebc677f33017f2a740fe313c10c930786c` | E333 binary V2 own-maker log `0x11a`: economic taker SELL 5 shares, gross 2.5 pUSD, zero fee. |
| `0x2cbdb07c12eeefff1f4f6efd8de02a877a1709d30c502244650b252701757bef` | Historical CTF own-maker log `0x46d`: economic taker SELL 200 shares, gross 40 USDC.e. |
| `0x57f0cbd7acccd7c86551796d2a302181dee8b9a93daa6ea90cd1e847318a2aab` | Direct CTF `PayoutRedemption`, log `0x3bf`, account redeemer, USDC.e, zero parent collection, aggregate payout zero. Losing-position redemption is valid; per-outcome shares are not present in this event. |

These are historical decoding facts from bounded wallet-filtered reads, not natural Telegram delivery evidence. Gamma exact-token lookup aligned token IDs with the market's outcome array; named selections are never automatically suffixed YES/NO.

## Redemption boundaries

Direct CTF `PayoutRedemption` at `0x4d97dcd97ec945f40cf65f87097ace5ea0476045` is accepted only when the indexed redeemer is the followed account, the parent collection is zero and collateral is recognized. It yields an aggregate condition payout, without invented outcome shares or profit.

The official AutoRedeemer at `0xa1200000d0002264c9a1698e001292d00e1b00af` has wallet-indexed `Redemption`, `BinaryRedemption` and `NegRiskRedemption` events. Source code establishes holder-to-module correlation: it pulls the named holder's positions, redeems to itself and forwards/wraps payout to that holder as pUSD. The dedicated own-from event is canonical; module recipient events are not duplicate redemptions. Deployed proxy bytecode was observed, but no historical or natural automatic-redemption example was exercised in this implementation pass.

Generic module/router redemption is not inferred from recipient or initiator alone. Wallet-filtered transfer/module context is retained and counted as unsupported when holder/payout attribution is ambiguous. Standalone transfers, splits, merges, conversions and funding are not trade alerts. No global contract firehose, receipt fetch or native block scanner is used.

## Runtime boundaries

Profile lookup uses the existing `Polymarket.Enabled` flag. Acquisition/delivery additionally require `PolymarketPolygon.Enabled`, default false. Polygon uses the shared Infura key unless an explicit protocol key is supplied. It does not depend on generic Polygon token tracking or change other chain settings.

One dedicated PostgreSQL session lock owns ingestion; per-recipient locks protect delivery across hosts. All six subscriptions must acknowledge, and both HTTP and WSS must identify Polygon. Live frames persist through a bounded channel before bounded filtered recovery; raw identities include block hash, transaction hash and log index. Recovery covers all filters before advancing each durable block checkpoint. Request/range/horizon limits retain resumable progress and visible gaps. Quiet accounts use pushed heads/logs, not recurring `eth_getLogs` polling. The enrollment boundary is the latest sealed block obtained at enablement: only later blocks qualify, even for another follower of an existing source. Late-arriving logs from that sealed block are pre-enrollment activity.

Default notifications wait for the verified Polygon `finalized` head and canonical block-hash validation. A coordinator read-only probe observed the finalized head two blocks behind the latest head; actual end-to-end latency remains to be measured during rollout. The explicit confirmation-depth mode defaults to 128 blocks when selected. Reconnect validates saved hashes and rewinds retained ancestry. Rewind invalidates only hashes proven noncanonical, preserving already-buffered replacement-branch logs. Removed/orphaned raw work invalidates unsent deliveries and creates durable correction work for sent activity. Reorg scenarios have not been exercised against a live reorg in this pass. Corrections remain subject to owner/source/generation eligibility.

Gamma misses coalesce in bounded positive/negative caches. A normalization batch has a bounded enrichment time budget; unavailable metadata retains raw work until its configured wait expires, then verified quantities use safe position IDs. An outcome/question is never guessed. Recipient generation, ownership, binding, global flags and current minimum are checked again after shared Telegram limiter waits. External accepted-send/crash ambiguity remains possible.

## Initial implementation checks (historical stage)

The following records the first implementation pass. The later [completion and live rollout](polymarket-polygon-tracking-implementation-brief.md#completion-and-live-rollout-2026-10-25) supersedes its pending-stage statements.

- Runtime `dotnet build src/Presentation.Telegram/Presentation.Telegram.csproj --no-restore --disable-build-servers -m:1 -v minimal` passed. Normal parallel builds returned failure without compiler errors in this environment; single-node compilation succeeds. Existing NU1902/NU1903 warnings concern transitive `System.Security.Cryptography.Pkcs` 9.0.0.
- Generated EF migration `20261025181509_AddPolymarketPolygonSettlement` with `dotnet ef migrations add`; generated additive upgrade SQL, checked for destructive operations. No live database was mutated.
- Normal and debug Compose config validation passed against `.env.example`. A synthetic APPDATA/user-secrets export check passed without loading real secrets. Export now correctly accepts an absolute output path.
- No mocked tests or test suite were run, following the user's live-validation preference. Fresh integration, independent review, actual schema application, natural Telegram delivery, restart/overlap, owner lifecycle, reorg and latency validation remain coordinator stages. No live settings, `.env`, infrastructure or Telegram recipients were changed by this implementation pass.

Subsequent integration/review added effective sorted-family checkpoint identity, hash-proven canonical resurrection with sent-correction handling, shared advisory locking for normalization/orphan commits, bounded terminal-event retention and backlog/coverage logs. Live HTTP 429 evidence added shared RPC pacing/cooldown and separated adaptive log-range errors from throttling. Normal RPC spacing defaults to 250 ms, with at least 1,000 ms after a log query; project limits may still delay recovery. The rollout record documents observed delivery and unexecuted edge cases.
