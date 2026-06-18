# Inline Notification Editing Implementation Plan

Status: implemented
Date: 2026-06-18

Implemented: 2026-06-18

Implementation notes:

- Notifications and `/list` entries expose a Telegram deep-link pencil using `edit_wallet_{walletId:N}`.
- Deep links open a compact tracked-wallet settings menu and best-effort delete the generated `/start ...` message.
- Callback queries drive overview, configured-network selection, rename prompt, minimum USD prompt, and delete confirmation.
- Focused Application commands update alias, minimum USD, chain preferences, and deletion by wallet id while refreshing the tracked-wallet cache.
- Pending text actions are generalized for add aliases, wallet rename, and minimum USD edits.
- Transfer direction and token-filter editing remain out of scope for this plan.

Add Drops-style inline editing for tracked wallets directly from Telegram notifications. The first cut should support editing the wallet name, enabled networks, minimum USD transaction value, and deletion. Direction/token filters and favorites stay out of scope for now.

## Goals

- Attach an edit affordance to each wallet activity notification.
- Open a tracked-wallet settings menu from that notification.
- Let users update:
  - networks
  - alias/name
  - minimum transaction USD value
  - delete tracked wallet
- Keep all mutations in Application/Domain, with Telegram only orchestrating UI state.
- Avoid offering networks that are not currently configured for tracking.

## Non-Goals

- Favorite wallets.
- Incoming/outgoing/token filter editing.
- Admin-only label editing.
- Rich pagination/search across wallets.
- Web app settings pages.

## UX Shape

### Notification

Target notification header can visually include a pencil:

```text
H whale · ETH | ✏️
Received: 3,309.511 H (~$2,389,798) From: Gate.io
Tx hash
```

The pencil is not an inline keyboard button. Render it as a MarkdownV2 hyperlink that points to the bot's deep-link `/start` URL:

```markdown
H whale · ETH | [✏️](https://t.me/EvmWhaleTrackerBot?start=edit_wallet_4f7c0e...)
```

Telegram clients show this as a small clickable pencil in the notification text. When the user taps it, Telegram sends the bot a normal private-chat message:

```text
/start edit_wallet_4f7c0e...
```

The bot should handle that `/start` payload, immediately delete the user-generated `/start ...` message when possible, and leave only the bot's settings menu response visible.

The deep link should use a compact wallet token rather than the raw wallet address. A `Guid` in `N` format is 32 characters and fits comfortably after an `edit_wallet_` prefix.

### Wallet Settings Menu

When the user taps edit, send or edit a bot message that looks like:

```text
Main
debabki.eth · ETH · BNB · Base · Arbitrum
0xb16c0c0f98b77129c27a7345c3677d2e6a32cb23
Minimum USD: $0
```

Inline keyboard:

```text
[ 💵 Min USD ] [ 🛰 Networks ]
[ ✏️ Rename ]
[ 🗑 Delete ]
[ ↩️ Back ]
```

The first implementation should send a separate settings message instead of editing the original notification. Notifications are historical activity records; changing them into menus would make old alerts harder to read.

### Networks Menu

Show only currently enabled runtime networks, not every `SupportedChain` enum value. Today that means Ethereum, BSC, Base, and Arbitrum in the normal local setup. The enum contains future or catalog-only chains, and showing them before workers/RPCs are enabled would let users select networks that cannot notify.

Menu text:

```text
Choose ✅ Networks to add and press ☑️ Done:
```

Buttons:

```text
[ ✅ ETH ] [ ✅ BNB ]
[ ✅ Base ] [ ✅ Arbitrum ]
[ Deselect All Networks ]
[ ❌ Cancel ] [ ☑️ Done ]
```

Rules:

- `EnabledChains.Count == 0` means all configured networks are enabled.
- In the menu, empty enabled chains renders as all configured networks checked.
- Tapping a chain uses idempotent `SetChainTracking(chain, enabled)` semantics.
- Do not allow saving zero enabled chains.
- If every configured network becomes enabled, the domain should collapse storage back to empty enabled chains, preserving the existing "empty means all" convention.

### Rename Flow

Tap `✏️ Rename`, then bot prompts:

```text
Send a new name for this wallet.
```

The next private text message from that Telegram user is consumed as the alias. Reuse the same idea as the existing pending alias flow, but generalize it so the bot can track pending edit actions.

### Minimum USD Flow

Tap `💵 Min USD`, then bot prompts:

```text
Send the minimum transaction USD value.
Use 0 to notify on every matched transaction.
```

The next private text message is parsed as an invariant-culture/current-culture decimal. Negative values are rejected. On success, update the settings menu.

### Delete Flow

Do not delete immediately from the first button tap. Show confirmation:

```text
Delete debabki.eth?
0xb16c...cb23
```

Buttons:

```text
[ 🗑 Delete ] [ ❌ Cancel ]
```

On confirm, remove the tracked wallet and refresh cache.

## Deep-Link And Callback Data

Telegram deep-link payloads and callback data are both size-constrained. Prefer wallet ids instead of raw addresses. Use `Guid.ToString("N")` to avoid hyphens and keep payloads compact.

