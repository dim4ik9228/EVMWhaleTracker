# Tracking Pipeline

## Active Pipeline

The MVP currently tracks token transfers through websocket log subscriptions:

```text
Tracked wallet cache
        |
        v
TokenTransferLogSubscriberWorker
        |
        v
Channel<RawBlockData> with token logs
        |
        v
WalletTrackerWorker
        |
        v
Channel<NotificationPayload>
        |
        v
NotificationDispatcherWorker
        |
        v
Telegram
```

`TokenTransferLogSubscriberWorker` subscribes to ERC-20/BEP-20 `Transfer(address,address,uint256)` logs. It creates filters where the tracked wallet appears in either indexed topic:

- `topics[1]`: token sender
- `topics[2]`: token receiver

When a matching log arrives, the worker wraps it in `RawBlockData` with an empty transaction list and writes it to the existing pipeline. `WalletTrackerWorker` then parses the log, resolves metadata/pricing, applies wallet settings, and emits a notification payload.

## Disabled Native Pipeline

Native BNB/ETH transfer tracking is available in the codebase but disabled at runtime.

The disabled path is:

```text
BlockchainIndexerWorker
        |
        v
NethereumBlockchainBlockSource
        |
        v
new block -> fetch block transactions -> match tx.from / tx.to
```

This path is reliable for native transfers because native transfers live in transaction envelopes. It is also more expensive because standard EVM RPC cannot subscribe to native transfers by wallet address. A `newHeads` subscription only announces new blocks; the app still has to fetch block transactions to inspect `from`, `to`, and `value`.

## Why Token Tracking Is Different

ERC-20 transfers emit indexed logs, so the node can filter them by wallet address and push only matching events. Native transfers do not emit standard logs, so there is no equivalent cheap `logs` subscription for BNB/ETH movement.

## Rules Of Thumb

- Use websocket log subscriptions for token transfers.
- Avoid per-block `eth_getLogs` polling for tracked wallets.
- Avoid per-transaction receipt fetching.
- Re-enable native scanning only when native transfer alerts are worth the extra RPC load.
