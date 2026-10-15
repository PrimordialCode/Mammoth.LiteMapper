# Allocation regression tests

The generator test suite includes exact allocation-count regressions for recursive mapping. Run them through MSTest as usual:

```sh
dotnet restore Mammoth.LiteMapper.sln
dotnet build Mammoth.LiteMapper.sln -c Release --no-restore
dotnet test tests/Mammoth.LiteMapper.Generator.Tests/Mammoth.LiteMapper.Generator.Tests.csproj -c Release --no-build --no-restore --filter "TestCategory=Allocation"
```

The allocation-sensitive test methods dispatch to the test-only `Mammoth.LiteMapper.AllocationTestHost` process. Its runtime configuration disables background GC (`System.GC.Concurrent=false`). The ordinary generator test host keeps its default GC configuration, and the other correctness tests continue to run there. This configuration does not change LiteMapper packages or applications.

The child loads the same test assembly and runs the existing assertions once. Boolean data rows are forwarded explicitly. Assertion failures, missing methods, invalid configuration, nonzero process exits and timeouts fail the parent test; there is no retry, tolerance or selection of the lowest measurement. Standard output and error are returned to MSTest for diagnosis.

## Why isolation is necessary

[dotnet/runtime#134724](https://github.com/dotnet/runtime/issues/134724) reports phantom `GC.GetAllocatedBytesForCurrentThread` deltas under background GC, including Windows and Ubuntu x64. The [proposed runtime fix](https://github.com/dotnet/runtime/pull/134855) retires unused allocation-context space when background GC clears an abandoned context. This is CLR accounting behavior; the Roslyn version in the CI matrix selects a compiler, while these tests execute on .NET 10.

The investigation reproduced the counter anomaly in a package-free .NET 10.0.13 program without LiteMapper or Roslyn. Exact historical CI failures did not capture GC traces, so the matching runtime issue is strong supporting evidence rather than a retrospective proof of each spike. Disabling JIT tiering or PGO did not eliminate the local test failures. Extra warm-up, retries or a wider allocation budget do not address the defective accounting path.

The child verifies its effective GC configuration before executing tests. Regression controls also verify that ordinary-host GC remains unchanged, zero-allocation work reads zero, a real heap allocation is counted exactly, and an intentional exact-allocation assertion failure reaches the parent as a failed assertion. The existing tracker size, promotion, spill-reuse and traversal assertions remain exact.

This mitigation does not establish throughput or latency performance and is not a substitute for functional cycle-tracking tests under normal GC. It also does not make arbitrary allocation measurements deterministic: allocations must stay outside measurement windows unless they are the behavior being checked. Reassess the mitigation against a verified runtime fix before removing it; retain the positive controls and exact regression assertions.
