# Changelog

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
