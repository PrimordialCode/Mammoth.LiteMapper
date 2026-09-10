# Mammoth.LiteMapper Implementation Plan

Current execution state (2026-09-10): W01 through W10 remain complete for local review and validation. The approved Milestone 13 flat-mapping optimization is also complete: all three Roslyn hosts pass 540 tests, the Windows solution passes 607 tests with one linker-prerequisite AOT skip, and required Linux Native AOT passes 1/1. Current paired disassembly shows generated LiteMapper code inlined into one 70-byte method versus 73 bytes for manual code, with means of 4.465 ns and 4.307 ns and identical 40-byte allocation. Remote CI remains unexecuted.

PENDING-0006 and PENDING-0007 Option A are approved, incorporated, implemented, and regression-tested. All milestone and dated checkpoint sections below are historical planning and revision-specific evidence; their pending-work statements are superseded by the current execution state above.

`SPECIFICATION.md` is authoritative. This plan explains execution order and validation only; it does not redefine product semantics.

Performance evidence (CV-007): the17 existing cases were extended with three acyclic nested-graph cases (manual/default/ThrowOnCycle) and12 recursive cases (manual/generated, tracking off/on, depths1/16/128). Setup verifies equivalent outputs before timing. Baseline and candidate were compared on the same machine with the same affinity-pinned harness and pinned dependencies. Local release tag1.0.0 resolves to2df99925bc38638acc8352f904f8cc69facbad96. Different or invalid behavior is not treated as a comparable performance baseline.

Comparison handling covers missing cases, environment/configuration mismatch, allocations, and statistical throughput loss. Section23.4 uses throughput:100ns to121ns is a17.36% loss, requiring review if significant; it is not a greater-than20% blocking result. Any allocation increase blocks unless explicitly approved/documented. The accepted affinity-pinned Medium artifacts are under `artifacts/performance/controlled-20260909`; the comparison script reports five passing scenarios.

## Milestone 13 inline follow-up (2026-09-10)

- Objective: remove the extra call observed between the benchmark wrapper and a small generated flat mapping without changing mapping semantics or public API.
- Test-first: `InliningOptimizationTests.SmallStraightLineRootMappingUsesAggressiveInlining` failed because the generated method had no inline hint. A capability regression then failed with `CS0122` before accessibility checks were added. The final five-test fixture proves generated-source compilation and excludes guarded, collection, nullable-path, tracking, constructor-bound, helper-based, update, and inaccessible/inexact-capability cases.
- Implementation: emit `MethodImplOptions.AggressiveInlining` only for root new-object mappings whose assignments are all direct member copies and only when the exact attribute constructor and enum field exist in the compilation. Nullable/guarded roots, reference tracking, constructor arguments, preconditions, helpers, collections, updates, and custom bodies remain unannotated.
- Validation: related focused tests pass 15/15; Roslyn 4.8/4.14/5.9 pass 540 each; Windows solution passes 607 with one Native AOT prerequisite skip; isolated Linux Native AOT passes 1/1. The current paired Medium run reports Manual 4.307 ns/73 bytes and LiteMapper 4.465 ns/70 bytes, both allocating 40 bytes.
- Documentation: no consumer API or behavior changed. `docs/USAGE.md` and the consumer skill remain unchanged after alignment review and skill validation.

## Milestone 1: repository, solution, package, and CI skeleton

- Objective: create the repository, project, solution, package, sample, benchmark, test, and CI skeleton.
- Specification sections implemented: 4.1, 4.2, 4.6, 4.7, 22.1, 22.8, 25, 25.1, 25.2.
- Prerequisites: complete review of `AGENTS.md` and `SPECIFICATION.md`; no Milestone 1 blocker recorded.
- Files: root build props, central packages, solution files, CI workflows, required `src/`, `tests/`, `samples/`, and `benchmarks/` projects.
- Public API affected: none; no public mapping or abstraction API is introduced in this milestone.
- Diagnostics affected: none implemented; diagnostic catalogue starts with generator work.
- Tests written first: repository shape, project naming, MSTest-only tests, analyzer/private Roslyn references, no public mapping API.
- Focused validation: `dotnet restore Mammoth.LiteMapper.sln`; `dotnet build Mammoth.LiteMapper.sln --no-restore`; `dotnet test Mammoth.LiteMapper.sln --no-build`; `dotnet sln Mammoth.LiteMapper.sln list`; `dotnet sln Mammoth.LiteMapper.slnx list`.
- Full validation: current full solution restore, build, test, solution list, project/package checks, final tree inspection, and CI YAML parse where locally available.
- Completion criteria: all required skeleton paths exist; both solution formats include every project; projects compile; tests pass; CI skeleton exists for Windows and Linux; `DECISIONS.md` and `STATUS.md` updated.
- Status: Completed on 2026-06-17 with restore, build, test, solution-list, structural, YAML syntax, runtime-output leak, and final tree validation.
- Risks: `.slnx` support depends on installed SDK; NuGet restore requires network; CI runtime syntax is finally proven only by GitHub Actions.
- Out of scope: public abstractions, generator implementation, mapping behavior, diagnostics, packaging consumer tests, AOT/trimming, benchmarks with scenarios, usage documentation.

## Milestone 2: public abstractions API and public API baselines

