---
name: agentstack-protocol-work-bootstrap
description: >
  Use when an agent must prepare an isolated branch, worktree, or workspace before claiming and implementing a work item.
compatibility: >
  AgentStack Protocol repository layout with .agent-stack as the canonical source of truth.
metadata:
  tags: "agentstack-protocol backlog autonomous-agents"
allowed-tools: Read Bash(agentstack:*) Bash(git:*) Bash(gh:*) Bash(az:*)
---

# Work Bootstrap

## CLI reference

Use `agentstack help work-item graph --json`, `agentstack help agent identity --json`, and `agentstack help work-item claim --json` to inspect the command contracts involved in safe startup.

## When to use

Use this skill after graph validation and before claim when an isolated branch, worktree, or workspace is needed. This is required when multiple agents may work concurrently.

## Commands

1. Run `agentstack work-item graph <id>` from the coordination checkout.
2. Resolve the base branch from `.agent-stack/modules/protocol/workspace.json` `baseBranch`, then from the repository default branch, unless a human gave an explicit branch.
3. Create a dedicated git worktree and branch for the work item.
4. Change into the dedicated worktree.
5. Run `agentstack agent identity init` or `agentstack agent identity show` in that worktree.
6. Claim from inside that worktree with `agentstack work-item claim <id> --branch <name> --workspace <path>`.

## Decision rules

- Use deterministic branch/workspace names that include the work item id.
- Use `.agent-stack/modules/protocol/workspace.json` `worktreeRoot` when configured; otherwise use `../<repo>-worktrees`.
- The identity used for claim must live in the worktree that will implement the item.
- Claim only after entering the isolated worktree.
- Do not modify source files before the plan is recorded.

## Stop or escalate

- Stop if the item is already claimed by another agent.
- Stop if branch/worktree setup would overwrite unrelated local changes.
- Stop if the repository cannot create an isolated workspace and concurrent agents may run.

## Example

```sh
repo_name=$(basename "$(git rev-parse --show-toplevel)")
worktree_root="../${repo_name}-worktrees"
base_branch=$(git symbolic-ref --quiet --short refs/remotes/origin/HEAD || echo origin/main)
mkdir -p "$worktree_root"
git config --global --add safe.directory "$(cd "$worktree_root" && pwd -P)/*"
git worktree add "${worktree_root}/123" -b agentstack/123 "$base_branch"
cd "${worktree_root}/123"
agentstack agent identity init
agentstack work-item claim 123 --branch agentstack/123 --workspace "${worktree_root}/123"
```
