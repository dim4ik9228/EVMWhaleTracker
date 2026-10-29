# Subscription screen layout

- Status: completed
- Date: 2026-10-28
- Summary: replace the crowded subscription dump with a compact overview and separate navigable detail views.

## Requirements

The user rejected the deployed subscription screen as too crowded. Keep the overview focused on the current plan, wallet usage and relevant expiry/Free Polymarket selection. Use short labelled lines, whitespace and restrained Telegram HTML emphasis. Hide absent expiry, empty history, global capacity counters, repeated tier descriptions, raw IDs and operational jargon from the overview. A meaningful paused-wallet count is allowed when saved entries exceed the active allowance.

Expose a clear wallet-management action and concise navigation to Pro details, payments/history and help. Free and Pro views must reflect the agreed allowances. Pro details carry the 50-wallet/Polymarket/ten-Hyperliquid benefits, USD29 USDTBSC or1450Stars/30days, manual renewal and shared-capacity caveat. Show unavailable-sales state honestly and never offer payment/terms confirmation when sales are disabled. History is fetched only when opened, is owner-scoped/bounded, and has a clean empty state; receipt IDs remain available there for support. Help carries terms/support navigation.

Callback navigation should update the same bot message, including Free-selection pagination/return navigation, instead of appending a new message for every page. `/subscription` opens one fresh overview. Preserve owner/private-chat checks, stale selection protection, limiter behavior and existing financial callback compatibility. Accepting/continuing terms may prepare the existing Stars invoice and crypto option; submitting an actual payment remains a separate explicit action. Do not add another checkout step or change the agreed Stars-first/crypto-alternative flow. HTML-escape dynamic text. Do not alter prices, tiers, flags, grants, billing storage, source capacity or tracking behavior.

## Validation and delivery

Use the repository's sole-writer implementation, fresh integration and independent review workflow. Focus verification on readable Free/Pro output, hidden empty information, navigation/private ownership, no invoice on read-only views and disabled-sales behavior. No migration or financial test is needed. Build and relevant presentation regressions are sufficient; do not rerun unrelated provider suites without cause.

Deploy the presentation fix as continuation of the authorized deployment/live UI testing. Inspect the real Telegram overview and detail/back navigation without changing original selections, spending or enabling flags. Save a private screenshot/report under ignored live-validation artifacts. Update affected operational/runbook copy to match navigation.

## Completion — 2026-10-28

The overview now shows a short plan heading, wallet usage, relevant current expiry/Free selection and a wallet-list shortcut. Pro details, Payments and Help have separate views; empty expiry/history and global counters are removed from the overview. Payments retains bounded owner-scoped receipts and nonreversed access periods. Same-message navigation includes Free selection/pagination/return and preserves legacy callbacks and the existing checkout flow. Current expiry excludes disconnected future periods after a refund gap.

Implementation and fresh integration completed. Independent review found stale navigation documentation; the operations guide and live runbook were corrected and verified. No actionable review findings remain. Coordinator build/presentation verification passed **49 tests, zero failed/skipped**; existing dependency advisory warnings remain. No schema or billing-policy changes were made.

Rebuilt/recreated the local app only. Deployed image: `sha256:4bc64acb583263b83b034e41dfc0853be18d56085bd7b256dd935c8ecece0961`. In the real Telegram bot, verified the compact overview, legacy button compatibility, Pro details/unavailable-sales state, empty Payments, Help and return navigation within the same message. No original selection or wallet/protocol setting changed. Billing health returned 200 with empty work counters; app had zero restarts/error-level log lines during observation. Ledger remained empty and flags unchanged; no payment was prepared or taken. Private baseline, logs and screenshot are under ignored live-validation artifacts.
