# Mammoth.LiteMapper handoff

## Latest: all-defects goal locally complete, 2026-09-09

The authorized whole-project review and repair is complete locally. PENDING-0002 through PENDING-0007 are approved and incorporated. Final regressions cover configured-path converter nullability, patch skipping, required nullable/non-null construction, nested helper null checks, exact/fallback default pairs, and generated-name collisions. Example: `Data.Child == null` under `NullableMismatch.Throw` now throws `InvalidOperationException` naming `Data.Child` before a nested helper can dereference it; a required nullable child preserves null.

Final evidence: Roslyn 4.8/4.14/5.9 each pass 535 generator tests; default 4.8 is restored. Windows solution passes 602 tests with one Native AOT linker-prerequisite skip, including 12 consumers. Required isolated Linux Native AOT passes 1/1 with no skip. The final back-to-back affinity-pinned Medium comparison passes all five benchmark scenarios; Manual changes from 4.2054 ns to 4.1690 ns (-0.87%), LiteMapper changes from 4.8502 ns to 4.8219 ns (-0.58%), and allocation remains 40 bytes. The normalized artifacts are `artifacts/performance/controlled-20260909/affinity1-final-pair-baseline.normalized.json` and `affinity1-final-pair-candidate.normalized.json`. Remote CI was not executed. No commit, push, or publication occurred. Review the dirty diff before any user-authorized commit.

## Historical checkpoints

The remaining sections preserve intermediate evidence and instructions from earlier revisions. They are superseded by the latest checkpoint above.

### W01 partial nested updates and orchestration

User invoked .agents/skills/subagent-orchestrator/SKILL.md and requested remaining task count. docs/WORK_LEDGER.md defines10 planned packages with acceptance/dependencies. W01 partial: local/registered non-null get-only updates preserve identity; explicit Use/configured source path now work. First4cases2fail/2pass then8focused/full433green; two later selection/path failures now10focusedgreen (TRX13:44:08). Still incomplete: nullable/patch/writable explicit behavior, external grouping/defaults, overload ranking and recursive tracker forwarding. Current resolver walks external types one at a time; must prove/fix competing-container behavior. Do not claim earlier429 platform runs validate these source changes.

Two new leaf agents accepted gpt-5.6-luna/medium/fork-none: handwritten_registration_edges owns only HandwrittenRegistrationEdgeTests.cs; collection_contract_edges owns only CollectionContractEdgeTests.cs. No agent may build/restore or touch generator. Root has no live command. Inspect agent/file status before editing or compiling. Old converter_result_tests failed for credits; old matrix_review interrupted before writing W01 tests. Root added NestedUpdateMappingTests.cs. Follow orchestration ledger, batch focused fixes before final full matrix, preserve user files and all approvals; no commit/push/publication. USAGE/skill include verified non-null example; no new normative ambiguity.

### Final CR-016 platforms and helper repair

Current generator429 passes on each Windows/Linux Roslyn4.8/4.14/5.9. Full Windows482 passes/one missing-linker AOT skip (packaging13:18:31). Linux first full482 passes/one false-positive warning-helper failure/zero skips; all12 consumers and AOT passed. Eight new helper regressions began2fail/6pass and now8pass. Full Linux packaging retry56 passes, zero failures/skips,3m7s. Generator/runtime unchanged by helper fix. Windows current helper evidence is focused8 plus earlier full482, not a new490 full run. Evidence artifacts/validation/linux-20260909-cr016 includes original and retry TRX/scripts/environment. Default4.8 selected; all sessions terminal and agents handed back.

Next implement CR-017 after failing tests: get-only Child identity preserved and Value3 through declared updater; cover complete eligibility/precedence/null/cycle/getter behavior described in CONFORMANCE_REVIEW. Continue remaining feature gaps and controlled performance; local Linux matrices now verified, remote CI remains unexecuted. Source Link10.0.303 remains patched. USAGE/skill updated, validators pass, no normative ambiguity. Preserve all edits; no commit/push/publication. Task-budget checkpoint; do not repeat unchanged validation without cause.

### CR-016 repaired, 2026-09-09

Final default4.8 generator429 passes, zero failures/skips,34s; TRX13:13:34. Registration29 cases began16 failures/13 controls; focused97 passed. Newer4.14/5.9 each427 passed before two additional ref/ref-readonly positive tests exposed an over-strict new check; removed it and full429 now passes. Final newer-host/fullWindows/Linux validation remains pending. SourceLink.GitHub now10.0.303, resolved Build.Tasks.Git10.0.303; security regression three failures then three passes (13:09:10/13:09:53). Initial restore sandbox-blocked, escalated restore succeeded. Default4.8 selected, all sessions terminal/agents handed back.

