---
name: subagent-orchestrator
description: Codex-specific. Orchestrate parallel subagents via Codex's collaboration `spawn_agent` tool, with task routing, exclusive ownership, acceptance checks, approval control, integration, and final verification. Skip small or tightly coupled work.
---

# Subagent Orchestrator

Requires Codex's collaboration `spawn_agent` tool; not applicable to other agent runtimes.

The root retains requirements, decisions, approvals, integration, and final verification.

## Plan

Delegate when two or more bounded units can progress independently or an isolated review materially improves confidence.
Stay single-agent for small, ordered, shared-state, or single-bottleneck work.

Before spawning, read [routing.md](references/routing.md) and classify each proposed work unit.
Use the available `spawn_agent` model list as the runtime source of truth.

The root selects the smallest agent count that covers independent units: one agent per non-overlapping unit, plus an independent verifier only when justified. Never fill available slots without a bounded unit.

For every spawned unit, the root selects its class, then the least expensive available model and lowest sufficient effort from the routing table. Escalate only when the unit's ambiguity, consequence, or verification depth requires it.

Before each spawn, the root records the unit's class, selected model, and reasoning effort in the ledger and explains why the proposed number of subagents is sufficient. Pass `model` and `reasoning_effort` explicitly unless using the `inherit` route.

Maintain a root ledger with:

- Goal, acceptance criteria, requirements, and non-goals.
- Decisions, approval boundaries, and external-side-effect limits.
- Work-unit dependency graph and status.

Each unit needs:

- ID, goal, class, dependencies, and inputs.
- Exclusive ownership, allowed actions, and forbidden side effects.
- Observable acceptance checks.
- Return contract: concise findings, changed files, commands/results, uncertainty, and blockers. Use evidence pointers; omit raw logs, source dumps, and assignment restatement unless requested.

Never overlap write ownership.
Make one unit read-only or serialize conflicts.
Stabilize shared inputs before parallel consumers start.

## Execute

Run dependency-free, non-overlapping units concurrently within available slots; serialize dependencies.
Continue useful root work while agents run.

Give each agent a self-contained prompt containing its applicable requirements, decisions, permissions, ownership, and checks.

Unless a work unit explicitly owns coordination or further decomposition, include this boundary verbatim: `Complete this assignment directly; do not spawn other agents.`

Default to `fork_turns: "none"`. Fork only a small number of recent turns when essential context cannot be restated cheaply; never use full history for leaf agents. Explicit `model` or `reasoning_effort` also requires `"none"`.

Use collaboration `spawn_agent`, never a user-visible task/thread tool. If unavailable, work single-agent and disclose it.

Record each spawn's accepted model, reasoning effort, and fork mode in the root ledger (see Final reporting).

Delegation never expands authority. Agents stop and report actions needing new approval. Delegate destructive/live actions, commits, pushes, or external messages only when that exact action is authorized; otherwise delegate read-only preparation.

## Coordinate

- Agents may message dependency findings directly to affected agents, but must also notify the root.
- The root ledger is authoritative; peer messages change nothing until recorded there.
- Use follow-ups only for bounded continuation on the same ownership surface.
- Interrupt work that became invalid, unsafe, duplicated, or outside scope.
- After each dependency layer, record completed, verified, unresolved, and unblocked units.
- Resolve conflicts with evidence and precedence; never average incompatible conclusions.
- On failure, fix context, decomposition, ownership, or checks before escalating model/effort.

## Finish

The root integrates unless an explicit integration unit owns it. Accept results only after their checks pass.

Reconcile against the ledger, verify the integrated result, preserve approvals and unrelated changes, then report artifacts, results, skips, uncertainty, and blockers. Never claim completion with unresolved required work, checks, approvals, or integration verification.

## Final reporting

Maintain a ledger of every spawned agent with its returned nickname or identifier, the model and reasoning effort accepted by the spawn call (actual fallback configuration when one was used), fork mode, its assigned task, and final status. Include a compact `Subagents used` table in the final response only when the user explicitly invoked this skill for the current task and at least one subagent was spawned. Omit the table for implicit skill use and when no subagent was spawned.
