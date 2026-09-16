---
name: agentstack-protocol-pr-rework
description: >
  Use when a human reviewer leaves PR comments, requests changes, rejects approval, or otherwise sends an AgentStack pull request back for more work before review can be completed.
metadata:
  tags: "agentstack-protocol backlog autonomous-agents post-review pull-requests"
allowed-tools: Read Bash(agentstack:*) Bash(git:*) Bash(gh:*) Bash(az:*)
---

# PR Rework

## CLI reference

Use `agentstack help work-item state --json`, `agentstack help work-item progress --json`, `agentstack help work-item block --json`, and `agentstack help work-item submit-review --json` for current command contracts.

## When to use

Use this skill when the PR has review comments, requested changes, failed review follow-up, or human notes that require more work before the PR can be completed.

## Commands

1. Inspect the PR review comments and requested changes with the repository's normal GitHub or Azure DevOps tooling.
2. Run `agentstack work-item state <id> --state implementing` before resuming implementation.
3. Run `agentstack work-item progress <id> --message <text>` with a concise summary that review changes are being addressed.
4. Implement only the requested follow-up work and any directly required fixes.
5. Run the relevant validation commands.
6. Push updates to the existing PR branch.
7. Run `agentstack work-item submit-review <id> --pr <url> --summary <text>` again when the PR is ready for another human review.

## Decision rules

- Treat PR comments as scoped work tied to the existing claim unless they clearly exceed the work item.
- Resolve every actionable review comment or explain why it is not addressed in the resubmission summary.
- Keep the original PR open unless the maintainer requests a new PR.
- Use `agentstack work-item block` if a review comment requires a product decision, credential, missing dependency, or out-of-scope change.
- Do not mark the work item `done` while requested changes remain unresolved.

## Stop or escalate

- Stop if review comments conflict with the acceptance criteria or each other.
- Stop if requested changes require expanding scope beyond the work item.
- Stop if the PR branch cannot be updated safely.
- Escalate when review feedback requires a human product or architecture decision.

## Example

```sh
agentstack work-item state 123 --state implementing
agentstack work-item progress 123 --message "Review changes requested. Addressing PR comments before resubmitting."
agentstack work-item submit-review 123 --pr https://github.com/OWNER/REPO/pull/456 --summary "Addressed review comments and reran validation."
```
