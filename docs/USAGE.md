# Mammoth.LiteMapper Usage

This guide is assembled from compiling sample projects and from the 1.0 implementation tests. The referenced samples are the executable smoke examples; feature-specific snippets use the same public API and syntax covered by the test suite.

Validated samples:

- `samples/Mammoth.LiteMapper.Samples.Basic/Program.cs`
- `samples/Mammoth.LiteMapper.Samples.Collections/Program.cs`
- `samples/Mammoth.LiteMapper.Samples.AspNetCore/Program.cs`

## Installation

Normal consumers reference the primary package only:

```xml
<PackageReference Include="Mammoth.LiteMapper" Version="1.0.0" />
```

`Mammoth.LiteMapper.Abstractions` is pulled in as a dependency and contains the public attributes, enums, and `LiteMapperCycleException`. The generator is included as an analyzer asset and is not a runtime dependency.

## Mapper declarations

A generated mapper is a partial method inside a class marked with `[LiteMapper]`.

From `samples/Mammoth.LiteMapper.Samples.Basic/Program.cs`:

```csharp
[LiteMapper]
public static partial class StaticMapper
{
    public static partial StaticTarget Map(StaticSource source);
}
```

Static and instance mapper classes are supported:

```csharp
[LiteMapper]
public sealed partial class InstanceMapper
{
    public partial InstanceTarget Map(InstanceSource source);
}
```

Supported generated method shapes include new-object mappings, extension-method mappings on static mapper classes, and existing-target update mappings. Generated methods must be bodyless partial methods. Handwritten methods can live beside generated methods.

Mapper classes can be top-level or nested; nested mapper classes require every containing type to be partial. Generic mapper classes and generic containing types are not generated in 1.0.

Instance mappers may use fields, properties, constructor-injected dependencies, and instance converter methods. Static mapper classes may use static converters only.

Ordinary inherited handwritten methods may participate when normal C# accessibility and resolution permit them, but mapper configuration is not inherited implicitly.

## Public API quick reference

Public types live in the `Mammoth.LiteMapper` namespace:

- `LiteMapperAttribute`: marks mapper classes and holds mapper-level options.
- `MappingOptionsAttribute`: overrides supported options on one mapping method.
- `LiteMapperDefaultsAttribute`: assembly-level defaults for stable cross-cutting options.
- `UseMapperAttribute`: registers external static mapper or converter-container types.
- `DefaultMappingAttribute`: marks one visible default mapping for a source/destination pair.
- `MappingConverterAttribute`: marks a converter method.
- `MappingConstructorAttribute`: marks the constructor LiteMapper should select.
- `MapPropertyAttribute`: configures source path, target member, and converter selection.
- `IgnoreTargetAttribute`, `IgnoreSourceAttribute`, `UseTargetDefaultAttribute`: member-level mapping controls.
- `LiteMapperCycleException`: thrown by generated cycle tracking.
- Enums: `NameMatching`, `UnmappedMemberPolicy`, `NullableMismatchPolicy`, `NullCollectionStrategy`, `NumericConversion`, `EnumMappingStrategy`, `EnumNumericConversion`, `UnmatchedEnumValuePolicy`, `ReferenceHandling`, and `OptionState`.

## Configuration scope and defaults

Configuration resolves from the most specific scope to the least specific scope:

1. `[MappingOptions]` on the mapping method.
2. `[LiteMapper]` on the mapper class.
3. `[assembly: LiteMapperDefaults(...)]` for assembly-level defaults.
4. Library defaults.

```csharp
[assembly: LiteMapperDefaults(
    NameMatching = NameMatching.ExactThenIgnoreCase,
    UnmappedTargetMembers = UnmappedMemberPolicy.Warning,
    NullableMismatch = NullableMismatchPolicy.Error)]

[LiteMapper(AllowExplicitOperators = true)]
public static partial class CustomerMapper
{
    [MappingOptions(
        UnmappedTargetMembers = UnmappedMemberPolicy.Error,
        AllowExplicitOperators = OptionState.Enabled,
        GuardNonNullSource = OptionState.Enabled,
        IgnoreNullSourceMembers = OptionState.Disabled)]
    public static partial CustomerDto Map(Customer source);
}
```

