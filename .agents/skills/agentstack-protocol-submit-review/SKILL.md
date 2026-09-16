---
name: agentstack-protocol-submit-review
description: >
  Use when an agent has completed implementation and must submit the work for human review.
compatibility: >
  AgentStack Protocol repository layout with .agent-stack as the canonical source of truth.
metadata:
  tags: "agentstack-protocol backlog autonomous-agents"
allowed-tools: Read Bash(agentstack:*) Bash(git:*) Bash(gh:*) Bash(az:*)
---

# Submit Review

## CLI reference

Use `agentstack help work-item submit-review --json` for the current review submission command syntax, output shape, and policy effects.

## When to use

Use this skill when implementation is complete, validation has run, and the work is ready for human review through a PR.

## Commands

1. Verify the working tree and validation results.
2. Open or update the PR with the repository's normal GitHub/Azure workflow.
3. Run `agentstack work-item submit-review <id> --pr <url> --summary <text>`.

## Decision rules

- The summary must include changes, validation, risks, and known limitations.
- `submit-review` means the agent's scoped work is complete and review-ready.
- Do not use this skill for partial work transfer; true partial handoff is future protocol behavior.
- Do not self-approve or merge from this skill. Use `agentstack-protocol-pr-complete` only after explicit human authorization.
- Use `agentstack-protocol-pr-rework` if the human requests changes after review.

## Stop or escalate

- Stop if validation has not run or failed without explanation.
- Stop if no PR URL exists.
- Escalate if policy requires review and a human reviewer is not identifiable.

## Example

```sh
agentstack work-item submit-review 123 --pr https://github.com/OWNER/REPO/pull/456 --summary "Implemented claim checks; npm test passes."
```