Next serialize newerhost429 runs, restore4.8, full Windows including12 consumers, Linux hosts/AOT. Earlier398/448/Linux evidence below is historical. Then CR-017 get-only nested updates (Child.Value3, same identity), legal mixed reference/value recursion and remaining feature gaps. Linux consumer matrix/remoteCI/performance remain open. USAGE/skill updated with registration and ref-value-copy examples; no normative change or approval needed. Preserve all dirty/user edits; no commit/push/publication. Task-budget checkpoint, goal active.

### Platform validation checkpoint, 2026-09-09

CR-015/read-only revision passes398 tests on each Windows/Linux Roslyn4.8/4.14/5.9 host. Full Windows retry448 passes, zero failures, one Native AOT missing-linker skip; includes12 consumer combinations. Required isolated Linux AOT one pass/zero skips (14s). Windows packaging TRX12:55:54, generator12:55:25; Linux TRX and SDK evidence in artifacts/validation/linux-20260909-cr015. Default4.8 restored; all sessions terminal, agents handed back. First sandbox Windows attempt435 passes/14 NuGet failures/zero skips is recorded in STATUS. No source changes during these runs.

Next: failing CR-016 missing-type/usability registrations; a valid two-parameter update method and an unrelated-pair converter must remain legal. Then prove get-only nested update behavior (Child.Value3, same identity) and remaining22.11–22.16 gaps in CONFORMANCE_REVIEW. New NU1902 dependency finding: SourceLink.GitHub10.0.300 pulls vulnerable Build.Tasks.Git10.0.300; advisory identifies patched10.0.303 and NuGet lists corresponding SourceLink package. Pin remains unchanged; verify resolved fix and rerun affected validation. Linux consumer12 matrix, remote CI, controlled performance and whole-project review remain incomplete. No new normative blocker; approved decisions remain incorporated. Usage/skill consumer behavior unchanged. No commit/push/publication.

### CR-015 and constructor/read-only repairs, 2026-09-09

Current default Roslyn4.8 generator suite passes398 tests, zero failures/skips (45s), TRX `tests/Mammoth.LiteMapper.Generator.Tests/TestResults/aless_SPECTRE_GRD_2026-09-09_12_40_32_net10.0.trx`. No validation sessions remain active; agents handed back ownership. Default4.8 is selected. Run Roslyn4.14/5.9 serially, restore4.8, full Windows solution, and required isolated Linux AOT next. Earlier368/418/AOT results below predate these source changes. Do not claim current full/platform validation yet.

CR-015 is fixed: valid enum casts, enclosing partial record/record-struct/interface declarations, fatal invalid assembly defaults. Red11 failures/nine controls; final declaration/isolation/incremental58 passes, including IgnoreCase3 overriding Exact. An intermediate M3 failure required complete runtime references, not weaker assertions. Constructor fixtures now use legal C# and validate compilation/runtime behavior. Four read-only-field failures led to skipping illegal unbound root/nested assignments and honoring target policy. Corrected two new test assumptions about configurable errors retaining legal generated output; full default suite now passes. Examples: source lowercase value3 maps to Value3; constructor-bound readonly Value maps3, ordinary unbound readonly Value stays0 with configured reporting.

Next source finding CR-016: ValidateUseMapper has a name-specific missing-type branch (ExternalMapper->0011, other missing names->0010) and only checks for non-implicit methods. Prove missing-name and unusable-container cases before repair. No CR-016 regressions have run. Continue CV-003/004 Linux matrix, CV-007 performance, and remaining CV-009 signature/registration/feature evidence. PENDING-0002/0003/0004/0005 remain approved; no new ambiguity. Preserve user AGENTS/config and all dirty work; no commit/push/publication. Task budget boundary was surfaced.

### PENDING-0005 approved, 2026-09-09

The user accepted rejection of inconsistent named composites with LITEMAPPER7002. Sections13.1/13.4 and the diagnostic catalogue were updated before implementation; DECISIONS records approval. Source Both3 with mapped target Read4/Write8 accepts target Both12, rejects Both16, and still allows no target Both. Seven new regressions produced four failures/three controls; final enum/nullable focused suite39 passes. Do not request approval again. Previous stop details below are historical.