Library defaults are exact name matching, unmapped members ignored, nullable mismatches reported as errors, null collections reported as errors except where nullable target collections can preserve null, implicit numeric conversion only, explicit operators disabled, enum mapping by name, unmatched enum values reported as errors, no cycle tracker, non-null root source guards disabled, and patch null skipping disabled.

## Member matching and unmapped members

By default, LiteMapper maps readable source members to writable or constructible target members by name. Configure name matching and unmapped-member handling on the mapper or on a method:

```csharp
[LiteMapper(
    NameMatching = NameMatching.ExactThenIgnoreCase,
    UnmappedTargetMembers = UnmappedMemberPolicy.Error,
    UnmappedSourceMembers = UnmappedMemberPolicy.Warning)]
public static partial class CustomerMapper
{
    public static partial CustomerDto Map(Customer source);
}
```

Useful options:

- `NameMatching.Exact`: ordinal exact member names only.
- `NameMatching.ExactThenIgnoreCase`: exact first, then unique ignore-case match.
- `NameMatching.IgnoreCase`: unique ignore-case match.
- `UnmappedMemberPolicy.Ignore`, `Info`, `Warning`, `Error`: controls diagnostics for unmapped source or target members.

Member candidates are public instance properties and fields. Hidden members are resolved to the most-derived usable member, with property-over-field preference when needed.

Source paths in `[MapProperty]` may use dotted member paths such as `"Email.Value"`. Source paths must resolve to members and cannot use method-call syntax. Source methods are not discovered automatically; use a converter or root-source `MapProperty.Use` method when a method-derived value is needed.

Target paths are direct members only. Dotted target paths are not supported. Duplicate configuration for the same target or ambiguous configuration reports diagnostics.

## Mapping method signatures

New-object mappings return a destination:

```csharp
public static partial CustomerDto Map(Customer source);
```

Static mapper classes can declare extension methods:

```csharp
public static partial CustomerDto ToDto(this Customer source);
```

Existing-target mappings update a destination by parameter:

```csharp
public static partial void Update(Customer source, CustomerDto destination);
public static partial CustomerDto UpdateAndReturn(Customer source, CustomerDto destination);
public static partial void UpdateCounter(Counter source, ref CounterDto destination);
```

Unsupported generated method forms include async methods, generic mapping methods, generic mapper classes, generic containing types, ambiguous bodyless partial signatures, unsupported accessibility, and open generic mapping declarations.

## Construction, records, init, required, and value types

LiteMapper can construct classes, structs, records, and record structs using accessible constructors and object initializers. It supports `init` and `required` members when the target construction path satisfies them.

Use `[MappingConstructor]` when one constructor should be selected explicitly:

```csharp
public sealed class CustomerDto
{
    [MappingConstructor]
    public CustomerDto(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public int Id { get; }
    public string Name { get; }
}
```

Use `[UseTargetDefault]` when an optional constructor parameter or initializer member should keep its declared target default:

```csharp
[LiteMapper]
public static partial class CustomerMapper
{
    [UseTargetDefault(nameof(CustomerDto.DisplayName))]
    public static partial CustomerDto Map(Customer source);
}
```

## Explicit member configuration

Use `[MapProperty]` for renames, dotted source paths, and explicit conversion methods:

```csharp
[LiteMapper]
public static partial class CustomerMapper
{
    [MapProperty(Source = "Email.Value", Target = nameof(CustomerDto.Email))]
    [MapProperty(Target = nameof(CustomerDto.DisplayName), Use = nameof(MapDisplayName))]
    public static partial CustomerDto Map(Customer source);

    private static string MapDisplayName(Customer source) =>
        source.FirstName + " " + source.LastName;
}
```

Use `[IgnoreTarget]` and `[IgnoreSource]` to suppress specific members while still validating the configured names:

```csharp
[LiteMapper]
public static partial class CustomerMapper
{
    [IgnoreTarget(nameof(CustomerDto.DisplayName))]
    [IgnoreSource(nameof(Customer.LegacyCode))]
    public static partial CustomerDto Map(Customer source);
}
```

