# Mammoth.LiteMapper Performance Optimization Notes

These notes are non-authoritative follow-up candidates. `SPECIFICATION.md` remains the product contract.

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