Current revision:368 passes each on Windows Roslyn4.8/4.14/5.9; restored default4.8. Full Windows solution418 passes, zero failures, one local Native AOT prerequisite skip. All12 consumer combinations, packaging/API/reproducibility, trimming, samples/usage, and benchmark smoke checks passed. Packaging TRX `tests/Mammoth.LiteMapper.Packaging.Tests/TestResults/aless_SPECTRE_GRD_2026-09-09_12_24_22_net10.0.trx`; generator TRX times12:21:39/12:22:46/12:24:01. Required isolated Linux AOT passed one test, zero failures/skips (26s). All task-owned sessions are terminal and delegated work is handed back. Skill validator and scoped diff checks pass; USAGE remains consumer-only. No commit, push, publication, or whole-project completion claim.

Next: CR-015 failing regressions for valid `(NameMatching)1` attribute constants and mapper classes nested in partial records/interfaces. The read-only22.9/22.10 audit also found invalid constructor fixtures (duplicate constructor signatures and required get-only properties) and missing selection/accessibility/runtime evidence; see the requirement table in CONFORMANCE_REVIEW. No CR-015 tests/fixes have executed. Continue remaining CV-003/004 Linux matrix, CV-007 performance, and CV-009 coverage after repairs; do not repeat unchanged validation without cause.

### PENDING-0005 normative stop, 2026-09-09

The all-defects goal is marked blocked after three consecutive turns confirmed the same unresolved decision. Resume after the user decides PENDING-0005; the goal remains incomplete.

Implementation stopped for a genuinely new specification ambiguity. See DECISIONS PENDING-0005: source flags Read1/Write2/Both3 versus target Read4/Write8/Both16. Section13.1 exact-name mapping suggests16; section13.4 atomic reconstruction suggests12. No behavior has been invented. Obtain an explicit decision and update the specification before implementing it. PENDING-0002/0003/0004 remain approved; do not reopen them.

CR-010 validation completed before this follow-up:345 passes each on Windows Roslyn4.8/4.14/5.9,395 Windows solution passes with one local AOT prerequisite skip, required Linux AOT one pass/zero skips (23s). Default4.8 was restored. Then CR-014's14 enum edge regressions yielded12 failures/two controls. Those six defects are repaired, with two additional unchecked-alias controls;32 focused enum/nullable tests and361 full default-host generator tests pass. Generator TRX `tests/Mammoth.LiteMapper.Generator.Tests/TestResults/aless_SPECTRE_GRD_2026-09-09_11_54_21_net10.0.trx`. This already-running suite finished after the ambiguity was identified. No validation process remains active; all delegated work is handed back.

CR-014 fixes missing composite names, unchecked declared overflow, signed flags, ordinary alias consistency, generated-name collisions, and once-only unknown-value paths. The earlier345/395/AOT evidence predates these changes; required current-revision full solution/newer hosts/Linux AOT validation is pending, not waived. The existing positive AlsoRead fixture was corrected to include the target alias, with a runtime assertion. Usage/skill and all control documents are synchronized. No commit, push, publication, or goal completion. Continue only after the new decision; CV-003/004 actual Linux matrix, CV-007 performance, and CV-009 complete feature/diagnostic audit remain open.

CV-007's concrete32-case benchmark plan is in IMPLEMENTATION_PLAN. Local tag1.0.0 resolves to2df99925bc38638acc8352f904f8cc69facbad96 as a possible comparable baseline; correctness/equivalent behavior must be proven first. No controlled acceptance run or new benchmark/gate implementation occurred.

### Previous checkpoint: cross-type nullability, 2026-09-09

CR-010 boxing and nullable enum repairs are implemented and pass the345-test default Roslyn4.8 generator suite. Eight boxing regressions produced six failures/two controls after correcting one converter-selection fixture. Fifteen enum regressions produced12 failures/three controls. Two follow-up regressions proved oblivious reference annotation and generated temporary-name defects; both are repaired. STATUS records intermediate failures, including a generator build error and duplicate null checks.

Examples: int?3 boxes to object3; null follows the non-null target's Error/Throw policy. From.Ready=1 maps by name to To.Ready=9 even with nullable types; null is preserved for nullable targets, patch null skips assignment, and unknown99 still throws. Configured nullable enum paths are evaluated once.

Full Windows solution passed395 tests (345 generator, five runtime, one integration,44 packaging), zero failures, one Windows Native AOT prerequisite skip. Includes all12 consumer combinations, trimming, package/API/reproducibility, samples/usage, and benchmark smoke. Packaging TRX `tests/Mammoth.LiteMapper.Packaging.Tests/TestResults/aless_SPECTRE_GRD_2026-09-09_11_32_01_net10.0.trx`; generator TRX run time11:31:36. Session41603 is terminal; no task-owned validation process remains active. Current-revision Roslyn4.14/5.9 suites and required Linux AOT rerun remain pending; prior results below predate CR-010. Run hosts serially, restore default4.8, then isolated Linux AOT. Default4.8 is currently selected. Usage/skill/decisions/plan/review are updated; validator/diff checks pass. No new normative ambiguity. All delegated work is handed back.

