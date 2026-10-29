# Billing live acceptance

Read [the index](README.md) and [billing operations](../../operations/billing.md) first. These are manually invoked cases, not CI checks. Normal observation budget: ten minutes total, at most three activity alerts, one temporary tracked wallet at a time. Default selection is **BILL-01 only**, which does not create a payable invoice or spend money.

The other suites' prohibition on financial actions remains in force. BILL-03/BILL-04/BILL-06 require an explicit request naming deployment, payment method, payer, exact amount or spending limit, and any refund/restart scope. An ordinary request to run all live smoke checks does not select them. Missing merchant configuration or payer authorization means `Blocked`, never permission to improvise credentials, pricing, signatures or transfers. The human may perform checkout while the agent observes the authorized evidence.

Keep run evidence and original wallet/settings baselines privately under `artifacts/live-validation/<run-id>/`. Preserve provider identities and receipt references there; redact credentials, full payloads and unrelated users from reports. BILL-07 is an explicitly selected admin navigation check; it does not authorize confirming a grant or revocation.

## BILL-01 — Subscription display, support and privacy

Preconditions: deployed billing revision is known; the designated private bot chat is accessible. Use current flags and account state.

1. Open `/subscription`; record current plan, wallet usage and current paid expiry when applicable. Expect no empty expiry/history, repeated tier comparison, raw receipt IDs or capacity dump on the overview. Open Pro details and verify 1,450 Stars or USD 29 USDTBSC for thirty days, manual renewal and the capacity caveat, without claiming equal merchant proceeds.
2. Open Payments and Help, then return to the overview. Empty payments should have a short explicit empty state, and callback views should update the same bot message rather than append new ones. Open `/terms` and `/paysupport`. With missing sales configuration, expect an unavailable/pending explanation and no usable purchase path. Do not enable flags to make the case pass.
3. Open Manage Free selection without changing it. Check aliases/short addresses, counts, pagination and return navigation where present. No raw internal IDs are required to choose wallets.
4. If an explicitly authorized second test account exists, inspect only its own view. Copied/obsolete callbacks must not expose or modify the first owner's state. Group-chat entry must redirect to private subscription management. Do not message an unrelated person to obtain a second account.

Pass evidence: observed screen values match the designated owner's persisted state where accessible; support/terms work; no user/configuration mutation occurred. Absence of a second authorized account limits privacy coverage and must be reported.

## BILL-02 — Free selection and quota enforcement

Additional authorization/preconditions: dedicated test owner with known disposable entries already arranged near the relevant boundary, enforcement enabled, and explicit permission to change that owner's selection. Do not create ten wallets or alter the original user's selections under the default one-wallet budget.

1. Save the existing selection and protocol preferences. Attempt one temporary add at a full Free ten-entry boundary; it must fail without creating an extra row. If no prepared boundary exists, mark this assertion `Blocked` rather than rewriting unrelated entries.
2. Switch an authorized disposable entry in/out through buttons. Observe effective counts and paused status, preserving aliases, thresholds and desired preferences. Old buttons must reject stale revisions.
3. With two prepared, verified PM bindings, switch the one Free PM selection. Observe one effective PM entry; existing source capacity must remain enforced. No Hyperliquid opt-in may become effective on Free.
4. If a qualifying natural event occurs after a new activation boundary, correlate it to the public source and delivery. Silence does not prove suppression; use actual generation/outbox evidence for negative assertions.

Restore the exact test-owner selection/preferences, remove only the one temporary add if it succeeded unexpectedly, and record cleanup separately. This case does not authorize an operator grant or plan change.

## BILL-03 — Stars purchase and optional refund

Additional authorization/preconditions: explicit Stars spending budget and designated payer/test deployment, terms/support configured, sales enabled, known baseline paid periods. Production price is 1,450 Stars; lower isolated test prices are allowed only when separately configured and authorized, never by silently editing the shared deployment.

1. Review terms and create the purchase through the designated owner's subscription screen. Verify invoice amount, owner and plan before the authorized payer confirms it.
2. Record the Telegram charge, durable receipt, unique fulfilled order and resulting 720-hour paid period. A pre-checkout approval alone is not proof of payment.
3. Observe the subscription view/confirmation. If early renewal was the requested scenario, verify the new period follows existing contiguous access rather than restarting it immediately.
4. Observe normal replay/reconciliation if evidence is available; do not inject provider requests into a live endpoint unless the request explicitly authorizes that scope. Verify one grant per order/charge.
5. Only when refund scope is explicitly authorized, have an authorized operator refund the identified charge. Verify recorded reversal and removal of only that grant. An unknown timeout is reconciled, not blindly retried.

Pass evidence requires both Telegram payment evidence and durable ledger/access state. Screenshots alone do not prove settlement uniqueness. The ordinary cleanup rule does not authorize refunding a real purchase; report the purchase and any remaining paid period explicitly.

## BILL-04 — NOWPayments USDTBSC purchase and settlement

