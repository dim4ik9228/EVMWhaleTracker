# Operator crypto test checkout

- Status: completed
- Date: 2026-10-31
- Summary: explicit default-off real crypto test checkout for allowlisted operators, with a persisted USD price and unchanged authenticated settlement.

The private command `/billing_test_checkout` creates or resumes a separate `Pro.OperatorCryptoTest.v1` order and shows its stored USD amount and order identity before a confirmation button creates the hosted invoice. The response repeats that stored amount beside the checkout link. It buys the ordinary 720-hour Pro period with USDTBSC; it is a real payment, with no settlement bypass.

Set `Billing:TestCheckoutEnabled=true` only for the authorized live run. `Billing:TestCheckoutUsdPrice` defaults to 1 USD and must be positive, at most 29 USD and representable in cents. Normal runtime, a positive allowlisted operator, merchant USDTBSC verification, API/IPN credentials and an HTTPS callback are required. Public support/terms readiness remains a prerequisite for public sales; the dedicated operator test path can run while both public sales switches remain false. Sandbox mode rejects test enablement.

The test plan identity separates pending public and test orders. No order is repriced; a changed configured test price creates a distinct order subject to existing owner/global limits. Stars attempts, Stars pre-checkout and Stars settlement are rejected for test orders. Normal public pricing remains USD 29 / 1,450 Stars. Existing provider minimum checks and authenticated USD price, asset/network, quote, paid amount, identity, refund and once-only ledger verification continue to use the stored snapshot, including after test enablement is turned off.

No schema changes, private settings changes, deployment, payment, live Telegram messages or payout claims are part of implementation. Merchant readiness and real settlement must be established by the coordinator's separately authorized live acceptance. Existing manual grant/revoke and enforcement behavior remain unchanged.

Focused coverage includes default-off and operator/readiness checks; test amount bounds; separation from normal pending orders; Stars rejection; disabled public sales; and provider rejection of a USD price differing from the stored order followed by correct-price settlement. Validation outcomes are reported in the implementation handoff.

## Validation and local rollout — 2026-10-31

Subsequent real acceptance completed: the owner paid the hosted invoice, a multiple-payment-identity defect was fixed and deployed, and authenticated recovery fulfilled exactly one paid period. Repeat recovery was idempotent, merchant custody credit was confirmed, and test checkout was disabled again. See [the recovery and live evidence record](hosted-invoice-payment-identities.md). The preparation observations below are historical; the invoice is no longer unpaid.

Fresh integration and independent review completed without remaining findings. The isolated deployment source starts from commit `2472447f` and includes only this feature and the existing provider string-valued quote parsing fix; unrelated pending swap/sandbox runtime work and migrations are excluded. Billing/subscription regression checks against disposable BillingQa passed: **68 passed, zero failed, four unrelated Hyperliquid environment-gated skips**. The production Docker image built successfully. Existing dependency advisory warnings remain.

The owner confirmed USDT BEP-20 payout-wallet configuration. The live merchant API reported a same-asset minimum below USD 1; minimums are dynamic and the adapter rechecks before invoice creation. Deployed the isolated image with operator testing enabled at USD 1, merchant verification recorded and the current ngrok callback saved; public Stars/crypto sales remain disabled and enforcement remains enabled. Local billing health returned 200, unsigned public IPN POST returned 401, and other public routes returned 404 when bypassing ngrok's browser interstitial. No new migration was included. A private database backup was saved before deployment.

The authorized operator's Telegram command created a stored USD 1 / 720-hour test order, and its confirmation created a real hosted invoice. Hosted checkout and authenticated provider status both showed USDTBSC, USD 1, waiting and zero paid. The quoted crypto amount is dynamic; the payer must use the exact current checkout quote. No payment, settlement, payout or resulting paid grant has been validated yet. Existing manual Pro remains active; a successful purchase should extend contiguous access. Keep the app, database and tunnel running while payment is outstanding, retain accounting credentials, and disable test checkout after the run without disabling reconciliation. Private invoice/payment identities, before-state evidence and the screenshot are under the Git-ignored directory referenced by `artifacts/live-validation/current-dollar.txt`.