Next: finish current-revision validation and continue CV-003/004 actual Linux framework/compiler matrix, CV-007 controlled performance, and CV-009 remaining feature/diagnostic audit. The open known CR-010 cases are now executed and repaired; do not recreate them or treat all-feature review as complete. Preserve all dirty changes, including user AGENTS/config edits. No commit, push, or publication.

### Previous checkpoint: inheritance/converter results, 2026-09-09

The user resumed the goal. Current branch is `feature/improvement_and_skill`; preserve the dirty worktree. The earlier pause narrative below is historical and this checkpoint supersedes its pending-validation statements.

- Read AGENTS, the complete specification, plan, decisions, status, and conformance ledger during this continuation. No new normative ambiguity was found. Approved PENDING-0002/0003/0004 remain incorporated.
- Repaired a Roslyn 5.9 compatibility defect: malformed bodyless async partial declarations lost IsAsync and therefore 0006. Three new cases yielded two failures/one control pass, then all passed after a shared syntax-plus-symbol async check. Independent healthy mappings still generate.
- Latest complete Windows generator suites: **320 passed each** on Roslyn4.8.0/4.14.0/5.9.0, zero failures/skips. Versions4.14/5.9 ran in Release; restored baseline4.8 ran in the full solution, including its loaded-host assertion. Earlier async-repair runs (290 passes) and the original5.9 failure remain historical evidence in STATUS.
- Repaired consumer package provenance: two regressions proved alternate-feed fallback when the local package was missing. Both now pass with Mammoth.LiteMapper* mapped exclusively to LocalPackages. Agent `matrix_review` handed back all work; no builds remain active.
- CR-013 inheritance repaired: 16 cases pass after eight initial failures, three additional ambiguity/hiding failures, and one later overload-nullability failure. Inherited `ISource.Value == 3` produces target `Value == 3`; explicit protected base converters use actual C# overload binding and accessibility. Inherited configuration is not implicitly adopted.
- CR-004 converter-result conversions repaired: 14 new cases produced 12 failures/two controls; all 33 result/contract cases now pass. Checked conversion of 2147483648L to int throws; Unchecked produces int.MinValue. Nullable results are checked once before conversion. Combined inheritance/converter validation:49 passes, zero failures/skips.
- Final full Windows solution: **370 passed**, zero failures, **one Windows AOT prerequisite skip** (320 generator, five runtime, one integration,44 packaging). Includes all12 framework/language combinations. Packaging TRX: `tests/Mammoth.LiteMapper.Packaging.Tests/TestResults/aless_SPECTRE_GRD_2026-09-09_11_13_57_net10.0.trx`; generator TRX: `tests/Mammoth.LiteMapper.Generator.Tests/TestResults/aless_SPECTRE_GRD_2026-09-09_11_13_34_net10.0.trx`.
- Required isolated Linux Native AOT passed **one test, zero failures/skips** (27s) after the latest source changes, with clang/zlib, source/Git copy, and LITEMAPPER_REQUIRE_NATIVE_AOT=1. This does not establish Linux framework/compiler matrix or remote CI results.
- Usage/skill include synchronous declarations, inherited source/converter behavior, and numeric converter-result examples. Skill validator and scoped diff checks pass. No commit, push, publication, or goal completion. Existing user changes remain preserved.
- Next: CR-010 failing regressions for nullable boxing and cross-enum conversion. Example: int?[] containing null mapped to object[] must apply Error/Throw policy rather than inserting null; From.Ready=1 mapped by name to nullable To must yield To.Ready=9 and preserve a null source. Member boxing already has an outer null guard and should be a control. These follow-up cases have source-review evidence only, not executed regressions. CV-003/004 Linux/CI evidence, CV-007 performance, and CV-009 coverage also remain open.
- The repository task budget was exceeded and this was surfaced. Continue the same goal from this verified checkpoint without repeating unchanged work. An optional process inventory was sandbox-denied; persisted TRX and session polling verified Windows completion. All task-owned validation sessions are terminal; delegated work has been handed back. No new normative blocker is known.

### Pause checkpoint, 2026-09-08

Paused at the user's request. Resume only when the user returns; no scheduled continuation was created. The all-defects goal remains incomplete. This file records development state, not consumer usage.

### Historical scope and authority

