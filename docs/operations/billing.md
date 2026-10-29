# Billing operation and rollout

The billing implementation supports one-time, manually renewed Pro purchases: **USD 29 through NOWPayments USDT on BNB Smart Chain**, or **1,450 Telegram Stars**, for 720 hours. These prices are independent; Stars merchant proceeds do not equal the advertised USD price.

| Capability | Free | Pro |
| --- | --- | --- |
| Active address entries | 10 | 50 |
| Supported runtime-enabled EVM chains | All | All |
| Polymarket tracking | 1 selected entry | Within the 50-entry allowance |
| Hyperliquid tracking | Unavailable | Up to 10 entries, subject to shared capacity |
| Alias resolution, filters and complete notifications | Included | Included |

One owner/address entry counts once across tracking modes. Expiry preserves all saved entries and settings; the saved Free selection determines continuing access. New adds are blocked when saved entries meet the current tier's allowance, including an expired subscriber with more than ten saved entries. Existing Hyperliquid global capacity is ten distinct sources and Polymarket defaults to twenty; buying Pro does not reserve sources or increase those limits.

## Configuration

See [runtime configuration](configuration.md) for precedence, migration and deployment. Ordinary billing settings belong under `Billing` in appsettings or ignored `appsettings.Local.json`: `EnforcementEnabled`, `StarsSalesEnabled`, `CryptoSalesEnabled`, `SupportContact`, `TermsUrl`, `CallbackUrl`, `MerchantUsdtBscVerified`, `ProjectionFreshnessSeconds` and private `OperatorIds`. All three activation switches remain false until explicitly enabled. The callback URL must be public HTTPS ending in `/billing/nowpayments/ipn`.

Provider credentials stay in User Secrets as `Billing:NowPaymentsApiKey` and `Billing:NowPaymentsIpnSecret`. The exporter writes canonical `Billing__NowPaymentsApiKey` and `Billing__NowPaymentsIpnSecret` to ignored `.env` for Docker. No dashboard credentials are used. Do not place ordinary switches or operator IDs in dotenv. Docker publishes billing HTTP on loopback port 8080; topology belongs in Compose.

The app listens on container port 8080; Compose publishes it on `127.0.0.1`. Terminate TLS in a reverse proxy and forward the exact callback path. Keep `/health/billing` private to operational monitoring. It returns aggregate backlog/attention indicators, not payment payloads. Do not expose PostgreSQL publicly as part of billing deployment.

NOWPayments hosted invoices initially identify an invoice, not an individual payment. The app uses only an API key and IPN secret; it never logs into the merchant dashboard. Known payment IDs are polled for missed updates, including after settlement. If every callback was missed, the app cannot discover the payment from an invoice ID alone; use the operator recovery tools below. Payout addresses, same-asset settlement and checkout currency restrictions are configured in the merchant account. Verify eligibility, actual USDTBSC checkout, minimum payment and fees there. `pay_currency=usdtbsc` alone is not proof of an immutable currency restriction. Customer quotes may differ slightly from 29 USDT because the product is priced in USD; the exact provider quote is authoritative, before merchant processing fees.

Stars use the existing bot identity and no third-party payment-provider token. Owner withdrawal is managed separately through Telegram/Fragment and is not automated by this application. Refund processing and merchant support remain the operator's responsibility.

## Rollout sequence

1. Back up PostgreSQL and verify restore readiness. Deploy the additive migrations with sales and enforcement disabled. Do not downgrade to a pre-billing executable while payments are outstanding: it cannot account for financial updates.
2. Configure a private support channel, HTTPS terms, operator IDs and payment credentials. Use an operator `/billing_preview` to inspect affected saved counts, existing protocol preferences and proposed Free selections. Preview is read-only; it does not grant Pro.
3. Enable enforcement deliberately after reviewing the impact. Existing users start Free; no automatic grandfathered Pro grant is created. Explain the selection screen and preserved settings before changing a public deployment.
4. Verify the callback route/TLS and actual merchant settings. Validate each payment method in its test environment where supported. Test credentials and tokens must belong to a separate test deployment, never be substituted into the shared bot silently.
5. Enable new sales for the validated methods. Explicitly authorized production payment and settlement checks are separate from builds/deployment; use the [billing acceptance cases](../testing/live/billing.md).
6. Watch backlog, review receipts, expiry reconciliation and notice delivery. A payment can be recorded even if its Telegram confirmation cannot yet be sent.

Disabling a sales flag stops new purchases through that method but must leave its API/IPN credentials available for already-issued invoices, refunds and reconciliation. Keep enforcement independent. Disabling enforcement is an explicit return to legacy unrestricted tracking, not a payment-refund procedure. It must never be used as an automatic response to a provider outage.

