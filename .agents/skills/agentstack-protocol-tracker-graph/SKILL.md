---
name: agentstack-protocol-tracker-graph
description: >
  Use when an agent must inspect parent, child, blocking, blocked-by, and related backlog relationships before work starts or changes scope.
compatibility: >
  AgentStack Protocol repository layout with .agent-stack as the canonical source of truth.
metadata:
  tags: "agentstack-protocol backlog autonomous-agents"
allowed-tools: Read Bash(agentstack:*) Bash(git:*) Bash(gh:*) Bash(az:*)
---

# Tracker Graph

## CLI reference

Use `agentstack help work-item graph --json` for the current graph command syntax, output shape, and policy effects.

## When to use

Use this skill before claim, before broadening scope, or whenever dependency and hierarchy status may affect safe execution.

## Commands

1. Run `agentstack work-item graph <id>`.
2. If detail is needed, run `agentstack work-item get <related-id>` for parent, child, blocker, or blocked work items.

## Decision rules

- Start work only when `canStart` is true.
- Treat open `blockedBy` relations as blockers.
- Treat missing relation support as a protocol risk, not as permission to continue.
- Use child items for separable work instead of silently expanding the current item.

## Stop or escalate

- Stop when `canStart` is false.
- Escalate unsupported relation mappings, inaccessible related items, or unclear parent/child ownership.

## Example

```sh
agentstack work-item graph 123
```
