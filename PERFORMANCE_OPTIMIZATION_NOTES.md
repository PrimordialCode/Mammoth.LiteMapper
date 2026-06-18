# Mammoth.LiteMapper Performance Optimization Notes

These notes are non-authoritative follow-up candidates. `SPECIFICATION.md` remains the product contract.

## Flat-object benchmark observations

- Benchmark: `FlatObjectBenchmarks`.
- Current result: LiteMapper is close to manual mapping but slightly slower in the measured flat-object case.
- Observed generated-code delta:
  - Manual benchmark assumes a non-null source.
  - Mapperly emits direct target construction and assignments without a root null guard.
  - LiteMapper emits a root null guard before direct target construction.
- Practical impact in the latest run:
  - Manual: about `4.929 ns`.
  - LiteMapper: about `5.257 ns`.
  - Mapperly: about `4.835 ns`.
  - LiteMapper allocation matched manual and Mapperly at `40 B`.

## Candidate optimizations

1. Inspect JIT output for flat mappings.
   - Compare manual, LiteMapper, and Mapperly generated IL/assembly.
   - Confirm whether the root null guard, object initializer shape, call boundaries, or inlining explain the delta.

2. Review root null-check emission.
   - Do not remove or weaken the root null behavior unless `SPECIFICATION.md` explicitly permits it.
   - If the spec allows a narrower optimization, consider eliding redundant checks only when caller-side nullability and generated semantics remain identical.

3. Compare object initializer versus assignment rendering.
   - For simple mutable targets, test whether emitting `var target = new Target(); target.Member = ...;` changes JIT output or throughput.
   - Preserve `init` and `required` behavior where object initializers are semantically required.

4. Evaluate inlining hints.
   - Test generated `[MethodImpl(MethodImplOptions.AggressiveInlining)]` on simple private helpers or public generated methods.
   - Do not add attributes unless measured results justify the generated-source noise and compatibility surface.

5. Add a focused performance regression benchmark.
   - Keep thresholds allocation-first.
   - Avoid fragile nanosecond gates on shared runners.
   - Use controlled release benchmark runs for throughput decisions.

## Constraints

- No optimization may introduce runtime reflection, dynamic dispatch, runtime registries, or runtime code generation.
- No optimization may change documented null semantics, diagnostics, or generated behavior without an approved specification update.
- Benchmark conclusions must record runtime, SDK, CPU, OS, library versions, and BenchmarkDotNet configuration.
