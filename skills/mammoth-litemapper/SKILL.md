---
name: mammoth-litemapper
description: Use Mammoth.LiteMapper in C# applications to declare compile-time object mappings, configure members and converters, map collections, update existing objects, and troubleshoot LITEMAPPER diagnostics. Use when adding or changing Mammoth.LiteMapper consumer code or migrating mappings to this library.
---

# Mammoth.LiteMapper

Generate ordinary C# mapping calls from bodyless partial methods. This skill targets consumer usage of Mammoth.LiteMapper 3.0.0, not development of the generator.

## Start with the consumer

Inspect the consumer's package version, target framework, language version, nullable context, source/destination models, and existing mapper conventions. Preserve the application's intended null, update, and conversion semantics. Ask when those semantics are unclear.

Use the primary `Mammoth.LiteMapper` NuGet package. One package reference supplies the abstractions and generator analyzer; do not add Roslyn runtime dependencies or install the generator package as the normal consumer route. Follow existing central package management when present. This skill documents release `3.0.0`; verify availability in the consumer's configured feed before changing versions.

```xml
<PackageReference Include="Mammoth.LiteMapper" Version="3.0.0" />
```

The documented support matrix is `netstandard2.0`, `net8.0`, `net9.0`, and `net10.0`, with C# 9+ and a Roslyn 4.8.0+ compiler host. Shipping assemblies target `netstandard2.0`. Newer model syntax such as `required` still needs the corresponding compiler capabilities.

## Declare and call a mapping

Adapt this complete example from the Basic sample's instance mapping:

```csharp
using Mammoth.LiteMapper;

public sealed class InstanceSource
{
    public int Id { get; set; }
}

public sealed class InstanceTarget
{
    public int Id { get; set; }
}

[LiteMapper]
public sealed partial class InstanceMapper
{
    public partial InstanceTarget Map(InstanceSource source);
}
```

Call `new InstanceMapper().Map(new InstanceSource { Id = 42 })`. Static mappers use a `static partial class` and `static partial` methods. Extension mapping adds `this` to the source parameter in a legal static extension-method container. Do not write generated method bodies.

- Mappers are non-abstract partial classes. Nested containers must also be partial and non-generic. Generic mapper classes and generic mapping methods are unsupported; closed generic model pairs are permitted.
- New-object methods take one by-value source parameter and return the destination. Use explicit `public`, `internal`, or `private` accessibility.
- Reference updates take source and destination and return `void` or the destination. Value-type updates require `ref` on the destination.
- Instance mappers can use their own dependencies and converters. External `[UseMapper(typeof(...))]` containers must be static; closed constructed generic containers are allowed, while unbound generic containers and generic methods are not. No library DI registration extension exists.
- Assembly-level `LiteMapperDefaults` values and `UseMapper` registrations are compile-time inputs. Changing one replans affected generated mappings and diagnostics during an incremental build; unrelated output remains cached where the host preserves candidate identity. Never describe `UseMapper` as runtime registration.

## Choose the relevant mapping rules

Read [references/mapping-rules.md](references/mapping-rules.md) for member configuration, construction, converter precedence and defaults, null policies, collections, patch updates, enums, cycles, or diagnostic troubleshooting. It includes source links and version-specific verification limits.

When upgrading from 1.x or 2.x, re-check any reliance on implicit root null guards, convention-only handwritten helpers, interface collection result types, tuple member names, former diagnostic IDs, or unmapped target members. In 3.0.0, root guards are opt-in through `GuardNonNullSource`, handwritten helpers need an explicit eligibility mechanism, equal-arity tuples map positionally, read-only sequence interfaces use array results, ordinary unmapped target members default to `LITEMAPPER1001` errors, and the specification's current diagnostic catalogue is canonical.

For simple matching, public instance properties and fields map by name. The default is exact matching followed by a unique case-insensitive match. Ordinary unmapped targets are errors; unmapped sources are ignored. Explicit `UnmappedTargetMembers` values (`Ignore`, `Info`, `Warning`, or `Error`) override that default, and standard compiler severity configuration can also relax configurable `LITEMAPPER1001`. That suppression or downgrade does not suppress valid generation and is not proof of complete mapping. Required, non-nullable, inaccessible, invalid, and otherwise mandatory targets still cause hard errors. `IgnoreTarget` and `UseTargetDefault` are explicit member-level opt-outs that retain their validation.

For a boundary that must account for source-model evolution, opt into strict source-member completeness with `UnmappedSourceMembers = UnmappedMemberPolicy.Error` on the mapper, `[MappingOptions]` method, or `[LiteMapperDefaults]` assembly scope. `LITEMAPPER1003` identifies each unused source member and remains configurable; `Warning` or `Info` can stage adoption, and `IgnoreSource` records an intentional exception. The default remains `Ignore` because source models often contain metadata or fields that do not belong in every consumer DTO. This source check does not relax target completeness or mandatory target diagnostics.

If mandatory unmapped-target coverage is required, configure `LITEMAPPER1001` as an error in CI and audit or avoid project-wide suppression. The library cannot prevent an intentional consumer `.editorconfig` override while this diagnostic remains configurable.

Do not invent runtime `IMapper`, `Map<TDestination>(object)`, registration/scanning, reflection fallback, async mapping, EF projections, hooks, object factories, mapping contexts, runtime polymorphism, or reference preservation. For behavior outside generated support, use ordinary handwritten C# with explicit boundaries. Do not add unsupported attributes borrowed from another mapper library.

## Verify consumer behavior

Build the affected consumer project and inspect the actual `LITEMAPPER` diagnostic before changing configuration. Test the behavior relevant to the request: null preservation or throwing, destination identity, collection replacement/copying, converter results, and cycles where applicable. Use the consumer's existing test conventions. Do not suppress hard semantic errors or insert `default!` to fabricate a valid mapping.

Source-generated implementations are private build artifacts apart from the methods the consumer declared. Nested and collection helper names are private, allocated within each generated mapper, and deterministic. Handwritten members and other generated helper signatures reserve names; identical closed source/destination identities reuse one name, while distinct identities are disambiguated even if preferred shape-plus-hash names collide. Allocation is independent of syntax-tree or equivalent compilation order. Do not edit generated files or assume extra public collection overloads exist, and do not depend on helper names or signatures. Do not infer deep copies of identical non-collection reference types; absent an explicit mapping, the same reference is assigned.

For version-specific behavior, consult the matching upstream revision. For 3.0.0, `SPECIFICATION.md` is the product contract, `docs/USAGE.md` is the consumer guide, and the public API, compiling samples, and tests establish demonstrated usage. Report discrepancies rather than silently treating documentation or implementation drift as a new contract.
