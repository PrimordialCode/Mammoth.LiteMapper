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

### DEC-0031

- Date: 2026-06-18
- Milestone: 15
- Status: Accepted
- Context: Section 23 requires benchmark comparisons against pinned manual, LiteMapper, Mapperly, Mapster, and AutoMapper implementations; section 26 requires samples to compile and remain the future source for usage documentation.
- Decision: Keep samples as source-tree project-reference executables for development validation, and pin benchmark package versions centrally: BenchmarkDotNet 0.15.8, AutoMapper 16.1.1, Mapster 7.4.0, and Riok.Mapperly 4.3.1. Use a BenchmarkDotNet dry run as smoke validation and reserve controlled benchmark interpretation for release benchmarking.
- Specification references: 23.1, 23.4, 26.
- Consequences: Milestone 15 validates benchmark wiring without fragile throughput thresholds on a shared/local runner; benchmark result output records runtime, SDK, CPU, OS, library versions, and job configuration.

### DEC-0032

- Date: 2026-06-18
- Milestone: 16
- Status: Accepted
- Context: Section 26 requires usage documentation to be assembled from compiling sample projects and not become a parallel specification.
- Decision: Maintain `docs/USAGE.md` as a checked-in source-backed guide that directly references the compiling Basic, Collections, and ASP.NET Core sample source files, and validate the guide with a packaging test that runs those samples and checks documented snippets, diagnostics, deferred features, and absent runtime-dispatch API claims.
- Specification references: 26, 29, 30.
- Consequences: Documentation remains reviewable in source control while CI guards against documenting APIs not represented by compiling samples.

### DEC-0033

- Date: 2026-06-22
- Milestone: Post-1.0 specification update
- Status: Accepted
- Context: Non-null root source parameters previously always emitted a runtime `ArgumentNullException` guard. The project wants an opt-in guard so hot mapping paths can avoid the branch when callers already satisfy the declared non-null contract.
- Decision: Add `GuardNonNullSource` as a mapper-level `bool` and method-level `OptionState`. The library default is disabled. When enabled, generated code rejects runtime null root source values with `ArgumentNullException`; when disabled, no root-source runtime null guard is emitted solely because the source parameter is non-null.
- Specification references: 5.2, 5.3, 12.5, 22.11, 28.1.
- Consequences: Public abstractions, API baselines, generator configuration resolution, nullability tests, generated-source snapshots, usage documentation, and package API compatibility validation must be updated before implementation can be considered synchronized with the specification.

### DEC-0034

- Decision: Keep exception fault injection internal to generator tests through a stateless pipeline registration overload. Production registration passes no callback. Combine the documented development options before mapper planning so validation, planning, and rendering failures obey the same sanitization/rethrow policy.
- Specification references: 19.1, 20.3, 21.3, 22.4.
- Evidence: `InternalFailureIsolationTests` injects an actual exception; default handling yields one sanitized mapper diagnostic and unrelated generated output, while the development option exposes original exception details. No public consumer API or mutable generator instance state is added.

### DEC-0035

- Decision: Ordinary unmapped-member diagnostics use configurable descriptors at the effective policy severity. Only non-configurable semantic errors prevent generation of a valid method model; Roslyn applies standard severity overrides after diagnostics are reported.
- Specification references: 6.2, 6.4, 6.5, 20.1, 20.2.
- Evidence: `UnmappedDiagnosticSeverityTests` covers every source/target policy, tree-scoped compiler severity overrides, retained valid implementations, and mandatory non-nullability/default checks. Canonical member/converter/update IDs are verified separately in `MemberConfigurationDiagnosticTests`; earlier incorrect ID expectations were replaced after failing regressions.

### DEC-0036

