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

## Rule 6 — Control exploration cost
Prefer targeted searches, concise context, and incremental validation.
If exploration grows beyond the task’s needs, summarize the current state and narrow the scope.

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

## Usage guide synchronization

`docs/USAGE.md` is the consumer usage guide, it MUST remain synchronized with `SPECIFICATION.md`, compiling samples, tests, and other project documents.

Whenever library usage, public API behavior, diagnostics, package layout, supported platforms, samples, or documented validation changes, update `docs/USAGE.md` in the same change so it stays accurate.

`docs/USAGE.md` MUST reflect actual supported usage only. Do not document speculative behavior, deferred features, or APIs not defined by `SPECIFICATION.md` and represented by compiling samples or tests.

## Updating this file

You may add verified repository-specific operational guidance, including exact build, test, packaging, formatting, and benchmark commands.

Do not remove, weaken, or reinterpret the user-authored normative rules in this file without explicit approval.

## Compilation

Compile, run and test dotnet projects outside the sandbox due to NuGet package restore issues.

## Milestone execution

Implement exactly one milestone per task unless the user explicitly authorizes a range.

Before starting a milestone, read SPECIFICATION.md, IMPLEMENTATION_PLAN.md,
DECISIONS.md, and STATUS.md in full.

A milestone is not complete merely because the solution compiles.

When SPECIFICATION.md is ambiguous, contradictory, or incomplete, stop all
implementation, record the issue in DECISIONS.md with section references, and
request an explicit decision. Do not infer or invent normative behavior.

## Verified validation commands

Milestone 14 packaging validation uses:

* `dotnet test tests\Mammoth.LiteMapper.Packaging.Tests\Mammoth.LiteMapper.Packaging.Tests.csproj --no-restore --filter Milestone14PackagingAndAotTests`
* `dotnet pack src\Mammoth.LiteMapper.Abstractions\Mammoth.LiteMapper.Abstractions.csproj -c Release -o artifacts\packages`
* `dotnet pack src\Mammoth.LiteMapper.Generator\Mammoth.LiteMapper.Generator.csproj -c Release -o artifacts\packages`
* `dotnet pack src\Mammoth.LiteMapper\Mammoth.LiteMapper.csproj -c Release -o artifacts\packages`
* `dotnet tool restore`
* `dotnet apicompat package artifacts\packages\Mammoth.LiteMapper.Abstractions.1.0.0.nupkg --run-api-compat`
* `dotnet apicompat package artifacts\packages\Mammoth.LiteMapper.1.0.0.nupkg --run-api-compat`

Native AOT publish validation requires the platform linker prerequisites for
the current OS. Set `LITEMAPPER_REQUIRE_NATIVE_AOT=1` in CI to fail validation
instead of reporting an inconclusive prerequisite result. Windows CI must run
the AOT validation from an MSVC developer environment.

Milestone 15 sample and benchmark validation uses:

* `dotnet test tests\Mammoth.LiteMapper.Packaging.Tests\Mammoth.LiteMapper.Packaging.Tests.csproj --no-restore --filter Milestone15SamplesAndBenchmarksTests`
* `dotnet run --project samples\Mammoth.LiteMapper.Samples.Basic\Mammoth.LiteMapper.Samples.Basic.csproj --no-restore`
* `dotnet run --project samples\Mammoth.LiteMapper.Samples.Collections\Mammoth.LiteMapper.Samples.Collections.csproj --no-restore`
* `dotnet run --project samples\Mammoth.LiteMapper.Samples.AspNetCore\Mammoth.LiteMapper.Samples.AspNetCore.csproj --no-restore -- --smoke`
* `dotnet run --project benchmarks\Mammoth.LiteMapper.Benchmarks\Mammoth.LiteMapper.Benchmarks.csproj -c Release --no-restore -- --filter *FlatObjectBenchmarks* --job Dry --warmupCount 1 --iterationCount 1`
