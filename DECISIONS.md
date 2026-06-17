# Mammoth.LiteMapper Decision Log

`SPECIFICATION.md` is authoritative. Accepted entries here are non-semantic implementation decisions unless explicitly stated otherwise.

## Accepted non-semantic implementation decisions

### DEC-0001

- Date: 2026-06-17
- Milestone: 1
- Status: Accepted
- Context: The specification requires exact project names and both solution formats.
- Decision: Use SDK-style projects under the exact section 4.6 paths and generate `Mammoth.LiteMapper.slnx` from `Mammoth.LiteMapper.sln` with `dotnet sln Mammoth.LiteMapper.sln migrate`.
- Specification references: 4.6, 25.
- Consequences: `.sln` remains the editable source solution; `.slnx` is regenerated after solution membership changes.

### DEC-0002

- Date: 2026-06-17
- Milestone: 1
- Status: Accepted
- Context: The skeleton needs repeatable dependency versions and deterministic project settings.
- Decision: Use central package management in `Directory.Packages.props` and shared deterministic defaults in `Directory.Build.props`.
- Specification references: 3.4, 4.1, 4.2, 22.1, 22.7, 24.6.
- Consequences: Package versions are controlled centrally; shipping assemblies target `netstandard2.0`.

### DEC-0003

- Date: 2026-06-17
- Milestone: 1
- Status: Accepted
- Context: Milestone 1 must compile without implementing Milestone 2 public abstractions or Milestone 3 generator behavior.
- Decision: Shipping assemblies contain internal marker types only. No public mapping or abstraction API is introduced.
- Specification references: 4.6, 5, 25.
- Consequences: Public API remains empty until Milestone 2.

### DEC-0004

- Date: 2026-06-17
- Milestone: 1
- Status: Superseded by DEC-0013
- Context: The generator originally had to use the Roslyn 4.0.1 API baseline.
- Decision: Pin `Microsoft.CodeAnalysis.CSharp` to `4.0.1` in central package management and reference it from the generator with `PrivateAssets="all"`.
- Specification references: 4.1, 22.7.
- Consequences: Superseded because the approved minimum requirement is now netstandard2.0 generator output with .NET 8.0+ consumer/compiler support, and the Roslyn baseline has been raised to 4.8.0.

### DEC-0005

- Date: 2026-06-17
- Milestone: 1
- Status: Accepted
- Context: Milestone 1 requires MSTest with built-in assertions.
- Decision: Use `MSTest.TestFramework` `4.2.3`, `MSTest.TestAdapter` `4.2.3`, and `Microsoft.NET.Test.Sdk` `18.6.0`; do not add FluentAssertions, Shouldly, or another assertion library.
- Specification references: 22.1.
- Consequences: Tests use only MSTest assertion APIs.

### DEC-0006

- Date: 2026-06-17
- Milestone: 1
- Status: Accepted
- Context: Milestone 1 requires initial Windows and Linux CI.
- Decision: Add separate GitHub Actions workflows for Windows and Linux that restore, build, test, and list both solution formats.
- Specification references: 22.8, 25.
- Consequences: CI skeleton exists; release, trimming, AOT, package-consumer, and benchmark validation remain later milestones.

### DEC-0008

- Date: 2026-06-17
- Milestone: 2
- Status: Accepted
- Context: Milestone 2 requires public API baselines and Roslyn public API analyzer use.
- Decision: Reference `Microsoft.CodeAnalysis.PublicApiAnalyzers` `3.3.4` from `Mammoth.LiteMapper.Abstractions` with `PrivateAssets="all"` and maintain `PublicApi.Shipped.txt` plus `PublicApi.Unshipped.txt` in that project.
- Specification references: 22.20, 24.4, 25.2.
- Consequences: API changes are checked at build time without adding a runtime package dependency.

### DEC-0009

- Date: 2026-06-17
- Milestone: 2
- Status: Accepted
- Context: The specification assigns public abstraction types to the abstractions package and does not require the primary package assembly itself to expose forwarding public types in this milestone.
- Decision: Keep `Mammoth.LiteMapper` with no exported public types during Milestone 2 while it references `Mammoth.LiteMapper.Abstractions` as the normal dependency.
- Specification references: 4.1, 5, 25.
- Consequences: Consumers get the public API through the dependency; package-consumer behavior remains a later validation milestone.

### DEC-0010

- Date: 2026-06-17
- Milestone: 2
- Status: Accepted
- Context: Section 22.20 requires package-level API compatibility validation.
- Decision: Use `Microsoft.DotNet.ApiCompat.Tool` `10.0.301` as a temporary validation tool and run `apicompat package --run-api-compat` against the Milestone 2 packages.
- Specification references: 22.20, 24.4.
- Consequences: Package API compatibility is validated without adding tool binaries or package outputs to the repository.

### DEC-0011