Suggested notification deep-link payload:

```text
edit_wallet_{walletId:N}
```

Suggested menu callback format:

```text
tw:v1:n:{walletId:N}              // open networks menu
tw:v1:nt:{walletId:N}:{chainId}:1 // enable chain
tw:v1:nt:{walletId:N}:{chainId}:0 // disable chain
tw:v1:r:{walletId:N}              // prompt rename
tw:v1:m:{walletId:N}              // prompt minimum USD
tw:v1:d:{walletId:N}              // delete confirmation
tw:v1:dc:{walletId:N}             // delete confirmed
tw:v1:b:{walletId:N}              // back/close
```

Every deep-link or callback handler must:

- Use `Message.From.Id` or `CallbackQuery.From.Id` as the Telegram user id.
- Load the wallet by `walletId`.
- Verify `wallet.UserId == user id`.
- Answer callback queries to stop Telegram's loading spinner.
- Delete the generated `/start edit_wallet_*` user message when handling a deep link, ignoring delete failures.
- Return a generic "wallet not found" response if the wallet is missing or owned by another user.

## Application Changes

Add focused CQRS operations rather than routing everything through `UpdateTrackedWalletSettingsCommand`:

- `GetTrackedWalletQuery(long telegramUserId, Guid walletId)`
- `UpdateTrackedWalletAliasCommand(long telegramUserId, Guid walletId, string? alias)`
- `UpdateTrackedWalletMinimumUsdValueCommand(long telegramUserId, Guid walletId, decimal minimumUsdValue)`
- `SetTrackedWalletChainTrackingCommand(long telegramUserId, Guid walletId, SupportedChain chain, bool enabled)`
- `RemoveTrackedWalletByIdCommand(long telegramUserId, Guid walletId)`

Handler pattern:

1. Load by id through `ITrackedWalletRepository.GetByIdAsync`.
2. Verify ownership.
3. Call the focused domain method.
4. Save changes.
5. Refresh `ITrackedWalletCacheService`.
6. Return `TrackedWalletDetails`.

Existing address-based commands can remain for slash commands.

## Notification Payload Changes

`NotificationPayload` and `AggregatedNotificationPayload` need `TrackedWalletId`.

Changes:

- Add `Guid TrackedWalletId` to `NotificationPayload`.
- Add `Guid TrackedWalletId` to `AggregatedNotificationPayload`.
- Populate it from `TrackedWalletCacheEntry.Id` in `WalletTrackerWorker`.
- Preserve it in `NotificationAggregationWorker.PendingNotification`.
- Include it in aggregation keys if needed. Current key already uses user id, chain, address, and transaction hash, so id is mostly needed for callback targeting, not grouping.

## Presentation Changes

### Command Response Model

`TelegramCommandResponse` currently only carries HTML text. Inline menus need reply markup.

Extend it to something like:

```csharp
public sealed record TelegramCommandResponse(
    string Text,
    ParseMode ParseMode = ParseMode.Html,
    IReplyMarkup? ReplyMarkup = null);
```

Or create a Telegram-specific response type used by both message commands and callback handlers.

### Update Receiver

Current receiver allows only message updates:

```csharp
AllowedUpdates = new[] { UpdateType.Message }
```

Keep message updates because deep links arrive as `/start <payload>` messages, and add callback query updates for menu buttons:

```csharp
AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
```

`TelegramCommandUpdateHandler` should branch:

- `Message.Text` -> existing command/pending-text/deep-link flow.
- `CallbackQuery` -> new callback flow.

For `/start edit_wallet_{walletId}` responses, call `DeleteMessage` on the user-generated `/start` message after processing. This should be best-effort; the menu should still render if deletion fails.

### Deep-Link Processor

The existing command processor already handles `/start`. Extend it to parse supported start payloads:

```text
/start edit_wallet_{walletId:N}
```

Responsibilities:

- Parse the wallet id.
- Verify ownership.
- Render the tracked-wallet overview menu.
- Return response metadata indicating that the source `/start` message should be deleted.

Keep this parsing separate from normal help text so `/start` without a payload still returns the welcome/help response.

### Callback Processor

Add a dedicated service:

```csharp
public interface ITelegramCallbackProcessor
{
    Task<TelegramCallbackResponse?> ProcessAsync(TelegramCallbackRequest request, CancellationToken cancellationToken);
}
```

Responsibilities:

- Parse callback data.
- Dispatch MediatR commands.
- Render overview/network/delete-confirmation menus.
- Decide whether to edit an existing menu message, delete a menu message, or answer with an alert.

Keep callback parsing in a small parser class with unit tests. Callback payloads are compact and easy to break with stringly typed code.

### Pending Text Input

The existing `IPendingWalletAliasRequestStore` is specific to `/add`. Rename/generalize it:

```csharp
public interface IPendingTelegramActionStore
{
    void Set(long telegramUserId, PendingTelegramAction action);
    bool TryGet(long telegramUserId, out PendingTelegramAction action);
    void Clear(long telegramUserId);
}
```

