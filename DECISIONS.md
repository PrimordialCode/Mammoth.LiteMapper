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
- Decision: Reference `Microsoft.CodeAnalysis.PublicApiAnalyzers` `3.3.4` from `Mammoth.LiteMapper.Abstractions` with `PrivateAssets="all"` and maintain `PublicAPI.Shipped.txt` plus `PublicAPI.Unshipped.txt` in that project.
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

### DEC-0017

- Date: 2026-06-17
- Milestone: 7
- Status: Accepted
- Context: Milestone 7 nullable behavior must be implemented without starting collection mapping or existing-target patch mapping, which are later milestones.
- Decision: Apply nullable mismatch `Error` and `Throw` behavior to mapping shapes already implemented through Milestone 6. Defer collection null materialization and `IgnoreNullSourceMembers` patch behavior until the milestones that introduce those mapping forms.
- Specification references: 6.3, 12.4, 12.5, 15.11, 16.3, 25.
- Consequences: Null-collection and patch-null tests remain documented as later-milestone validation even though their configuration APIs already exist.

### DEC-0018

- Date: 2026-06-17
- Milestone: 8
- Status: Accepted
- Context: Section 14 requires private closed-type structural nested helpers, while section 24.4 states exact generated helper organization and local names are not public API.
- Decision: Emit deterministic private helper methods named from the closed source and destination type names, and treat the names as generated implementation details only.
- Specification references: 14.1, 14.2, 19.7, 24.4.
- Consequences: Generated source is deterministic and reviewable; future generator refactoring may rename helpers without changing public API.

### DEC-0019

- Date: 2026-06-17
- Milestone: 8
- Status: Accepted
- Context: Section 14.1 requires reuse of a visible explicit or default mapping before automatic structural nested mapping, and DEC-0016 previously avoided arbitrary local helper selection for scalar conversion.
- Decision: For nested structural type pairs only, consider compatible visible local mapping methods as default mappings before generating a structural helper; equal-precedence multiple compatible methods report `LITEMAPPER3001`.
- Specification references: 11.1, 14.1, 20.4, 28.4.
- Consequences: Nested mapping honors the specified precedence without extending arbitrary local helper selection to scalar conversions.

### DEC-0020

- Date: 2026-06-17
- Milestone: Cross-cutting validation
- Status: Accepted
- Context: Linux builds reported `RS0016` for public abstraction types because `Microsoft.CodeAnalysis.PublicApiAnalyzers` only auto-includes files named `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`; the repository used `PublicApi.*`, which worked only on case-insensitive filesystems.
- Decision: Rename public API baseline files and specification/test references to the analyzer-required `PublicAPI.*` casing.
- Specification references: 22.20.
- Consequences: Public API analyzer baselines are loaded consistently on Windows and Linux.

### DEC-0021

- Date: 2026-06-17
- Milestone: 9
- Status: Accepted
- Context: Milestone 9 collection mappings require generated loops and private collection helpers while exact helper organization is not public API.
- Decision: Represent collection mappings as mapping models with deterministic custom loop bodies, reusing the existing helper rendering and nested helper graph instead of adding a separate public or runtime collection mapper abstraction.
- Specification references: 3.2, 15.2, 19.7, 24.4.
- Consequences: Generated collection helpers remain private implementation details; no undocumented public API or runtime registry is introduced.

### DEC-0022

- Date: 2026-06-17
- Milestone: 9
- Status: Accepted
- Context: Section 15.6 recommends capacity preallocation only when count is cheaply available and section 15.5 forbids counting arbitrary enumerables.
- Decision: Use direct `Count` or `Length` only for arrays and known count-bearing collection shapes; custom enumerable sources are enumerated once into a temporary list when an array target requires a final length.
- Specification references: 15.5, 15.6, 28.5.
- Consequences: `IEnumerable<T>` sources do not receive speculative `Count()` calls; array targets from non-count sources allocate a temporary `List<T>` before `ToArray()`.

### DEC-0023

- Date: 2026-06-18
- Milestone: 10
- Status: Accepted
- Context: Existing-target mappings need direct mutation behavior without adding runtime mapper abstractions or public APIs.
- Decision: Reuse the existing member matching, conversion, helper, and deterministic rendering model for update methods, then render direct destination assignments with optional patch guards instead of introducing a separate runtime update pipeline.
- Specification references: 3.2, 16.1, 16.3, 19.7, 24.4.
- Consequences: Update generated source remains private/direct and shares helper generation with new-object mappings; exact update local formatting is not public API.

### DEC-0024