- Decision: apply approved optional constructor defaults before source matching, and retain C# required-member satisfaction checks for ignored/defaulted or constructor-bound members. Resolve built-in numeric/operator policies through a shared language-conversion helper; nullable numeric Throw checks precede casts.
- Specification references: 6.4, 6.5, 10.3, 12.1-12.5, 20.2.
- Evidence: 21 mandatory-target cases and 13 numeric/operator cases pass after failing regressions. This does not approve alternate semantics or close constructor/general-nullable/precedence findings in the conformance ledger.

### Pause checkpoint: implementation evidence (2026-09-08)

- No new normative decision. Existing approved contracts now have regression-backed handling of duplicate defaults across visible local/external scopes, nullable value converter results before implicit widening, unsupported member types, and ref-like updates. A handwritten Span member converter remains supported.
- Examples: two defaults for the same pair report 3002 even across stages; a selected int? result of 12 becomes long 12L, while null follows Error/Throw policy and is checked once.
- Evidence: combined focused suite 60 passed (17 precedence, 19 converter, 24 unsupported-type), zero failures/skips, following failing regressions. Twelve Windows consumer combinations passed; complete compiler-host and final-revision platform validation remain pending. See `STATUS.md` and `docs/HANDOFF.md`.
- User requested a pause and manual resume prompt. Remaining CR-004/CR-010/CR-013 are implementation/coverage findings, not new approved semantics. The existing goal is incomplete; do not mark it complete or invent a normative blocker to represent a pause.

### Async declaration compiler compatibility (2026-09-09)

- Sections 8.4/20.2 require 0006 for async mapping declarations. The complete Roslyn 5.9 suite failed an existing diagnostic case; three focused cases then yielded two failures/one control pass.
- Repair: detect the async modifier in declaring method syntax in addition to IMethodSymbol.IsAsync. Use the same detection for declaration validation and converter/candidate eligibility. This accommodates malformed bodyless declarations without raising the shipping Roslyn 4.8 API baseline.
- Example: `async partial Target Invalid(Source source);` must produce 0006 and no implementation while `partial Target Healthy(Source source);` in the same mapper still generates. All three focused cases pass; full host/platform evidence belongs in STATUS.
- No normative decision or changed diagnostic expectation was required.

### Clean package matrix provenance (2026-09-09)

- Section 22.19 requires consumers to install the packages under test from the local feed. The framework matrix previously allowed NuGet.org to provide the same package/version.
- Two regression cases simulate a published package with an alternate local feed: populated LocalPackages must restore; empty LocalPackages must fail despite the same valid package being available elsewhere. Both failed before the fix, then passed.
- Map Mammoth.LiteMapper* exclusively to LocalPackages; retain the NuGet wildcard for framework dependencies. Each consumer has an isolated package cache. This strengthens validation without changing product semantics.

### Inherited members and converter results (2026-09-09)

- Sections7.3/9.1/9.2/9.4/11.1/18.1: include inherited interface contracts once, retain unrelated same-name candidates for ambiguity, and use most-derived declarations with hiding warnings. Source paths and ignore/default validation share inherited member discovery. No runtime reflection or inherited mapper profile composition is added.
- Explicit inherited converter calls use Roslyn symbol lookup and speculative invocation binding to preserve actual C# overload behavior. A nullable-returning base Parse(string) must not supply the nullability contract when an applicable derived Parse(object) is the method C# calls. New tests first exposed this mismatch; all16 inheritance cases now pass.
- Sections11.4/12.2-12.4: converter result eligibility uses effective numeric/operator options and rendering reuses language conversion after the once-only converter null check. Fourteen cases initially gave12 failures/two controls; all33 new/existing converter-contract cases pass afterward. Converter exceptions retain identity.
- These are repairs under the current contract, not new normative decisions. Full host/platform validation and remaining conformance limits are recorded in STATUS.

## Cross-type nullability repair (2026-09-09)

