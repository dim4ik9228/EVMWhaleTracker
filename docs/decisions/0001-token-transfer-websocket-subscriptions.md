# 0001 Use WebSocket Log Subscriptions For Token Transfers

## Status

Accepted

## Context

Incoming ERC-20/BEP-20 transfers do not appear as transactions where the tracked wallet is `tx.to`. The transaction usually targets the token contract, and the tracked wallet appears only in the emitted `Transfer` log.

Fetching every transaction receipt or polling `eth_getLogs` for every block creates too much RPC load, even for quiet wallets.

## Decision

Track token transfers with websocket log subscriptions filtered by ERC-20 `Transfer` topics:

- `topics[1]` for tracked-wallet senders
- `topics[2]` for tracked-wallet receivers

Disable native block scanning in the active runtime for now. Keep `NethereumBlockchainBlockSource` and `BlockchainIndexerWorker` in the codebase so native transfer tracking can be re-enabled later.

## Consequences

- API usage drops sharply because quiet tracked wallets do not cause per-block token log queries.
- Incoming token transfers are detected correctly.
- Native BNB/ETH transfers are not detected while native scanning is disabled.
- Re-enabling native transfer tracking requires accepting block-fetching RPC cost or using an external indexed provider.
