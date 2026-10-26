# Hyperliquid notification layout

Status: completed
Date: 2026-10-20
Summary: Match the existing compact explorer-linked Telegram alerts, using the authorized May 28 Drops Bot references and Hypurrscan.

## Requirements

- Linked wallet alias to https://hypurrscan.io/address/{address}, compact `HL` header and the existing pencil edit deep link.
- Compact trade line with accurate position effect, size, market, approximate USD notional, native perpetual venue and weighted execution price. Preserve supplied realized PnL without inventing percentages or entry prices.
- Link valid nonzero transaction hashes to https://hypurrscan.io/tx/{hash}; deduplicate aggregate links, cap the displayed list and use an honest wallet-activity fallback when absent. Keep source identities unchanged. Persist optional source hash/effect detail in existing JSON payloads, compatible with old events.
- Keep deposits, withdrawals and liquidation facts readable, without raw JSON or fabricated settlement confirmation. No spot/HIP-3/TWAP coverage expansion.
- Escape provider/alias content for Telegram HTML, preserve owner-specific preferences, aggregation eligibility and shared rate limits. Rendering belongs in Presentation behind Application contracts.
- Reference chat access is read-only. Do not copy private reference aliases/addresses or messages into repository artifacts. Use anonymous fixtures.

## Validation and delivery

Follow repository single-writer implementation, fresh integration and independent review. Check layout/link/escaping/legacy payload cases with focused checks as useful; the user prefers live Telegram verification rather than mock-driven testing. Deploy the small update to the existing local Docker bot and observe a naturally occurring public-wallet alert through @EVMWhaleTrackerBot, then remove the temporary enrollment and preserve original wallets. Verify actual Hypurrscan links and pencil navigation through the UI. Record actual results and limits here.

## Implementation handoff

The Application recipient envelope now carries wallet identity and structured activities to Presentation. Optional source transaction hash and position-side metadata remain in existing JSON payloads; old rows keep generic effect labels and wallet links. Source IDs and database schema are unchanged. Delivery aggregation separates position sides as well as effects, and the existing eligibility checks and shared Telegram limiter remain in use.

Presentation renders the linked alias, `HL` header, pencil deep link, compact perpetual trade line, weighted execution price, supplied realized P&L, and readable ledger facts. Full nonzero source hashes link to Hypurrscan; aggregate links are deduplicated and capped at five with wallet-activity fallback. Provider text and aliases are HTML escaped.

Validation at first implementation handoff: `dotnet build src/Presentation.Telegram/Presentation.Telegram.csproj --no-restore -m:1 -v:minimal` succeeded with zero errors and four existing NU1902/NU1903 dependency warnings. An earlier solution build exited unsuccessfully with zero reported warnings/errors. No synthetic or mock tests were executed, following the user's live-verification preference. Fresh integration, independent review and coordinator live rollout remain pending.


## Integration, review and live verification

Completed 2026-10-20. A fresh integration pass corrected negative-money formatting, HTML entity escaping, precise sub-dollar execution prices, malformed liquidation-fact shape handling and the sender timeout around username resolution. Independent review found no actionable defects and rechecked the final PnL polish.

The coordinator ran `dotnet build EVMWhaleTracker.slnx --no-restore -m:1`; the solution and existing fixture adapters compile with zero errors. Existing dependency audit warnings remain. No mock/isolated suites or synthetic Telegram messages were run for this change, per the user's live-verification preference. No schema migration was needed.

Deployed the production Docker app and observed natural BTC fills through @EVMWhaleTrackerBot using one temporary public wallet. Live alerts showed the linked wallet | HL | pencil header, accurate increased/reduced long/short and flip labels, compact size/approximate notional/execution-price lines, actual Hypurrscan transaction links and a wallet-activity fallback for zero hashes. A grouped live alert displayed numbered transaction links. Exact-zero realized PnL is omitted; nonzero PnL below one cent preserves decimal precision rather than showing signed zero. Both positive and negative larger realized PnL appeared correctly in final live alerts.

Opened an actual alert transaction URL on Hypurrscan and verified its Transaction Details page; opened its wallet URL and verified the address page. Clicking the actual Telegram pencil opened the correct temporary wallet editor. Only public source addresses/hashes were used for explorer navigation; the reference conversation was read without sending messages or changing that bot's settings. Reference aliases and private message content were not copied into repository artifacts.

Turned tracking off through Telegram and removed the temporary wallet. Final audit: the same three original wallet rows and identical full-row digest; zero enabled Core settings, zero reservations and zero pending delivery work. The deployed app was running with zero automatic restarts. Actual-message screenshots are held in private session artifacts rather than the repository.

Limits: this live window did not contain ledger deposit/withdrawal/liquidation events, a true full-position close, or a sub-cent realized PnL after the final precision polish; those paths were inspected in code, not claimed live-verified. No spot, HIP-3 or TWAP coverage was added. Previously stored JSON payloads without a hash or position side use honest wallet links and generic effect labels.