## Custom converters and extension patterns

Use `[MappingConverter]`, `Map{TargetMember}`, or explicit `MapProperty.Use` methods for custom values and conversions:

```csharp
[LiteMapper]
public static partial class CustomerMapper
{
    public static partial CustomerDto Map(Customer source);

    private static string MapDisplayName(Customer source) =>
        source.FirstName + " " + source.LastName;

    [MappingConverter]
    private static EmailDto ConvertEmail(EmailAddress source) =>
        new EmailDto(source.Value);
}
```

Use `[DefaultMapping]` when a visible handwritten or generated method should be the default mapping for a source/destination pair:

```csharp
[DefaultMapping]
public static CustomerDto ToCustomerDto(Customer source) =>
    new CustomerDto { Id = source.Id };
```

Use `[UseMapper]` on a mapper class or assembly to register an external static mapper or converter container:

```csharp
[UseMapper(typeof(SharedConverters))]
[LiteMapper]
public static partial class CustomerMapper
{
    public static partial CustomerDto Map(Customer source);
}
```

For post-processing, wrap a generated core mapping in a handwritten method:

```csharp
[LiteMapper]
public static partial class CustomerMapper
{
    [IgnoreTarget(nameof(CustomerDto.DisplayName))]
    private static partial CustomerDto MapCore(Customer source);

    public static CustomerDto Map(Customer source)
    {
        var dto = MapCore(source);
        dto.DisplayName = source.FirstName + " " + source.LastName;
        return dto;
    }
}
```

This is the supported post-processing wrapper pattern. No after-map hook is provided in 1.0.

## Built-in, explicit, and numeric conversions

LiteMapper uses identity and implicit C# conversions automatically. Explicit operators are disabled by default and must be enabled deliberately:

```csharp
[LiteMapper(
    AllowExplicitOperators = true,
    NumericConversion = NumericConversion.Checked)]
public static partial class InvoiceMapper
{
    public static partial InvoiceDto Map(Invoice source);
}
```

`NumericConversion.ImplicitOnly` rejects narrowing numeric conversions. `NumericConversion.Checked` emits checked conversions. `NumericConversion.Unchecked` emits unchecked conversions. String parsing, formatting, `Parse`, `TryParse`, `ToString`, culture-dependent conversion, date/string conversion, and `Guid`/string conversion are not automatic; use a converter.

## Nullability and null collections

Null behavior is configured with `NullableMismatchPolicy` and `NullCollectionStrategy`:

```csharp
[LiteMapper(
    NullableMismatch = NullableMismatchPolicy.Throw,
    NullCollections = NullCollectionStrategy.Empty)]
public static partial class CustomerMapper
{
    public static partial CustomerDto Map(Customer source);
}
```

`NullableMismatchPolicy.Error` reports compile-time diagnostics for nullable-to-non-null mappings. `NullableMismatchPolicy.Throw` emits runtime checks for supported paths. `NullCollectionStrategy.Empty` maps null collections to empty target collections; `Preserve` preserves null when legal; `Error` reports unsupported null collection flows.

Root source null behavior follows the declared source and return nullability. A non-nullable source parameter rejects nullable input at compile time when visible to analysis. By default, LiteMapper does not emit a runtime guard only because a root source parameter is non-nullable. Enable `GuardNonNullSource` on the mapper or mapping method to emit an `ArgumentNullException` guard for that root source parameter.

## Nested object mapping

Nested object mappings are generated as private closed-type helper methods when LiteMapper can construct the nested target type:

```csharp
public sealed class OrderSource
{
    public CustomerSource Customer { get; set; } = new CustomerSource();
}

public sealed class OrderDto
{
    public CustomerDto Customer { get; set; } = new CustomerDto();
}
```

Visible handwritten or generated mapping methods for the same nested pair are reused before structural helper generation. Abstract/interface destinations and `object` runtime dispatch require an explicit converter or handwritten mapping.

Nested helpers are closed over the concrete source/destination type pair. Closed generic model types can be mapped when the containing mapper and mapping method are not generic. Nullable nested objects follow the same nullability policy as other members.