- Objective: implement all public abstraction types exactly as specified and establish API baselines.
- Specification sections implemented: 5, 6, 20.1, 22.20, 24.4.
- Prerequisites: Milestone 1 complete; no pending semantic question about public API shapes.
- Files: `src/Mammoth.LiteMapper.Abstractions`, public API baseline files, runtime/API tests.
- Public API affected: all attributes, enums, and `LiteMapperCycleException`.
- Diagnostics affected: none required beyond API analyzer configuration.
- Tests written first: public API baseline tests and compile-time API shape tests.
- Focused validation: abstraction project build and API tests.
- Full validation: full solution build/test plus API compatibility checks.
- Completion criteria: public API exactly matches section 5; no Roslyn dependency in abstractions; API baselines updated deliberately.
- Status: Completed on 2026-06-17 with public abstraction types, PublicAPI baseline files, PublicApiAnalyzer configuration, runtime API shape tests, packaging boundary tests, focused abstraction build, full solution restore/build/test, and solution-list validation.
- Deviations: used MSTest reflection-based API shape tests in addition to Roslyn PublicApiAnalyzers so attribute usage, enum numeric values, and exception state are verified directly.
- Remaining tasks: none for Milestone 2.
- Validation commands: `dotnet package search Microsoft.CodeAnalysis.PublicApiAnalyzers --exact-match --format json`; `dotnet restore Mammoth.LiteMapper.sln`; `dotnet test tests\Mammoth.LiteMapper.Runtime.Tests\Mammoth.LiteMapper.Runtime.Tests.csproj --no-restore`; `dotnet test tests\Mammoth.LiteMapper.Packaging.Tests\Mammoth.LiteMapper.Packaging.Tests.csproj --no-restore`; `dotnet build src\Mammoth.LiteMapper.Abstractions\Mammoth.LiteMapper.Abstractions.csproj --no-restore`; `dotnet build Mammoth.LiteMapper.sln --no-restore`; `dotnet test Mammoth.LiteMapper.sln --no-build`; `dotnet sln Mammoth.LiteMapper.sln list`; `dotnet sln Mammoth.LiteMapper.slnx list`; `dotnet package search Microsoft.DotNet.ApiCompat.Tool --exact-match --format json`; `dotnet tool install Microsoft.DotNet.ApiCompat.Tool --version 10.0.301 --tool-path $env:TEMP\mammoth-lite-api-tools`; `dotnet pack src\Mammoth.LiteMapper.Abstractions\Mammoth.LiteMapper.Abstractions.csproj -c Release -o artifacts\packages`; `apicompat package artifacts\packages\Mammoth.LiteMapper.Abstractions.1.0.0.nupkg --run-api-compat`; `dotnet pack src\Mammoth.LiteMapper\Mammoth.LiteMapper.csproj -c Release -o artifacts\packages`; `apicompat package artifacts\packages\Mammoth.LiteMapper.1.0.0.nupkg --run-api-compat`.
- Risks: nullable annotations and netstandard2.0 C# surface must remain compatible.
- Out of scope: generator discovery and mapping semantics.

## Milestone 3: generator discovery, incremental infrastructure, and declaration diagnostics

- Objective: implement incremental mapper discovery and declaration/configuration diagnostics.
- Specification sections implemented: 7, 8, 19, 20.1-20.3, 21, 22.2, 22.7, 22.9.
- Prerequisites: Milestone 2 complete.
- Files: generator project, generator test infrastructure, diagnostic descriptors, declaration tests.
- Public API affected: none beyond using Milestone 2 abstractions.
- Diagnostics affected: `LITEMAPPER0001` through `LITEMAPPER0013`, `LITEMAPPER9001`.
- Tests written first: valid and invalid mapper declarations, language-version rejection, duplicate configuration, external mapper validation, incremental cache tests.
- Focused validation: generator declaration tests.
- Full validation: full solution tests and Roslyn baseline matrix where available.
- Completion criteria: declaration diagnostics stable and tested; generator is `IIncrementalGenerator`; no mapping bodies generated beyond declaration infrastructure.
- Status: Completed on 2026-06-17 with incremental generator discovery, declaration-only generated source, declaration/configuration diagnostics, language-version validation, tracked incremental-step validation, and generator declaration tests.
- Deviations: raised the Roslyn compile-time baseline from 4.0.1 to 4.8.0 by approved specification decision `DEC-0013`.
- Remaining tasks: none for Milestone 3.
- Validation commands: `dotnet restore Mammoth.LiteMapper.sln`; `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore`; `dotnet build Mammoth.LiteMapper.sln --no-restore`; `dotnet test Mammoth.LiteMapper.sln --no-build`.
- Risks: the official Roslyn source-generator MSTest package only exists up to `1.1.2` and emits NU1608 warnings because it depends on Workspaces 3.8.0 while the repository resolves Roslyn 4.8.0.
- Out of scope: flat mapping behavior, mapping method bodies, member matching, construction, converters, collections, existing-target mapping, enum mapping, recursive mapping, package-consumer validation, trimming, Native AOT, benchmarks.

## Milestone 4: flat mapping

- Objective: generate direct member mapping for flat new-object mappings.
- Specification sections implemented: 3, 6, 9, 11.1, 12.1, 12.4, 12.5, 19.
- Prerequisites: Milestone 3 complete.
- Files: generator mapping model/rendering, runtime tests, snapshots.
- Public API affected: none.
- Diagnostics affected: relevant `LITEMAPPER1xxx`, `LITEMAPPER2xxx`, `LITEMAPPER3xxx`.
- Tests written first: matching policies, unmapped source/target behavior, nullable root/member behavior, deterministic snapshots.
- Focused validation: flat mapping generator/runtime tests.
- Full validation: full solution tests and snapshot review.
- Completion criteria: flat mappings generate deterministic direct code with no runtime reflection.
- Status: Completed on 2026-06-17 with flat new-object mapping generation, direct public member assignment, effective name matching and unmapped-member policies, inherited-member discovery, hidden-member diagnostics, root/member nullable diagnostics, identity/implicit conversion validation, runtime invocation tests, and a checked-in generated-source snapshot.
- Deviations: constructor support is limited to accessible parameterless target construction for this milestone; richer constructor, record, `init`, `required`, and value-type construction remains in Milestone 5 as specified.
- Remaining tasks: none for Milestone 4.
- Validation commands: `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore`; `dotnet restore Mammoth.LiteMapper.sln`; `dotnet build Mammoth.LiteMapper.sln --no-restore`; `dotnet test Mammoth.LiteMapper.sln --no-build`.
- Risks: configurable diagnostic severity is represented with paired internal descriptors for the same stable diagnostic IDs; future diagnostics work should keep editorconfig severity override behavior under review.
- Out of scope: constructors beyond trivial construction, nested objects, collections.

## Milestone 5: constructors, records, init, required, and value types

- Objective: implement construction algorithm and supported value/record shapes.
- Specification sections implemented: 10, 22.10.
- Prerequisites: Milestone 4 complete.
- Files: construction planner, generated initializer rendering, tests.
- Public API affected: none.
- Diagnostics affected: `LITEMAPPER1002`, `LITEMAPPER1010`-`LITEMAPPER1014`.
- Tests written first: constructor selection, ambiguity, required members, init, records, fields.
- Focused validation: constructor test suite.
- Full validation: full solution tests.
- Completion criteria: construction behavior matches section 10 and never emits `default!`.
- Status: Completed on 2026-06-17 with constructor planning, records and record structs, object-initializer rendering for `init` members, required-member validation, `SetsRequiredMembersAttribute` handling, readonly constructor binding, value-type construction, and constructor diagnostics.
- Deviations: rendered all new-object member assignments with object initializers so `init` properties and ordinary settable members share one deterministic code path.
- Remaining tasks: none for Milestone 5.
- Validation commands: `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore --filter Milestone5ConstructionTests` initially failed for expected missing constructor behavior; after implementation it passed with 5 tests. `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore` passed with 19 tests. `dotnet restore Mammoth.LiteMapper.sln` passed with known NU1608 warnings. `dotnet build Mammoth.LiteMapper.sln --no-restore` passed with the same 3 NU1608 warnings. `dotnet test Mammoth.LiteMapper.sln --no-build` passed with 31 total tests.
- Risks: C# required metadata across TFMs remains a future portability matrix concern; current validation uses the available net10.0 test host.
- Out of scope: converters and explicit member configuration.