Sections12.1/12.4/12.5/13/16.3 already define these semantics; no approval or product-contract change is needed. Nullable boxing must honor explicitly non-null reference targets while preserving oblivious annotations. Nullable enum wrappers must not bypass by-name mapping: From.Ready=1 maps to To.Ready=9, null is preserved for nullable targets, and patch null skips assignment. Failing regressions also established once-only source-path evaluation and collision-free generated names. Validation and intermediate failures are recorded in STATUS.

## Resolved specification questions and historical repair evidence

The sections below preserve the original decision context and revision-specific repair evidence. Their pending-work and next-action statements are historical; current completion evidence is in `STATUS.md`.

### PENDING-0006

- Date: 2026-09-09
- Status: Option A approved by the user on 2026-09-09 and incorporated into section 20.2; implementation resumed.
- Specification references: section 20.2 requires the implementation to define and test every listed stable diagnostic; section 22.3 requires exact diagnostic assertions. Sections 15.1 and 15.3 define every supported collection destination and its concrete result, while custom collections are rejected by `LITEMAPPER4006`. Sections 17.1 through 17.4 define `ReferenceHandling.None`, `ThrowOnCycle`, and legal value-type traversal. Section 6.5 and `LITEMAPPER0008` already reject invalid enum-valued configuration.
- Missing definitions: the catalogue lists `LITEMAPPER4003` (`CollectionTargetCannotBeConstructed`), `LITEMAPPER6002` (`InvalidReferenceHandling`), and `LITEMAPPER6003` (`CyclePathCannotBeTracked`), but no behavioral clause identifies a valid declaration that must produce any of them. The currently supported collection shapes always have a specified concrete construction; invalid custom shapes use `4006`. Invalid `ReferenceHandling` values use `0008`. Value types on recursive paths are copied without identity tracking, so they are not a valid `6003` trigger.
- Concrete examples: `IReadOnlyList<int>` has the required array result, while `MyCustomCollection<int>` is rejected as `4006`, leaving no defined `4003` case. `[LiteMapper(ReferenceHandling = (ReferenceHandling)42)]` is invalid option syntax and already reports `0008`, leaving no defined `6002` case. A legal `class -> struct -> class` recursive path must map successfully under section 17.4, so it cannot be used to force `6003`.
- Option A (recommended): remove `4003`, `6002`, and `6003` from the required 1.0 catalogue and implementation because their states are unreachable under the normative feature set. Retain `4006` for unsupported custom collection construction and `0008` for invalid option values.
- Option B: add precise behavioral clauses and valid triggering declarations for each diagnostic. These clauses must distinguish `4003` from `4006`, `6002` from `0008`, and `6003` from the legal value-type rule in section 17.4 before tests or generator branches are implemented.
- Approved decision: remove `4003`, `6002`, and `6003` from the required 1.0 catalogue and generator descriptors. `4006` remains the diagnostic for unsupported custom collections, `0008` remains the diagnostic for invalid mapper-wide option values, and section 17.4 remains authoritative for legal value-type traversal.
- Consequence: no unreachable diagnostic triggers or tests are invented. The remaining defined diagnostics `3004` and `5001` now have direct focused-green coverage in `DiagnosticEmissionCoverageTests`.

### Declaration and read-only-field conformance repair (2026-09-09)

No semantic decision was required. Sections7.2/10.6 and the configuration contract already require semantic enum-value validation, preserving containing declarations, fatal invalid assembly configuration, and no illegal read-only assignment. Corrected malformed constructor fixtures before using them as evidence. Ordinary configurable unmapped errors retain legal generated output, consistent with existing severity tests; hard required/non-nullable obligations remain fatal. Example: constructor-bound readonly Value maps3, while an ordinary unbound readonly Value follows policy and stays0. Actual failing/passing results are in STATUS; PENDING-0005 remains approved.

### PENDING-0005

