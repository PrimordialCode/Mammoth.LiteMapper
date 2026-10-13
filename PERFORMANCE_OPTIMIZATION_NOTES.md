# Mammoth.LiteMapper Performance Optimization Notes

These notes are non-authoritative follow-up candidates. `SPECIFICATION.md` remains the product contract.

## Compact cycle tracker allocation evidence (2026-10-13)

The private generated tracker has one object slot. Its own instance denotes empty state, an ordinary first source is stored inline (including null), and a private sealed HashSet subtype denotes spill storage. Reserved sentinel/type-tag sources escape into a fresh set. Promotion populates a local set before publishing it; failed duplicate entry leaves occupancy intact. Spill sets use reference identity and remain available after unwind. Existing Enter-before-try/finally-Exit calls and lazy exception paths are unchanged. No public API, mapping reflection, runtime code generation or specification semantics change.

Source comparison: merged develop `221459f715f2c4a864212ea6838eebacdf067157`, the earlier inline-first prototype, and this compact design. On SPECTRE_GRD (Windows 11 x64, i7-1260P, SDK 10.0.401/runtime 10.0.12), a separate typed allocation probe used 5,000 warmups and five rounds of 1,000 mappings per depth; every round validated mapped shape, cycle failures and tracker reuse. Source construction and assertions were outside measurement.

| Chain depth | Develop bytes/op | Inline-first prototype bytes/op | Compact bytes/op |
|---:|---:|---:|---:|
| 1 | 232 | 72 | 56 |
| 2 | 264 | 280 | 264 |
| 16 | 1,744 | 1,760 | 1,744 |
| 128 | 15,528 | 15,544 | 15,528 |

These are total managed allocations per mapping, not peak memory. The fieldless private spill subtype matches the original HashSet allocation footprint; direct constructor probes measured 24 bytes for the tracker and 64 for either empty set type. Generator tests verify deferred storage and reuse relative to runtime allocation controls.

A separate six-pass BenchmarkDotNet experiment used develop/inline-first/compact/compact/inline-first/develop, 16 cases per pass, two launches, five warmups and ten measured iterations. It retained default CLR policies, affinity mask 4, a 45-second cooldown and a start-CPU check of at most 10%. `DontEnforcePowerPlan` (Guid.Empty) kept Balanced unchanged. Actual iterations were at least 164.905 ms. Timing controls drifted by up to 94.14%; compact depth-1 mappings were faster than develop in all four cross-pass comparisons, while deeper cases produced slowdown signals. Compact versus inline-first shallow ordering reversed between passes. The repository gate blocked all four compact/develop comparisons. This is allocation evidence with inconclusive throughput attribution, not a general speed win or release acceptance. The earlier 3.0.0/develop shallow tracked slowdown remains a separate historical concern; measurements from other PCs are not pooled.

Raw JSON, normalized results, all twelve gate reports, launch statistics, environment, hashes, source patch and test evidence are preserved in `LiteMapper-compact-state-SPECTRE_GRD-20261013.zip` (SHA256 `1D78057FA23247A5A3637132A9B9A5FFBE0A4E445C4C7F4A91588EDA6B995F59`). No diagnostic harness, binaries or benchmark thresholds are part of this shipping change. Final-head CI and stable same-binary controls remain necessary before a release performance decision.

## Issue #32: lazy cycle-path allocation evidence (2026-10-09)

- Baseline: unchanged develop `7b19def74753df90cb3770c7410ff5d7a3951244`. Candidate: issue #32's private inline-leaf/immutable-link diagnostic path representation.
- Environment: Debian 13 Linux x64 cloud container, AMD EPYC 9V74 host as exposed by `/proc/cpuinfo`, SDK 10.0.401, runtime 10.0.12, Roslyn 4.8.0, Release compilation, C# 9, `DOTNET_PROCESSOR_COUNT=1`. This is not a controlled release throughput benchmark.
- Harness: `LazyCyclePathTests.SuccessfulChainAllocationsScaleLinearlyAtSafeDepths`, using `GC.GetAllocatedBytesForCurrentThread`. Each depth uses 40 warmups followed by three rounds of 100 mappings. Source construction and reporting are outside measurement; each mapped chain is validated without allocating. All three rounds give identical values. The no-tracking control allocates destination nodes only.

| Chain depth | Previous ThrowOnCycle bytes/op | Lazy-path bytes/op | None bytes/op (unchanged) |
|---:|---:|---:|---:|
| 16 | 3,032 | 1,744 | 512 |
| 32 | 9,200 | 3,560 | 1,024 |
| 64 | 31,000 | 7,440 | 2,048 |
| 128 | 111,792 | 15,528 | 4,096 |
| 256 | 421,520 | 32,392 | 8,192 |

`ShallowAndWideTraversalAllocationControls` additionally measures depths 1 and 2 at 232 and 264 bytes/op, and an array of 256 sibling depth-2 chains at 18,656 bytes/op. These values are unchanged by the optimization. Runtime manual controls verify that first named edges introduce no path-object allocation and that shallow siblings share only one tracker overhead.

