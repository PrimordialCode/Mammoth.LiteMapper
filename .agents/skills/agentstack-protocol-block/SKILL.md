---
name: agentstack-protocol-block
description: >
  Use when an agent must stop unsafe execution and publish the smallest useful blocker record needed for a human or dependency to unblock work.
compatibility: >
  AgentStack Protocol repository layout with .agent-stack as the canonical source of truth.
metadata:
  tags: "agentstack-protocol backlog autonomous-agents"
allowed-tools: Read Bash(agentstack:*) Bash(git:*) Bash(gh:*) Bash(az:*)
---

# Block

## CLI reference

Use `agentstack help work-item block --json` for the current block command contract and `agentstack help work-item release --json` if the active claim may need to be released.

## When to use

Use this skill when safe progress is impossible because of ambiguity, missing dependency work, tool failure, validation failure, or scope conflict.

## Commands

1. Run `agentstack work-item block <id> --reason <text>`.
2. If abandoning the work, run `agentstack work-item release <id>`.

## Decision rules

- The block reason must state the exact impediment and the smallest decision needed.
- Include completed work, current branch/workspace, and validation status when relevant.
- Prefer blocking over guessing through ambiguous requirements.

## Stop or escalate

- Do not mark blocked work as complete.
- Escalate immediately when continuing could corrupt data, broaden scope, or hide failed validation.

## Example

```sh
agentstack work-item block 123 --reason "Acceptance criteria do not define retry behavior for transient tracker failures."
```
