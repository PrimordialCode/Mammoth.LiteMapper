---
name: agentstack-protocol-orchestrator
description: >
  Use when an autonomous agent needs to decide which AgentStack Protocol skill to invoke next while executing backlog-driven software work.
compatibility: >
  AgentStack Protocol repository layout with .agent-stack as the canonical source of truth.
metadata:
  tags: "agentstack-protocol backlog autonomous-agents"
allowed-tools: Read Bash(agentstack:*) Bash(git:*) Bash(gh:*) Bash(az:*)
---

# Orchestrator

## CLI reference

Use `agentstack help --json` for the full command catalog and `agentstack help <command> --json` for the current command contract before invoking workflow commands.

## When to use

Use this skill to choose the next AgentStack workflow skill while executing tracker-backed work.

## Commands

1. Run `agentstack doctor` before relying on protocol assets.
2. Run `agentstack help --json` when command contracts are needed.
3. Delegate to the specific workflow skill for intake, graph, bootstrap, claim, plan, implement, sync, block, submit-review, PR completion, or PR rework.

## Decision rules

- Start with `agentstack-backlog-language` when tracker meaning is unclear.
- Use the normal path: intake, graph, bootstrap workspace, identity, claim, plan, implement, sync, submit-review.
- Use `agentstack-protocol-pr-complete` only when a human explicitly approves and authorizes agent completion.
- Use `agentstack-protocol-pr-rework` when PR review comments or requested changes send the work back for implementation.
- In concurrent agent runs, claim only from the dedicated workspace that will do the implementation.
- Invoke `agentstack-block` whenever safe progress is impossible.
- Do not parse tracker-native labels or fields when an `agentstack` command can provide normalized output.

## Stop or escalate

- Never skip claim before implementation.
- Never claim implementation work from a shared mutable checkout when concurrent agents may run.
- Never implement blocked work.
- Never close work unless policy explicitly allows it.
- Stop when `agentstack doctor` fails.

## Example

```sh
agentstack doctor
agentstack help work-item claim --json
```