- Workspace: `C:\Work\Mammoth.LiteMapper`, branch `develop`; changes are uncommitted.
- User authorized whole-project review and defect fixes across milestones, with continued analysis after each fix. No commit, push, or publication is authorized.
- Read `AGENTS.md`, the complete `SPECIFICATION.md`, `IMPLEMENTATION_PLAN.md`, `DECISIONS.md`, and `STATUS.md` before resuming implementation. The specification is the sole product contract.
- PENDING-0002/0003/0004 are approved and incorporated. Do not reopen them. A genuinely new normative ambiguity requires a recorded decision request and an implementation stop.
- Always explain findings and proposed behavior with a concrete example. Keep `docs/USAGE.md` consumer-only: no links to this handoff or the conformance ledger, implementation history, or test tracking.
- Preserve all existing work, particularly user edits in `AGENTS.md` and `.tokensave/config.json`. Do not reset/clean the worktree.

### Earlier fixes and evidence

The latest combined focused run passed **60 tests, zero failures/skips**:

| Area | Current evidence |
|---|---|
| Visible-scope defaults | 17 precedence tests pass. Three new root/member/element cases failed first. Two defaults for the same exact pair across local and registered external scopes emit 3002; unrelated healthy mappings still generate. |
| Nullable value converter results | 19 converter-contract tests pass. Ten added cases initially gave nine failures and one passing control. A selected `int?` result can widen to `long`: 12 becomes 12L; null yields 2010 under Error or a single checked result under Throw. Nullable targets preserve null. |
| Unsupported members and ref-like updates | 24 tests pass. Seven new cases failed first; the handwritten Span member converter control passed. Dynamic/pointer/function-pointer members cannot bypass signature restrictions, and ref-like structural creation/update is rejected. |

New test files and extensive earlier fixes are already in the dirty worktree. Use `git status --short` and `docs/CONFORMANCE_REVIEW.md` rather than recreating them. The latest relevant files are `ConversionPrecedenceTests.cs`, `ConverterContractTests.cs`, `UnsupportedTypeDiagnosticTests.cs`, and `LiteMapperGenerator.cs`.

Earlier verified repairs include method failure isolation; canonical diagnostics and configurable severities; required/default/ignored target obligations; numeric/operators; nullable values and collection type emission; constructor planning and update creation; converter precedence; jagged array copying; incremental output isolation; bounded subprocess handling; deterministic package fingerprints; API compatibility; and clean package-backed samples. Their evidence and limits remain in STATUS and the conformance ledger.

### Historical consumer/compiler matrix

- `ConsumerMatrixTests.cs` now covers 12 clean-package combinations: netstandard2.0/net8/net9/net10 with C#9/14/latest. All 12 passed on Windows, zero failures/skips, in 1m23s.
- Evidence: `tests/Mammoth.LiteMapper.Packaging.Tests/TestResults/aless_SPECTRE_GRD_2026-09-08_18_05_15_net10.0.trx`.
- Initial sandbox run: nine passed and three netstandard cases failed with NU1301. The unrestricted retry passed all 12. These packaged inputs predate the latest unsupported-member/ref-like update fixes, so rerun through the final solution suite.
- `RoslynHostMatrixTests.cs` checks loaded compiler/workspace versions against test assembly metadata and verifies the shipping generator's Common/CSharp references remain 4.8.0. Its focused default-host assertion passed.
- The generator test project has a test-only `RoslynTestVersion` override; default is 4.8.0. Shipping baseline was not raised.
- CI now installs SDK8/9/10 and includes Windows/Linux Roslyn4.8.0/4.14.0/5.9.0 jobs, with publishing dependent on them. No remote CI result is claimed.
- **No complete Roslyn host run finished.** The delegated 4.8 attempt was blocked by sandbox policy; its escalated retry was aborted by the user after waiting for approval, without a session handle. A later direct root 4.8 attempt also failed on blocked `api.nuget.org` access before a session was returned. Versions4.14/5.9 were not attempted.
- Last inspected test assets selected4.8.0. Verify assets again before testing; never assume a previous restore selected the intended host.

### Historical full validation, not final-revision proof

- Last full Windows suite: **301 passed**, zero failures, **one Native AOT prerequisite skip** (265 generator, five runtime, one integration, 30 packaging). Generator TRX: `tests/Mammoth.LiteMapper.Generator.Tests/TestResults/aless_SPECTRE_GRD_2026-09-08_17_52_10_net10.0.trx`; packaging TRX: `tests/Mammoth.LiteMapper.Packaging.Tests/TestResults/aless_SPECTRE_GRD_2026-09-08_17_52_49_net10.0.trx`.
- Last Linux Native AOT: one passed, zero skipped, 31s, in an isolated SDK10 container with clang/zlib and Git metadata retained.
- Both runs predate the latest fixes and matrix additions. Do not describe the current full suite as passed.
- An earlier benchmark Dry process exceeded its existing 60-second bound. Later full validation passed without weakening the timeout or assertions. Serialize resource-heavy Windows packaging/benchmark and Linux AOT runs.
- Skill format validation and scoped diff checks passed before the pause. USAGE and skill contain duplicate-default and nullable-result examples. Pause edits only change handoff/control documents.