Action types:

- `AddWalletAlias`
- `RenameTrackedWallet`
- `UpdateMinimumUsdValue`

The message path should check pending actions before slash-command parsing, same as today.

## Rendering/Menus

Add a small menu renderer in `Presentation.Telegram`, separate from notification rendering:

```csharp
public interface ITrackedWalletMenuRenderer
{
    TelegramMenuMessage RenderOverview(TrackedWalletDetails wallet, IReadOnlyList<ChainMetadata> configuredChains);
    TelegramMenuMessage RenderNetworks(TrackedWalletDetails wallet, IReadOnlyList<ChainMetadata> configuredChains);
    TelegramMenuMessage RenderDeleteConfirmation(TrackedWalletDetails wallet);
}
```

Use `IChainMetadataProvider` plus the enabled `BlockchainNetworkOptions` list to build configured chains.

Suggested display names:

- Ethereum -> `ETH`
- BSC -> `BNB`
- Base -> `Base`
- Arbitrum -> `ARB`

Avoid relying directly on `SupportedChain.ToString()` in user-facing menus when a short chain display name exists.

## Domain Notes

`TrackedWallet.SetChainTracking(SupportedChain chain, bool enabled)` already gives the right domain primitive for network buttons.

Important behavior to preserve:

- Empty `EnabledChains` means all supported/configured chains.
- Disabling one chain from "all" expands to an explicit all-except list.
- Enabling the final missing chain collapses back to empty.
- Disabling the last explicit chain fails with `AtLeastOneEnabledChainRequired`.

## Implementation Phases

### Phase 1: Callback Plumbing

- Add deep-link payload parsing for `/start edit_wallet_{walletId}`.
- Add best-effort deletion of the generated `/start` message.
- Add callback update support in `TelegramUpdateReceiverWorker`.
- Add callback request/response contracts.
- Add deep-link and callback parser tests.
- Add callback processor skeleton.
- Ensure callback ownership validation exists before any mutation.

Acceptance criteria:

- Tapping a pencil hyperlink opens the wallet overview menu.
- The generated `/start edit_wallet_*` user message is removed when Telegram permits it.
- Bot receives callback queries.
- Unknown/invalid callback data is answered safely.
- No wallet can be edited by a different Telegram user.

### Phase 2: Menu Rendering

- Extend Telegram response model to carry inline keyboards.
- Add tracked-wallet menu renderer.
- Add overview menu.
- Add networks menu using configured enabled networks only.
- Add delete confirmation menu.

Acceptance criteria:

- Edit callback opens the overview menu.
- Networks callback opens the network checklist.
- Buttons render stable callback data under 64 bytes.

### Phase 3: Application Commands

- Add wallet-id-based query/update/delete commands.
- Use focused domain methods:
  - `UpdateAlias`
  - `UpdateMinimumUsdValue`
  - `SetChainTracking`
- Refresh cache after every mutation.

Acceptance criteria:

- Rename updates DB/cache.
- Minimum USD updates DB/cache.
- Network button updates DB/cache.
- Delete removes DB/cache entry.

### Phase 4: Notification Edit Button

- Add `TrackedWalletId` to notification payloads.
- Populate wallet id from matching cache entries.
- Add bot username/deep-link configuration if the configured `ITelegramBotClient` cannot expose it cheaply.
- Render the pencil as a MarkdownV2 link to `https://t.me/{botUsername}?start=edit_wallet_{walletId:N}`.

Acceptance criteria:

- Every wallet notification has a linked pencil.
- Clicking it opens settings for the correct tracked wallet.

### Phase 5: Pending Text Flows

- Generalize pending action store.
- Implement rename prompt and response.
- Implement minimum USD prompt and response.
- Add cancel handling.

Acceptance criteria:

- After tapping Rename, the next text message updates alias.
- After tapping Min USD, the next text message updates threshold.
- Invalid values keep the pending action and show a helpful error.

### Phase 6: Tests

Add focused coverage for:

- Callback data parser.
- Deep-link start payload parser.
- Ownership checks.
- Overview menu rendering.
- Networks menu rendering with empty-enabled-chains-as-all.
- `SetTrackedWalletChainTrackingCommand`.
- Rename pending text flow.
- Minimum USD pending text flow.
- Delete confirmation and confirmed delete.
- Notification renderer emits a pencil deep link.

## Open Questions

- Should `Back` close the menu or return to a wallet list once a richer `/list` UI exists?
- Should delete confirmation edit the same menu message or send a fresh confirmation message?
- Should notification deep-link pencils be shown on every notification forever, or only when the bot can still respond meaningfully?
- Should we add a short "settings updated" toast via `AnswerCallbackQuery` instead of sending extra messages after button taps?

## Recommended MVP Cut

Build only:

1. Linked pencil on notifications.
2. Overview menu.
3. Networks menu for configured enabled networks.
4. Rename prompt.
5. Minimum USD prompt.
6. Delete with confirmation.

Skip filters/favorites until the inline menu foundation is stable.
