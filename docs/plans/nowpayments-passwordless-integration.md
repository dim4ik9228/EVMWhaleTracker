# NOWPayments without dashboard credentials

- Status: completed
- Date: 2026-10-29
- Summary: retain hosted crypto checkout using an API key and IPN secret, remove merchant-password authentication, and make unavailable payment discovery an explicit support workflow.

## 1. Product decision and scope

The merchant uses Google sign-in and does not have a NOWPayments dashboard password. Do not require creating one, storing Google credentials, reusing browser sessions or implementing Google OAuth for the bot. The only provider secrets required by this app will be the merchant API key and separate IPN signature secret. This is API-key authentication, not unauthenticated payment processing.

Keep the current external hosted invoice checkout, USD 29 product price, USDTBSC restriction/merchant verification, 30-day periods, original order-owner attribution, receipts and audit. Stars, manual Pro administration, Free/Pro allowances, tracking and independent sales/enforcement flags remain unchanged. A public HTTPS callback, support contact, purchase terms and merchant payout configuration remain required.

This request authorizes a plan only. Do not modify the running integration, private credentials, flags or deployment during planning. Implementation and deployment are subsequent work; real payment/refund checks require the existing explicit payer/budget authorization.

The user subsequently requested implementation. The code/configuration work below is implemented; the original planning-only boundary above records the planning stage. Deployment, activation and live financial checks were not performed under the implementation request.

## 2. Verified provider boundary