## Milestone 6: explicit member configuration and converters

- Objective: implement explicit mapping attributes and converter resolution.
- Specification sections implemented: 5.5-5.8, 9.4-9.7, 11, 22.16.
- Prerequisites: Milestone 5 complete.
- Files: configuration parser, converter resolver, diagnostics tests.
- Public API affected: none if Milestone 2 API is complete.
- Diagnostics affected: duplicate configuration, invalid paths, invalid converters, ambiguous converters/mappings.
- Tests written first: `MapProperty`, ignore/default attributes, converter precedence, overload ambiguity.
- Focused validation: configuration and converter tests.
- Full validation: full solution tests.
- Completion criteria: full precedence chain is deterministic and tested.
- Status: Completed on 2026-06-17 with explicit `MapProperty` source paths, `IgnoreTarget`, `IgnoreSource`, `UseTargetDefault`, local converter resolution, `Map{TargetMember}` resolution, registered external converter and mapping-method resolution, invalid configuration diagnostics, invalid converter diagnostics, ambiguity diagnostics, runtime invocation coverage, and a checked-in generated-source snapshot.
- Deviations: local unmarked helper methods are not selected by signature; only explicit `Use`, `[MappingConverter]`, `Map{TargetMember}`, registered external methods, and implicit conversion are active in this milestone so arbitrary local helper selection is avoided.
- Remaining tasks: none for Milestone 6.
- Validation commands: `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore --filter Milestone6ConfigurationAndConverterTests` initially failed for expected missing Milestone 6 behavior, then passed with 5 tests after implementation; `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore` passed with 24 tests; `dotnet restore Mammoth.LiteMapper.sln` passed with known NU1608 warnings; `dotnet build Mammoth.LiteMapper.sln --no-restore` passed with the same 3 NU1608 warnings; `dotnet test Mammoth.LiteMapper.sln --no-build` passed with 36 total tests.
- Risks: constructor-parameter explicit configuration and optional-parameter `UseTargetDefault` interactions need broader coverage when constructor binding is expanded beyond current member-initializer scenarios.
- Out of scope: nullable policy completion, nested structural mapping, collections, enum mapping, recursive behavior, existing-target mapping, explicit operators and numeric narrowing policy.

## Milestone 7: nullable behavior

- Objective: complete nullability and null-collection semantics.
- Specification sections implemented: 6.3, 12.4, 12.5, 15.11, 16.3, 22.11.
- Prerequisites: Milestone 6 complete.
- Files: capability/nullability model, rendering, tests.
- Public API affected: none.
- Diagnostics affected: `LITEMAPPER2001`-`LITEMAPPER2003`, `LITEMAPPER5006`.
- Tests written first: root null, member null, nullable-oblivious projects, collection nulls, patch null skipping.
- Focused validation: nullability tests.
- Full validation: full solution tests.
- Completion criteria: compile-time errors and runtime throws match effective policy.
- Status: Completed on 2026-06-17 with effective nullable-mismatch policy resolution, `Error` diagnostics with invalid implementation suppression, `Throw` root/member/source-path runtime checks, nullable-oblivious coverage, and focused runtime invocation tests.
- Deviations: Milestone 7 covers nullable behavior only for mapping shapes already implemented through Milestone 6; collection-specific null strategies and existing-target patch null skipping remain deferred because collections and existing-target mappings are Milestones 9 and 10.
- Remaining tasks: cross-feature source-path work remains under PENDING-0007 and the queued nested/collection/update path regressions; the original Milestone 7 closeout evidence is historical.
- Validation commands: `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore --filter Milestone7NullableBehaviorTests` initially failed for expected missing `NullableMismatch.Throw` behavior, then passed with 3 tests after implementation; `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore` passed with 27 tests; `dotnet restore Mammoth.LiteMapper.sln` passed with known NU1608 warnings; `dotnet build Mammoth.LiteMapper.sln --no-restore` passed with the same 3 NU1608 warnings; `dotnet test Mammoth.LiteMapper.sln --no-build` passed with 39 total tests.
- Risks: nullable annotations differ by reference assembly; nullable source-path flow checks are currently direct generated guards for implemented flat/explicit paths and may need sharing with nested/collection renderers in later milestones.
- Out of scope: nested object mapping unless required by null tests; collection mapping and null collection materialization; existing-target mapping and patch null skipping.

## Milestone 8: nested object mapping

- Objective: generate private structural nested helpers.
- Specification sections implemented: 14, 18.1, 18.2, 22.16.
- Prerequisites: Milestone 7 complete.
- Files: nested dependency graph, helper renderer, tests.
- Public API affected: none.
- Diagnostics affected: `LITEMAPPER3001`-`LITEMAPPER3005`, `LITEMAPPER2012`.
- Tests written first: nested helpers, explicit/default mapping selection, abstract/interface destination rejection.
- Focused validation: nested mapping tests.
- Full validation: full solution tests and snapshot review.
- Completion criteria: helpers are private, closed-type, deterministic, and no runtime dispatch is used.
- Status: Completed on 2026-06-17 with automatic private structural helpers for closed source/destination type pairs, visible local mapping reuse before structural helper generation, nullable nested member handling for implemented new-object mappings, object/runtime-dispatch rejection, abstract/interface destination rejection, ambiguous visible mapping diagnostics, runtime invocation coverage, and a checked-in generated-source snapshot.
- Deviations: helper names are deterministic implementation details based on closed source and destination type names; exact helper naming remains non-public generated formatting.
- Remaining tasks: nullable configured paths into nested targets still require null-preserving, once-only traversal regressions and repair. Exact closed helper identity was repaired after the original milestone closeout.
- Validation commands: `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore --filter Milestone8NestedObjectMappingTests` initially failed for expected missing nested behavior, then passed with 4 tests after implementation and snapshot update; `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore` passed with 31 tests; `dotnet restore Mammoth.LiteMapper.sln` passed; `dotnet build Mammoth.LiteMapper.sln --no-restore` passed with 0 warnings; `dotnet test Mammoth.LiteMapper.sln --no-build` passed with 43 total tests.
- Risks: recursive component analysis and cycle-tracker plumbing remain deferred to Milestone 12; collection element nested helpers remain deferred to Milestone 9.
- Out of scope: collections, existing-target nested mutation/update semantics, enum mapping, recursive cycle tracking, open generic helper templates.