Additional authorization/preconditions: exact crypto spending budget/payer, real merchant/test account eligibility established, configured API/IPN and reconciliation credentials, public HTTPS callback, merchant USDTBSC verification, and an explicitly designated receiving wallet. Preserve the original owner's paid-period baseline.

1. Open an invoice from the designated owner; record opaque local order, provider invoice and displayed network/quote privately. Confirm USDT on BNB Smart Chain. USD 29 need not equal exactly 29 token units.
2. The authorized payer completes the exact quoted transfer. Do not approve a wallet signature or send funds without that financial authorization. Record provider payment and blockchain identity privately.
3. Observe provider progression. Pending/confirming/sending is insufficient. Verify authenticated final status, invoice/order association, currency and actual amount; underpayment must not grant access even if labelled `finished`.
4. Correlate one applied receipt/order/grant and the subscription view. Independently verify actual merchant-wallet settlement; payment detection alone does not establish payout.
5. Missing initial callback recovery is checked only in an explicitly authorized isolated test environment; do not disable a shared production endpoint. Verify invoice-filtered reconciliation finds the payment and creates no duplicate grant when callbacks later arrive.

Partial/excess/wrong-network/duplicate-payment experiments require separate budgets and scenarios; do not deliberately mispay under a normal smoke request. Crypto refunds are a separate authorized operator/provider action. Cleanup records the financial result and restores only temporary tracking state, not ledger evidence.

## BILL-05 — Expiry and restoration with preserved settings

Additional authorization/preconditions: designated disposable test owner with an explicitly authorized paid/operator period already due to expire within the observation budget, prepared Free selection and protocol preferences. If no such period exists, mark `Blocked` or `Inconclusive`; do not change the system clock or shared subscription price.

1. Record wallet/settings/selection and active enrollment generations before expiry. Observe the exact paid end boundary and resulting Free effective selection, zero effective HL entries and at most one PM entry.
2. Check preserved saved entries/aliases/filters and paused explanations. Worker delays must not extend paid delivery permission. A natural qualifying event plus durable evidence is required to establish suppression.
3. If a renewal/operator restoration was separately authorized, verify new generations/boundaries for interrupted modes, continuity for unchanged Free entries, and capacity-paused state when shared capacity is unavailable. Activity from the entitlement gap must not replay.

Do not revoke a real purchase or grant Pro merely to prepare this case. Restore only the authorized disposable owner's temporary preferences and report any surviving paid/operator period.

## BILL-06 — Payment recovery through an authorized restart

Passwordless integration uses API-key polling only for known payment IDs. Include first-identity attachment and child-evidence import only when explicitly authorized for the named payment scenario. If every callback for an unknown payment was missed, record manual dashboard/attachment recovery; do not claim automatic invoice discovery. Preserve the primary identity, original quote, audit and grant count. A received/imported additional deposit must remain separate review evidence and cannot grant another period. `/billing_recovery` inspection is read-only and operator-only. No scenario authorizes a merchant dashboard login/password to be stored by the bot.

Additional authorization/preconditions: explicitly scoped isolated payment plus restart/financial budget, prepared private baseline and provider evidence. This is not selected by “run billing smoke.”

1. Arrange the named boundary (receipt persisted, payment pending, or confirmation not yet delivered) using the authorized test provider/account. Do not kill a production database or trigger an unrequested outage.
2. Restart only the explicitly authorized service. Observe durable receipt/reconciliation progress, exactly one grant and eventual notice state.
3. Verify disabled new-sales flags do not stop accounting for already-issued invoices, if that flag change was part of the explicit request. Restore the original flags afterward.

Accepted-send/crash acknowledgement ambiguity can repeat a Telegram notice; it must not duplicate paid access. Record inaccessible evidence as `Inconclusive`. Use [the report template](report-template.md) with financial scope, amounts, outcome and cleanup stated separately.

## BILL-07 — Private admin navigation without granting access

Additional preconditions: designated authorized operator, deployed admin revision, private chat, and a baseline of wallet preferences and grant/audit/order/receipt counts. Do not change the allowlist to manufacture a passing privacy check.

1. Open `/myid` privately and verify it returns only the sender's ID. Open `/admin`; expect My account and Find user. Verify Admin is also available on the operator's private subscription overview.
2. Open My account and verify the plan/expiry matches persisted grants. Open Grant Pro and inspect 1-hour, 1/7/30/90-day and custom-day options. Cancel with `/admin_cancel`; do not confirm a grant.
3. Optionally use Find user with the same designated account's numeric ID. If a reason/confirmation preview is explicitly included in the check, inspect target/dates/reason and cancel. Never press Confirm under this navigation-only case.
4. If an already-authorized non-operator account is available, verify `/admin` and copied callbacks cannot expose account data; group entry must be refused. Otherwise record that privacy coverage as not run; automated tests alone do not establish live multi-account coverage.
5. Verify original wallet settings and grant/audit/order/receipt counts are unchanged. Keep screenshots/IDs in the private report. No ledger cleanup should be necessary; report any unexpected mutation as a failure.

Actual manual grants/revocations require separate explicit authorization naming the test owner, duration/action and intended surviving access. Never erase their audit evidence as cleanup.
