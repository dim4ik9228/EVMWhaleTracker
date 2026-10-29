# Payments and paywall integration

- Status: completed
- Date: 2026-10-27
- Summary: durable Pro purchases through Telegram Stars and NOWPayments, owner-scoped Free/Pro enforcement, expiry and recovery are implemented and reviewed. Completed 2026-10-28; live deployment and financial operations remain separate rollout actions.

## 1. Agreed product scope

| Capability | Free | Pro |
| --- | --- | --- |
| Active tracked address entries per Telegram user | 10 | 50 |
| EVM token transfers and supported runtime-enabled chains | Included | Included |
| Hyperliquid tracking | Unavailable | Up to 10 entries, subject to shared source capacity |
| Polymarket prediction tracking | 1 enabled entry | Up to 50 enabled entries within the total allowance |
| Alias resolution, including Polymarket profile names | Included | Included |
| Filters, chain selection, settings, explorer links and complete alerts | Included | Included |

Pro buys 30 days for either **USD 29 through NOWPayments, accepting USDT on BNB Smart Chain only**, or **1,450 Telegram Stars**. These are distinct prices, not a promise of equal merchant proceeds. No artificial delivery delay for Free users. One owner/address entry consumes one total slot even when several tracking modes are enabled. A Polymarket trading-account binding does not create another charged entry; retain the existing same-owner duplicate-source protection.

Keep Hyperliquid's existing global limit of ten distinct addresses and its reservation protocol. Multiple owners following the same source still consume separate personal allowances but share a global source. Scaling Hyperliquid, extra IPs, new nodes and infrastructure partitioning are outside this implementation.

Polymarket also currently has a global distinct-source cap of 20 (`PolymarketPolygonOptions.MaxUniqueAddresses`); the 50-entry Pro entitlement cannot override it. Preserve that configured admission guard in this slice. Show available capacity separately from plan allowances in the purchase and tracking screens. A broad public launch advertising 50 simultaneous Polymarket accounts per customer requires a separately validated capacity increase. Do not silently raise either global cap as part of billing.

## 2. Explicit v1 defaults

These resolve details not specified in the discussion and can be adjusted before implementation without changing the agreed prices/allowances:

- One-time purchases with manual renewal for both methods. Do not configure recurring Stars debits or crypto custody auto-debits in v1.
- Use UTC and an injectable clock. Access is active on `[start, end)`; 30 days means 720 hours. A settled renewal adds 30 days after the current paid-through time, or starts at activation time if access has expired. Do not consume a new purchase's service period while its payment is still processing.
- No trials, coupons, annual plans, prorated upgrades, referrals or multiple paid tiers.
- Preserve all existing entries and preferences on expiry. More than ten saved entries is permitted after downgrade; only the selected ten are effective. A new add is refused when saved entries already meet/exceed the current tier's limit, preventing unlimited saved-wallet accumulation. Users can remove saved entries or switch the effective selection without buying again.
- Provide a persistent Free selection of up to ten entries and one Polymarket entry among them. Seed an unset selection deterministically by creation time then wallet ID; prefer the previously active Polymarket entry in that set. Users can replace the selection atomically at any time. Expiry uses this selection immediately instead of pausing everything until the user returns.
- A newly added Free entry fills a vacant selection slot. Removal clears dependent selections. Replacing the Polymarket selection pauses the old one and activates the new eligible, explicitly opted-in binding atomically; it never evicts another user's source.
- Desired tracking preferences remain separate from effective admission. Renewing restores desired modes within available capacity; unavailable sources show a capacity-paused state and a retry action. No guaranteed queue position or automatic preemption. An explicit user opt-out always wins over restoration.
- Existing users start Free when enforcement is enabled. Migration does not silently gift Pro or destroy data. A deployment preview lists affected counts and proposed effective selections privately; local testing can use an explicit, expiring, audited operator grant.

## 3. Repository baseline and integration surface

