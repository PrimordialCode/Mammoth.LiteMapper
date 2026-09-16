---
name: agentstack-protocol-tracker-intake
description: >
  Use when an agent must find or select a tracker work item that is eligible for autonomous execution under AgentStack Protocol.
compatibility: >
  AgentStack Protocol repository layout with .agent-stack as the canonical source of truth.
metadata:
  tags: "agentstack-protocol backlog autonomous-agents"
allowed-tools: Read Bash(agentstack:*) Bash(git:*) Bash(gh:*) Bash(az:*)
---

# Tracker Intake

## CLI reference

Use `agentstack help work-item intake --json` for intake syntax and output, and `agentstack help work-item get --json` when a candidate item needs to be normalized in detail.

## When to use

Use this skill when the agent needs to discover eligible backlog work or choose among candidate work items.

## Commands

1. Run `agentstack work-item intake --limit <n>`.
2. For a candidate, run `agentstack work-item get <id>`.
3. Before claiming, hand off to `agentstack-tracker-graph`, then `agentstack-work-bootstrap`, then `agentstack-tracker-claim`.

## Decision rules

- Prefer items with `executionMode = agent`, `readyForAgent = true`, and a claimable protocol state.
- Do not infer readiness from prose; rely on the CLI-normalized fields.
- Return a single best candidate only when the rationale is explicit.

## Stop or escalate

- No eligible item exists.
- Required mapping or tracker data is missing.
- Candidate data conflicts with the active tracker mapping.

## Example

```sh
agentstack work-item intake --limit 10
agentstack work-item get 123
```
