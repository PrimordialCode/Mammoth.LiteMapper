---
name: agentstack-protocol-work-plan
description: >
  Use when an agent must turn normalized tracker context and repository context into a concrete scoped execution plan before coding.
compatibility: >
  AgentStack Protocol repository layout with .agent-stack as the canonical source of truth.
metadata:
  project: agentstack-protocol
  version: "0.1.0"
  tags: "agentstack-protocol backlog autonomous-agents"
allowed-tools: Read Bash(agentstack:*) Bash(git:*) Bash(gh:*) Bash(az:*)
---

# Work Plan

## CLI reference

Use `agentstack help work-item plan --json` for the current plan command syntax and output contract.

## When to use

Use this skill after graph check, workspace bootstrap, identity initialization, and a valid claim, before making code changes.

## Commands

1. Run `agentstack work-item get <id>` if the work item context is stale.
2. Run `agentstack work-item graph <id>` if dependencies or scope may have changed.
3. Run `agentstack work-item plan <id> --message <plan>`.

## Decision rules

- The plan must state outcome, impacted areas, implementation steps, validation, assumptions, and scope boundaries.
- Keep the plan scoped to the claimed work item.
- Propose child work items for separable or expanded scope.

## Stop or escalate

- Acceptance criteria are absent and the change is ambiguous.
- Dependencies are unresolved.
- The change exceeds the claimed scope.

## Example

```sh
agentstack work-item plan 123 --message "Update parser, add adapter tests, run npm test."
```
