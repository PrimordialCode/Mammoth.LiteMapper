---
name: agentstack-protocol-work-implement
description: >
  Use when an agent has a claim and execution plan and must implement scoped code changes with validation.
compatibility: >
  AgentStack Protocol repository layout with .agent-stack as the canonical source of truth.
metadata:
  tags: "agentstack-protocol backlog autonomous-agents"
allowed-tools: Read Bash(agentstack:*) Bash(git:*) Bash(gh:*) Bash(az:*)
---

# Work Implement

## CLI reference

Use `agentstack help work-item progress --json`, `agentstack help work-item block --json`, and `agentstack help work-item submit-review --json` when implementation reaches those tracker sync points.

## When to use

Use this skill after the work item is claimed and an execution plan has been recorded.

## Commands

1. Run repo discovery commands as needed, such as `rg`, `npm test`, or project-specific checks.
2. Run `agentstack work-item progress <id> --message <text>` after meaningful milestones.
3. Run `agentstack work-item block <id> --reason <text>` if safe progress stops.
4. Hand off to `agentstack-submit-review` when implementation and validation are complete.

## Decision rules

- Keep edits inside the claimed scope.
- Prefer existing project patterns over new abstractions.
- Add or update tests according to risk and blast radius.
- Treat failing validation as part of the work unless it needs a human decision.

## Stop or escalate

- Stop when requirements, ownership, or validation failures require a product or maintainer decision.
- Stop when the implementation would exceed the claimed work item.

## Example

```sh
agentstack work-item progress 123 --message "Implemented mapping validation; typecheck is passing."
```