## Milestone 9: arrays, collections, sets, and dictionaries

- Objective: implement supported collection and dictionary mapping.
- Specification sections implemented: 15, 22.12.
- Prerequisites: Milestone 8 complete.
- Files: collection planner/renderer, collection tests.
- Public API affected: none.
- Diagnostics affected: `LITEMAPPER2002`, `LITEMAPPER4001`, `LITEMAPPER4002`, `LITEMAPPER4006`; `LITEMAPPER4004` and `LITEMAPPER4005` remain for existing-target collection behavior in Milestone 10.
- Tests written first: top-level arrays/lists, member arrays/sets/dictionaries, nested element mapping, null collections, unsupported rectangular arrays/queues/custom targets, single enumeration without count, comparer preservation, and dictionary key collision.
- Focused validation: collection tests.
- Full validation: full solution tests.
- Completion criteria: supported shapes map correctly and unsupported shapes diagnose.
- Status: Completed on 2026-06-17 with top-level and member collection helpers, supported interface target defaults, mutable-copy behavior, nested element mapping, dictionary key/value conversion, null collection handling, one-pass enumerable mapping, cheap-count capacity use, comparer preservation for compatible sets/dictionaries, collision-through-`Add` behavior, and unsupported-shape diagnostics.
- Deviations: collection helper methods use generated custom loop bodies within the existing mapping model rather than a separate planner type; exact helper names remain deterministic generated implementation details.
- Remaining tasks: configured collection paths with nullable intermediate segments still require compile-time `LITEMAPPER2002` coverage and repair for contextual, explicit `Error`, and `Preserve` behavior.
- Validation commands: `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore --filter Milestone9CollectionMappingTests` initially failed for expected missing collection behavior, then passed with 6 tests after implementation; `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore` passed with 37 tests; `dotnet restore Mammoth.LiteMapper.sln` passed; `dotnet build Mammoth.LiteMapper.sln --no-restore` passed with 0 warnings; `dotnet test Mammoth.LiteMapper.sln --no-build` passed with 49 total tests; `git diff --check` passed.
- Risks: broader TFM/language matrix remains a later capability/portability milestone; collection helper formatting is not public API.
- Out of scope: existing-target update semantics.

## Milestone 10: existing-target mapping and patch behavior

- Objective: implement update mappings into existing destinations.
- Specification sections implemented: 8.2, 8.3, 15.12, 16, 22.13.
- Prerequisites: Milestone 9 complete.
- Files: update mapping classifier/renderer, tests.
- Public API affected: none.
- Diagnostics affected: `LITEMAPPER5001`-`LITEMAPPER5006`.
- Tests written first: void/reference-returning update, struct `ref`, null destination, nested update, collection replacement, init-only rejection.
- Focused validation: update tests.
- Full validation: full solution tests.
- Completion criteria: update semantics match section 16 and do not perform partial collection updates.
- Status: Completed on 2026-06-18 with existing-target method classification, void and destination-returning reference updates, `ref` struct updates, null destination checks, nullable destination-returning construction, writable nested and collection replacement, `IgnoreNullSourceMembers` patch guards, update-specific diagnostics, runtime behavior coverage, and a checked-in generated-source snapshot.
- Deviations: update rendering reuses the existing assignment/conversion model and emits direct assignment statements into the destination instead of introducing a separate runtime update abstraction.
- Remaining tasks: none for Milestone 10.
- Validation commands: `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore --filter Milestone10ExistingTargetMappingTests` initially failed for expected missing update behavior, then passed with 4 tests after implementation and snapshot assertion; `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore` passed with 41 tests; `dotnet restore Mammoth.LiteMapper.sln` passed; `dotnet build Mammoth.LiteMapper.sln --no-restore` passed with 0 warnings; `dotnet test Mammoth.LiteMapper.sln --no-build` passed with 53 total tests; `git diff --check` passed.
- Risks: explicit get-only nested mutation through handwritten update methods is currently represented by the specified rejection path; recursive nested update and cycle tracking remain deferred to Milestone 12.
- Out of scope: enum mapping.

## Milestone 11: enum mapping

- Objective: implement enum name/value/flags mapping.
- Specification sections implemented: 13, 22.15.
- Prerequisites: Milestone 10 complete.
- Files: enum planner/renderer, enum tests.
- Public API affected: none.
- Diagnostics affected: `LITEMAPPER7001`-`LITEMAPPER7005`.
- Tests written first: by-name, by-value, aliases, zero, flags, overflow, runtime unknown values.
- Focused validation: enum tests.
- Full validation: full solution tests.
- Completion criteria: enum behavior and throws match section 13.
- Status: Completed on 2026-06-18 with by-name enum switch generation, checked/unchecked by-value conversion, `UnmatchedEnumValues` policy handling, source/target alias validation, zero-member validation, flags atomic validation, runtime flags composite reconstruction, runtime unknown-value throws, custom converter precedence, enum diagnostics, and a checked-in generated-source snapshot.
- Deviations: enum rendering is integrated into the existing conversion resolver and top-level direct-conversion path rather than a separate planner type.
- Remaining tasks: none for Milestone 11.
- Validation commands: `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore --filter Milestone11EnumMappingTests` initially failed for expected missing enum behavior, then passed with 7 tests after implementation and snapshot assertion; `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore` passed with 48 tests; `dotnet restore Mammoth.LiteMapper.sln` passed; `dotnet build Mammoth.LiteMapper.sln --no-restore` passed with 0 warnings; `dotnet test Mammoth.LiteMapper.sln --no-build` passed with 60 total tests; `git diff --check` passed.
- Risks: broader TFM/language matrix remains a later capability/portability milestone; flags reconstruction currently uses generated direct bit tests and `System.Convert.ToUInt64` for runtime composite validation.
- Out of scope: recursive cycle detection.

## Milestone 12: recursive type analysis and ThrowOnCycle