For existing-target nested objects, LiteMapper replaces writable nested targets by default. Mutation of an existing nested object requires an explicit compatible existing-target nested mapping method.

## Supported collections

From `samples/Mammoth.LiteMapper.Samples.Collections/Program.cs`:

```csharp
[LiteMapper]
public static partial class CollectionMapper
{
    public static partial OrderTarget Map(OrderSource source);
}
```

Supported collection mapping includes:

- arrays and jagged arrays;
- `IEnumerable<T>`, `IReadOnlyCollection<T>`, `IReadOnlyList<T>`, `ICollection<T>`, `IList<T>`, `List<T>`;
- sets including `HashSet<T>` and set interfaces;
- dictionaries including `Dictionary<TKey, TValue>` and dictionary interfaces;
- nested collections and collections of nested objects;
- dictionary key and value conversion.

LiteMapper enumerates arbitrary enumerable sources once. Capacity is preallocated only when cheap count or length information is available. Comparers are preserved for compatible set and dictionary shapes. Unsupported shapes include custom collections, immutable collections, queues, stacks, and rectangular multidimensional arrays.

Public collection mappings are allowed when the declared mapping method itself maps one supported collection shape to another supported collection shape:

```csharp
public static partial List<CustomerDto> MapCustomers(Customer[] source);
```

Interface target defaults use ordinary mutable implementations, such as `List<T>` for list/sequence interfaces, `HashSet<T>` for set interfaces, and `Dictionary<TKey, TValue>` for dictionary interfaces. Ordering is preserved for sequence mappings. Sets and dictionaries use normal destination semantics. Collection mappings produce a mutable copy; existing-target mappings replace collection members rather than mutating them in place.

## Existing-target and patch mapping

Existing-target mapping updates an existing destination object:

```csharp
[LiteMapper]
public static partial class CustomerMapper
{
    public static partial void Update(Customer source, CustomerDto destination);

    public static partial CustomerDto UpdateAndReturn(Customer source, CustomerDto destination);
}
```

Patch-style null skipping is enabled with `IgnoreNullSourceMembers`:

```csharp
[LiteMapper(IgnoreNullSourceMembers = true)]
public static partial class CustomerPatchMapper
{
    public static partial void Apply(CustomerPatch source, CustomerDto destination);
}
```

Existing-target mappings replace collection members rather than applying partial collection updates. `init` members are not assigned during updates.

Null destination parameters throw at runtime. Destination-returning update methods return the same destination instance after mutation. Struct destination updates use `ref` destination parameters.

## ASP.NET Core

From `samples/Mammoth.LiteMapper.Samples.AspNetCore/Program.cs`:

```csharp
[LiteMapper]
public static partial class UserMapper
{
    public static partial UserDto Map(User source);
}
```

The ASP.NET Core sample maps a domain object to a DTO before writing JSON from a minimal API endpoint. No dependency-injection registration extension is required for this static mapper shape.

## Enum mapping

Enums can be mapped by name or by value:

```csharp
[LiteMapper(
    EnumMapping = EnumMappingStrategy.ByName,
    UnmatchedEnumValues = UnmatchedEnumValuePolicy.Throw)]
public static partial class StatusMapper
{
    public static partial TargetStatus Map(SourceStatus source);
}
```

`EnumMappingStrategy.ByName` emits deterministic name-based mapping. `EnumMappingStrategy.ByValue` uses numeric conversion controlled by `EnumNumericConversion.Checked` or `Unchecked`; for example, set `EnumNumericConversion = EnumNumericConversion.Checked` to reject numeric overflow. `[Flags]` enum composites are supported for valid atomic flag mappings. Use custom converters when enum semantics are domain-specific.

## Recursive mappings and cycle detection

From `samples/Mammoth.LiteMapper.Samples.Basic/Program.cs`:

```csharp
[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class CycleMapper
{
    public static partial NodeTarget Map(NodeSource source);
}
```

`ReferenceHandling.ThrowOnCycle` enables generated cycle tracking for recursive type graphs. A detected cycle throws `LiteMapperCycleException`. Non-recursive mappings do not allocate cycle-tracker state.

