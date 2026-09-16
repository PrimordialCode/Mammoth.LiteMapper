# AgentStack Protocol assets

This folder contains the repository-local AgentStack Protocol assets consumed by AI agents and the global `agentstack` CLI.

## Contents

- `active-tracker.json`: selects the active tracker and points to its config/mapping.
- `protocol/AGENT-PROTOCOL.md`: collaboration protocol, states, events, and review submission rules.
- `language/backlog-language.yaml`: canonical backlog vocabulary and DDD-style ubiquitous delivery language.
- `trackers/<tracker>.mapping.yaml`: tracker-specific dictionary from native tracker concepts to canonical concepts.
- `trackers/<tracker>.config.json`: repository-specific tracker connection/configuration. This file must not contain placeholders.
- `policy/AGENT-POLICY.json`: execution policy gates for autonomous agents.
- `workspace.json`: optional local workspace defaults such as base branch and worktree root.
- `templates/markdown-style.md`: repository-customizable Markdown style for tracker text.

## Policy

`.agent-stack/modules/protocol/policy/AGENT-POLICY.json` is the repository-local execution policy. The CLI loads it for workflow decisions and falls back to built-in recommended defaults if it is missing.

Policy fields:

- `requireAcceptanceCriteria`: used by `work-item graph` and `work-item claim` when calculating whether an item can start. If true, claim requires parsed acceptance criteria unless `--force` is used.
- `requireHumanReviewBeforeMerge`: reported by `work-item submit-review` as `reviewRequired`.

## CLI Help

The CLI is the source of truth for AgentStack command contracts.

Use:

```sh
agentstack help
agentstack help work-item claim
agentstack work-item claim --help
```

For agent and tool consumption, prefer machine-readable help:

```sh
agentstack help --json
agentstack help work-item claim --json
agentstack help work-item submit-review --json
```

AgentStack skills should reference `agentstack help ... --json` for current flags, output, examples, and policy effects instead of duplicating full command manuals.

## Local Agent Identity

The CLI can create local, uncommitted agent identity state under:

```text
.agent-stack/local/agent-identity.json
```

Use:

```sh
agentstack agent identity init
agentstack agent identity show
```

`agentstack work-item claim` uses this identity when `--agent` is omitted and saves the claim token under `.agent-stack/runs/<id>/claim.json`. Release can use that saved token when `--claim-token` is omitted.

For concurrent work, initialize identity inside the dedicated git worktree or isolated workspace that will own the claim, then claim from that workspace with branch/workspace metadata.

## Skills

Skills do **not** live under `.agent-stack`.

AgentStack protocol skills live only under:

```text
.agents/skills/agentstack-protocol-*/SKILL.md
```

The `agentstack-` prefix reduces naming collisions with other skill packages. Supporting skills may also be deployed when the protocol depends on them. `git-worktree-ops` is the supporting skill for git worktree isolation in concurrent agent workflows.

Skill index:

| Skill | Use |
| --- | --- |
| `agentstack-protocol-orchestrator` | Choose the next AgentStack workflow skill. |
| `agentstack-protocol-tracker-intake` | Find eligible tracker-backed work. |
| `agentstack-protocol-tracker-graph` | Inspect blockers, children, parents, and readiness. |
| `agentstack-protocol-work-bootstrap` | Prepare an isolated worktree or workspace. |
| `agentstack-protocol-tracker-claim` | Claim eligible work before implementation. |
| `agentstack-protocol-work-plan` | Publish an execution plan before coding. |
| `agentstack-protocol-work-implement` | Implement claimed scoped work with validation. |
| `agentstack-protocol-tracker-sync` | Sync progress, state, blockers, and PR links. |
| `agentstack-protocol-block` | Stop safely and publish a blocker. |
| `agentstack-protocol-submit-review` | Submit completed work for human PR review. |
| `agentstack-protocol-pr-complete` | Human approved the PR and explicitly authorized agent completion. |
| `agentstack-protocol-pr-rework` | PR comments or requested changes require more work. |
| `agentstack-protocol-backlog-language` | Interpret or normalize tracker language through AgentStack terms. |
| `git-worktree-ops` | Support isolated git worktree operations. |

## Generic agent entrypoint

`AGENTS.md` is the generic agent entrypoint. Setup merges an AgentStack-managed block into existing `AGENTS.md` files instead of overwriting them.
