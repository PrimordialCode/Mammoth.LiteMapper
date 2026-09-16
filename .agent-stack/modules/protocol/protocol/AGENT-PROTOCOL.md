# AgentStack Protocol

AgentStack Protocol defines a collaboration protocol and workflow toolkit for humans and AI agents implementing backlog-driven software work.

It is intentionally tracker-independent. Agents reason in canonical terms, then use a tracker mapping dictionary to translate those terms into GitHub Issues, Azure DevOps Work Items, or future tracker concepts.

## Core idea

```text
protocol at the core
workflow at runtime
ubiquitous language at the boundary
tracker dictionary at the integration layer
skills at .agents/skills
```

## Canonical work item types

- `epic`
- `feature`
- `story`
- `task`
- `bug`
- `spike`

## Canonical states

- `draft`
- `ready`
- `claimed`
- `implementing`
- `blocked`
- `pr-open`
- `in-review`
- `done`
- `abandoned`

## Canonical relations

- `parent`
- `child`
- `blocks`
- `blocked-by`
- `related`

## Collaboration rules

1. Humans decide when a work item is ready for agent implementation.
2. Agents may only claim work explicitly mapped to `executionMode = agent` and `readyForAgent = true`.
3. Agents must check dependency relations before implementation.
4. Agents must use an isolated workspace before claiming work when multiple agents may run concurrently.
5. Agents must create or select the local identity in the same workspace that will own the claim.
6. Agents must create an execution plan before coding.
7. Agents must synchronize meaningful progress back to the tracker.
8. Agents must stop and raise a blocker instead of guessing through ambiguity.
9. Agents open a PR and submit completed work for review unless policy explicitly says otherwise.
10. Humans decide merge and final closure by default.

## Runtime workflow

```text
load language -> intake -> graph -> bootstrap workspace -> identity -> claim -> plan -> implement -> sync -> submit-review -> human review -> complete or rework
```

The workflow is executed through protocol skills under `.agents/skills/agentstack-protocol-*/SKILL.md`, with `.agents/skills/git-worktree-ops/SKILL.md` used for git worktree isolation.

After review, use `.agents/skills/agentstack-protocol-pr-complete/SKILL.md` only when a human explicitly authorizes agent completion. Use `.agents/skills/agentstack-protocol-pr-rework/SKILL.md` when PR comments or requested changes require more implementation before review can complete.

Agents must write tracker comments, execution plans, blockers, review summaries, and child work item descriptions in readable Markdown. Use `.agent-stack/modules/protocol/templates/markdown-style.md` for repository-local formatting guidance.

## Concurrent agent workspaces

Multiple agents may work in the same repository at the same time only when each claimed work item uses an isolated mutable workspace.

Recommended layout:

```text
main checkout
  used for setup, fetch, intake, and orchestration

../<repo>-worktrees/
  <work-item-id>-<agent-suffix>/
    dedicated git worktree
    dedicated branch
    dedicated .agent-stack/local/agent-identity.json
    dedicated .agent-stack/runs/<work-item-id>/claim.json
```

The repo-scoped worktree root should be registered as a Git safe directory root for sandboxed agents:

```sh
git config --global --add safe.directory "$(cd ../<repo>-worktrees && pwd -P)/*"
```

Do not register concrete worktree paths by default. If Git still reports dubious ownership inside a worktree, register the concrete path as a fallback:

```sh
git config --global --add safe.directory "$(pwd -P)"
```

After deleting a worktree that used a concrete fallback entry, remove that exact `safe.directory` value:

```sh
git config --global --fixed-value --unset-all safe.directory "$(cd ../<repo>-worktrees && pwd -P)/<work-item-id>-<agent-suffix>"
```

Keep the repo-scoped wildcard entry while that worktree root is still used by agents.

Agents must not share one mutable checkout for implementation work. Shared checkouts share a git index, working tree, local runtime files, dependency/build outputs, and branch state, so parallel agents can interfere with each other even when they claim different tracker items.

The safe startup sequence for one item is:

```text
from coordination checkout:
  doctor -> get -> graph -> create worktree

from the dedicated worktree:
  identity init/show -> claim with branch/workspace -> plan -> implement
```

The claim should be written from inside the dedicated workspace so claim metadata records the branch and workspace that will actually perform the implementation.

Workspace bootstrap should start from `.agent-stack/modules/protocol/workspace.json` `baseBranch` when configured. If it is not configured, use the repository default branch from `origin/HEAD`, then fall back to `origin/main`, `origin/master`, `main`, or `master`.

## Skill naming

All AgentStack Protocol skills must:

- live only under `.agents/skills/`
- use a directory named `agentstack-<skill-name>`
- contain a `SKILL.md` file
- have frontmatter `name` matching the directory name
- start with the `agentstack-` prefix to avoid collisions with other skill packages

Supporting skills may be deployed alongside AgentStack Protocol skills when they cover a required operational concern. `git-worktree-ops` is the supported git worktree operations skill used by the workspace bootstrap flow.

## Deployable repository footprint

A real repository should contain only the active tracker mapping and config.

```text
AGENTS.md
.agent-stack/
  README.md
  active-tracker.json
  workspace.json
  templates/markdown-style.md
  protocol/AGENT-PROTOCOL.md
  language/backlog-language.yaml
  policy/AGENT-POLICY.json
  trackers/<active-tracker>.mapping.yaml
  trackers/<active-tracker>.config.json
.agents/
  skills/
    agentstack-protocol-*/SKILL.md
    git-worktree-ops/SKILL.md
```

Optional vendor shims such as `CLAUDE.md`, `GEMINI.md`, and `.github/copilot-instructions.md` may be added, but `AGENTS.md` remains the generic entrypoint.

## Local execution files

If local machine-readable runtime state is needed, store it under:

```text
.agent-stack/runs/<work-item-id>/
  claim.json
  protocol-state.json
  protocol-log.jsonl
  execution-plan.json
  submit-review-summary.md
```

Skills belong only in `.agents/skills`.

## Local agent identity

Claims use two related values:

- `agentId`: identifies the autonomous agent instance that owns the work.
- `claimToken`: proves ownership of one specific claim.

The CLI may store local, uncommitted identity state under each workspace:

```text
.agent-stack/local/agent-identity.json
```

Agents may initialize or inspect it with:

```sh
agentstack agent identity init
agentstack agent identity show
```

`agentstack work-item claim` uses this identity when `--agent` is omitted. A successful claim stores its token locally under `.agent-stack/runs/<work-item-id>/claim.json` so later commands, such as release or future resume behavior, can prove ownership without requiring the human to paste a token.

Local identity and claim files are runtime state and must not be committed. `agentId` is useful for continuity across sessions in the same workspace, but token-sensitive operations must rely on `claimToken`. For concurrent work, each worktree should have its own local identity unless the same long-lived agent process intentionally owns multiple claims.

## Tracker profiles

Use `agentstack setup protocol ...` to deploy a selected tracker profile. Do not deploy inactive tracker mappings into ordinary product repositories unless the repository intentionally uses multiple backlog trackers.