- Status: approved by the user on2026-09-09 and incorporated into sections13.1/13.4 and diagnostic7002. Implementation resumed. PENDING-0002/0003/0004 remain approved and incorporated.
- References: section13.1 requires "By-name mapping MUST match declared enum member names exactly." Section13.4 requires "runtime values MUST be decomposed and reconstructed without losing unmatched bits" and allows composite aliases without matching names when atomic components map consistently. Section13.5 does not define precedence for a single declared composite value.
- Concrete conflict:

  ```csharp
  [Flags] enum From { None = 0, Read = 1, Write = 2, Both = Read | Write }
  [Flags] enum To { None = 0, Read = 4, Write = 8, Both = 16 }
  // From.Both and From.Read | From.Write are the same runtime value (3).
  // Exact-name mapping selects16; atomic reconstruction selects12.
  ```

- Original ambiguity: the specification did not establish precedence or rejection for this case. No behavior was invented before approval.
- Approved decision: reject inconsistent named composites with LITEMAPPER7002 and omit the invalid implementation. Missing composite names remain allowed when their atoms map consistently; matching composite names must equal the mapped atomic OR. In the example, target Both16 is rejected; target Both12 is valid. The specification was updated before implementation; failing regressions precede the fix.
- Implementation evidence: seven new cases produced four failures/three controls before repair. Final edge/nullable suites pass39 tests with zero failures/skips, including signed values and independent healthy mappings. Full/platform validation and whole-project review remain incomplete; see STATUS.

### Approved precedence and nullable update repair evidence (2026-09-08)

- PENDING-0004 is implemented for the tested stage conflicts. Generated root declarations retain their own implementation/options rather than delegating to peer bodyless declarations; nested resolution still considers eligible partial mappings. This avoids the observed mutual delegation, guard-option leakage, and changed boxing result without changing handwritten default eligibility.
- Nullable update construction now retains selected arguments and approved optional defaults. Creation does not repeat constructor-bound setters; existing-target calls still execute writable setters. Required members use construction-time initialization or require the appropriate constructor language metadata. Six new cases are covered by the full generator run.
- Converter reference-result nullability is checked after method selection: Error emits 2010, Throw checks the returned value once. Unusable defaults for the requested pair emit 3003 instead of falling through; unrelated inaccessible defaults do not poison a usable pair. Nine tests pass after six failures established the defects.
- Jagged array creation uses valid array-rank syntax, and inner mutable arrays are copied. Root collection converters and enum element conversion follow the preceding custom-conversion stages.
- Evidence: initial precedence 7 failures/6 passes, root-delegation regression 1 failure, nullable update 2 failures/1 control pass followed by 3 required-obligation failures. Combined focused run passed 65 tests; final complete generator run passed 265 with no failures/skips. Full platform/package evidence belongs in STATUS.
- Remaining: duplicate defaults across combined visible scopes, nullable value converter-result eligibility, and the broader conformance ledger. These findings have not been declared complete.

### PENDING-0004

- Date: 2026-09-08
- Status: Approved by the user on 2026-09-08 after the concrete 13-versus-23 example; incorporated into sections 11.1/11.2. Implementation and regression validation resumed.
- Specification references: 11.1 steps 5-8, 11.2, 11.3, 11.5; approved PENDING-0002 defines eligibility but explicitly preserves precedence.
- Ambiguity: section 11.1 places a default mapping after registered external converters and mappings, while section 11.2 selects a unique default for a nested pair. It is unclear whether a handwritten local default participates at local-mapping step 5, at default step 8, or in a combined visible mapping pool. This changes observable results when eligible methods compete.
- Concrete example: for a nested `S -> T` pair with source value 3, a local `[DefaultMapping]` returns value `source.Value + 10`, and a registered external `[MappingConverter]` returns `source.Value + 20`. Selecting the default gives 13; treating it solely as step 8 after the external converter gives 23. This is a contract illustration, not an executed regression.
- Recommended clarification: use `[DefaultMapping]` as a tie-breaker within the mapping-method precedence stage being considered. Include eligible defaults in the local mapping stage (5) or registered external mapping stage (7); remove the separate default stage (8); scope section 11.2 selection to that stage. Preserve the visible-scope duplicate-default restriction and section 11.5 registration precedence. In the example the local default wins, while explicit selection, local marked converters, and member converters still precede local mappings.
- Execution: approved rule incorporated into sections 11.1/11.2 and matching pseudocode 28.4 before resolver repairs. Competing-method regressions and usage/skill synchronization are validated; final evidence is recorded in `STATUS.md`.