- Date: 2026-06-18
- Milestone: 11
- Status: Accepted
- Context: Enum mappings need direct generated behavior without adding runtime mapper abstractions, registries, or reflection.
- Decision: Integrate enum handling into the existing conversion resolver and emit direct switch expressions or checked/unchecked casts. For `[Flags]` by-name runtime composites, emit direct bit-test reconstruction over mapped atomic flags.
- Specification references: 13, 19.7, 24.1, 28.4.
- Consequences: Enum mapping remains deterministic generated source and private implementation detail; generated flags composite validation uses `System.Convert.ToUInt64` and ordinary bit operations rather than runtime reflection or dynamic dispatch.

### DEC-0025

- Date: 2026-06-18
- Milestone: 12
- Status: Accepted
- Context: Section 17.6 allows either a private mapper-specific tracker helper or an internal shared runtime helper.
- Decision: Emit a private mapper-specific cycle tracker and private reference-identity comparer only when a public mapping has a recursive helper component and effective `ReferenceHandling.ThrowOnCycle`.
- Specification references: 17.2, 17.3, 17.5, 17.6, 28.6, 28.7.
- Consequences: No public API or shared runtime helper is added; generated source remains deterministic and non-recursive mappings do not allocate tracker state.

### DEC-0026

- Date: 2026-06-18
- Milestone: 12
- Status: Accepted
- Context: A self-recursive member conversion could select the public generated mapping method as a visible mapping, which would create a fresh public entry call for each descent.
- Decision: Do not select the current generated partial method as its own nested visible mapping candidate; use the private structural helper path so recursive calls can share one tracker.
- Specification references: 17.2, 28.6, 28.7.
- Consequences: Other visible mapping methods remain eligible; self-recursive generated mappings use private helper plumbing for correct active-path tracking.

### DEC-0027

- Date: 2026-06-18
- Milestone: 13
- Status: Accepted
- Context: Section 21.3 defines development analyzer options and section 3.5 requires framework API capability detection by symbols and exact signatures.
- Decision: Consume the documented analyzer options as global build properties, make them part of source-output invalidation, and gate optimized empty-array emission on finding `System.Array.Empty<T>()` by Roslyn symbol signature.
- Specification references: 3.5, 19.4, 19.9, 21.3, 21.4.
- Consequences: Option comments/debug metadata do not alter mapping semantics, internal generator failures can be rethrown for development when explicitly enabled, and generated empty-array code falls back to `new T[0]` if the API is not present.

### DEC-0028

- Date: 2026-06-18
- Milestone: 14
- Status: Accepted
- Context: The primary package must be the normal one-package installation route and must make the generator available as an analyzer without making the generator or Roslyn assemblies runtime dependencies.
- Decision: Pack `Mammoth.LiteMapper.Generator.dll` explicitly under `analyzers/dotnet/cs` in both `Mammoth.LiteMapper` and `Mammoth.LiteMapper.Generator`; keep the source-tree project reference to the generator as an analyzer with `ReferenceOutputAssembly="false"` and `PrivateAssets="all"`.
- Specification references: 4.1, 22.19, 24.1.
- Consequences: Package consumers get generated implementations from one `Mammoth.LiteMapper` package reference; generator and Roslyn assemblies are absent from consumer runtime output.

### DEC-0029

- Date: 2026-06-18
- Milestone: 14
- Status: Accepted
- Context: Package-consumer publish validation exercises the supported install shape, while project-reference sample publishing propagates trimming/AOT publish properties into netstandard library projects and fails before reaching LiteMapper-generated code.
- Decision: Use local-feed package-consumer tests as the authoritative trimming and Native AOT validation path. Cover static mapping, instance mapping, nested collections, and cycle detection in that package consumer. Keep the Basic source-tree sample as a runtime sample with a direct analyzer project reference for normal build/run validation.
- Specification references: 22.18, 22.19, 24.1.
- Consequences: Trimmed package-consumer publish and run are validated locally. Native AOT publish and run are validated in Linux with clang/zlib prerequisites; CI sets `LITEMAPPER_REQUIRE_NATIVE_AOT=1` so missing Native AOT prerequisites fail instead of producing an inconclusive local result.

### DEC-0030

- Date: 2026-06-18
- Milestone: 14
- Status: Accepted
- Context: NuGet package archives include generated metadata entries whose names vary between packs, while LiteMapper's owned package payload must remain deterministic.
- Decision: Deterministic package tests compare owned package payload entries under `lib/`, `analyzers/`, and `.nuspec` content shape instead of NuGet-generated metadata filenames.
- Specification references: 24.6.
- Consequences: Tests validate deterministic LiteMapper package payload without failing on NuGet's generated package metadata identifier.

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