- Objective: implement recursive component analysis and optional runtime cycle detection.
- Specification sections implemented: 17, 28.6, 28.7, 22.14.
- Prerequisites: Milestone 11 complete.
- Files: dependency graph analysis, tracker rendering/helper, cycle tests.
- Public API affected: uses `LiteMapperCycleException` from Milestone 2.
- Diagnostics affected: `LITEMAPPER6001`.
- Tests written first: finite recursion, direct/indirect cycles, collections, shared non-cyclic references, concurrency, no tracker for acyclic graphs.
- Focused validation: cycle tests.
- Full validation: full solution tests and allocation-sensitive checks where practical.
- Completion criteria: one tracker per recursive public call only when required.
- Status: Completed on 2026-06-18 with recursive helper graph analysis, recursive helper reuse during model construction, `ReferenceHandling.None` informational diagnostics, `ThrowOnCycle` tracker plumbing, reference-identity active-path detection, collection-recursion forwarding, shared-reference behavior, concurrent-call coverage, and acyclic graph no-tracker coverage.
- Deviations: implemented the tracker as a private generated mapper-specific helper rather than an internal shared runtime helper; this avoids new runtime package surface and keeps the public API unchanged.
- Remaining tasks: none for Milestone 12 new-object recursive graph behavior.
- Validation commands: `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore --filter Milestone12RecursiveCycleTests` initially crashed the test host with a stack overflow from recursive helper expansion, then passed with 13 tests after implementation; `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore` passed with 61 tests; `dotnet restore Mammoth.LiteMapper.sln` passed; `dotnet build Mammoth.LiteMapper.sln --no-restore` passed with 0 warnings; `dotnet test Mammoth.LiteMapper.sln --no-build` passed with 73 total tests; `git diff --check` passed.
- Risks: generated tracker helper uses private generated support code; broader trimming/AOT and package-consumer validation remain later milestones.
- Out of scope: target-specific optimization.

## Milestone 13: capability-based emitted optimization

- Objective: implement compiler/framework capability detection for emitted syntax/API choices.
- Specification sections implemented: 3.5, 4.4, 4.5, 19.4, 19.9, 21.3, 21.4, 22.6, 22.17.
- Prerequisites: Milestone 12 complete.
- Files: capability model, analyzer option handling, language/TFM tests.
- Public API affected: none.
- Diagnostics affected: `LITEMAPPER0012` and capability-related internal diagnostics.
- Tests written first: C# 9/latest, supported TFMs, analyzer option invalidation, API availability fallbacks.
- Focused validation: capability matrix tests.
- Full validation: full solution tests across available SDK/TFM matrix.
- Completion criteria: generated code is compatible with effective language and available symbols.
- Status: Completed on 2026-06-18 with analyzer-option consumption, language-version participation in source-output invalidation, symbol-gated `Array.Empty<T>()` emission, C# 9 generated-source compatibility coverage, debug/comment option coverage, and focused generated-source inspection.
- Deviations: local validation used the installed SDK and available reference assemblies; broader release TFM/language/Roslyn matrix remains deferred to later CI/release validation.
- Remaining tasks: none for Milestone 13.
- Validation commands: `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore --filter Milestone13CapabilityBasedEmissionTests` initially failed for expected ignored analyzer options, then passed with 4 tests after implementation; `dotnet test tests\Mammoth.LiteMapper.Generator.Tests\Mammoth.LiteMapper.Generator.Tests.csproj --no-restore` passed with 65 tests; `dotnet restore Mammoth.LiteMapper.sln`; `dotnet build Mammoth.LiteMapper.sln --no-restore`; `dotnet test Mammoth.LiteMapper.sln --no-build`; `git diff --check`.
- Risks: local SDK availability may not cover release matrix.
- Out of scope: packaging/AOT release validation.

## Milestone 14: packaging, trimming, and Native AOT validation

- Objective: prove package layout and runtime portability.
- Specification sections implemented: 4.1, 22.18, 22.19, 24.1, 24.6.
- Prerequisites: Milestone 13 complete.
- Files: packing metadata, package consumer tests, AOT/trimming samples and CI.
- Public API affected: package-level API compatibility validation.
- Diagnostics affected: none unless packaging exposes generator issues.
- Tests written first: local feed consumer install, one-package install, analyzer asset placement, Roslyn output absence, deterministic package content, AOT/trimming.
- Focused validation: package consumer tests.
- Full validation: pack, install, build, run, publish trimmed/AOT.
- Completion criteria: packages behave as specified and Roslyn/generator assemblies do not leak to runtime.
- Status: Implemented on 2026-06-18 with explicit analyzer packing in the primary and generator packages, local-feed consumer package tests covering static mapping, instance mapping, nested collections, and cycle detection, independent abstractions package consumer validation, deterministic package payload checks, trimmed package-consumer publish/run validation, Basic sample runtime coverage for the same AOT-relevant shapes, Linux CI Native AOT prerequisite installation plus required AOT validation, Windows CI MSVC developer-environment AOT validation, and package API compatibility validation.
- Deviations: The source-tree Basic sample remains project-reference based; package-consumer publish tests are the authoritative trimming/AOT validation because project-reference publish propagates publish properties into netstandard library projects.
- Remaining tasks: none for Milestone 14.
- Validation commands: `dotnet test tests\Mammoth.LiteMapper.Packaging.Tests\Mammoth.LiteMapper.Packaging.Tests.csproj --no-restore --filter Milestone14PackagingAndAotTests` initially failed for missing analyzer assets and package generator activation, then passed locally with 6 tests and 1 inconclusive Windows Native AOT prerequisite result after the AOT consumer was expanded to static, instance, nested collection, and cycle mapping; Linux Docker validation with `docker run --rm -v C:\Work\Mammoth.LiteMapper:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash -lc "apt-get update && apt-get install -y clang zlib1g-dev && dotnet restore Mammoth.LiteMapper.sln && dotnet test tests/Mammoth.LiteMapper.Packaging.Tests/Mammoth.LiteMapper.Packaging.Tests.csproj --no-restore --filter Milestone14PackagingAndAotTests"` passed with 7 tests, 0 skipped, proving Native AOT publish/run; `dotnet restore Mammoth.LiteMapper.sln` passed; `dotnet run --project samples\Mammoth.LiteMapper.Samples.Basic\Mammoth.LiteMapper.Samples.Basic.csproj --no-restore` passed; `dotnet build Mammoth.LiteMapper.sln --no-restore` passed with 0 warnings; `dotnet test Mammoth.LiteMapper.sln --no-build` passed with 83 tests and 1 inconclusive Windows Native AOT prerequisite result; `dotnet pack src\Mammoth.LiteMapper.Abstractions\Mammoth.LiteMapper.Abstractions.csproj -c Release -o artifacts\packages` passed with NuGet readme warning; `dotnet pack src\Mammoth.LiteMapper.Generator\Mammoth.LiteMapper.Generator.csproj -c Release -o artifacts\packages` passed with NuGet readme warning; `dotnet pack src\Mammoth.LiteMapper\Mammoth.LiteMapper.csproj -c Release -o artifacts\packages` passed with NuGet readme warning; `apicompat package artifacts\packages\Mammoth.LiteMapper.Abstractions.1.0.0.nupkg --run-api-compat` passed; `apicompat package artifacts\packages\Mammoth.LiteMapper.1.0.0.nupkg --run-api-compat` passed; `git diff --check` passed.
- Risks: NuGet pack emits non-fatal missing-readme warnings for all three packages.
- Out of scope: performance comparison benchmarks.