### Incremental host identity boundary (2026-09-08)

- Sections 19.1/19.2/19.8/21.4: use per-candidate immutable output values with structural equality; keep current diagnostics sorted separately. Successful emission equality excludes source locations because moving a declaration does not change generated content. Test output files by stable hint identity, not the host's file-array enumeration order; deterministic ordering inside each generated file is unchanged.
- Section 19.2 explicitly qualifies isolation with "where possible". Minimal stateless probes using both CreateSyntaxProvider and ForAttributeWithMetadataName reproduce Roslyn candidate identity loss when a mapper is inserted/removed before another mapper in the same file. Roslyn compares modified items but does not compare new/removed inputs: https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md#comparing-items . No public keyed output registration can recover this identity without stateful workarounds.
- Decision: retain strict tracked caching assertions for ordinary edits, cross-file population changes, same-file mapper invalidation, unrelated model/location changes, and unaffected capability changes. For the demonstrated same-file population boundary, verify observable re-emission plus identical hint/source content. These host-limit tests replace newly introduced unconditional caching assumptions; the product specification and mapping semantics are unchanged. The older declaration test now finds files by stable path instead of asserting a global array order not prescribed by section 19.8.

### Package validation repair (2026-09-08)

- Sections 22.19/24.6: compare SHA-256 hashes of every uncompressed entry in all three nupkg and all three snupkg artifacts. Normalize only NuGet-generated relationship IDs and core-property filenames; retain metadata content and relationship targets in the comparison. ZIP envelope/compression details are outside the payload comparison. This does not relax the ban on content-changing timestamps or machine-specific paths.
- Test-first evidence: eight equal-length payload mutation cases failed with the former length-only fingerprint and pass with content hashing. A normalization control proves differing metadata contents still fail equality. Actual package comparison and package-level API compatibility validation run in the automated packaging suite.

### Nullable value and collection rendering repair (2026-09-08)

- Non-semantic repair under sections 6.3, 12.4, 12.5, 15.8, 15.11, and 16.3: detect Nullable<T>, check before unwrapping, retain nullable element types during collection construction, and let the explicit Empty collection strategy handle root null input. Root/member/element tests preserve the required exception distinctions and patch target preservation.
- Generated namespace/containing types and collection element types use qualified symbol display so source-file imports are not required by generated files. Guid/DateTime/Span regression cases exposed this compilation defect. No public API or specification semantics changed.

### Consumer skill verification findings (2026-09-08)

- Scope: user-requested consumer skill packaging, with no new implementation milestone or product semantics.
- Decision: distribute `skills/mammoth-litemapper/SKILL.md` and its focused reference through Skills CLI repository discovery. Keep guidance usable without a source checkout; use the specification as the contract and compiling examples as usage evidence.
- Documentation corrections: align usage defaults with specification section 6.2 and exception properties with section 5.9, both verified against current source.
- Resolved implementation discrepancy: section 15.3 requires array results for `IEnumerable<T>`, `IReadOnlyCollection<T>`, and `IReadOnlyList<T>` destinations. The subsequent user-authorized fix selects array construction for these interfaces while preserving source classification and mutable-list targets. Regression tests first failed with 9 expected array/list mismatches and 4 passing mutable-interface cases; all 19 collection tests passed after the fix. No specification change was required.
- Verification gap: section 18.5 permits positional tuple-to-tuple conversion, while the usage guide does not demonstrate it. The skill requires a compiling version-specific example before promising tuple support.
- Publication: local discovery validation does not publish the skill or establish a skills.sh listing. GitHub installation requires the files to be available at the selected remote ref.

