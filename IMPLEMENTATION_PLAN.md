# Mammoth.LiteMapper Implementation Plan

`SPECIFICATION.md` is authoritative. This plan explains execution order and validation only; it does not redefine product semantics.

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
- Status: Completed on 2026-06-17 with public abstraction types, PublicApi baseline files, PublicApiAnalyzer configuration, runtime API shape tests, packaging boundary tests, focused abstraction build, full solution restore/build/test, and solution-list validation.
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
- Risks: Roslyn 4.0.1 API baseline limits helper APIs.
- Out of scope: flat mapping behavior.

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
- Risks: nullable analysis and member hiding.
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
- Risks: C# required metadata across TFMs.
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
- Risks: exact diagnostic locations for attribute arguments.
- Out of scope: collection, enum, recursive behavior.

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
- Risks: nullable annotations differ by reference assembly.
- Out of scope: nested object mapping unless required by null tests.

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
- Risks: ambiguity resolution across visible mappings.
- Out of scope: recursive cycle tracking.

## Milestone 9: arrays, collections, sets, and dictionaries

- Objective: implement supported collection and dictionary mapping.
- Specification sections implemented: 15, 22.12.
- Prerequisites: Milestone 8 complete.
- Files: collection planner/renderer, collection tests.
- Public API affected: none.
- Diagnostics affected: `LITEMAPPER4001`-`LITEMAPPER4006`.
- Tests written first: every mandatory shape, unsupported shapes, single enumeration, capacity, comparers, key collision.
- Focused validation: collection tests.
- Full validation: full solution tests.
- Completion criteria: supported shapes map correctly and unsupported shapes diagnose.
- Risks: interface availability such as `IReadOnlySet<T>` by compilation.
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
- Risks: distinguishing replace vs mutate.
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
- Risks: alias and flags decomposition edge cases.
- Out of scope: recursive cycle detection.

## Milestone 12: recursive type analysis and ThrowOnCycle

- Objective: implement recursive component analysis and optional runtime cycle detection.
- Specification sections implemented: 17, 28.6, 28.7, 22.14.
- Prerequisites: Milestone 11 complete.
- Files: dependency graph analysis, tracker rendering/helper, cycle tests.
- Public API affected: uses `LiteMapperCycleException` from Milestone 2.
- Diagnostics affected: `LITEMAPPER6001`-`LITEMAPPER6003`.
- Tests written first: finite recursion, direct/indirect cycles, collections, shared non-cyclic references, concurrency, no tracker for acyclic graphs.
- Focused validation: cycle tests.
- Full validation: full solution tests and allocation-sensitive checks where practical.
- Completion criteria: one tracker per recursive public call only when required.
- Risks: accurate path reporting without retaining object graphs.
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
- Risks: AOT support depends on SDK workloads/platform.
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
- Risks: docs drifting from samples.
- Out of scope: new product semantics.
