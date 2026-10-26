# Development Subagent Workflow

Established: 2026-10-19

Use a coordinated development team for substantial changes as EVMWhaleTracker grows beyond its initial Telegram MVP. This workflow adapts the BitUnixFairPriceSpreadTracker pattern to EVM tracking. It concerns development agents; application workers remain ordinary hosted services.

## Roles

Model and reasoning assignments live in `.codex/config.toml` and `.codex/agents/*.toml`. The table summarizes those files; update both when changing assignments.

| Role | Model | Effort | Responsibility |
| --- | --- | --- | --- |
| Coordinator | `gpt-6.1-sol` | medium | Scope, product decisions, architecture, task briefs, integration, validation, and user communication. Explicit chat settings take precedence. |
| `evm-researcher` | `gpt-6.1-sol` | medium | Verify protocol/provider/API behavior and map relevant code; read only. |
| `qa-planner` | `gpt-6.1-sol` | low | Independently derive acceptance criteria, boundaries, and failure cases; read only. |
| `implementer-files` | `gpt-6.1-sol` | low | Implement a bounded vertical slice and focused checks; sole writer. |
| `implementer-integration` | `gpt-6.1-sol` | medium | Fresh second pass through cross-layer behavior, configuration, and lifecycle; sole writer. |
| `reviewer` | `gpt-6.1-sol` | medium | Independent correctness/regression review; read only. |

These assignments reflect the user's model preferences. All roles use GPT-6.1 Sol, with low reasoning for QA planning and implementation and medium reasoning for coordination, research, integration, and review. Revisit assignments using observed quality and latency.

## Execution

1. **Brief:** Translate user intent into requirements, acceptance criteria, exclusions, affected flow, and validation. Read current context, conventions, and relevant plans. Record a plan for a meaningful feature or fix using `docs/plans/` conventions.
2. **Research and QA:** Delegate only useful independent investigations. Research and QA may run in parallel; reconcile assumptions before starting code that depends on them. Skip external research when local evidence is sufficient.
3. **Implementation:** Assign `implementer-files` one vertical slice. Give it explicit file ownership and interfaces; do not start another writer or edit concurrently in the coordinator.
4. **Integration:** Once the first writer finishes, start a new `implementer-integration` with fresh context. Supply requirements, acceptance criteria, repository paths, current code/diff access, and neutral check commands. Exclude the first implementer's reasoning, conclusions, and self-review.
5. **Review:** After writers finish, start a separate fresh `reviewer` against the requirements and current diff. This agent reports findings without making edits. Route fixes through a sole writer, then request review of the fixes.
6. **Finish:** The coordinator verifies the combined diff and relevant checks, updates affected plans/context, and reports changes, validation evidence, and unresolved limits. Agent completion alone does not establish feature completion.

Existing user authorization carries through these stages. Present unresolved material product choices when needed; do not require a new approval for every brief, agent handoff, or implementation pass. The BitUnix project's separate approval gate is not adopted here.

For documentation/configuration work, a coordinator edit followed by independent review is sufficient. For trivial fixes, explanations, or repository summaries, work directly when delegation adds no value. Two fresh implementation passes are expected for non-trivial code changes, not as ceremony for every task.

## Assignment Template

Give each agent a compact, self-contained assignment:

```text
Role: <role name; include its instructions when the tool cannot load a named role>
Repository: <absolute workspace path>
Goal and authorization: <requested outcome and relevant user decisions>
Requirements and acceptance criteria: <observable behavior>
Context: <runtime context, conventions, feature plan, relevant paths>
Ownership: <files allowed to change, or read-only>
Constraints and exclusions: <architecture rules and scope boundaries>
Dependencies: <interfaces or completed prerequisite stages>
Validation: <focused scenarios and commands, or checks to request from coordinator>
Deliverable: <changes/findings, evidence, assumptions, unresolved limits>
```

Use `fork_turns="none"` when available. Separate role names do not guarantee fresh context. When a tool exposes only model/effort/prompt parameters, explicitly apply settings and instructions from the role file. Keep findings and self-review from the first implementer out of integration/review assignments; those agents can inspect the implementation itself.

## Coordination and Guardrails

- The configuration caps concurrently open subagent threads at three, excluding the coordinator, subject to tighter runtime limits. There are more roles than simultaneous slots. Dependent stages run sequentially.
- Before spawning the next stage, collect needed results and close/release completed threads with the host's supported tool. A finished turn does not necessarily close a thread or release capacity. If no release tool exists and a fresh spawn is blocked, disclose the capacity limit and continue authorized work in the coordinator where feasible, reporting any unexecuted independent pass. Do not reuse a previous implementer as a substitute for a fresh integration agent or independent reviewer.
- At most one repository writer, including the coordinator. Research/QA can inspect stable files in parallel; integration and final review inspect a settled diff. Subagents do not spawn their own agents.
- Read-only is an instruction boundary: custom agent files inherit session permissions and do not provide a separate enforced sandbox. Read-only roles must avoid writes, including build/test artifacts; ask the coordinator to run such checks.
- Preserve unrelated user edits. No agent resets, cleans, or reverts others' changes. Coordinate when ownership needs to expand.
- Preserve Clean Architecture, Result business failures, PostgreSQL as the source of truth, immediate cache updates, bounded channels, chain/user isolation, and in-memory caching. Do not introduce Redis or split services as part of routine implementation.
- Token detection uses websocket Transfer subscriptions. Native scanning is disabled; per-block token log polling and per-transaction receipt fetching must not be introduced without an explicit scope change.
- Do not use real user messages or live infrastructure mutations as routine validation. Keep secrets out of prompts, logs, and handoff artifacts.
- Record actual commands and outcomes. Run `dotnet test EVMWhaleTracker.slnx` after adding a generated EF migration. Do not broaden/repeat checks without a relevant reason.
- If a model or tool is unavailable, disclose the fallback and its limits. If subagents cannot run, finish authorized work in the coordinator where feasible and state that independent review was not executed. Do not claim configuration alone proves a role was used.

## Configuration Reference

Project defaults and standalone role files follow the [official subagent configuration](https://learn.chatgpt.com/docs/agent-configuration/subagents). They apply to future sessions/spawns as supported by the host; they do not switch the model of a running chat. This session can apply role settings explicitly through its spawning tool.