### Historical resume sequence

1. Read the authority/control documents, inspect the dirty worktree, and verify no task-owned test process is active. No live handle is known at handoff; do not terminate unrelated IDE/build processes.
2. Run the complete Roslyn host matrix serially, using the existing tests. If network access is blocked, request the appropriate escalation; do not retry silently or count a blocked command as executed.

   ```powershell
   dotnet test tests/Mammoth.LiteMapper.Generator.Tests/Mammoth.LiteMapper.Generator.Tests.csproj -c Release -p:RoslynTestVersion=4.8.0 --logger trx
   dotnet test tests/Mammoth.LiteMapper.Generator.Tests/Mammoth.LiteMapper.Generator.Tests.csproj -c Release -p:RoslynTestVersion=4.14.0 --logger trx
   dotnet test tests/Mammoth.LiteMapper.Generator.Tests/Mammoth.LiteMapper.Generator.Tests.csproj -c Release -p:RoslynTestVersion=5.9.0 --logger trx
   dotnet restore tests/Mammoth.LiteMapper.Generator.Tests/Mammoth.LiteMapper.Generator.Tests.csproj -p:RoslynTestVersion=4.8.0
   dotnet test Mammoth.LiteMapper.sln --no-restore --logger trx
   ```

   Capture actual results and restore default4.8 before the full solution run. Do not change expectations to hide host incompatibilities.

3. Run final Linux Native AOT serially after Windows validation. The known working command retains Git metadata and uses an isolated copy, excluding locked IDE/build outputs:

   ```powershell
   docker run --rm -e MSBUILDDISABLENODEREUSE=1 -e DOTNET_CLI_USE_MSBUILD_SERVER=0 -v C:\Work\Mammoth.LiteMapper:/source:ro mcr.microsoft.com/dotnet/sdk:10.0 bash -lc 'set -euo pipefail; apt-get update -qq; apt-get install -y -qq clang zlib1g-dev > /tmp/prerequisites.log; mkdir /work; tar -C /source --exclude=.vs --exclude=.tokensave --exclude=artifacts --exclude=bin --exclude=obj --exclude=node_modules --exclude=TestResults --exclude=BenchmarkDotNet.Artifacts -cf - . | tar -C /work -xf -; cd /work; dotnet restore tests/Mammoth.LiteMapper.Packaging.Tests/Mammoth.LiteMapper.Packaging.Tests.csproj --verbosity quiet; LITEMAPPER_REQUIRE_NATIVE_AOT=1 dotnet test tests/Mammoth.LiteMapper.Packaging.Tests/Mammoth.LiteMapper.Packaging.Tests.csproj --no-restore --filter NativeAotConsumerPublishesAndRunsWithoutLiteMapperWarnings --logger "console;verbosity=minimal"'
   ```

4. Continue remaining defects with failing MSTest regressions before implementation. Update source, usage/skill when behavior changes, and control evidence together. Revalidate appropriately after further fixes.
5. Finish the outstanding feature/diagnostic/performance review. Only mark the goal complete when all required work and validation are actually complete.

### Historical remaining findings

- **CR-004: converter-result conversion.** `IsCompatibleConverterResult` accepts implicit conversions and nullable underlying implicit conversions only. Example to test: selected converter returns `long`, target is `int`, effective numeric policy is Checked. Sections11.4/12 require normal permitted conversion policy; enabled narrowing/explicit operators need regression coverage and repair.
- **CR-010: cross-type nullability.** Audit nullable boxing to non-null reference targets and nullable enum-to-different-enum mappings, including member/element/update policies. Existing same-underlying-type unwrapping cases do not establish complete coverage.
- **CR-013: inherited members/converters.** Read-only findings, not yet executed regressions:

  ```csharp
  public interface IBase { int Value { get; } }
  public interface ISource : IBase { }
  public class Target { public int Value { get; set; } }
  // ISource.Value == 3 should produce Target.Value == 3, including configured paths.
  ```

  `GetVisibleMembers` walks BaseType only, omitting inherited interface members. `GetReadableDirectMembers` uses declared members only; check inherited class paths too. `HasDirectMember` may likewise affect inherited ignore/default configuration; establish failures rather than assuming.

  Explicit `Use = nameof(Parse)` should find an accessible inherited handwritten `protected int Parse(string)` converter. `ResolveConverterSet` currently searches declared mapper methods only. Preserve C# accessibility/hiding/overload rules; do not discover arbitrary base helpers or inherit mapper profiles. Section7.3's MAY alone is not a mandate to discover every inherited method; explicit selection is the bounded case.

