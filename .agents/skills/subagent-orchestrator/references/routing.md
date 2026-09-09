# Classification and Routing

Classify locally; do not spawn a classifier. Select the least expensive available route that meets the checks. Runtime `spawn_agent` metadata is authoritative. Record unavailable-route substitutions in the root ledger.

## Classes

| Class | Typical work | Model (least to most expensive) | Effort (least to highest) |
| --- | --- | --- | --- |
| `scan` | Discovery, evidence, logs, mechanical comparison | `gpt-5.6-luna` | `low` -> `medium` -> `high` -> `xhigh` for synthesis |
| `scoped` | Bounded implementation, debugging, tests, docs | `gpt-5.6-luna` -> `gpt-5.6-terra` | `medium` -> `high` -> `xhigh` for edge cases |
| `deep` | Architecture, cross-cutting diagnosis, security/correctness | `gpt-5.6-terra` -> `gpt-5.6-sol` -> `gpt-6-astra` | `medium` -> `high` -> `xhigh` |
| `verify` | Independent acceptance review | `gpt-5.6-terra` -> `gpt-5.6-sol` -> `gpt-6-astra` | `medium` -> `high` -> `xhigh` for high-risk work |
| `inherit` | Work where parent context and identical capabilities matter more than specialization | Parent model | Parent effort |

Effort follows ambiguity, consequence, and verification depth: `low` clear/narrow, `medium` normal work, `high` complex/edge cases, `xhigh` ambiguous/high-consequence. Do not route by title or use escalation as an automatic retry.

## Spawn Contract

```text
task_name: stable ownership-oriented name
fork_turns: "none"
model: selected available model
reasoning_effort: selected supported effort
message: goal, inputs, constraints, ownership, satisfied dependencies,
         acceptance checks, leaf boundary, and concise result schema
```

Results use evidence pointers and omit raw logs, source dumps, and repeated assignment context unless requested.

Use `reasoning_effort` for collaboration `spawn_agent`; persistent Codex config uses `model_reasoning_effort`.