### Historical converter discovery conformance finding (2026-09-08)

- Status at this checkpoint: reclassified as blocked by PENDING-0002. PENDING-0002 was subsequently approved, incorporated, implemented, and validated below.
- Specification references: 11.1, 11.3, 11.4, 15.10; DEC-0016 and DEC-0019 distinguish scalar converter eligibility from structural nested mapping resolution.
- Finding: `ResolveElementExpression` invokes `ResolveVisibleMapping` for scalar dictionary key conversion. Its candidate filter accepts an unmarked compatible local helper solely by signature. A private `ToInt(string source) => source.Length` is selected for `Dictionary<string, string>` to `Dictionary<int, string>` without explicit selection, `[MappingConverter]`, or a target-member convention.
- Evidence: `Milestone9CollectionMappingTests.SetComparerIsPreservedAndDictionaryKeyCollisionThrows` defines the unmarked helper and asserts its generated invocation. Focused execution passed with 1 test and 0 skipped, demonstrating that an existing test endorses behavior prohibited by section 11.3.
- Historical next action: resolve PENDING-0002 before defining negative eligibility tests or changing selection. That action is complete; the approved rule and implementation evidence are recorded below and in `STATUS.md`.

### PENDING-0002

- Date: 2026-09-08
- Status: Approved by the user on 2026-09-08; incorporated into specification section 11.3, implemented, and validated.
- Specification references: 7.5, 8.1, 11.1 (step 5), 11.2, 11.3, 11.4, 14.1, 28.4.
- Missing definition: section 7.5 separately permits handwritten mapping methods and helper methods. Section 11.3 forbids selecting arbitrary helpers solely by signature but permits explicit mapping methods. Section 8.1 defines generated bodyless partial declarations, and section 11.4 defines valid converter signatures; neither identifies an unmarked handwritten method as an eligible mapping rather than a helper. Thus `ToInt(string)` and the nested test's unmarked `ToAddressDto(Address)` have no specification-defined distinguishing eligibility predicate. DEC-0019's structural-only distinction is absent from the authoritative specification and cannot resolve this semantic gap.
- Approved clarification: generated eligible partial mapping declarations participate automatically. Handwritten local methods participate only through explicit `MapProperty.Use`, `[MappingConverter]`, `[DefaultMapping]`, or the existing `Map{TargetMember}` convention. Unmarked local methods outside those rules remain ordinary helpers, regardless of scalar/structural parameter types or accessibility. Explicit `[UseMapper]` registration remains opt-in for compatible methods in that static external container; no new public attribute or arbitrary method-name heuristic is introduced.
- Implementation sequence: specification updated first, then failing tests, implementation, samples/usage, and skill. Default-selection precedence remains governed by sections 11.1 and 11.2. This approved clarification supersedes DEC-0019's permission to infer handwritten mapping eligibility from structural types.

### PENDING-0003

- Date: 2026-09-08
- Status: Approved by the user on 2026-09-08; incorporated into specification section 23.3, implemented, benchmarked, and documented.
- Specification references: 15.3, 15.5, 15.6, 23.3; related DEC-0022.
- Conflict: sections 15.3 and 15.5 require exact array results for sequence/read-only interfaces while consuming arbitrary unknown-count sources once. Section 23.3 permits only the destination collection and mapped elements as collection allocations. An arbitrary unknown-length source needs temporary/growing storage before constructing its final exact array. DEC-0022 explicitly records a temporary `List<T>` followed by `ToArray()`, but its non-semantic status cannot override the allocation budget. The previous interface-result fix is functionally tested; its general allocation conformance cannot be claimed under the current wording.
- Approved clarification: permit temporary buffering with O(n) total storage/allocation for unknown-count enumerable-to-array mappings, including the corresponding interface destinations. Preserve exactly-once source enumeration, prohibit preliminary counting, and retain direct final-array allocation when a cheap count is available. The exception does not grant unrelated mapping overhead and must be reflected in allocation benchmarks and the usage/skill guidance.
- Consequence: DEC-0022's temporary-list implementation is now authorized by the specification rather than only a non-semantic decision. Tests and allocation benchmarks must verify the permitted scope.

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

