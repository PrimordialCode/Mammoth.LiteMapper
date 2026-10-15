# Changelog

## Unreleased

## 4.0.1

### Breaking Changes

These are conditional source/runtime or diagnostic-compatibility changes for affected mapping shapes.

- **Accessible target setters (#55):** target properties whose accessors are accessible from generated mapper code, including internal `set` and `init` accessors in the same assembly, now participate where C# permits. Creation can use accessible `set` or `init`; updates can use accessible non-`init` setters. These members were previously skipped, so affected destination values and unmapped-target diagnostics can change. [#55](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/55)
- **Nested required diagnostics (#56):** a constructor-bound required member whose selected constructor lacks `SetsRequiredMembers` now reports the canonical non-configurable `LITEMAPPER1002` diagnostic instead of leaking compiler `CS9035` from generated code. The invalid mapping implementation is omitted. Update diagnostic assertions or tooling that inspect the ID. [#56](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/56)

### Changed

- Updated readme, usage guide and agent skill to reflect 4.0.x changes.
- Updated the specification's release framing and documented the historical diagnostic-ID reconciliation while retaining future diagnostic stability.

### Fixed

- Fixed automatic nested object helpers to honor `UnmappedTargetMembers`, including collection elements and existing-target replacements. Ordinary configurable diagnostics preserve valid implementations; mandatory targets remain hard errors.

## 4.0.0

### Breaking Changes

These are conditional source/runtime compatibility changes from `3.0.0`, even though public package API compatibility checks pass. They correct unintended behavior and affect consumers using the described shapes; they are not universal API removals.

- **Ambiguous name matching (#28):** `IgnoreCase` mappings that previously selected an exact member from multiple case-insensitive matches now fail with `LITEMAPPER1004`. Select the intended source with `MapProperty.Source`, or use `Exact`/`ExactThenIgnoreCase` when exact-name priority is intended.
- **Collection equality (#31, #51):** dictionary value-only conversion now preserves a compatible source key comparer; custom key or set-element conversion uses the destination default comparer. Lookups, duplicate-key exceptions, set membership, and element counts can change. Recheck equality-sensitive mappings and collision expectations after upgrading.
- **Per-entry policies (#21):** mappings sharing model pairs now consistently honor each entry's numeric, enum, null, and cycle policies. Previously masked nullable/null-collection errors can surface as `LITEMAPPER2001` or `LITEMAPPER2002`; checked/unchecked, by-name/by-value, and preserve/empty results can differ. Review each entry's intended policies and satisfy reported nullability obligations instead of relying on another entry's helper behavior.
- **Explicit root-source converters (#23):** omitted-`Source` configuration invokes the root converter even when no matching source member exists and unmapped targets are ignored; a former default value can become the converter result. Patch calls no longer depend on an unrelated same-name nullable member. Recheck converter outputs and patch null behavior; select a source member explicitly when member-based input is intended.
- **Patch getter and overload behavior (#29, #47):** guarded source getters are consumed once, and captured nullable values retain the selected nullable updater overload instead of binding a competing non-nullable overload. Side effects, selected updater results, and exceptions from a second getter read can change. Recheck stateful getters and overloaded updaters; do not rely on a second read or the previously incorrect overload.
- **Null structural elements (#26):** built-in structural collection mapping now preserves null for nullable element targets. For nonnullable element targets under `Throw`, it raises `InvalidOperationException` before constructing the target object or reading source members, rather than dereferencing null in the helper. Higher-precedence nullable converters still receive null. Recheck null handling, side effects, and exception expectations.
- **Enum exception metadata (#27):** nullable-root by-name enum failures now identify the declared input parameter instead of a generated capture name. Update assertions or error handling that inspect `ParamName` or related exception text.

### Performance

- Cycle tracking stores the first active source inline and creates reference-identity spill storage only for a second simultaneous source. Spill storage is retained for later siblings. This reduces measured depth-1 tracked allocations from 232 to 56 bytes per mapping on Windows x64; measured depths 2, 16 and 128 retain their previous allocations. Timing comparisons showed unstable controls and deeper slowdown signals, so this change does not establish an overall throughput improvement or release performance acceptance. Public cycle behavior and statically generated mapping remain unchanged.

### Fixed

- Issue #51: set mappings use the destination default comparer after custom element conversion, including same-type converters, preventing incorrect collapse of distinct mapped values. Identity-mapped sets retain compatible source comparers. [#51](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/51)
- Issue #48: configured enum paths preserve missing reference intermediates as null for nullable enum destinations, including constructor-bound and existing-target mappings. By-name errors, by-value conversion, single evaluation, patch skipping, and converter precedence are preserved. [#48](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/48)
- Issue #47: configured direct and dotted nullable-value patch captures retain their source type when invoking nested updaters, preserving the selected nullable overload when a competing non-nullable overload exists. [#47](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/47)
- Issue #32: recursive cycle paths retain compact immutable segments and format the full path only when a cycle is detected, removing quadratic path-prefix allocations while preserving exception metadata and first-edge allocation behavior. [#32](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/32)
- Issue #31: dictionary mappings preserve compatible key comparers independently of value conversion. Keys transformed by custom converters use the destination default comparer, even when key types match. [#31](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/31)
- Issue #30: set mappings probe exact accessible HashSet capacity constructors and use portable fallbacks when absent, including on netstandard2.0. Capacity-plus-comparer support is detected independently. [#30](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/30)
- Issue #29: patch-mode nested updaters consume the captured direct source value once, preserving null skips, overload selection, and sequential update behavior. [#29](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/29)
- Issue #28: IgnoreCase rejects multiple case-insensitive source matches even when one is exact; nested structural ambiguity retains LITEMAPPER1004. Exact and ExactThenIgnoreCase keep their existing exact-match priority. [#28](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/28)
- Issue #27: by-name enum exceptions use stable source names and paths instead of applying `nameof` to guarded expressions; nullable enum captures retain source metadata. [#27](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/27)
- Issue #26: structural collection mapping preserves nullable elements and applies Throw guards before object helpers; nullable nested collection helper signatures retain their annotations. [#26](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/26)
- Issue #25: cycle detection now forwards one context through acyclic object and collection bridges, retaining complete member paths without tracking the bridge objects. [#25](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/25)
- Issue #24: partial mappers with framework attributes or `UseMapper` on another part now generate once; genuine duplicate `LiteMapper` configuration still reports one fatal diagnostic. [#24](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/24)
- Issue #23: explicit omitted-`Source` converters receive the root object before automatic source-member matching in new-object, constructor-bound, and update mappings. Unrelated source names and patch getters no longer override that selection. [#23](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/23)
- Issue #22: generated nested and collection helpers preserve the calling mapper instance for stateful converters and mappings, while instance-free helpers remain static. Static entry methods no longer select instance-only mappings. [#22](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/22)
- Issue #21: nested and collection helpers retain each entry method's effective mapping policies and nullable shape. Equivalent recursive helpers remain shared while preserving the initiating entry's cycle metadata. [#21](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/21)
- Issue #20: release validation and each NuGet package push now stop immediately on a nonzero native exit, reporting the failed operation or package before later successes can conceal it. [#20](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/20)
- Issue #19: tag-selected manual workflow dry runs no longer enter the NuGet publish job. Automatic tag publishing requires a push event; manual branch publishing retains its explicit publish and dry-run gates. [#19](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/19)

## 3.0.0

### Breaking Changes

- Issue #1: ordinary unmapped target properties and fields now default to errors; explicit target policies and `IgnoreTarget`/`UseTargetDefault` opt-outs remain supported, while unmapped source members remain ignored by default. Unmapped-member diagnostics identify the exact source or target member. [#1](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/1)

### Changed

- Issue #7: documented opt-in strict source-member completeness across mapper, method, and assembly scopes, including staged `LITEMAPPER1003` severities, editorconfig overrides, intentional `IgnoreSource` exceptions, and the rationale for the default `Ignore` policy. [#7](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/7)
- Issue #6: existing-target updates now have documented non-transactional semantics with deterministic target-member evaluation and assignment order. Earlier assignments remain when a later mapping operation fails, while per-member captures prevent repeated source getter/path evaluation where guards and assignments both need the value. [#6](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/6)
- Issue #5: closed constructed static generic external registration containers are now supported; unbound generic registrations and generic methods remain rejected. Declaration, registration, constructor, accessibility, and diagnostic-contract coverage was expanded across the Roslyn host matrix. [#5](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/5)
- Issue #4: clarified that ordinary unmapped-target suppression or downgrade does not suppress valid generation or prove complete mapping; mandatory target obligations remain hard errors, and CI guidance recommends treating configurable `LITEMAPPER1001` as an error when full coverage is required. [#4](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/4)
- Issue #3: generated nested and collection helper names are allocated deterministically within each mapper, reserve handwritten/generated helper signatures, reuse identical closed identities, and disambiguate preferred-name collisions independently of syntax-tree or equivalent compilation order. [#3](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/3)
- Issue #2: assembly-wide `LiteMapperDefaultsAttribute` values and `UseMapperAttribute` registrations now participate explicitly in incremental mapper planning and emission invalidation. Same-driver regressions cover affected generated content and diagnostics while preserving unrelated cached output where Roslyn permits. [#2](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/2)

### Fixed

- Issue #11: existing-target mappings with `NullableMismatch.Throw` now evaluate and enforce nullable preconditions in deterministic per-member order, preserving earlier assignments when a later member fails. [#11](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/11)
- Issue #12: declared cycle tracking now identifies mappings by symbol identity instead of method name, preventing overload false positives, invalid tracker arguments, and unnecessary tracking state. [#12](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/12)
- Issue #13: direct nullable sources passed to nested updaters are evaluated once, preventing duplicate source getter or path evaluation. [#13](https://github.com/PrimordialCode/Mammoth.LiteMapper/issues/13)

## 2.0.0

### Breaking Changes

These changes affect consumers using the described converter, nullability, collection, or diagnostic behavior.

- **Converter selection and precedence:** unmarked local helpers are no longer selected solely because their signatures are compatible. Select them with `MapProperty.Use`, `[MappingConverter]`, `[DefaultMapping]`, or the documented `Map{TargetMember}` convention. Local mappings and their defaults are resolved before registered external converters, and method-level conversion options apply throughout the selected path; affected mappings can select a different method, value, diagnostic, or exception.
- **Null and collection behavior:** nullable source paths and converter results now follow the configured nullable and numeric policies consistently, including once-only evaluation. `IEnumerable<T>`, `IReadOnlyCollection<T>`, and `IReadOnlyList<T>` destinations materialize as arrays, and unknown-count enumerable-to-array mappings use a single enumeration plus temporary O(n) buffering. Recheck null outcomes, exception paths, runtime collection types, casts, mutation assumptions, and allocation expectations.
- **Diagnostic compatibility:** `LITEMAPPER2005`, `LITEMAPPER2006`, and `LITEMAPPER2007` changed from invalid member configuration, invalid converter, and ambiguous converter to ambiguous conversion, disabled explicit operator, and disabled narrowing numeric conversion. `LITEMAPPER4003`, `LITEMAPPER6002`, and `LITEMAPPER6003` were retired. Update severity rules, assertions, and diagnostic-based tooling. Invalid member configuration now uses `LITEMAPPER1006`, `LITEMAPPER1007`, `LITEMAPPER1008`, `LITEMAPPER1014`, or `LITEMAPPER1015`; invalid and ambiguous converters use `LITEMAPPER2009` and `LITEMAPPER2011`; unsupported custom collections use `LITEMAPPER4006`; invalid mapper and method options use `LITEMAPPER0008` and `LITEMAPPER0009`.

### Added

- Added positional tuple-to-tuple mapping when arity matches and every element is convertible. For example, `(Number: 7, Text: "seven")` can map to `(long Id, string Label)` as `(7L, "seven")`; automatic tuple-to-object and object-to-tuple structural mapping remain unsupported.
- Added an installable Mammoth.LiteMapper agent skill with source-backed guidance for declarations, configuration, nullability, converters, collections, recursion, diagnostics, and package usage.
- Added Windows and Linux CI matrix jobs for Roslyn 4.8, 4.14, and 5.9, plus clean-package consumer coverage for `netstandard2.0`, .NET 8, .NET 9, and .NET 10 with C# 9, C# 14, and `latest`.

### Changed

- Package consumer tests now map all `Mammoth.LiteMapper*` packages exclusively to the local package source, preventing another feed from masking a missing package.

### Notes

- Root-source guard behavior is unchanged from 1.0.0: `GuardNonNullSource` was already available on mapper and method options, and non-nullable root parameters did not receive a runtime null guard by default. The option remains an explicit opt-in for `ArgumentNullException` validation.

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
