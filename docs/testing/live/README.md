# Manual live acceptance checks

These runbooks validate the bot that is actually running: Telegram interaction, real provider data, persisted state where accessible, and delivered notifications. They are executed by a human or an agent through existing tools; they are not an automatic test runner.

## Invoke only when requested

Example prompts:

- `Run the live runtime smoke checks.`
- `Run the live Hyperliquid smoke checks. Use one temporary public wallet and stop after three alerts or ten minutes.`
- `Run PM-01 and PM-02 from the live Polymarket runbook.`
- `Run the live Polymarket recovery case PM-04; restarting the local app container is allowed.`

A change, build, deployment, ordinary test request or instruction to create/update cases does not invoke these runbooks. They do not run in CI or after every edit. An explicit live-check request selects only its relevant cases; ask about scope only if it is materially ambiguous. Do not turn a requested check into an implementation or deployment task without authorization.

| Suite | Default selection | Additional cases |
| --- | --- | --- |
| [Runtime](runtime.md) | RT-01, RT-02 | RT-03 natural EVM transfer |
| [Hyperliquid](hyperliquid.md) | HL-01, HL-02 | HL-03 threshold; HL-04 restart recovery; HL-05 natural ledger event |
| [Polymarket](polymarket.md) | PM-01, PM-02 | PM-03 threshold; PM-04 restart recovery; PM-05 natural redemption |

Feature suites include RT-01 as a prerequisite. A request for all live smoke checks runs the three default selections, within one overall budget. A feature request does not select its additional cases unless named. Financial actions are never part of these suites.

## Before a run

1. Read current [runtime context](../../ai/context.md), this index and only the selected suite. Verify the case still describes the implementation. Report stale steps instead of inventing expected behavior.
2. Identify the target environment, deployed revision if known, actual bot identity, recipient chat and available access. Do not silently test a different deployment. A working-tree revision does not prove which image is running.
3. Use the current deployment and feature flags. Missing Docker, Telegram login, provider access or disabled features produce `Blocked`; do not deploy, enable global flags, switch VPN, or restart services implicitly. If restoration is requested separately, perform and report it separately.
4. Record a baseline of existing wallet IDs, addresses, aliases, network choices, thresholds and protocol preferences for the designated test owner. Use read-only database evidence where available; otherwise use the UI and state that preservation evidence is narrower. Keep private baselines outside committed docs.
5. For tests needing a wallet, choose one recent, naturally active public address appropriate to the case. Check that it is not already tracked by the test owner and that distinct-address capacity is available. Do not delete/re-add an existing wallet, borrow its settings or evict another user's source to make room. Record the chosen public account and source of activity in the local report.
6. Use an unmistakable temporary alias, such as `LIVE-HL-<run-id>` or `LIVE-PM-<run-id>`. Save the created wallet ID immediately. Limit EVM networks/directions to the case so unrelated alerts do not obscure results. A Hyperliquid or Polymarket run must not opt the wallet into the other protocol.

## Bounded execution and evidence

- Default overall budget: ten minutes of observation and at most three received test activity alerts across the run. Use at most one temporary wallet at a time. Reserve cleanup time; do not treat budget exhaustion as permission to keep waiting or change accounts repeatedly. The alert budget is a stop trigger, not a promise that an already in-flight message can be recalled.
- Stop activity collection early if evidence is sufficient, provider throttling repeats, a backlog grows, delivery targets the wrong chat, or ordinary use is disrupted. Disable the temporary source and clean up before investigating further.
- A source event must occur after the recorded enrollment/activation boundary to establish future live detection. Historical API responses, reconnect snapshots and database records alone do not prove a fresh Telegram alert.
- Match at least one observed alert to independent public API/explorer evidence. Record event identity, amounts/units, source time and Telegram receipt time where visible. Do not claim measured latency from approximate UI timestamps.
- For a negative assertion such as threshold suppression or opt-out, prove that a qualifying source event actually happened in the relevant window. Silence alone is inconclusive. If source/normalization/outbox evidence is inaccessible, state what could not be established.
- No trading, transfers, signatures, wallet connection, financial checkout, artificial liquidation, forced reorg, provider outage or database edits. Restarts and multi-user scenarios require a request that explicitly includes that scope. Do not inspect unrelated chats or send messages to human contacts.
- Keep reports concise: selected cases, decisive evidence, outcome and cleanup. Reuse evidence across selected cases when valid; do not repeatedly dump full logs, message histories or database rows. Existing shell/browser tools are sufficient; no new framework is required.

## Cleanup is mandatory

Disable/remove only temporary wallets created by this run, identified by saved wallet ID and owner. Use supported bot/application actions; do not delete persistent source history or rewrite database rows. Check pending deliveries for these wallet IDs where access permits and distinguish pending/cancelled work from an already in-flight message. Do not wait indefinitely for retained historical rows to disappear.

Compare the original owner baseline after cleanup. Other users' settings and runtime/global configuration must remain untouched. If a run is interrupted, retain its baseline and temporary IDs and perform cleanup before another run. Failed or inaccessible cleanup must be reported prominently; functional success does not make cleanup successful.

## Results and storage

Use [report-template.md](report-template.md). Store run-specific reports, screenshots and private baselines under `artifacts/live-validation/<run-id>/` (Git-ignored), or an approved private artifact directory outside the Docker build context. Do not commit credentials, raw environment/config dumps, private aliases, chat IDs or full user histories. Do not send these artifacts to a remote service without authorization.

Case results are `Passed`, `Failed`, `Blocked`, `Inconclusive` or `Not run`. `Blocked` means a prerequisite is missing; `Inconclusive` means observation did not establish the behavior, for example no suitable natural event. Mark checks requiring unavailable evidence accordingly rather than passing the whole suite. Record cleanup separately as `Passed`, `Failed`, `Blocked` or `Not needed`.

Changing case definitions or fixing a defect does not make an old failure pass. A new execution supplies a new result. Committed implementation briefs may retain sanitized historical rollout summaries; these runbooks are the reusable instructions.