## Milestone 15: benchmarks and compiling samples

- Objective: add real benchmark scenarios and compiling usage samples.
- Specification sections implemented: 23, 26, 22.18.
- Prerequisites: Milestone 14 complete.
- Files: benchmark scenarios, sample projects, benchmark docs/results.
- Public API affected: none.
- Diagnostics affected: none.
- Tests written first: samples compile/run; benchmark harness smoke tests.
- Focused validation: sample build/run and benchmark dry run.
- Full validation: full solution tests plus controlled benchmarks when required.
- Completion criteria: samples compile and benchmarks compare pinned libraries with recorded context.
- Status: Completed on 2026-06-18 with compiling Basic, Collections, and ASP.NET Core samples plus a BenchmarkDotNet flat-object comparison across manual mapping, LiteMapper, Mapperly, Mapster, and AutoMapper.
- Deviations: The benchmark dry-run command may require unsandboxed NuGet access because BenchmarkDotNet creates and restores an autogenerated benchmark project even when the outer `dotnet run` uses `--no-restore`.
- Remaining tasks: none for Milestone 15.
- Validation commands: `dotnet test tests\Mammoth.LiteMapper.Packaging.Tests\Mammoth.LiteMapper.Packaging.Tests.csproj --no-restore --filter Milestone15SamplesAndBenchmarksTests` initially timed out against the stub ASP.NET sample, then failed for expected missing sample/benchmark behavior, then passed with 2 tests after implementation; `dotnet restore Mammoth.LiteMapper.sln` passed; `dotnet build Mammoth.LiteMapper.sln --no-restore` passed with 0 warnings; `dotnet test Mammoth.LiteMapper.sln --no-build` passed with 85 tests and 1 inconclusive Windows Native AOT prerequisite result; `dotnet run --project samples\Mammoth.LiteMapper.Samples.Basic\Mammoth.LiteMapper.Samples.Basic.csproj --no-restore` passed; `dotnet run --project samples\Mammoth.LiteMapper.Samples.Collections\Mammoth.LiteMapper.Samples.Collections.csproj --no-restore` passed; `dotnet run --project samples\Mammoth.LiteMapper.Samples.AspNetCore\Mammoth.LiteMapper.Samples.AspNetCore.csproj --no-restore -- --smoke` passed; sandboxed benchmark dry run failed inside BenchmarkDotNet autogenerated restore with NuGet SSL/auth errors, then unsandboxed `dotnet run --project benchmarks\Mammoth.LiteMapper.Benchmarks\Mammoth.LiteMapper.Benchmarks.csproj -c Release --no-restore -- --filter *FlatObjectBenchmarks* --job Dry --warmupCount 1 --iterationCount 1` passed and executed 5 benchmarks; `git diff --check` passed.
- Risks: benchmark noise and dependency pinning.
- Out of scope: standalone usage guide.

## Milestone 16: usage documentation assembled from compiling samples

- Objective: create usage documentation from compiling samples only.
- Specification sections implemented: 26, 29, 30.
- Prerequisites: Milestone 15 complete.
- Files: usage documentation generated or copied from sample source, docs validation.
- Public API affected: none.
- Diagnostics affected: documentation references diagnostic IDs and meanings already implemented.
- Tests written first: docs sample extraction/compile validation.
- Focused validation: documentation build/check commands.
- Full validation: full solution, samples, package consumer tests, docs checks.
- Completion criteria: no documented API fails to compile; docs distinguish 1.0 behavior from deferred features.
- Status: Implemented on 2026-06-18 with `docs/USAGE.md` assembled from Basic, Collections, and ASP.NET Core compiling samples, plus focused validation that runs those samples and checks documented snippets, diagnostic references, deferred-feature wording, and absence of runtime mapper API claims.
- Deviations: The usage guide is maintained as a checked-in Markdown document that directly references compiling sample source instead of generating a separate artifact during build.
- Remaining tasks: none for Milestone 16.
- Validation commands: `dotnet test tests\Mammoth.LiteMapper.Packaging.Tests\Mammoth.LiteMapper.Packaging.Tests.csproj --no-restore --filter Milestone16UsageDocumentationTests` initially failed because `docs/USAGE.md` was missing, then passed with 1 test after implementation; `dotnet restore Mammoth.LiteMapper.sln` passed; `dotnet build Mammoth.LiteMapper.sln --no-restore` passed with 0 warnings; `dotnet test Mammoth.LiteMapper.sln --no-build` passed with 86 tests and 1 skipped Windows Native AOT prerequisite result; Basic, Collections, and ASP.NET Core smoke sample `dotnet run` commands passed; sandboxed BenchmarkDotNet dry run reported NuGet SSL/auth restore failures inside the autogenerated project despite exit code 0, then unsandboxed `dotnet run --project benchmarks\Mammoth.LiteMapper.Benchmarks\Mammoth.LiteMapper.Benchmarks.csproj -c Release --no-restore -- --filter *FlatObjectBenchmarks* --job Dry --warmupCount 1 --iterationCount 1` passed and executed 5 benchmarks; `git diff --check` passed.
- Risks: docs drifting from samples.
- Out of scope: new product semantics.

## Consumer skill distribution (2026-09-08)

- Scope: user-requested documentation/package work after Milestone 16; no new specification milestone or generator implementation.
- Deliverables: `skills/mammoth-litemapper/SKILL.md`, its self-contained mapping reference, and README/usage installation links for Skills CLI.
- Validation: skill-creator `quick_validate.py` passed; `npx --yes skills add . --list` discovered `mammoth-litemapper`; `Milestone16UsageDocumentationTests` passed with 1 test and 0 skipped, including all three sample runs.
- Findings: defaults and cycle-exception documentation corrected against source/specification. The section 15.3 interface-result implementation discrepancy and section 18.5 tuple usage verification gap are recorded in `DECISIONS.md`; no semantic fix is included here.
- Handoff: skill files are ready for publication to a remote ref. Local validation does not establish remote installation or a skills.sh listing. Full release/AOT/benchmark validation was not rerun for this documentation change.

## Collection-interface conformance fix and resumed review (2026-09-08)