- Date: 2026-06-17
- Milestone: 3
- Status: Superseded by DEC-0013
- Context: Milestone 3 required Roslyn source-generator test infrastructure while the generator was still constrained to the Roslyn 4.0.1 API baseline.
- Decision: Use the available official `Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing.MSTest` `1.1.2` package and a custom Roslyn compilation harness for generator execution, diagnostics, deterministic source inspection, and baseline-compatible incremental checks.
- Specification references: 19.1, 21, 22.2, 22.7.
- Consequences: Superseded because the approved Roslyn baseline is now 4.8.0.

### DEC-0012

- Date: 2026-06-17
- Milestone: 3
- Status: Accepted
- Context: Milestone 3 must establish source emission infrastructure without implementing flat mapping behavior assigned to Milestone 4.
- Decision: Generate one deterministic declaration-only partial source file per valid mapper and intentionally omit mapping method bodies until Milestone 4.
- Specification references: 19.5, 19.7, 19.8, 25.
- Consequences: Generated source can be inspected for deterministic layout, namespace, nesting, and hint names without introducing mapping semantics early.

### DEC-0013

- Date: 2026-06-17
- Milestone: 3
- Status: Accepted
- Context: The approved support floor is a `netstandard2.0` generator assembly and .NET 8.0 minimum consumer/compiler requirements. Roslyn 4.0.1 prevented required observable tracked incremental-step validation.
- Decision: Raise the generator compile-time Roslyn baseline from 4.0.1 to 4.8.0 and update `SPECIFICATION.md`, central package management, tests, and status to match.
- Specification references: 4.1, 22.7.
- Consequences: The generator remains `netstandard2.0`, can use Roslyn 4.8.0 APIs, and Milestone 3 can satisfy tracked incremental generator validation without the previous 4.0.1 blocker.

### DEC-0014

- Date: 2026-06-17
- Milestone: 4
- Status: Accepted
- Context: Milestone 4 introduces configurable unmapped-member diagnostics while the shared diagnostic catalogue uses stable IDs and default severities.
- Decision: Represent policy-specific unmapped-member severities with paired internal `DiagnosticDescriptor` instances that share the same stable diagnostic ID and message but differ by default severity.
- Specification references: 6.2, 9.1, 20.1, 20.2.
- Consequences: Generated output is suppressed when an effective policy is `Error`; future diagnostics work should verify editorconfig severity override behavior for configurable diagnostics.

### DEC-0015

- Date: 2026-06-17
- Milestone: 5
- Status: Accepted
- Context: Milestone 5 requires `init` property support while Milestone 4 emitted ordinary post-construction assignments for flat mappings.
- Decision: Render new-object member assignments through object initializers for both `init` properties and ordinary settable members.
- Specification references: 10.5, 19.7, 25.
- Consequences: Generated source remains deterministic and direct; the representative Milestone 4 snapshot was updated to the new construction shape.

### DEC-0016

- Date: 2026-06-17
- Milestone: 6
- Status: Accepted
- Context: Section 11.3 forbids selecting arbitrary compatible helper methods solely by signature while section 11.1 includes local converters and mapping methods in the resolution chain.
- Decision: For Milestone 6, local member conversion is limited to explicit `MapProperty.Use`, local `[MappingConverter]` methods, and local `Map{TargetMember}` methods. Unmarked local helper methods are not selected by signature. Registered external types may contribute converter or mapping methods because registration is explicit user intent.
- Specification references: 11.1, 11.3, 11.4, 11.5, 28.4.
- Consequences: Local helper methods remain ordinary C# unless explicitly named or marked; future nested/default mapping work can add mapping-method/default resolution without introducing arbitrary local helper selection.

## Pending specification questions

### PENDING-0001

- Date: 2026-06-17
- Milestone: 3
- Status: Resolved by DEC-0013
- Context: Section 19.2 requires incrementality to be tested through observable tracked generator steps, but section 22.7 requires the generator baseline to remain Roslyn 4.0.1. The newer public tracked-step inspection APIs used by current Roslyn tests are not available in the Roslyn 4.0.1 API surface used by this milestone.
- Question: Should Milestone 3 raise the test harness Roslyn API level for tracked-step inspection only, define an approved custom observable tracking mechanism, or defer tracked-step inspection until a later approved Roslyn baseline?
- Specification references: 19.2, 21.4, 22.2, 22.7.
- Consequences: User approved raising the generator compile-time Roslyn baseline to 4.8.0; Milestone 3 may proceed with Roslyn tracked incremental-step validation.

## Rejected alternatives

### DEC-0007

- Date: 2026-06-17
- Milestone: 1
- Status: Rejected
- Context: Public marker APIs could make test code easier to write.
- Decision or question: Do not expose public marker types in shipping assemblies during Milestone 1.
- Specification references: 3.2, 5, 25.
- Consequences: Tests load assemblies by name and validate that no public mapping API exists yet.

## Superseded decisions

None.