- **CV-003/CV-004:** final-revision framework/language/compiler validation, actual Linux matrix evidence and CI evidence. An SDK10-only AOT container does not establish net8/net9 runtime matrix coverage.
- **CV-007:** controlled release benchmark acceptance, statistically meaningful regression policy and nested/cycle allocations. The earlier Short collection allocation run matched manual baselines for0/16/1024 elements but does not prove throughput acceptance.
- **CV-009:** complete behavior/diagnostic checklist and remaining feature-area audit. Test counts and milestone labels do not prove complete conformance.

### Historical operational notes

- Context7 lookup was exhausted for this library and returned unrelated document-conversion libraries; use the repository contract, not unrelated docs.
- Root TokenSave served stale `main` while checkout is `develop`; context/search refused. Check freshness before relying on it and use current source when necessary. Do not overwrite the user's TokenSave configuration.
- Delegated work is handed back. `consumer_matrix` completed its handoff; `precedence_repair` and `usage_skill_sync` are completed. No delegated build is intentionally running. New agents can be assigned bounded work under AGENTS; serialize shared restores/builds.
- The goal tool reported active/incomplete at pause. It has no pause operation; do not misuse complete/blocked. The user's pause instruction is controlling, with manual continuation tomorrow.
- Verification commands for documentation:

  ```powershell
  python C:/Users/aless/.codex/skills/.system/skill-creator/scripts/quick_validate.py skills/mammoth-litemapper
  git diff --check -- src tests docs skills DECISIONS.md SPECIFICATION.md STATUS.md IMPLEMENTATION_PLAN.md README.md benchmarks .github/workflows/ci.yml
  ```

- Copyable restart prompt: `docs/RESUME_PROMPT.md`.

### Focused repair checkpoint (2026-09-09)

W02: the two valid handwritten registration regressions failed before repair. Registration now permits legal pointer/function-pointer and ref-like handwritten signatures without relaxing generated declaration restrictions. HandwrittenRegistrationEdgeTests, ExternalRegistrationTests and UnsupportedTypeDiagnosticTests: 58 passed, 0 failed/skipped (13:47:28 TRX).

W03: Preserve nullable-source to non-nullable-target collection mapping previously omitted required diagnostic 2002. The root collection path now checks this contract. CollectionContractEdgeTests, Milestone9CollectionMappingTests, CrossTypeNullableEnumTests and NullableValueMappingTests: 69 passed, 0 failed/skipped (13:48:12 TRX). Example: a nullable source list with Preserve cannot map to a non-nullable target list and must report 2002.

Both delegated test files have returned and their focused checks passed after root fixes. Remaining W03 source-path/update coverage and W01 selection, null/patch and recursion behavior remain open. No final-revision full platform matrix has run after these changes. All-defects goal remains active; ten packages remain the planning structure, not ten completed tasks.

### Nested updater registration checkpoint (2026-09-09)

Sections11.2/11.5: two new valid-input MSTest regressions failed before repair (13:53:10 TRX): equal external registrations omitted3001; assembly registration produced13 instead of class-registration23. ResolveNestedUpdater now groups local/class/assembly candidates before default/ambiguity resolution. NestedUpdateMappingTests, Milestone10ExistingTargetMappingTests and ExternalRegistrationTests:43 passed,0 failed/skipped (13:53:39 TRX). USAGE and skill now include this verified example. W01 remains partial: null/patch, writable explicit selection, overload and recursive tracker cases remain. No normative change, no final full matrix after this repair. TokenSave status failed with Transport closed; direct source inspection used.

### Explicit nested updater overload checkpoint (2026-09-09)

Section11.4: ExplicitNestedUpdaterPrefersIdentitySourceOverload failed with3001 before repair (13:55:05 TRX). Explicit Use now prefers identity source compatibility before resolving remaining candidates. Example: Chosen(ChildSource, ChildTarget) adds10 to source3 and wins over Chosen(object, ChildTarget), producing13. Focused nested/update/registration suite:44 passed,0 failed/skipped (13:55:26 TRX). W01 remains partial; no final full-platform validation after this change. TokenSave remains unavailable (Transport closed), so source inspection was used.

### Writable nested updater checkpoint (2026-09-09)