- Scope: user-authorized fix for specification section 15.3 within Milestone 9 collection behavior, followed by review until the next confirmed implementation problem. No new API or specification semantics.
- Implementation: select arrays only when constructing `IEnumerable<T>`, `IReadOnlyCollection<T>`, and `IReadOnlyList<T>` destinations. Preserve source classification, array-update classification, mutable-list results, and existing array emission/capability handling.
- Test-first evidence: 13 added runtime cases produced 9 expected array/list failures and 4 passing mutable-interface cases before the fix. All 19 focused collection cases passed afterward with no skips. Tests cover counted/one-shot sources, empty/nonempty collections, independent copies, and nested/null-to-empty interface results.
- Documentation: usage guide now contains the exact interface-result table and links to executable regression coverage; skill reference describes corrected behavior and distinguishes older installed packages.
- Verification: solution build passed with 0 warnings/errors; scoped diff check and skill validation passed. Final full-suite and Native AOT evidence is recorded in `STATUS.md`.
- Review outcome: stopped at section 11.3 scalar converter eligibility. Existing dictionary mapping selects an arbitrary unmarked `ToInt` helper by signature, and an existing passing test endorses it. See `DECISIONS.md`; no fix for that separate defect is included.
- Handoff: collection result fix is implemented; remaining whole-project review is incomplete and awaits authorization for the newly reported defect.

## All-defects goal: normative blockers (2026-09-08)

- Authorization: repair all discovered implementation defects and continue review across milestones; the earlier stop-at-each-defect instruction is superseded. Existing user changes remain preserved.
- Contract audit: PENDING-0002 identifies missing handwritten mapping eligibility rules; PENDING-0003 identifies incompatible array/enumeration/allocation requirements. See `DECISIONS.md` for exact sections and reviewable proposed clarifications.
- Execution state: stopped before converter implementation or new tests because expected behavior depends on explicit normative decisions. No proposal is treated as approved, and no specification semantics changed.
- Next: apply approved specification clarifications first, add failing regression tests, implement minimal fixes, then resume the remaining whole-project coverage audit and complete required validation. Prior functional collection tests do not prove whole-project or allocation conformance.

## Approved clarifications and converter repair (2026-09-08)

- User approved PENDING-0002 and PENDING-0003. Specification sections 11.3 and 23.3 now define explicit handwritten eligibility and the narrow O(n) buffering exception.
- Test-first: collection/nested tests demonstrated unmarked scalar and structural helper selection, ignored marked collection converters, and incorrect local default selection. Existing positive helper fixtures now explicitly opt in; ambiguity tests retain genuinely eligible competing methods.
- Implementation: unmarked local helpers are excluded; eligible partial declarations and `[DefaultMapping]` participate; marked collection/dictionary converters precede implicit conversion; registered external conversion stages are available for elements. A unique local default is selected and duplicate defaults report `LITEMAPPER3002`.
- Allocation evidence: added `CollectionAllocationBenchmarks` for counted/unknown-count inputs at 0, 16, and 1024 elements, each with a manual baseline and output verification. Updated usage/skill guidance to explain eligibility and temporary storage.
- Current verification: 88 generator tests passed with 0 skipped. Full-suite, new benchmark, and Linux Native AOT results are recorded in `STATUS.md` when complete.
- Remaining scope: `docs/CONFORMANCE_REVIEW.md` inventories open implementation and validation defects for sections 19-24/29; other feature sections still need complete review. This repair does not establish whole-project compliance.

## Method failure isolation repair (2026-09-08)

- CR-001: regression tests first demonstrated that invalid signatures, method configuration, and conversions suppressed independent mappings (3 failures/1 passing mapper-wide control).
- Separated mapper-wide errors from method diagnostics and excluded only failed method models from rendering. Recursive mapping errors are evaluated within the owning method. Invalid mapper-wide configuration still prevents that mapper's generation.
- Focused regression passed all 4 cases; full validation results are recorded in `STATUS.md`. Usage and skill guidance explain the corrected generation behavior without implying a successful build when a mapping error remains.
- Next: complete the remaining ledger findings and unaudited specification sections. No new approval is required for ordinary repairs.

## Planning isolation, deterministic overloads, and bounded validation (2026-09-08)

- CR-002: moved documented development-option handling before mapper planning and caught planning/validation/rendering exceptions at the mapper boundary. Internal fault injection verifies sanitization and original exception visibility without adding consumer API/state; see DEC-0034.
- CR-007: sort overloads by full method identity. Reversing syntax-tree order originally changed generated source; the regression now passes.
- CR-008: shared bounded process runner for packaging/sample/usage validation, with concurrent stream draining and child build-server reuse disabled. Four regression cases failed before repair and passed afterward.
- Generator suite: 95 passed, 0 skipped. Full Windows and isolated Linux process-runner/AOT evidence is recorded in `STATUS.md` when complete.
- Next: canonical diagnostic repairs CR-003/CR-004 using the concrete repair map in `docs/CONFORMANCE_REVIEW.md`, then configurable severities, incremental isolation, validation gaps, and remaining feature review. No normative blocker was found in the diagnostic audit.

## Canonical diagnostics and configurable severity repair (2026-09-08)

- Added canonical member/configuration/converter/update regressions first: 16 failed before repair and pass afterward. Corrected source/target path, duplicate target, requested default, ignore-name, converter signature/ambiguity, read-only member, get-only collection, and array update diagnostic cases. Named registered external converter selection now avoids a premature local error and invalid named selection is reported once.
- CR-005: exact Info/Error policy handling, configurable ordinary-error descriptors, and generation gates based on hard semantic errors. Initial severity regressions: 6 failed/11 passed. Four additional mandatory-nullability/default cases yielded 2 failures/2 passes before repair. All 21 now pass; ordinary policy Ignore no longer silently leaves a non-nullable member unmapped.
- Existing milestone expectations were updated against new failing contract regressions. Full generator suite passed 132 tests. Final Windows solution validation passed 157 tests with one local AOT prerequisite skip; Linux Native AOT passed separately. See STATUS for commands/artifacts.
- Remaining: CR-009 explicit IgnoreTarget mandatory-member bypass, remaining CR-003/CR-004 conversion/diagnostic behavior, CR-006 incremental isolation, validation gaps, and remaining feature-section review. CR-003/CR-004 are partially repaired, not complete. No new normative blocker.

## Mandatory targets, numeric conversion, and official harness (2026-09-08)