## Historical validation checkpoints and resolved PENDING-0007

No accepted normative decision is superseded in this section. The following entries preserve intermediate validation state and the final PENDING-0007 decision; pending-work language is historical and superseded by `STATUS.md`.
Validation checkpoint, 2026-09-09: no new semantic decision required. Current CR-015/read-only revision passes398 tests on each Windows/Linux Roslyn4.8/4.14/5.9 host,448 Windows solution tests with one linker skip, and required Linux AOT one pass/zero skips. Source Link advisory repair is an operational dependency follow-up, not a product-contract change. CR-016 and nested-update findings still require failing regressions. See STATUS for failed sandbox attempt and complete evidence.
CR-016 checkpoint, 2026-09-09: no normative change. Registration shape and actual accessibility now follow5.5/8.2/8.3/11.3–11.5/20.2/27. Ref-return handwritten converters remain usable as ordinary value copies under11.4. SourceLink.GitHub10.0.303 resolves patched Build.Tasks.Git10.0.303; dependency regression verifies affected advisory ranges. Final default generator429 passes; full/platform reruns remain pending. See STATUS for red/green evidence.
2026-09-09 platform checkpoint: no normative decision changed. CR-016429 tests pass per Windows/Linux host. Linux validation exposed a helper false positive for successful `0 Warning(s)` output; two regressions failed before excluding only exact zero-count summary lines. Actual warnings/trimming/nonzero summaries still fail; eight focused and56 Linux packaging tests pass. Latest evidence in STATUS supersedes earlier pending platform entries.
2026-09-09 W01 checkpoint: no semantic decision changed. Section14.4 non-null get-only nested mutation now has failing-before-fix evidence for local/generated and external updaters; explicit Use/path follows11.1. Remaining null/patch/precedence/recursive cases are incomplete, not waived. User invoked subagent-orchestrator; docs/WORK_LEDGER.md records10 known work packages and exclusive ownership. No completion claim.

### Nested updater registration checkpoint (2026-09-09)

Sections11.2/11.5: two new valid-input MSTest regressions failed before repair (13:53:10 TRX): equal external registrations omitted3001; assembly registration produced13 instead of class-registration23. ResolveNestedUpdater now groups local/class/assembly candidates before default/ambiguity resolution. NestedUpdateMappingTests, Milestone10ExistingTargetMappingTests and ExternalRegistrationTests:43 passed,0 failed/skipped (13:53:39 TRX). USAGE and skill now include this verified example. W01 remains partial: null/patch, writable explicit selection, overload and recursive tracker cases remain. No normative change, no final full matrix after this repair. TokenSave status failed with Transport closed; direct source inspection used.

### Explicit nested updater overload checkpoint (2026-09-09)

Section11.4: ExplicitNestedUpdaterPrefersIdentitySourceOverload failed with3001 before repair (13:55:05 TRX). Explicit Use now prefers identity source compatibility before resolving remaining candidates. Example: Chosen(ChildSource, ChildTarget) adds10 to source3 and wins over Chosen(object, ChildTarget), producing13. Focused nested/update/registration suite:44 passed,0 failed/skipped (13:55:26 TRX). W01 remains partial; no final full-platform validation after this change. TokenSave remains unavailable (Transport closed), so source inspection was used.

### Writable nested updater checkpoint (2026-09-09)

Section14.4: explicit Use of ApplyChild on a writable child failed with2009 before repair (13:56:30 TRX). Writable reference properties now attempt explicitly selected updaters, retaining replacement by default and normal converter fallback when no updater resolves. Source Child.Value3 updates the original child to3 without changing identity. NestedUpdateMappingTests, Milestone10ExistingTargetMappingTests and Milestone6ConfigurationAndConverterTests:19 passed,0 failed/skipped (13:56:52 TRX). W01 null/patch and recursion remain open; final integrated platform checks are pending.