- `src/Presentation.Telegram/Program.cs` is a generic host, although the project already references ASP.NET Core and the Docker runtime is `aspnet:10.0`. Convert the composition root to an ASP.NET Core host with minimal billing endpoints and retain existing hosted services. Add the HTTP binding/Compose mapping and document production TLS termination.
- `TelegramUpdateReceiverWorker` currently uses `StartReceiving` with only Message/CallbackQuery updates. `TelegramCommandUpdateHandler` discards non-text messages. Stars pre-checkout, successful-payment and refunded-payment events must be handled before that text guard; pre-checkout updates must be requested explicitly.
- `Application/TrackedWallets/AddTrackedWalletCommandHandler` currently saves directly without a quota transaction. Other tracked-wallet mutation handlers can reinsert entries into the cache. All mutation paths must rebuild effective access, not bypass the policy via direct cache additions.
- `TrackedWalletCacheHydrationService` currently hydrates every saved wallet. Introduce entitlement-aware startup hydration and per-owner refreshes; `WalletTrackerWorker` and `NotificationDispatcherWorker` need recipient-level eligibility checks.
- `Infrastructure/Hyperliquid/HyperliquidSettings`, store/worker/delivery code and `Infrastructure/Polymarket/PolymarketSettings`, polygon store/worker/delivery code already implement global admission and generation-scoped delivery. Integrate billing with those mechanisms, including source restoration and reservation release, rather than bolt checks onto Telegram buttons.
- No billing account, payment ledger or subscription model currently exists. Telegram numeric user ID is the account key; usernames and payer blockchain addresses are not identity keys.

Relevant existing plans: [Hyperliquid](hyperliquid-native-websocket-implementation-brief.md), [Polymarket Polygon](polymarket-polygon-tracking-implementation-brief.md), [error ownership](error-catalog-layer-ownership.md), [manual acceptance](manual-live-acceptance.md).

## 4. Architecture and durable state

Use a single deployed application and PostgreSQL, with in-memory projections. Keep provider DTOs, status strings and signature handling out of Domain.

**Domain:** provider-neutral purchase/subscription invariants, paid access periods, refunds/reversals and plan identifiers. **Application:** versioned plan definitions, quota/access policies, checkout/settlement/renewal/selection use cases and provider/repository ports. **Infrastructure:** EF transactions, NOWPayments HTTP adapter, durable inbox/outbox, reconciliation and expiry processing. **Presentation:** Telegram checkout/support/settings and the NOWPayments HTTP callback adapter. Keep named errors and copy in their owning layers as required by conventions.

Persist at least:

| Record | Required purpose/invariants |
| --- | --- |
| Billing account | Unique Telegram user ID; entitlement revision; durable Free selection; concurrency control |
| Purchase order | Opaque ID; owner; immutable plan/version, 30-day duration and price snapshots; state; creation/checkout validity; at most one fulfilment |
| Payment attempt | Order/provider association; provider invoice/payment ID; quoted expected currency/network/amount; creation/settlement/refund state; no API secrets |
| Provider receipt/inbox | Durable authenticated event, minimal reconciliation evidence and processing state; provider event/charge uniqueness and bounded raw-data retention |
| Access grant | Unique fulfilled order; start/end UTC; originating receipt; reversal linkage; optional operator actor/reason/expiry |
| Billing outbox | Activation/expiry/payment-support notices and projection-refresh work, committed with ledger changes; stable deduplication keys |
| Reconciliation checkpoint | Progress for payment polling and Stars transaction recovery; retry time, attempt count and actionable exceptions |

Use decimal or integer minor units with explicit currencies, never floating point; Stars amounts are integers. USDT token quantities use the verified provider currency precision, not an assumed six-decimal representation. Define unique constraints for provider charge/payment identity and order fulfilment. Separate receipts for distinct payments from repeated notifications about the same payment.

Serialize owner quota and subscription changes in PostgreSQL (owner row/advisory transaction lock plus unique constraints), including first-account creation. Pick and document one lock order across owner and protocol admission locks before editing existing admission code; all add/enable/remove/expiry/renewal/selection paths follow it. No process-local count-then-insert or check-then-enable races. Avoid provider HTTP calls while holding long-lived owner locks: prepare externally, then revalidate owner revision and capacity inside the commit transaction.

