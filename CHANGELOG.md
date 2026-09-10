# Changelog

## 2.0.0

### Added

- Added `GuardNonNullSource` to mapper and method options. For example, `[LiteMapper(GuardNonNullSource = true)]` generates an `ArgumentNullException` guard for a non-nullable root source parameter.
- Added positional tuple-to-tuple mapping when arity matches and every element is convertible. For example, `(Number: 7, Text: "seven")` can map to `(long Id, string Label)` as `(7L, "seven")`; automatic tuple-to-object and object-to-tuple structural mapping remain unsupported.
- Added an installable Mammoth.LiteMapper agent skill with source-backed guidance for declarations, configuration, nullability, converters, collections, recursion, diagnostics, and package usage.
- Added Windows and Linux CI matrix jobs for Roslyn 4.8, 4.14, and 5.9, plus clean-package consumer coverage for `netstandard2.0`, .NET 8, .NET 9, and .NET 10 with C# 9, C# 14, and `latest`.

### Changed

- Non-nullable root source parameters no longer receive a runtime null guard by default. Enable `GuardNonNullSource` when runtime rejection is required.
- Converter and mapping selection now applies method-level options, local-before-registered precedence, exact and nullable source-pair rules, inherited accessibility, and explicit `Use` configuration consistently at root, member, collection-element, and dictionary boundaries.
- Unmarked handwritten local methods participate only when explicitly selected, marked with `[MappingConverter]` or `[DefaultMapping]`, or matched by the documented `Map{TargetMember}` convention. Registering an external mapper container explicitly opts its compatible methods into selection.
- Unknown-count enumerable sources are enumerated once when producing arrays. LiteMapper uses an O(n) temporary growing buffer before creating the final array; counted sources allocate the final array directly.
- Package consumer tests now map all `Mammoth.LiteMapper*` packages exclusively to the local package source, preventing another feed from masking a missing package.
- Diagnostic compatibility changed: `LITEMAPPER2005`, `LITEMAPPER2006`, and `LITEMAPPER2007` now represent ambiguous conversion, disabled explicit operators, and disabled narrowing numeric conversion. Unreachable diagnostics `LITEMAPPER4003`, `LITEMAPPER6002`, and `LITEMAPPER6003` were removed; unsupported custom collections continue to use `LITEMAPPER4006`, and invalid option values use `LITEMAPPER0008`.

### Fixed

- Fixed converter-result conversions, including checked and unchecked numeric conversions, user-defined operators, nullable results, and once-only null checks. For example, a converter returning `int?` can now map to `long` under the configured `Error` or `Throw` policy without evaluating the converter twice.
- Fixed cross-type nullability for boxed values, nullable enums, collection elements, configured source paths, and patch updates. For example, `From?` value `Ready = 1` maps by name to `To?` value `Ready = 9` while preserving null.
- Fixed inherited member and converter discovery across base classes and interfaces, including diamond inheritance, hiding, ambiguity, and normal overload binding. For example, `ISource : IBase` now maps an inherited `Value == 3` to `Target.Value == 3`.
- Fixed nested existing-target updates, including get-only identity preservation, writable replacement, local and registered updater selection, nullable path policies, and recursive tracker forwarding.
- Fixed constructor, record, `init`, `required`, readonly-member, optional-default, and value-type construction behavior, including recursive and nullable conversions used by constructor arguments.
- Fixed collection materialization and updates for arrays, list and read-only interfaces, sets, dictionaries, nullable elements, comparer preservation, collision detection, and configured nested paths.
- Fixed enum mapping for aliases, signed flags, named composites, declared numeric overflow, unknown values, and once-only configured-path evaluation.
- Fixed diagnostic severity handling, invalid-member configuration, unsupported generated types, async declaration detection on newer Roslyn hosts, generated identifier collisions, and mapper isolation so one invalid method does not suppress unrelated valid mappings.
- Fixed incremental output isolation and deterministic ordering across source-file insertion, removal, and unrelated declaration changes.
- Fixed package, trimming, Native AOT, sample, and benchmark process execution so stdout and stderr are drained concurrently and one bounded timeout covers child exit and retained pipes.

### Performance

- Eligible direct flat new-object mappings now request `MethodImplOptions.AggressiveInlining` when the compilation exposes the exact public framework capability. For example, a straight `Source.Value` to `Target.Value` copy receives the hint, while nullable, guarded, constructor-bound, collection, recursive, helper-based, and update mappings do not.
- On the recorded .NET 10 benchmark environment, paired measurements report 4.465 ns for LiteMapper and 4.307 ns for manual mapping, with 40-byte allocation for both. Disassembly shows the LiteMapper mapping body inlined into one 70-byte method, removing the previous separate mapper call.

### Security

- Updated the Source Link dependency to a patched release that resolves the affected `Microsoft.Build.Tasks.Git` dependency without suppressing NuGet audit diagnostics.

## 1.0.0

Initial LiteMapper 1.0 implementation completed against `SPECIFICATION.md`.

### Added

- Public `Mammoth.LiteMapper` abstraction API, including mapper/configuration attributes, option enums, and `LiteMapperCycleException`.
- Roslyn incremental source generator for compile-time C# object mapping with deterministic generated output.
- Static, instance, extension-method, new-object, and existing-target mapper method support.
- Flat, nested, collection, dictionary, enum, nullable, constructor, record, `init`, `required`, value-type, and recursive mapping support.
- Explicit member configuration, ignore/default controls, local and registered converter resolution, default mapping selection, and configuration precedence.
- Compile-time diagnostics for invalid declarations, unsupported mapping shapes, ambiguous mappings, invalid configuration, nullability, collections, existing-target mappings, recursion, enums, and internal generator failures.
- Package layout for `Mammoth.LiteMapper`, `Mammoth.LiteMapper.Abstractions`, and `Mammoth.LiteMapper.Generator`, with analyzer assets kept out of consumer runtime output.
- Package-consumer, trimming, Native AOT, API compatibility, deterministic package-content, generated-source, runtime, incremental, and capability validation.
- Compiling Basic, Collections, and ASP.NET Core samples.
- BenchmarkDotNet flat-object benchmark comparing manual mapping, LiteMapper, Mapperly, Mapster, and AutoMapper.
- Source-backed usage guide at `docs/USAGE.md`.

### Notes

- Normal consumers install `Mammoth.LiteMapper`; the generator is delivered as an analyzer asset.
- Supported generated paths use direct C# and no runtime reflection, runtime type scanning, dynamic dispatch, or runtime mapper registry.
- Release-quality benchmark interpretation remains separate from the dry-run benchmark smoke validation recorded during implementation.