Checked 2026-10-29 against the [official endpoint documentation](https://nowpayments.zendesk.com/hc/en-us/articles/21345824322717-API-and-endpoint-description) and [official SDK](https://github.com/NowPaymentsIO/nowpayments-sdk-nodejs):

| Operation | Authentication / identity | Planned use |
| --- | --- | --- |
| Create hosted invoice, `POST /invoice` | API key; returns invoice ID and checkout URL | Retain |
| Query `GET /payment/{payment_id}` | API key; requires an actual payment ID | Retain for verification and recovery |
| List payments, `GET /payment/` | API key plus Bearer JWT | Remove |
| Obtain JWT, `POST /auth` | Dashboard email/password | Remove |
| Receive IPN | Verify separate HMAC-SHA512 secret | Retain durable ingestion |

There is no verified API-key-only replacement for invoice-filtered listing in this plan. An invoice ID is not a payment ID; do not pass it to the payment-status endpoint. Do not precreate a second payment and assume hosted checkout reuses it. A direct-address checkout could obtain a payment ID immediately, but changes the customer experience and still has ambiguous-create/repeated-deposit boundaries; defer that separate redesign.

## 3. Target flow and recovery contract

1. Persist order and attempt before the create request. Keep the existing single-claim creation boundary. Create the invoice with API key, opaque order ID, callback and existing price/network configuration. Persist invoice ID/URL before presenting checkout.
2. Receive bounded IPN bodies, verify the existing canonical signature and durably persist payment identity before acknowledging. API-key status verification remains authoritative for invoice/order, original owner, USD quote, asset/network, amounts and actual settlement state. A browser redirect, screenshot or transaction hash alone never grants Pro.
3. Poll all durably known payment IDs with API-key status queries, including primary IDs and relevant receipt identities. Preserve durable event keys, owner locking, once-only fulfilment and individual reversal semantics. Keep settled known payments eligible for bounded refund/reversal checks while new sales are disabled.
4. For an invoice without any known payment ID, do not call listing or `/auth`. Keep it awaiting payment identity with no background network discovery. Checkout expiry does not prove nonpayment. After the local checkout window, classify unresolved identity as attention for optional investigation; it must not imply that the customer paid or failed. A later valid IPN must still recover normally.
5. Timeout/crash during invoice creation with no saved invoice ID remains `CreationUnknown`: do not blindly create a replacement or tell the user to pay again. A callback that races the create response remains durable and retryable; if its invoice cannot be safely matched after the existing grace window, send it to review rather than binding by order ID alone.

| Situation | Result after switch |
| --- | --- |
| Initial IPN received, later updates missed | Poll known payment ID and account for verified settlement/reversal |
| First IPN missed, a later one arrives | Learn identity from that signed event, verify and recover |
| Every IPN missed and payment ID unknown | Operator obtains provider payment ID from dashboard; audited recovery required |
| No callback because customer never paid | Awaiting identity/expired checkout; no invented payment, grant or payment-failure assertion |
| Late settlement after local checkout expiry | Verify and fulfil once, or retain duplicate/review evidence |
| Unknown later child payment on an already-settled invoice | Requires its own IPN or operator evidence import; polling primary cannot discover it |

No promise of exhaustive unattended reconciliation remains. Signed callback delivery/retries plus known-ID polling are the automatic paths. A merchant who needs full invoice discovery later must choose a separately reviewed design, rather than silently reintroducing dashboard credentials.

## 4. Operator recovery and customer copy

Retain private allowlisted `/billing_attach USER ATTEMPT INVOICE PAYMENT REASON` for a missing initial identity or ambiguous create. Query provider status with the API key, verify all supplied associations against persisted ownership and quote, commit audited attachment, then use the normal receipt/settlement path. Wrong-owner, wrong-invoice, conflicting primary and unsupported amount/asset cases cannot unlock access. Repeated attachment cannot duplicate paid access; preserve recoverability after attachment commits but subsequent processing fails.

The current attach command rejects a different payment when a primary already exists. Do not loosen it to overwrite that primary. Add a narrowly scoped, audited child-evidence import/recheck action for operators: fetch a specified provider payment ID, resolve its authenticated invoice to an existing attempt, retain the original primary and quote, and place conflicting/additional deposits in review without issuing another period. It is an investigation aid, not an automatic refund or arbitrary transaction-to-user attachment. Unknown/unmatched invoice identity remains unassigned review evidence.

Provide a bounded, paginated private operator recovery list (for example `/billing_recovery [page]`) containing attention category, attempt/order/invoice references and age. Distinguish unknown identity, ambiguous creation and actual received review evidence. Do not make operators inspect raw logs or write SQL to locate attempts. Keep public health aggregate-only; show no merchant secret, raw payload or other user's state in customer screens.

Extend aggregate billing health/monitoring with separate counts/oldest ages for unresolved invoice identities and ambiguous creations. Existing receipt-review counters alone do not include invoice-only attempts. These counts mean operational attention, not proof of money received, and must not contain user/provider identifiers.

Customer copy: while identity is unknown, say payment has not yet been verified and provide support with the user's own order reference. Do not say a completed payment failed, demand another transfer, expose provider internals or repeatedly send an unpaid-customer alert. Reuse terms/support and named error/text catalogs. Operator investigation may use the human's ordinary Google dashboard sign-in; the app never automates that session.

## 5. Implementation map

| Area | Required changes |
| --- | --- |
| `Infrastructure/Billing/NowPaymentsClient.cs` | Remove JWT cache/gate, `TokenAsync`, password requests and invoice listing. Separate known-ID status processing from scheduling. Preserve IPN verification, quote checks, API-key status and attachment; implement bounded child-evidence import. |
| `Presentation.Telegram/Billing/BillingWorker.cs` | Remove `DiscoverAsync` coupling. Persist next retry on every branch, including settled/reversed/no-identity paths; no hot loop or batch starvation. Preserve normal pending checks around one minute and settled/reversed checks around fifteen minutes, with failure backoff/jitter and rate-limit handling. Enumerate known IDs with deduplication and bounded batches. Identity-less state sweeps perform no provider discovery calls and must not crowd out real receipt/status work. |
| Application contracts/catalogs and Telegram billing adapter | Named awaiting-identity/attention states, scoped recovery list/action, authorization and safe customer status copy. No infrastructure errors in Domain. |
| HTTP billing health in `Program.cs` | Aggregate unresolved-identity/ambiguous-create attention monitoring without sensitive references. |
| `BillingOptions.cs`, host configuration | Remove dashboard email/password properties and validation requirements. Require API/IPN keys, verified merchant setup, HTTPS callback/terms and support for crypto sales. Missing API credentials must not grant access or discard durable work. |
| `.env.example`, Compose, appsettings, secrets exporter | Stop exporting/injecting `NOWPAYMENTS_EMAIL` and `NOWPAYMENTS_PASSWORD`. Preserve escaping and all unrelated settings. Do not read or print real secrets in tests. |
| Tests and documentation | Replace JWT-discovery fixtures with password-free status/manual-recovery cases. Update operations, billing live runbook, README/current context and supersession notes in the original billing plan. |

## 6. Durable state and upgrade

Prefer existing tables and string states. Replace active use of `ReconciliationNeedsCredentials` with an accurate awaiting-identity/attention concept. Keep the old persisted string as a compatibility token until old rows are safely classified; startup must resume rows with known IDs, preserve settled/reversed grants, and expose unknown rows for review without requiring removed credentials. Do not rewrite an unresolved attempt as a success or a payment failure. Use a bounded idempotent compatibility transition with explicit tests.

Retain all orders, invoice/payment IDs, receipt uniqueness, audit references, grants and provider retry metadata. Do not delete historical bodies beyond existing retention policy or rename persisted state values mechanically. Verify whether existing fields plus constants suffice; if a schema change is necessary, generate it through EF tooling and run the full solution suite. Never hand-write a migration to remove credentials, which are configuration rather than ledger columns.

In particular retain persisted `discovered:{payment_id}` receipt keys even if the method/name "discovery" disappears. Polling, manual import and old evidence must continue to converge on compatible identities rather than introducing a second receipt stream for the same observation.

After implementation, stale local email/password settings may be removed by a separately scoped local cleanup. The new app must ignore those keys and never export them. Do not silently edit the user's external User Secrets or merchant account during implementation. Preserve API/IPN credentials already entered.

## 7. Acceptance criteria

- Crypto-sales configuration accepts only API key/IPN secret plus existing non-secret prerequisites; email/password are neither read nor transmitted. A capturing HTTP fixture fails on `/auth`, payment-list requests or Bearer headers during all crypto flows.
- Signed IPN -> verified status -> grant works; invalid signatures, amount/currency/network/order mismatches and finished underpayment fail safely. Keep original-owner attribution and exactly-one-period ledger behaviour.
- Missed later IPN and verified refunds recover through known-ID polling, including after settlement, sales disablement and process restart. Poll known child receipt IDs without replacing the primary or granting again.
- Unknown first identity enters a durable support state with bounded scheduling, never a missing-password error; a late IPN recovers it without manual intervention. Unknown child payments are explicitly undiscoverable until IPN/import.
- Initial attachment, repeated/imported child evidence, foreign-account IDs, conflicting primaries, concurrent webhook/status/import, failure after audit/attachment commit and outage recovery do not double-credit or misattribute access.
- Old `ReconciliationNeedsCredentials` rows with/without known IDs and existing settled/reversed rows upgrade correctly. No flags, wallet preferences or prior paid periods change.
- Provider timeouts/429/401, malformed responses, DB failure, early callbacks and ambiguous create retain durable evidence and bounded retries; no undurable financial acknowledgement or automatic create retry.
- Recovery listing is operator/private-only, paginated and read-only; user receipts/support reveal only that owner's information. Logs/config export contain no dashboard credentials.

Use focused tests with disposable PostgreSQL for locks, unique constraints and transaction boundaries; fixtures prove deterministic behaviour, not live merchant settlement. Check normal/debug Compose and synthetic secret export, with sales disabled. Full suite is required for any migration; otherwise run billing/config/presentation scopes warranted by the change. Use first implementation, fresh integration and independent review with one writer at a time.

## 8. Rollout and completion

1. Review open attempts and save a private DB/settings baseline; verify restore readiness. Inventory API/IPN credential presence without exposing values. Keep sales/enforcement flags unchanged.
2. Deploy the reviewed change only when implementation/deployment is requested. Verify health, unchanged wallet/protocol/ledger state, resumed known-ID accounting and no dashboard-auth/listing calls. Do not disable existing callback/status accounting merely because sales are off.
3. Configure public HTTPS ingress, terms/support and merchant USDTBSC payout restrictions; verify actual checkout restrictions. Enable sales/enforcement only as separately authorized decisions.
4. Execute sandbox and separately authorized financial acceptance cases for initial/later missed IPN and support recovery. A production purchase/refund requires named payer, method, amount/budget and scope. Record inaccessible scenarios as not run; never claim verified settlement from a build or synthetic test.

Done means credential removal, truthful recovery states and usable audited operator recovery are implemented, reviewed, tested and documented. Planning completion is not implementation, deployment or payment activation.

## Planning review — 2026-10-29

Independent read-only QA mapped current JWT/scheduling/state coupling and the existing attachment restriction on child payments; this plan includes those boundaries, recovery visibility and persisted receipt-key compatibility. A separate fresh reviewer inspected the plan against the current client, worker, options, ingestion, health and ledger and reported no actionable findings. Only documentation was changed for this request. No implementation, tests, credential inspection, live provider calls, deployment or payment activation occurred.

## Implementation and validation — 2026-10-29

Subsequent authorized deployment: the [configuration-separation rollout](configuration-secrets-separation.md) deployed this implementation on 2026-10-29 with the user's API/IPN keys loaded. Sales/enforcement remain disabled; no real payment was verified. The no-deployment statements below describe the earlier implementation stage.

Removed dashboard credentials/JWT/listing from source, options validation, Compose and exporter. Added API-key known-payment/receipt polling, persisted retry coordination, legacy identity-state transitions, `/billing_recovery [page]`, audited `/billing_import PAYMENT REASON`, API-key initial attachment, safe customer support references and aggregate unresolved-identity/ambiguous-creation health. Receipt keys remain compatible; primary quote/identity, order ownership and once-only grant/reversal semantics are preserved. No schema migration was needed.

First implementation and fresh integration completed. Integration corrected legacy duplicate-child refund handling, foreign-order receipt visibility and imported-evidence restrictions, with regressions. Final command `dotnet test test/UnitTests/UnitTests.csproj --filter 'FullyQualifiedName~Billing|FullyQualifiedName~SubscriptionLayout' --no-restore` passed **63 tests, zero failures/skips** against disposable BillingQa. Existing dependency advisory warnings remain. Source search found no removed dashboard credential/JWT/discovery method references in runtime/config/test sources. `git diff --check` passed with existing CRLF warnings.

Coordinator synthetic-secret export verified old email/password values are not exported and API-key output remains present. Normal/debug Compose validation passed using the synthetic environment; real secrets and the live `.env` were not modified. Fixtures are Git-ignored under `artifacts/live-validation/passwordless-config/`. No merchant API, real payment/refund, Telegram message or deployment check was executed. Sales/enforcement flags and original wallet settings remain untouched; the current running image has not been replaced by this implementation.

A fresh independent source review of the completed code/configuration reported no actionable findings. The disposable QA database was stopped after validation. Real merchant checkout, lost-callback recovery and payout remain unverified live; automated fixtures do not establish them.