## User and support flow

`/subscription` opens a compact plan and wallet-usage overview, with the current paid expiry when applicable. Pro details carries benefits, pricing, renewal terms and the shared-capacity caveat; Payments carries bounded paid periods and receipt references; Help carries terms/support. Empty expiry/history information is omitted from the overview. Callback navigation edits the same message, including paginated Free selection and return navigation. Each selection change is owner-checked and revision-checked. `/list`, `/terms` and `/paysupport` remain available without Pro. Renewal extends a contiguous paid period by thirty days; it does not backfill activity from a period without access. A capacity-paused protocol retains the desired preference and can resume once admission and verification succeed.

Checkout credits the original order owner even when a crypto link is forwarded. A repeated provider notification is not another payment. If both methods settle one order, only one period is granted and the second payment is retained for refund review. Underpayments, unsupported assets, overpayments and uncertain creation results are visible support cases; never ask a user to blindly pay again because a confirmation is delayed.

### Manual Pro administration

Configured operators can open `/admin` in private chat, or choose Admin from `/subscription`. Use My account or Find user; the target must already have persisted bot tracking or billing state. Ask a user to send `/myid` privately to this bot and share the returned numeric ID. Usernames and wallet aliases are not administration identifiers. Configure trusted IDs through `Billing:OperatorIds` in ignored appsettings.Local.json; keep actual IDs out of committed files.

Choose Grant Pro, then 1 hour, 1 day, 7 days, 30 days, 90 days, or Custom days (1–365). Reply directly to the audit-reason prompt (1–300 characters), inspect the target and UTC dates, then Confirm. `/admin_cancel` abandons the pending action. Confirmations expire after ten minutes; restart/lost wizard state or a changed subscription requires a new preview. A repeated confirmed operation cannot issue a second grant. New grants extend contiguous access and leave disconnected future periods intact.

The account screen lists manual grants in pages of five. Revoke asks for a reason and confirmation, and reverses only that manual period. Purchased periods remain intact; this flow does not refund money. Grant/revoke and their audit are committed together; entitlement refresh follows commit and can recover after a delivery/projection interruption. Browse and preview do not issue grants or create payment records. The panel works independently of sales/enforcement flags; enabling an operator does not activate the paywall.

### Expert operator commands

Passwordless recovery: `/billing_recovery [page]` is a private read-only queue (zero-based pages) of ambiguous creation, unresolved identities and received review evidence, with references for action. Obtain provider payment IDs from the merchant dashboard using your normal human sign-in. `/billing_attach USER ATTEMPT INVOICE PAYMENT REASON` verifies and binds an initial identity; `/billing_import PAYMENT REASON` records audited evidence without replacing an existing primary or issuing a second period for an additional deposit. Unknown/unmatched invoices remain review evidence. Neither command initiates a refund. Never tell a user to pay again merely because verification is pending. Repeated or interrupted actions must be reconciled through the ledger, not erased.

`/health/billing` includes aggregate unresolved-identity/ambiguous-create counts and oldest ages; these do not prove payment occurred. Existing legacy credential-required attempts transition without a password. Old dashboard email/password settings are ignored and no longer exported; remove obsolete local entries separately if desired. Only API/IPN keys remain required provider secrets. Known-ID polling cannot discover an unknown later child deposit without a callback or operator import.

Operator commands work only in private chat from configured IDs. Reasons are required and recorded in the audit table. Use numeric IDs/opaque references from private evidence, not usernames or public screenshots:

```text
/billing_preview
/billing_grant USER HOURS confirm REASON
/billing_revoke USER GRANT confirm REASON
/billing_refund USER STARS_CHARGE confirm REASON
/billing_attach USER ATTEMPT INVOICE PAYMENT REASON
```

Expert command grants are explicitly expiring (1–720 hours) and extend contiguous access. Expert revocation can reverse the specified purchased or manual period; it does not refund money. Use the panel for ordinary manual-grant management. The refund command is for Stars only. An ambiguous refund request is reconciled before any retry. Crypto refunds use the provider's operator process; do not automatically return funds to an exchange sender or an unverified address. Attachment verifies provider invoice/payment/order identities before repairing a known uncertain attempt. It is not permission to attach a transaction hash to an arbitrary user.

Paid periods remain independent on reversal. Refunding an earlier period may create a gap before an already-purchased later period. Inspect the remaining dates and use a documented audited adjustment when needed, rather than subtracting thirty days from another purchase.

See the [implementation requirements](../plans/payments-and-paywall-integration.md) for contracts and failure boundaries. No live payment or deployment is established by these instructions alone.
