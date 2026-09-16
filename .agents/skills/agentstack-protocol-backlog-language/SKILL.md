---
name: agentstack-protocol-backlog-language
description: >
  Use when an agent must interpret, normalize, create, link, or update backlog items through the repository-specific ubiquitous delivery language and active tracker mapping dictionary.
compatibility: >
  AgentStack Protocol repository layout with .agent-stack as the canonical source of truth.
metadata:
  tags: "agentstack-protocol backlog autonomous-agents"
allowed-tools: Read Bash(agentstack:*) Bash(git:*) Bash(gh:*) Bash(az:*)
---

# Backlog Language

## CLI reference

Use `agentstack help language validate --json` and `agentstack help mapping validate --json` for the current validation command contracts.

## When to use

Use this skill before tracker intake, graph analysis, planning, synchronization, or tracker updates.

## Commands

1. Run `agentstack language validate`.
2. Run `agentstack mapping validate`.
3. Read `.agent-stack/modules/protocol/language/backlog-language.yaml` and the active tracker mapping only when interpretation details are needed.

## Decision rules

- Translate tracker-native fields, labels, tags, states, and relations only through explicit mappings.
- Verify that type, readiness, execution mode, protocol state, priority, and relations are mapped before relying on them.
- Load only the active tracker mapping.

## Stop or escalate

- Do not infer readiness from prose.
- Do not assume a tracker label, tag, or field has protocol meaning unless the mapping says so.
- Do not invent new canonical work item types.
- Stop when language or mapping validation fails.

## Example

```sh
agentstack language validate
agentstack mapping validate
```
