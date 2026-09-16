---
name: agentstack-protocol-pr-complete
description: >
  Use when a human reviewer has approved an AgentStack pull request and explicitly authorized the agent to merge or complete the reviewed work item.
metadata:
  tags: "agentstack-protocol backlog autonomous-agents post-review pull-requests"
allowed-tools: Read Bash(agentstack:*) Bash(git:*) Bash(gh:*) Bash(az:*)
---

# PR Complete

## CLI reference

Use `agentstack help work-item progress --json`, `agentstack help work-item state --json`, and `agentstack help work-item release --json` for current command contracts.

## When to use

Use this skill only after a human reviewer has approved the PR and explicitly authorized the agent to finish the work.

## Commands

1. Verify the human authorization is explicit and applies to the current PR/work item.
2. Verify the PR status, target branch, required checks, and repository merge policy using the repository's normal GitHub or Azure DevOps tooling.
3. Merge the PR only when the human authorization and repository policy both allow it.
4. Run `agentstack work-item progress <id> --message <text>` to record the approved completion action.
5. Run `agentstack work-item state <id> --state done` after the PR is merged or the human confirms completion.
6. Clear the active claim only when appropriate for the repository's audit convention.

## Decision rules

- Do not infer approval from green checks alone. Human authorization must be explicit.
- Do not merge when required checks are failing, pending, missing, or ambiguous.
- Do not bypass branch protection, required reviews, or repository merge policy.
- Do not mark the work item `done` before the PR is merged or a human explicitly says the work item is complete.
- Prefer tracker-native completion by a human when policy requires human-controlled merge.
- Treat `agentstack work-item release` as claim cleanup, not as proof of successful completion.

## Stop or escalate

- Stop if the human instruction is unclear, stale, or refers to a different PR/work item.
- Stop if PR checks or review state cannot be verified.
- Stop if merge conflicts, branch protection, or external checks block the merge.
- Escalate if the tracker state, PR state, and local branch disagree.

## Example

```sh
agentstack work-item progress 123 --message "Review approved. PR merged; marking the work item done."
agentstack work-item state 123 --state done
```
