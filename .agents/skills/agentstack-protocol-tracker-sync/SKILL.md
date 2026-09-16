---
name: agentstack-protocol-tracker-sync
description: >
  Use when an agent must keep tracker protocol state, progress comments, PR links, and local execution state aligned.
compatibility: >
  AgentStack Protocol repository layout with .agent-stack as the canonical source of truth.
metadata:
  tags: "agentstack-protocol backlog autonomous-agents"
allowed-tools: Read Bash(agentstack:*) Bash(git:*) Bash(gh:*) Bash(az:*)
---

# Tracker Sync

## CLI reference

Use `agentstack help work-item progress --json`, `agentstack help work-item state --json`, `agentstack help work-item block --json`, and `agentstack help work-item submit-review --json` for the current sync command contracts.

## When to use

Use this skill when implementation progress, protocol state, blockers, or PR links need to be reflected in the tracker.

## Commands

1. Run `agentstack work-item progress <id> --message <text>` after meaningful milestones.
2. Run `agentstack work-item state <id> --state <state>` only for intentional protocol transitions.
3. Run `agentstack work-item block <id> --reason <text>` when safe progress stops.
4. Run `agentstack work-item submit-review <id> --pr <url> --summary <text>` when work is ready for human review.

## Decision rules

- Keep updates concise and factual.
- Sync after meaningful state changes, not after every small edit.
- Do not use tracker-native comments for protocol updates when an `agentstack` command exists.
- Prefer `block` over continuing through ambiguity.

## Stop or escalate

- Stop if the tracker update fails or returns a state inconsistent with local work.
- Escalate when a requested state transition would misrepresent the actual work status.

## Example

```sh
agentstack work-item progress 123 --message "Parser implemented; running adapter tests."
```
