<!-- as:rules -->
Be concise and token-efficient. Give direct answers, minimal examples, and no extra background.
No sycophantic openers or closing fluff. No emojis or em-dashes.

These rules apply to every task in this project unless explicitly overridden.
Bias: caution over speed on non-trivial work. Use judgment on trivial tasks.
Use sub-agents to delegate tasks.

## Rule 1 — Think Before Coding
State assumptions explicitly. If uncertain, ask rather than guess.
Present multiple interpretations when ambiguity exists.
Push back when a simpler approach exists.
Stop when confused. Name what's unclear.

## Rule 2 — Simplicity First
Minimum code that solves the problem. Nothing speculative.
No features beyond what was asked. No abstractions for single-use code.
Test: would a senior engineer say this is overcomplicated? If yes, simplify.

## Rule 3 — Surgical Changes
Touch only what you must. Clean up only your own mess.
Don't "improve" adjacent code, comments, or formatting.
Don't refactor what isn't broken. Match existing style.

## Rule 4 — Goal-Driven Execution
Define success criteria. Loop until verified.
Don't follow steps. Define success and iterate.
Strong success criteria let you loop independently.

## Rule 5 — Use the model only for judgment calls
Use me for: classification, drafting, summarization, extraction.
Do NOT use me for: routing, retries, deterministic transforms.
If code can answer, code answers.

## Rule 6 — Token budgets are not advisory
Per-task: 4,000 tokens. Per-session: 30,000 tokens.
If approaching budget, summarize and start fresh.
Surface the breach. Do not silently overrun.

## Rule 7 — Surface conflicts, don't average them
If two patterns contradict, pick one (more recent / more tested).
Explain why. Flag the other for cleanup.
Don't blend conflicting patterns.

## Rule 8 — Read before you write
Before adding code, read exports, immediate callers, shared utilities.
"Looks orthogonal" is dangerous. If unsure why code is structured a way, ask.

## Rule 9 — Tests verify intent, not just behavior
Tests must encode WHY behavior matters, not just WHAT it does.
A test that can't fail when business logic changes is wrong.

## Rule 10 — Checkpoint after every significant step
Summarize what was done, what's verified, what's left.
Don't continue from a state you can't describe back.
If you lose track, stop and restate.

## Rule 11 — Match the codebase's conventions, even if you disagree
Conformance > taste inside the codebase.
If you genuinely think a convention is harmful, surface it. Don't fork silently.

## Rule 12 — Fail loud
"Completed" is wrong if anything was skipped silently.
"Tests pass" is wrong if any were skipped.
Default to surfacing uncertainty, not hiding it.
<!-- /as:rules -->

# LiteMapper repository instructions

## Authority

`SPECIFICATION.md` is the sole authoritative product contract.

Read `SPECIFICATION.md` completely before planning, modifying code, or creating implementation documents.

No other document may override `SPECIFICATION.md`.

## Implementation scope

Implement exactly one specification milestone per task unless the user explicitly authorizes a different scope.

Do not begin a later milestone merely because the current milestone finishes early.

Do not introduce:

* undocumented public APIs;
* deferred features;
* runtime reflection;
* runtime type scanning;
* dynamic dispatch;
* runtime code generation;
* semantic behavior not defined by the specification.

## Ambiguities and blockers

If `SPECIFICATION.md` contains a normative ambiguity, contradiction, missing definition, or impossible requirement:

1. record the issue in `DECISIONS.md` with exact section references, when that file exists;
2. stop implementation;
3. report the blocker;
4. do not guess or implement competing alternatives.

Non-semantic implementation choices may be recorded in `DECISIONS.md` without changing the specification.

## Testing and validation

Use MSTest with built-in assertions.

Add failing tests before implementing each feature.

Run focused tests while working and the complete required validation suite before declaring a milestone complete.

Do not suppress diagnostics, weaken tests, or change expected behavior merely to make validation pass.

Do not claim completion when a required command was skipped, failed, or could not be run.

## Generated code

Generated output must be deterministic.

Capability detection must be based on compiler and compilation capabilities as defined by the specification, not assumptions derived solely from target-framework names.

## Project documents

When present, read these files before starting work:

1. `IMPLEMENTATION_PLAN.md`
2. `DECISIONS.md`
3. `STATUS.md`

`IMPLEMENTATION_PLAN.md` defines execution steps but cannot override the specification.

`DECISIONS.md` records implementation decisions and unresolved proposals. Proposed semantic changes are not authoritative until explicitly approved and incorporated into `SPECIFICATION.md`.

`STATUS.md` records current progress, validation evidence, blockers, and the next action.

Keep these files current as required by the specification.

## Updating this file

You may add verified repository-specific operational guidance, including exact build, test, packaging, formatting, and benchmark commands.

Do not remove, weaken, or reinterpret the user-authored normative rules in this file without explicit approval.