Settlement transaction: validate receipt and immutable order -> claim unique fulfilment -> append grant -> update entitlement revision -> enqueue notices/reconciliation -> commit. Telegram delivery failure does not roll back a paid subscription. Replayed callbacks never extend it twice.

## 5. Checkout and payment attribution

`/subscription` (also linked from `/start`, limits and wallet menus) shows current tier, active/saved counts, expiry, available capacity and Renew/Upgrade. Create a private-chat order with a random unguessable identifier. Send a Stars invoice and a separate crypto option leading to a NOWPayments hosted checkout, following the requested interaction. Display the actual amount for each method; do not claim a Stars checkout error when none happened.

The database binds order -> owner -> payment attempts. Callback data, checkout URLs, transaction hashes pasted by users, success redirects and usernames cannot activate access. Every callback/menu operation verifies the requesting owner. A forwarded link may be paid by somebody else, but the purchase still belongs to its original order owner; make that behavior explicit.

Reuse an existing viable attempt when the owner repeatedly clicks the same payment method. Bound creation by owner and global rate limits. Store the order before calling providers. On an ambiguous create timeout, retain `CreationUnknown`, reconcile using the documented provider identifiers/order lookup, and avoid blind automatic retry creating multiple invoices. Do not assume NOWPayments treats `order_id` as an idempotency key. If lookup cannot establish the result, surface a recoverable support state.

One order can have both method attempts. The first valid settled payment fulfils it. Reject later Stars pre-checkout and retire visible checkout buttons once fulfilled where possible. A crypto payment or already-approved Stars payment can still arrive afterward: record it as a duplicate **payment**, notify the owner/support and route to refund review; do not silently discard money or extend access a second time. Intentional renewal creates a new order.

## 6. Telegram Stars adapter

- Send a single-chat invoice for `XTR` 1450 with opaque attempt payload and empty provider token. Validate owner, payload, currency, total and outstanding order at pre-checkout. Answer within Telegram's ten-second deadline via a priority path; do not wait behind normal alert-rate queues or external payment calls. Approval alone does not grant access.
- Persist and validate `successful_payment` before applying the common settlement transaction. Store the Telegram charge ID. Process refunds as separate durable events, including refund-before-success ordering.
- Replace/extend the current polling receiver so financial updates are durably ingested before advancing the acknowledged update offset. Maintain one polling owner, explicitly include required update types, and never drop pending updates on startup. Audit the installed Telegram.Bot 22.10.0 behavior rather than assuming an exception in a handler prevents acknowledgement. Nonfinancial command failures must not permanently block financial ingestion.
- Reconcile using `getStarTransactions`, with persisted pagination/progress and overlapping deduplication, because Telegram update retention is finite. Recover only transactions confidently tied to our order payload and owner; ambiguous transactions go to review. Test both the installed SDK and returned provider fields; upgrade the dependency only if needed for the chosen methods.
- Support `/terms`, `/paysupport`, purchase receipts and refund requests. Refund initiation is operator-authorized and idempotent; reconcile timeouts before repeating. Confirmed reversal revokes only the originating grant, never an unrelated renewal. Retain independent paid periods; if a reversed period creates a gap before a later paid period, show those dates and provide an audited support adjustment rather than blindly subtracting 30 days from a shared expiry.