### Root-source nested updater checkpoint (2026-09-09)

Section11.4: omitted Source with explicit Use incorrectly reported1001 when the root had no same-name child (red13:57:57). Matching now allows the root parameter in the update path. Intermediate edits targeted the wrong block: one compile failure CS0103 and two20-test runs with1failure/19passes (13:59:05,13:59:30); these are not acceptance evidence. Corrected focused suite:20 passed,0 failed/skipped (13:59:56 TRX). Example: root Value3 supplies ApplyChild(Source, ChildTarget), setting target.Child.Value3. W01 remains partial; full integrated validation pending.
### Nested updater nullability implementation note (2026-09-09)

No normative decision was added. Existing sections 12.4, 14.3, 14.4, and 16.3 require updater calls to honor nullability and patch behavior. Returning updater results are assigned only for writable children; a nullable get-only child reports `LITEMAPPER5005` because a replacement cannot be stored. Example: a nullable writable child created by `ApplyChild` is assigned to the parent, while a null patch source leaves the current child unchanged.

### Recursive value helpers and declared mappings (2026-09-09)

No normative decision was added. Section 17.4 requires value types on recursive paths to be copied without identity tracking or boxing, so the invalid self-containing-struct `6003` fixture cannot justify rejecting a legal class-to-struct-to-class graph. Sections 14.1 and 17.2 require mutually recursive declared generated mappings to share tracker state; private tracked overloads preserve public signatures and forward paths. Tuple/object automatic structural boundaries use existing `LITEMAPPER2004` under sections 18.5 and 20.2.

### PENDING-0007

- Date: 2026-09-09
- Status: Approved Option A on 2026-09-09 and incorporated into specification section 11.4.
- Specification references: 9.4; 11.1 steps 1-2; 11.3; 11.4; 12.4.
- Missing definition: a configured source path can become null because an intermediate segment is nullable even when the selected terminal member type is non-null. When an explicit converter is selected, the destination type may itself be nullable. Section 9.4 requires null policy at each segment, sections 11.3-11.4 require converter nullability compatibility and say the converter receives the selected source value, while section 12.4 defines only a potentially null source mapped to a non-null target. The contract does not say whether a missing intermediate bypasses the converter or is supplied to a nullable converter parameter.
- Example: `Address? Address`, path `Address.Code`, converter `string? Normalize(string? value)`, and target `string? Code`. If `Address` is null, should `Normalize(null)` run, or should the generated mapping assign null without invoking `Normalize`?
- Option A (recommended): converter-parameter nullability governs the boundary. A nullable-input converter receives null. A non-null converter parameter follows `NullableMismatch`: `Error` emits `LITEMAPPER2001`; `Throw` checks the full path before invoking the converter. This preserves explicit converter intent and enforces the selected method's declared contract.
- Option B: a missing path segment bypasses every converter and assigns null directly when the destination is nullable. Converter parameter nullability is irrelevant for traversal failure.
- Decision: converter-parameter nullability governs. A nullable-input converter receives null. A non-null converter parameter follows `NullableMismatch`: `Error` emits `LITEMAPPER2001`; `Throw` checks the full path before invoking the converter. This applies even when the destination is nullable.
- Consequence: add test-first coverage for the decided converter behavior and repair the five separate path defects governed by sections 6.3, 9.4, 14.3, 15.11, and 16.3.
- Implementation note (2026-09-09): completed with red-first coverage for nullable/non-null converter inputs, path-induced collection policy, nested preservation and Throw ordering, once-only patch evaluation, required nullable/non-null construction, generated identifier collisions, and exact/unwrapped default-pair diagnostics. Final validation evidence is recorded in `STATUS.md`.