Generated source retains one literal segment per immutable link and no materialized prefix; formatting occurs only in the duplicate-reference throwing branch. This establishes linear active diagnostic-path storage by code structure. The numbers above are total managed allocations, including destination objects and tracker/HashSet growth, not isolated diagnostic allocations or peak-memory measurements. No latency, throughput, release acceptance, or general stack-safety improvement is claimed. Safe test depth is capped at 256.

Reproduce after explicit restore/build with `dotnet test tests/Mammoth.LiteMapper.Generator.Tests/Mammoth.LiteMapper.Generator.Tests.csproj -c Release --no-build --no-restore --filter LazyCyclePathTests --logger trx`; per-round measurements are retained in TRX standard output. The existing `RecursiveCycleAllocationBenchmarks` remains available for controlled BenchmarkDotNet throughput comparison; its historical manual tracker does not build public exception paths and is not a diagnostic-path allocation baseline.

## Flat-object benchmark observations

- Benchmark: `FlatObjectBenchmarks`.
- Latest run command: `dotnet run --project benchmarks\Mammoth.LiteMapper.Benchmarks\Mammoth.LiteMapper.Benchmarks.csproj -c Release --no-restore -- --filter *FlatObjectBenchmarks* --warmupCount 3 --iterationCount 5`.
- Latest run context:
  - BenchmarkDotNet `0.15.8`.
  - Windows 11 `10.0.26200.8655`.
  - 13th Gen Intel Core i7-13700K, 1 CPU, 24 logical cores, 16 physical cores.
  - .NET SDK `10.0.301`.
  - Runtime `.NET 10.0.9`, X64 RyuJIT `x86-64-v3`.
  - Job: `WarmupCount=3`, `IterationCount=5`.
  - Compared package versions: AutoMapper `16.1.1`, Mapster `10.0.8`, Mapperly `4.3.1`.
- Latest results:
  - Manual: `3.927 ns`, `40 B`, ratio `1.00`.
  - LiteMapper: `4.230 ns`, `40 B`, ratio `1.08`.
  - Mapperly: `4.274 ns`, `40 B`, ratio `1.09`.
  - Mapster: `9.441 ns`, `40 B`, ratio `2.40`.
  - AutoMapper: `26.751 ns`, `40 B`, ratio `6.81`.
- Current interpretation:
  - The previous root-guard delta is addressed for the default non-null root source path.
  - LiteMapper now measured slightly faster than Mapperly in this local run, but the difference is within tiny-nanosecond benchmark noise and should not be treated as a stable lead without repeated controlled runs.
  - Allocation remains equal across all measured mappers at `40 B`.
- Previous result: LiteMapper was close to manual mapping but slightly slower in the earlier measured flat-object case.
- Previous generated-code delta:
  - Manual benchmark assumes a non-null source.
  - Mapperly emits direct target construction and assignments without a root null guard.
  - LiteMapper emitted a root null guard before direct target construction.
- Practical impact in that run:
  - Manual: about `4.929 ns`.
  - LiteMapper: about `5.257 ns`.
  - Mapperly: about `4.835 ns`.
  - LiteMapper allocation matched manual and Mapperly at `40 B`.
- Current specification and implementation:
  - Non-null root source guards are disabled by default.
  - `GuardNonNullSource` can opt a mapper or mapping method into the runtime `ArgumentNullException` guard.
  - Representative generated-source snapshots now omit the default root source guard for non-null source parameters.

## Candidate optimizations

1. Inspect JIT output for flat mappings.
   - Compare manual, LiteMapper, and Mapperly generated IL/assembly.
   - Optional follow-up only; the latest local run does not show a Mapperly throughput advantage after default root source guard removal.
   - If future controlled runs show a repeatable delta, confirm whether object initializer shape, call boundaries, inlining, or any remaining generated-code differences explain it.

2. Review root null-check emission.
   - Status: addressed for non-null root source parameters by `GuardNonNullSource`.
   - Default generated mappings now elide the root source guard for non-null source parameters.
   - Keep destination null guards for existing-target mappings and nullable-source `Throw` behavior intact unless `SPECIFICATION.md` changes.

3. Compare object initializer versus assignment rendering.
   - For simple mutable targets, test whether emitting `var target = new Target(); target.Member = ...;` changes JIT output or throughput.
   - Preserve `init` and `required` behavior where object initializers are semantically required.

4. Evaluate inlining hints.
   - Test generated `[MethodImpl(MethodImplOptions.AggressiveInlining)]` on simple private helpers or public generated methods.
   - Do not add attributes unless measured results justify the generated-source noise and compatibility surface.

5. Add a focused performance regression benchmark.
   - Keep thresholds allocation-first.
   - Avoid fragile nanosecond gates on shared runners.
   - Use repeated controlled release benchmark runs for throughput decisions.

## Constraints

- No optimization may introduce runtime reflection, dynamic dispatch, runtime registries, or runtime code generation.
- No optimization may change documented null semantics, diagnostics, or generated behavior without an approved specification update.
- Benchmark conclusions must record runtime, SDK, CPU, OS, library versions, and BenchmarkDotNet configuration.