Telegram contract references: [payment flow and merchant requirements](https://core.telegram.org/bots/payments-stars), [invoice](https://core.telegram.org/bots/api#sendinvoice), [pre-checkout](https://core.telegram.org/bots/api#answerprecheckoutquery), [payments](https://core.telegram.org/bots/api#successfulpayment), [refund](https://core.telegram.org/bots/api#refundstarpayment), [transaction history](https://core.telegram.org/bots/api#getstartransactions), [update acknowledgement](https://core.telegram.org/bots/api#getupdates). Verify exact SDK signatures during implementation.

## 7. NOWPayments adapter

The historical merchant-password/JWT discovery design in this section is superseded by [passwordless integration](nowpayments-passwordless-integration.md). Hosted invoices remain; API-key known-ID polling and audited operator recovery replace invoice listing. Historical rollout evidence below remains historical.

- Configure merchant API key, separate IPN secret, payout wallet externally in the provider account, HTTPS callback URL and USDTBSC allowlist. No private wallet keys belong in this bot. Use the hosted invoice API with USD 29, our opaque order ID and `pay_currency=usdtbsc`; configure the merchant checkout to restrict other coins/networks and verify that restriction in actual checkout.
- Prefer same-asset settlement to the operator's BSC USDT wallet with fees absorbed by the merchant. Verify provider quote/minimum before enabling checkout. USD pricing may quote a slightly different USDT quantity; show the provider's exact payable amount rather than hardcode 29 token units. Do not use net merchant payout after fees as the required customer amount.
- Accept POST callbacks on a dedicated bounded-body route. Verify `x-nowpayments-sig` using the documented HMAC-SHA512 canonical JSON procedure, constant-time comparison, and contract fixtures for number/string formatting and nested objects. No custom approximation of canonicalization. Invalid signatures cannot mutate orders. Durably store valid events before returning success; DB failure returns retryable failure.
- Verify payment/order/invoice association, expected currency/network and actual paid amount against authenticated provider data. An early callback arriving before the create response is saved is retained for reconciliation. Never trust an order ID alone to attach an unknown payment.
- Fulfil only a verified, sufficiently paid `finished` payment. `waiting`, `confirming`, `confirmed` and `sending` remain pending. `partially_paid`, wrong-asset/network, inconsistent identifiers and unexplained amounts go to review. **A `finished` label alone is insufficient:** provider settings can promote an underpayment to that status. Keep accepted underpayment tolerance zero for v1; do not silently enable a percentage discount.
- Reconcile pending/unknown attempts using authenticated status queries with bounded concurrency, backoff, jitter and 429 handling; callbacks are not the sole recovery path. Model provider states independently of local checkout expiry. Late genuine settlement is accounted for and fulfils an unfulfilled valid order or enters duplicate/review handling. It must never be ignored merely because the UI expired.
- Hosted invoices initially return an invoice ID, not a payment ID. Recover missed first callbacks through invoice-filtered payment listing, which additionally requires a merchant JWT obtained through configured merchant email/password. Keep those credentials external, cache/refresh authentication safely, and retain API-key-only callback/status accounting when new sales are disabled. Do not assume an API-key-only invoice lookup exists, or that precreating an embedded invoice payment makes the hosted checkout reuse it. An unknown creation with no saved invoice identity remains an audited support case.
- A verified overpayment fulfils once and records the excess for support; repeated deposits/child payment IDs require explicit reconciliation, not another automatic grant. Partial amounts are not combined across arbitrary orders/users. Crypto refunds are a documented operator/provider process; do not send funds to an arbitrary address supplied in a callback or automatically refund to an exchange's sending address.

References checked 2026-10-27: [official API](https://documenter.getpostman.com/view/7907941/S1a32n38), [invoice parameters](https://nowpayments.zendesk.com/hc/en-us/articles/21345824322717-API-and-endpoint-description), [IPN setup/signing](https://nowpayments.zendesk.com/hc/en-us/articles/21395546303389-IPN-and-how-to-setup), [status and underpayment semantics](https://nowpayments.zendesk.com/hc/en-us/articles/18395434917149-Payment-statuses), [fees](https://nowpayments.io/help/about-nowpayments/about/what-are-your-fees). Merchant-account eligibility, checkout restrictions, minima and actual settlement costs require account-level verification, not documentation assumptions.

## 8. Runtime enforcement and expiry

Implement one application entitlement policy used by every admission, cache projection, fanout and delivery path. Effective access is the intersection of desired preference, selected entry, current plan/time, valid protocol binding, operator enablement and global admission. Report `Pro required`, `Free selection paused`, `capacity unavailable` and existing provider faults distinctly.

1. **Commands:** atomically enforce 10/50 total entries, Free one PM and Pro ten HL on add/reactivation/enable. Settings edits and profile lookup stay available for paused entries. Stale menus cannot bypass a newer plan/selection revision.
2. **EVM:** hydrate/project only effective recipient entries. Include owner-specific entitlement revision and validity bounds in the read model and notification payload. Check validity in matching and again immediately before send, including after retry/rate-limit waits. Do not remove a shared source still required by another owner. Audit all existing direct `cache.Add` call sites.
3. **HL/PM:** retain desired settings separately and add effective enrollment state/revision. Query only effective recipients and sources. Invalidate delivery generations/cancel pending recipient work on access loss. Do not release HL global reservations until the old socket no longer uses them; preserve its advisory-lock ownership protocol. PM free-slot changes and renewal use existing source-binding verification and sealed activation boundary semantics.
4. **Time:** a scheduled expiry worker persists transitions, updates cache/source sets, releases capacity safely and emits at most one expiry notice per transition. Every eligibility check uses the actual expiry instant, so a delayed worker cannot extend paid access. Bounded projection refreshes check revisions; never keep paid access indefinitely on DB failure. Allow a cached grant only until its known expiry/freshness bound, then fail closed for uncertain permissions.
5. **Restoration:** renewal creates a fresh effective enrollment boundary only where access was actually interrupted. Pending work from revoked generations must not resurrect. Skip activity during an entitlement gap; do not backfill it after renewal. Continuously eligible Free-selected entries and early renewals keep their existing stream boundaries.
6. **Concurrency:** transactionally serialize activation/expiry/selection and recheck revision after awaits. A send already accepted by Telegram cannot be recalled; document that narrow race rather than promise absolute external exactly-once delivery. Preserve the existing shared Telegram rate limiter.

## 9. Delivery stages

1. **Foundation:** plan policy, clock, durable account/order/receipt/grant models, owner locks, generated EF migration and quota/ledger tests. No provider enabled yet.
2. **Entitlement integration:** effective selection/projection, all three tracking paths, expiry/restoration, generation cancellation and operator audit tooling. Add subscription/settings UI and accurate capacity copy.
3. **Stars:** invoices, durable receiver updates, settlement, refunds, transaction reconciliation, support/terms and notification outbox.
4. **NOWPayments:** typed client, hosted checkout, minimal HTTPS callback host, signature validation, settlement/reconciliation and duplicate-payment handling.
5. **Rollout:** environment/config documentation, merchant setup, runtime health/metrics, acceptance cases, migration preview and authorized deployment validation.

Follow the repository's one-writer workflow for each coherent slice: `implementer-files`, fresh `implementer-integration`, then fresh read-only `reviewer`. Coordinator integrates docs and runs validation. Reconcile QA findings before implementation. Keep one polling owner; do not add Redis, microservices or trading functionality.

## 10. Acceptance criteria and verification

Automated tests must prove behavior at transaction and failure boundaries, not just mirror constants:

- Free 10th add succeeds/11th fails; Pro 50th succeeds/51st fails; parallel requests at the boundary never over-admit. Duplicate owner/address and account initialization races remain safe.
- Free second PM enable fails, atomic slot switch succeeds, Pro HL 11th fails, and global caps remain independent. Same source across owners preserves isolation and shared admission.
- Exact expiry and restart-after-expiry enforce Free limits with preserved preferences. Selection replacements, removals, alias edits and startup cache hydration cannot reactivate excess entries. Check disable/refund during rate-limit wait and blocked source restoration.
- Successful payment grants exactly one 30-day period. Repeated/out-of-order events, concurrent renewals, two payment methods for one order, unknown create outcome, crash before/after commit, webhook retry and notice-send failure never lose or duplicate grants.
- Stars wrong owner/amount/currency/payload, pre-checkout timeout, success without text, refund-before-success, duplicated refund and missed update reconciliation behave correctly.
- NOWPayments invalid signature, altered amount/order/network, `finished` underpayment, wrong asset, partial/late/excess/repeated deposit, callback before create response and provider outage cannot improperly unlock access or lose payment evidence.
- Database downtime cannot acknowledge undurable financial events; recovery resumes them. Authenticated users cannot view another owner's orders or change their selection. Secrets/raw payment payloads do not leak into logs or committed fixtures.
- EVM/HL/PM behavior for eligible users remains unchanged; no receipt fetching or block scanning is introduced for tracking, and name resolution remains free.

Use a disposable PostgreSQL database for real uniqueness, locks, transactions and migration checks. Generate migrations with EF tooling; after migration run `dotnet test EVMWhaleTracker.slnx` as required. Also run the solution build, pending-model check and normal/debug Compose validation after relevant changes. Provider stubs/fixtures prove deterministic failure handling only, never live checkout or payout.

Author a separate `docs/testing/live/billing.md` runbook during implementation. Default read-only cases inspect subscription display, owner isolation and checkout state. Financial cases require explicit authorization naming environment, method, amount/budget, payer and refund scope; ordinary live acceptance never authorizes spending. Use Telegram's test environment and NOWPayments sandbox where supported, then an explicitly authorized small real payment and actual wallet settlement to verify production integration. Temporary pricing must use isolated test configuration, not silently alter public prices. Do not claim full validation without both provider evidence and durable entitlement state. Store private reports under `artifacts/live-validation/` and preserve original user wallets/settings.

## 11. Rollout and remaining operational inputs

Use separate controls for new Stars sales, new crypto sales and entitlement enforcement. Keep receipt ingestion/reconciliation active when new sales are disabled. Never make disabling billing automatically grant everyone Pro. Enforcement changes require an explicit deployment decision and private impact report; rollback must continue to account for payments already taken.

Production requires NOWPayments API key/IPN secret, merchant email/password for missed-callback reconciliation, payout configuration, public HTTPS ingress, support contact/terms, and a tested backup/restore procedure. Validate all required configuration before enabling sales. Keep secrets in the existing local secret/environment mechanisms, outside source control and Docker build context. Add health signals for durable inbox lag, unsettled/review orders, expired access awaiting reconciliation and provider failures, without sensitive identifiers.

The user selected the Stars-first plus external crypto-checkout interaction after discussing Telegram's requirement for Stars for in-app digital services. This plan records that product choice; it does not establish Telegram approval or a policy exception. Both sale methods must be independently switchable. [Published requirement](https://core.telegram.org/bots/payments-stars#faq).

Completion means the above flows and enforcement are implemented, reviewed and validated with honest evidence. Public launch capacity and real merchant onboarding are distinct from code completion. This implementation does not implicitly authorize creating a merchant account, changing live configuration, sending invoices, spending funds or restarting live services.

## 12. Implementation and validation record — 2026-10-28

Implementation and a fresh integration pass added the common ledger, owner-serialized quotas/selections, durable financial polling, Stars and hosted NOWPayments adapters, receipt reconciliation/refunds, independent rollout switches, operator audit/recovery commands, paginated subscription UI and runtime EVM/HL/PM entitlement gates. Desired preferences survive expiry; persisted enrollment boundaries and final delivery checks prevent stale work from becoming valid again after a gap. Each host refreshes local projections independently, while a PostgreSQL lease serializes financial reconciliation/notices. Existing global source caps are retained.

Three EF migrations were generated through tooling: `AddBillingLedgerAndEntitlements`, `EnforceSettledChargeUniqueness`, and `PersistRecipientEnrollmentAndReceiptRetry`. The secrets exporter, environment example and both Compose configurations include billing setup. Read [operations](../operations/billing.md) and [manual billing acceptance](../testing/live/billing.md) before enabling a deployment.

Independent review identified continued crypto reconciliation after settlement, default-off EVM delivery and duplicate Stars refund defects. A sole writer fixed them and added regressions; the reviewer independently verified all three resolutions and reported no remaining actionable findings. Crypto primary status and invoice discovery continue on a bounded schedule after settlement; child payments remain separate review evidence and do not overwrite the original quote or grant another period. Bounded invoice pagination remains an operator-attention limit for unusually large histories.

Validation executed:

- Final full solution suite against explicit disposable `BillingQa` and `HyperliquidQa` PostgreSQL databases on loopback port 55433: **194 passed, zero failed/skipped**. Includes quota races, simultaneous methods, refund-before-success, incorrect owner/currency/amount, actual SDK polling acknowledgement failure/recovery, missed first/later crypto callback recovery, duplicate Stars refund idempotency, cross-host enrollment gaps, failed-edit rollback, Free PM switching/capacity, personal HL admission and default-off delayed EVM dispatch.
- Solution compilation succeeded; existing five dependency advisory warnings remain (PKCS and SQLite native package). No dependency-advisory remediation is claimed by this feature.
- EF pending-model check: no model changes after the generated migrations. Normal/debug Compose validation passed. Synthetic secrets export/Compose roundtrip verified flags, operator IDs and credentials containing dollar/hash/quote/backslash characters without exposing real credentials.
- Coordinator isolated host startup against a fresh, empty QA database, with Telegram/EVM/HL/PM disabled and local user secrets isolated: billing health **200**, unsigned callback **401**, oversized callback **413**. Temporary process/database removed; private logs retained under ignored artifacts.
- Documentation links and whitespace checked. No source checkout, live merchant account, real blockchain payment, Stars purchase/refund or payout was exercised. Provider/Telegram HTTP fixtures establish deterministic behavior, not production integration proof.

At implementation completion, the running bot/database had not been redeployed or migrated; the subsequent authorized rollout is recorded below. Sales and enforcement remain checked-in default-off. Real merchant eligibility/credentials, HTTPS ingress, terms/support, an explicit enforcement-impact decision and separately authorized live payment/settlement checks remain operational prerequisites. Hyperliquid scaling and increasing Polymarket's global cap are not implemented by billing.

## 13. Authorized local deployment and live checks — 2026-10-28

The user requested deployment and testing. Saved a private PostgreSQL custom-format backup and restored it successfully into a new isolated QA database; the restored three-wallet count matched the baseline. Rebuilt/recreated only the app using `docker compose up -d --build --no-deps app`. Release publication and all three billing migrations succeeded. Deployed image identity: `sha256:254b30d72beda67c53e3bd8804a24bc37feb8669661d7d94dabc484f078750b2`.

Runtime/billing live checks (`RT-01`, single-owner display/support portion of `BILL-01`) passed: running app with zero restarts, healthy PostgreSQL, billing health HTTP 200 with empty work counters, unsigned callback HTTP 401, and real Telegram responses for `/subscription`, Manage Free selection (view only), `/terms`, `/paysupport` and `/list`. Displayed allowances/prices and saved/effective counts matched the designated owner's state. No purchase buttons were offered with sales disabled; pending terms/support were reported accurately. Cross-owner/group-chat checks were not exercised.

All original wallet and HL/PM setting rows matched the predeployment baseline exactly after migration and after UI checks. No temporary tracking enrollment or purchase was created. All five EVM network subscriptions reconnected; Hyperliquid acknowledged both subscriptions for its one reserved source and retained the existing history-incompleteness warning. This establishes connection recovery, not a newly observed end-to-end trade/transfer alert. No runtime error lines were found during the bounded observation.

Actual deployed sales and enforcement flags remain false. Merchant API/IPN/authentication credentials, terms/support and public callback configuration are absent; real checkout, payment settlement, payout and live paywall/expiry enforcement were therefore not exercised. No funds, grants or receipts were created. Private backup, baselines, logs, screenshot and report are under ignored `artifacts/live-validation/`; the isolated QA container was returned to its original stopped state.