Section14.4: explicit Use of ApplyChild on a writable child failed with2009 before repair (13:56:30 TRX). Writable reference properties now attempt explicitly selected updaters, retaining replacement by default and normal converter fallback when no updater resolves. Source Child.Value3 updates the original child to3 without changing identity. NestedUpdateMappingTests, Milestone10ExistingTargetMappingTests and Milestone6ConfigurationAndConverterTests:19 passed,0 failed/skipped (13:56:52 TRX). W01 null/patch and recursion remain open; final integrated platform checks are pending.

### Root-source nested updater checkpoint (2026-09-09)

Section11.4: omitted Source with explicit Use incorrectly reported1001 when the root had no same-name child (red13:57:57). Matching now allows the root parameter in the update path. Intermediate edits targeted the wrong block: one compile failure CS0103 and two20-test runs with1failure/19passes (13:59:05,13:59:30); these are not acceptance evidence. Corrected focused suite:20 passed,0 failed/skipped (13:59:56 TRX). Example: root Value3 supplies ApplyChild(Source, ChildTarget), setting target.Child.Value3. W01 remains partial; full integrated validation pending.
### Latest checkpoint: nested updater nullability (2026-09-09)

The generator now skips null updater sources under patch mode, reports `2001` under Error, preserves the path under Throw, assigns returning updater results to writable nullable children, and reports `5005` for nullable get-only replacement. Focused suite: 51 passed, 0 failed/skipped at 14:08:04. Continue W01 recursion/tracker review, then remaining ledger packages and final validation.

### Latest checkpoint: W04/W05 (2026-09-09)

Mixed value/reference recursion, tuple structural-boundary diagnostics, and declared recursive tracker sharing are repaired. Runtime `A -> B -> A` throws for `MapA` at `B.A`. Full default Roslyn 4.8 generator suite passes 469 tests with no failures/skips. W06 complete requirement audit, performance work, documentation reconciliation, and final multi-host/platform validation remain. Two delegated test agents returned successfully; see WORK_LEDGER for routes and acceptance.

### Resolved PENDING-0006 history (2026-09-09)

The sections 1-30 verifier found seven concrete implementation defects plus incomplete diagnostic coverage. Option A was approved and incorporated: unreachable `LITEMAPPER4003`, `6002`, and `6003` were removed from the active 1.0 catalogue and descriptor set. This section preserves the validation history; the current stop is PENDING-0007 below.

W07 is accepted: the benchmark project builds, six benchmark contract/policy tests pass, and all 15 added Dry scenarios execute, bringing the total to 32. The controlled Medium flat baseline from tag 1.0.0 completed on the same Windows host and is stored at `artifacts/performance/release-1.0.0/artifacts/bdn/results/Mammoth.LiteMapper.Benchmarks.FlatObjectBenchmarks-report-full-compressed.json`. The matching current-source candidate run did not start because implementation stopped for PENDING-0006.

The matching current-source Medium run subsequently completed as independent validation. Its first attempt executed zero cases after BenchmarkDotNet found the extracted baseline's duplicate project name; the corrected run executed all five. The cross-run gate is invalid as release evidence because the unchanged Manual control slowed 47.46% and three other controls also crossed the blocking threshold. LiteMapper allocated the same 40 bytes and improved relative to Manual within the run, but W08 must be rerun under stable conditions. Preserve the raw and normalized files under `artifacts/performance`; do not report the current gate failure as a LiteMapper code regression.

Subsequent work established the null-policy failures and repairs, added direct `3004`/`5001` coverage, and repaired the first generation-safety cases. The controlled performance comparison, current path defects, documentation/skill reconciliation, and final serialized validation remain incomplete. No commit, push, publication, or remote CI run has occurred.

### PENDING-0007 stop checkpoint (2026-09-09)

PENDING-0006 Option A is incorporated and its stop is superseded. Null-policy and generation-safety repairs are focused-green; the latest complete default-host generator run passed 489 tests before the final qualified-converter fix, which passed its focused regression.

PENDING-0007 Option A was approved and incorporated into specification section 11.4. Example: when `Address?` is null and `Address.Code` feeds `string? Normalize(string?)`, the generated mapping calls `Normalize(null)`. A non-null converter parameter follows `NullableMismatch`, including when the destination is nullable.

Add failing regressions and repair the five queued configured-path defects plus the approved converter behavior. Then reconcile stale status/review/usage/skill text, rerun controlled performance evidence, and execute all final host, consumer, solution, packaging/trimming, and Linux Native AOT validation. Preserve every dirty worktree change; do not commit, push, publish, or claim completion.
