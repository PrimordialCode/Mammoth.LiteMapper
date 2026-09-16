---
name: agentstack-protocol-tracker-claim
description: >
  Use when an agent must exclusively claim an eligible work item before planning or implementation.
compatibility: >
  AgentStack Protocol repository layout with .agent-stack as the canonical source of truth.
metadata:
  tags: "agentstack-protocol backlog autonomous-agents"
allowed-tools: Read Bash(agentstack:*) Bash(git:*) Bash(gh:*) Bash(az:*)
---

# Tracker Claim

## CLI reference

Use `agentstack help work-item get --json`, `agentstack help work-item graph --json`, and `agentstack help work-item claim --json` for current flags, output fields, policy effects, and failure shapes.

## When to use

Use this skill after graph validation and workspace bootstrap, immediately before planning or implementation.

## Commands

1. Run `agentstack work-item get <id>`.
2. Run `agentstack work-item graph <id>`.
3. Confirm the agent is inside the isolated workspace that will implement the item.
4. Run `agentstack agent identity show` or `agentstack agent identity init`.
5. If eligible, run `agentstack work-item claim <id> --branch <name> --workspace <path>`.

## Decision rules

- Claim only when `graph.canStart` is true.
- Treat an active claim by another agent as exclusive ownership.
- In concurrent runs, claim from the dedicated worktree, not from the shared coordination checkout.
- Include branch and workspace metadata when available.
- Use `--force` only for explicit human-approved exceptions or controlled smoke tests.
- Preserve the returned `claim.claimToken`; release and future conflict handling depend on it.

## Stop or escalate

- Stop on `claimed: false`, claim conflict, active claim, blocked dependency, unsupported relation data, or policy failure.
- Escalate when eligibility is ambiguous or the tracker response cannot prove the claim belongs to this agent.

## Example

```sh
agentstack work-item get 123
agentstack work-item graph 123
agentstack agent identity init
agentstack work-item claim 123 --branch agentstack/123 --workspace ../<repo>-worktrees/123
```