`LiteMapperCycleException` exposes the mapping method, `SourcePath`, and `DestinationPath` for the detected active-path cycle.

## Unsupported and special types

Interfaces and abstract classes are not automatically constructed as destinations. Use a handwritten mapping or converter that returns a concrete type.

`object` to a concrete destination requires an explicitly selected converter; LiteMapper does not generate runtime type dispatch. `dynamic` mapping is not generated. Ref-like types such as `Span<T>` and `ReadOnlySpan<T>`, pointer types, and function-pointer types require handwritten code when legal C# allows it.

Tuples and `ValueTuple` shapes are not treated as structural object mappings. Use handwritten methods or converters for tuple-oriented APIs.

## Generated code, trimming, and Native AOT

Generated mappings use direct C# constructs: constructors, assignments, loops, casts, and ordinary method calls. Supported generated paths use zero runtime reflection, no runtime type scanning, no dynamic dispatch, no runtime code generation, and no runtime mapper registry.

The package layout keeps the generator and Roslyn assemblies out of consumer runtime output. Package-consumer validation covers trimming and Native AOT, including static mapping, instance mapping, nested collections, and cycle detection.

## Package and platform support

Shipping assemblies target `netstandard2.0`. Supported consumer projects are tested for `netstandard2.0`, `net8.0`, `net9.0`, and `net10.0`, with a C# 9 minimum language version. The generator uses the Roslyn 4.8.0 API baseline and is delivered as an analyzer asset.

The primary `Mammoth.LiteMapper` package is the normal install path. `Mammoth.LiteMapper.Generator` is the generator implementation package and is not the normal consumer install route.

## Analyzer options

The generator recognizes these build properties for diagnostics and development:

```text
LiteMapper_EmitDebugMetadata
LiteMapper_IncludeGeneratedSourceComments
LiteMapper_TreatInternalGeneratorErrorsAsExceptions
```

These options must not change mapping semantics.

## Diagnostics

LiteMapper reports compile-time diagnostics for unsupported declarations, invalid configuration, and unsupported mapping shapes. Diagnostic IDs are stable once introduced.

Diagnostics are either configurable severity diagnostics, such as unmapped-member policy diagnostics, or non-configurable hard semantic errors, such as invalid declarations, unsupported shapes, ambiguous mappings, invalid converters, and generator internal failures. Diagnostics are reported at the most specific source location available, such as the mapping method or attribute argument.

Implemented diagnostic ranges:

- `LITEMAPPER0001` through `LITEMAPPER0013`: mapper declaration and configuration diagnostics.
- `LITEMAPPER1001` through `LITEMAPPER1014`: construction and member mapping diagnostics.
- `LITEMAPPER2001` through `LITEMAPPER2012`: nullability, source-path, conversion, and nested mapping diagnostics.
- `LITEMAPPER3001` through `LITEMAPPER3005`: nested mapping diagnostics.
- `LITEMAPPER4001` through `LITEMAPPER4006`: collection diagnostics.
- `LITEMAPPER5001` through `LITEMAPPER5006`: existing-target mapping diagnostics.
- `LITEMAPPER6001` through `LITEMAPPER6003`: recursive mapping diagnostics.
- `LITEMAPPER7001` through `LITEMAPPER7005`: enum mapping diagnostics.
- `LITEMAPPER9001`: internal generator error diagnostic.

## Deferred features

The following are not LiteMapper 1.0 usage features:

- EF Core expression projections.
- Expression-tree converter inlining.
- Runtime polymorphic mapping.
- Runtime mapper registration or assembly scanning.
- Dependency-injection registration extensions.
- Object factories, hooks, or mapping context propagation.
- Open generic mappings or generic mapper containers.
- External instance mapper resolution.
- Automatic flattening or naming-strategy plugins.
- Custom collections, immutable collections, queues, stacks, and rectangular multidimensional arrays.
- Async mapping.
- Runtime logging, telemetry, or network behavior.

Deferred features are either absent from the public API or diagnosed when requested through generated mapping declarations.
