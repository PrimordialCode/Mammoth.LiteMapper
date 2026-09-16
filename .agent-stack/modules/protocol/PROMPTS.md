# AgentStack Prompt Templates

Human-facing reusable prompts for asking an AI coding agent to work through AgentStack Protocol.

Agents do not need to read this file during normal execution. Runtime behavior should come from `AGENTS.md`, `.agent-stack/modules/protocol/README.md`, `.agent-stack/modules/protocol/protocol/AGENT-PROTOCOL.md`, AgentStack skills, and `agentstack help ... --json`.

Replace placeholders such as `<id>`, `<repo>`, and `<goal>` before use.

## Start From Intake

Use this when you want the agent to find eligible work itself.

```text
Use AgentStack Protocol in this repository.

Run `agentstack doctor`, then use the AgentStack skills and `agentstack` CLI to find one eligible work item with `agentstack work-item intake`. Before coding, inspect the graph, create or enter an isolated workspace/worktree, initialize identity in that workspace, claim the item from that workspace, record a plan, implement only the claimed scope, publish meaningful progress, and submit completed work for review with `agentstack work-item submit-review`.

Stop with `agentstack work-item block` if the item is blocked, ambiguous, already claimed, missing required policy data, or unsafe to continue.
```

## Work On A Specific Item

Use this when you already know the tracker work item id.

```text
Use AgentStack Protocol in this repository.

Work on work item <id>. Run `agentstack doctor`, then use:

1. `agentstack work-item get <id>`
2. `agentstack work-item graph <id>`
3. Create or enter an isolated workspace/worktree for `<id>`.
4. In that workspace, run `agentstack agent identity init`.
5. In that workspace, run `agentstack work-item claim <id> --branch <branch> --workspace <path>`.
6. `agentstack work-item plan <id> --message "<plan>"`
7. Implement only the claimed scope.
8. Publish meaningful progress with `agentstack work-item progress <id> --message "<update>"`.
9. When done, open a PR and run `agentstack work-item submit-review <id> --pr <url> --summary "<summary>"`.

Stop with `agentstack work-item block <id> --reason "<reason>"` if the item is blocked, ambiguous, already claimed, violates policy, or cannot be implemented safely.
```

## Investigate Before Claiming

Use this when you want an agent to inspect a work item but not start implementation yet.

```text
Use AgentStack Protocol in this repository.

Investigate work item <id> without claiming or modifying code. Run `agentstack doctor`, `agentstack work-item get <id>`, and `agentstack work-item graph <id>`. Summarize whether the item appears claimable, what dependencies or blockers exist, and what information is missing.

Do not run `agentstack work-item claim` and do not edit files.
```

## Resume After Human Resolution

Use this after a blocked item has been resolved and should return to normal intake.

```text
Use AgentStack Protocol in this repository.

The blocker for work item <id> has been resolved: <resolution>.

Re-check the item with `agentstack work-item get <id>` and `agentstack work-item graph <id>`. If it is safe to continue and no active claim is present, move it back to `ready` if needed, create or enter the isolated workspace, initialize identity there, claim it from that workspace, record a new plan, and continue through the normal AgentStack workflow.

If the item is still blocked, already claimed, or unsafe to continue, publish a new blocker with `agentstack work-item block <id> --reason "<reason>"`.
```

## Continue An Already Claimed Item

Use this only when the same agent/session already owns the active claim.

```text
Use AgentStack Protocol in this repository.

Continue work item <id> only if the active claim belongs to this agent. Run `agentstack work-item get <id>` and verify the claim metadata before making changes. If the claim is valid, inspect the current plan and latest progress, continue implementation within scope, publish progress, and submit for review when complete.

If the item is unclaimed, claimed by another agent, blocked, or ambiguous, stop and report the mismatch instead of continuing.
```

## Block When Unsafe

Use this when the agent must stop rather than guess.

```text
Use AgentStack Protocol in this repository.

Work item <id> cannot proceed safely because: <reason>.

Run `agentstack work-item block <id> --reason "<reason>"`. Include the smallest useful unblock request, current validation status, completed work if any, and branch/workspace context if relevant. If abandoning the work, release the claim with `agentstack work-item release <id>`.
```

## Submit Completed Work

Use this when the implementation is complete and a PR exists.

```text
Use AgentStack Protocol in this repository.

Submit work item <id> for human review. Confirm validation results, confirm the PR URL, then run `agentstack work-item submit-review <id> --pr <url> --summary "<summary>"`.

The summary must include what changed, what validation ran, known risks, and any limitations. Do not self-approve or merge unless repository policy explicitly allows it.
```

## Minimal Specific-Item Prompt

Use this when you want a short instruction for a capable agent.

```text
Use AgentStack Protocol. Work on item <id> through the `agentstack` CLI only: doctor, get, graph, isolated workspace/bootstrap, identity, claim, plan, implement, progress, and submit-review. Stop with `agentstack work-item block` if blocked or ambiguous.
```