- CR-009: 21 regressions cover ignored mandatory targets, C# required-member obligations, approved optional constructor defaults, and conflicting target attributes. Initial result: 13 failures/8 passes; all pass after repair.
- CR-003: shared language conversion honors checked/unchecked numeric and explicit-operator options with canonical diagnostics. Added nullable numeric Error/Throw handling after four additional failures. Final focused suite: 13 passes, no skips. Constructor conversion and complete resolver precedence remain open separately.
- CV-002: actual official Roslyn harness validates exact diagnostic location/arguments and generated source/hint/compilation; 2 passes. No new package dependency or shipping API.
- Combined generator suite: 168 passes, no failures/skips. Full solution and Linux validation evidence is recorded in STATUS when complete. Usage and consumer skill include tested numeric examples and constructor-default rules.
- Continued bounded review found CR-010 general nullable-value/patch defects, CR-011 constructor configuration/conversion/nullability/name/accessibility defects, and CR-012 resolver precedence inconsistencies. No new normative blocker. Add failing regressions before further repairs.
- CR-006 repair design: compare immutable per-mapper emission values by hint/source/eligibility, preserve deterministic order, and report diagnostics separately with fresh locations. CV-001 must inspect specific mapper outputs across mapper/model/options/language/capability changes and moved diagnostics. Design remains unimplemented.

## Nullable values, collection type emission, and diagnostic continuation (2026-09-08)

- CR-010: initial nullable-value regressions produced 12 failures/1 pass. Added general nullable-value detection/unwrapping and patch guards. Four additional collection construction cases failed because nullable element types were erased; preserve element nullability and qualified types in generated construction. Explicit root Empty collection strategy must reach the collection body. Full cross-type nullable conversion remains part of the continuing audit.
- CR-004: explicit indexer, unsupported signature/ref-like structural mapping, and asynchronous converter cases now emit 1016/2008/0006. Initial 15 failures/1 pass; focused suite now 16 passes with valid Span converter and sync-overload controls. Unsupported members/ref-like updates and 2010/3003 remain open.
- CR-006/CV-001: per-mapper emission implementation and named tracked-output tests are being validated; current results and residual cases are recorded in STATUS before handoff. No whole-project completion claim.

- Combined generator suite passed 212 tests with no failures/skips. Eleven incremental cases pass after per-mapper equality was added; mapper population changes and exact framework API invalidation remain open. Full Windows/Linux evidence is recorded in STATUS.

## Constructor planning, incremental boundaries, and package gates (2026-09-08)

- New-object constructor repair: 14 tests pass after test-first failures. Configuration/converters, actual parameter nullability, accessibility, candidate isolation, recursive call graphs, and ordinary self-typed constructors are covered. Nullable update destination construction remains open under CR-011.
- CR-006/CV-001: all 21 tracked tests pass. Per-candidate emission and location-independent successful equality preserve caching where public Roslyn identity permits it. Same-file insertion/removal probes demonstrate the host boundary; section 19.2 already says "where possible". DECISIONS records the evidence and why per-file determinism replaces host file-array order assertions.
- CV-005: content hashes cover all six package/symbol artifacts, with nine mutation/metadata tests. CV-006: public package API compatibility runs in the solution test suite. CV-008: unchanged Basic/Collections/ASP.NET Core sample sources compile/run from clean local-feed primary-package projects.
- Latest generator suite passed 236 tests. Packaging/runtime/integration and Linux AOT evidence, including initial full-run failures and subsequent corrections, is in STATUS.
- Next: nullable update construction, remaining conversion/default/nullability diagnostics, and full precedence. Then implement the consumer matrix (netstandard2.0/net8/net9/net10), minimum/intermediate/current Roslyn test hosts without changing the generator's 4.8 baseline, and required language variants. Verify current pins before selection. Whole-project conformance remains incomplete.

### Nested updater registration checkpoint (2026-09-09)

Sections11.2/11.5: two new valid-input MSTest regressions failed before repair (13:53:10 TRX): equal external registrations omitted3001; assembly registration produced13 instead of class-registration23. ResolveNestedUpdater now groups local/class/assembly candidates before default/ambiguity resolution. NestedUpdateMappingTests, Milestone10ExistingTargetMappingTests and ExternalRegistrationTests:43 passed,0 failed/skipped (13:53:39 TRX). USAGE and skill now include this verified example. W01 remains partial: null/patch, writable explicit selection, overload and recursive tracker cases remain. No normative change, no final full matrix after this repair. TokenSave status failed with Transport closed; direct source inspection used.

### Explicit nested updater overload checkpoint (2026-09-09)

Section11.4: ExplicitNestedUpdaterPrefersIdentitySourceOverload failed with3001 before repair (13:55:05 TRX). Explicit Use now prefers identity source compatibility before resolving remaining candidates. Example: Chosen(ChildSource, ChildTarget) adds10 to source3 and wins over Chosen(object, ChildTarget), producing13. Focused nested/update/registration suite:44 passed,0 failed/skipped (13:55:26 TRX). W01 remains partial; no final full-platform validation after this change. TokenSave remains unavailable (Transport closed), so source inspection was used.

### Writable nested updater checkpoint (2026-09-09)

Section14.4: explicit Use of ApplyChild on a writable child failed with2009 before repair (13:56:30 TRX). Writable reference properties now attempt explicitly selected updaters, retaining replacement by default and normal converter fallback when no updater resolves. Source Child.Value3 updates the original child to3 without changing identity. NestedUpdateMappingTests, Milestone10ExistingTargetMappingTests and Milestone6ConfigurationAndConverterTests:19 passed,0 failed/skipped (13:56:52 TRX). W01 null/patch and recursion remain open; final integrated platform checks are pending.

### Root-source nested updater checkpoint (2026-09-09)

Section11.4: omitted Source with explicit Use incorrectly reported1001 when the root had no same-name child (red13:57:57). Matching now allows the root parameter in the update path. Intermediate edits targeted the wrong block: one compile failure CS0103 and two20-test runs with1failure/19passes (13:59:05,13:59:30); these are not acceptance evidence. Corrected focused suite:20 passed,0 failed/skipped (13:59:56 TRX). Example: root Value3 supplies ApplyChild(Source, ChildTarget), setting target.Child.Value3. W01 remains partial; full integrated validation pending.
### Nested updater nullability checkpoint (2026-09-09)

Sections 12.4, 14.3, 14.4, and 16.3: regressions proved that updater calls bypassed patch/nullability behavior and discarded returned replacements. Patch/null and mismatch regressions failed 2/2 (14:02:34 TRX); nullable writable replacement failed 1/1 (14:05:56); eligible nullable get-only replacement failed 1/1 (14:07:43). After repair, 51 focused tests passed with no failures/skips (14:08:04). Intermediate CS0136 was corrected before acceptance. Full integrated validation remains pending.

### W04/W05 checkpoint (2026-09-09)

Recursion and enum/tuple/operator audits added seven delegated cases plus one root declared-recursion case. Fixed three demonstrated defects and corrected two invalid enum fixture revisions before treating results as product evidence. Focused recursion/conversion tests pass; full default Roslyn 4.8 generator suite passes 469 tests with no skips. Proceed to W06 complete requirement audit, W07/W08 benchmark evidence, documentation reconciliation, then W10 final matrices.
