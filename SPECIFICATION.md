# Mammoth.LiteMapper Technical Specification

**Document status:** Canonical implementation specification for Mammoth.LiteMapper 1.0  
**Specification version:** 1.0-draft.2  
**Product name:** Mammoth.LiteMapper  
**License:** MIT  
**Audience:** implementation agents, maintainers, reviewers, package authors, and contributors

> This file is the sole product specification until implementation stabilizes. A usage guide MUST NOT be treated as authoritative during initial development. Usage documentation MUST later be assembled from compiling sample projects.

---

## 1. Normative language

The terms **MUST**, **MUST NOT**, **REQUIRED**, **SHOULD**, **SHOULD NOT**, and **MAY** are normative.

- **MUST / MUST NOT** define mandatory behavior for LiteMapper 1.0.
- **SHOULD / SHOULD NOT** define strong recommendations that require a documented reason to violate.
- **MAY** defines an implementation choice that does not alter the public contract.

When this specification is ambiguous, contradictory, or incomplete, implementation MUST stop. The implementation agent MUST record the problem in `DECISIONS.md` and obtain an explicit decision before continuing any project work. It MUST NOT guess, silently select a behavior, or implement multiple competing semantics.

---

## 2. Product definition

LiteMapper is a convention-first, compile-time object mapping library for C#.

A consumer declares partial mapping methods. A Roslyn incremental source generator emits direct C# implementations using constructors, assignments, loops, casts, and ordinary method calls.

LiteMapper 1.0 is intentionally focused. It is not a runtime mapping container.

### 2.1 Naming and identifiers

The canonical product, solution, package, assembly, project, and root namespace prefix is `Mammoth.LiteMapper`.

The shorter name **LiteMapper** MAY be used in explanatory prose. It MUST NOT be used as the package ID, assembly name, project-file name, solution-file name, or root namespace.

The primary public mapper marker remains `[LiteMapper]`, implemented by `Mammoth.LiteMapper.LiteMapperAttribute`. Public attribute and type names defined in section 5 are not otherwise prefixed with `Mammoth`.

The diagnostic prefix remains `LITEMAPPER`.

### 2.2 Primary goals

LiteMapper 1.0 MUST provide:

- compile-time generated object mapping;
- static and instance mapper classes;
- extension-method mapping for static mapper classes;
- flat object mapping;
- nested object mapping;
- arrays, common sequence collections, sets, and dictionaries;
- records, constructors, `init` members, `required` members, structs, and record structs;
- explicit member renaming and ignoring;
- compile-time validated source paths;
- custom converters written as ordinary C# methods;
- mapping into existing objects;
- configurable null semantics;
- enum mapping;
- recursive type mapping with optional runtime cycle detection;
- deterministic generated source;
- strong compile-time diagnostics;
- zero runtime reflection in all generated mapping paths;
- trimming and Native AOT compatibility for all supported generated features;
- `netstandard2.0` consumer-library compatibility and first-class modern .NET consumer support.

### 2.3 Non-goals for 1.0

LiteMapper 1.0 MUST NOT provide:

- a universal runtime `IMapper` or `Map<TDestination>(object)` API;
- runtime mapper registration or assembly scanning;
- runtime reflection fallback;
- dynamic dispatch based on runtime destination types;
- asynchronous mapping or asynchronous converters;
- EF Core or LINQ provider projections;
- runtime polymorphic derived-type mapping;
- object factories;
- before-map or after-map hooks;
- a mapping-context parameter;
- dependency-injection integration or assembly-scanned registration;
- reference identity preservation;
- open generic mapping methods;
- generic mapper classes;
- mapping inside generic containing types;
- automatic PascalCase flattening;
- underscore or member-prefix normalization;
- dotted target paths;
- automatic source-method discovery;
- immutable-collection support;
- custom-collection construction conventions;
- queues or stacks;
- rectangular multidimensional arrays;
- `Span<T>`, `ReadOnlySpan<T>`, or automatic `ref struct` mapping;
- pointer or function-pointer structural mapping;
- `dynamic` mapping;
- a code-fix provider;
- runtime logging hooks;
- telemetry.

Unsupported features MUST produce a documented diagnostic when they are presented as generated partial mappings. Handwritten methods remain ordinary C# and MAY implement behavior outside LiteMapper's generated feature set.

---

## 3. Design principles

### 3.1 Compile-time first

Every generated mapping MUST be completely resolvable from the consumer compilation. Generated code MUST NOT inspect types through reflection at runtime.

### 3.2 Explicit public surface

LiteMapper MUST generate implementations only for user-declared eligible partial methods. It MUST NOT generate unexpected public collection overloads, universal dispatch APIs, or public nested helper methods.

### 3.3 Ordinary C# as the extension mechanism

Custom behavior MUST be expressed with ordinary methods whenever possible. Mapper classes MAY contain both generated partial methods and handwritten methods.

### 3.4 Deterministic behavior

Identical generator version, compilation inputs, parse options, analyzer options, and references MUST produce byte-for-byte identical generated source.

Generated output MUST NOT contain timestamps, random identifiers, absolute machine paths, user names, temporary directories, or non-deterministic member ordering.

### 3.5 Capability detection

The generator MUST detect capabilities from the consumer compilation rather than relying only on target-framework strings.

- C# language capabilities MUST be derived from `CSharpParseOptions.LanguageVersion`.
- Preprocessor symbols MAY be read from `CSharpParseOptions.PreprocessorSymbolNames`.
- Framework APIs MUST be detected using Roslyn symbols and exact member signatures.
- Target-framework metadata MAY guide optimization but MUST NOT be the sole proof that an API exists.

Target- or language-specific optimization MUST NOT change observable mapping semantics.

### 3.6 No semantic fallback

When LiteMapper cannot generate a correct mapping, it MUST report a diagnostic. It MUST NOT silently use reflection, dynamic invocation, `default!`, data filtering, partial collection updates, or throwing production stubs merely to make compilation succeed.

---

## 4. Package and repository structure

### 4.1 NuGet packages

LiteMapper MUST produce these packages:

#### `Mammoth.LiteMapper`

The primary supported installation package.

It MUST:

- reference `Mammoth.LiteMapper.Abstractions` as a normal dependency;
- include the generator assembly as an analyzer asset under `analyzers/dotnet/cs`, or provide an equivalent package layout that is proven by package-consumer tests;
- require only one consumer `PackageReference` for normal use;
- prevent generator and Roslyn assemblies from becoming runtime dependencies or being copied to application output.

#### `Mammoth.LiteMapper.Abstractions`

The optional advanced package containing public attributes, configuration enums, and `LiteMapperCycleException`.

It MUST target `netstandard2.0`.

It MUST NOT reference Roslyn packages or expose Roslyn types.

#### `Mammoth.LiteMapper.Generator`

The generator implementation package.

It MUST:

- target `netstandard2.0` unless a later measured optimization justifies additional analyzer assets;
- contain the analyzer assembly under `analyzers/dotnet/cs`;
- keep `Microsoft.CodeAnalysis` dependencies private;
- not be advertised as the normal installation route.

The minimum supported compiler host is Roslyn 4.8.0. The generator MUST compile against the Microsoft.CodeAnalysis 4.8.0 API baseline unless an approved specification change raises that minimum. APIs introduced after the baseline MUST NOT be called directly by the baseline generator asset.

### 4.2 Shipping target frameworks

All shipping assemblies MUST target `netstandard2.0` unless a benchmark and compatibility review explicitly approves an additional target-specific asset.

The implementation MUST NOT multi-target assemblies merely to use newer syntax or produce cosmetically modern binaries.

### 4.3 Supported consumer projects

LiteMapper 1.0 MUST test and support consumers targeting:

- `netstandard2.0`;
- `net8.0`;
- `net9.0`;
- `net10.0`.

A `netstandard2.0` consumer MUST use an SDK/compiler capable of C# 9 or newer.

Older .NET and .NET Framework consumers are not part of the supported test matrix, even when package compatibility happens to permit installation.

### 4.4 Language support

The minimum supported language version is C# 9.

The generator MUST detect the effective configured language version and emit compatible syntax. It SHOULD emit conservative C# 9-compatible code unless newer syntax provides a measured or necessary capability benefit.

The generator MUST NOT emit syntax unavailable in the configured consumer language version.

### 4.5 Conditional compilation

Conditional compilation MAY be used:

- internally when building intentionally different generator assets;
- in generated source when it is the safest way to support a multi-targeted consumer;
- to select optimized APIs guarded by symbols and verified symbol availability.

Direct capability-selected generation is preferred because a source generator runs separately for each target-framework compilation.

Conditional compilation MUST NOT create different mapping semantics across targets.

### 4.6 Repository layout

The repository MUST use this high-level layout:

```text
src/
  Mammoth.LiteMapper/
    Mammoth.LiteMapper.csproj
  Mammoth.LiteMapper.Abstractions/
    Mammoth.LiteMapper.Abstractions.csproj
  Mammoth.LiteMapper.Generator/
    Mammoth.LiteMapper.Generator.csproj

tests/
  Mammoth.LiteMapper.Generator.Tests/
    Mammoth.LiteMapper.Generator.Tests.csproj
  Mammoth.LiteMapper.Runtime.Tests/
    Mammoth.LiteMapper.Runtime.Tests.csproj
  Mammoth.LiteMapper.Packaging.Tests/
    Mammoth.LiteMapper.Packaging.Tests.csproj
  Mammoth.LiteMapper.IntegrationTests/
    Mammoth.LiteMapper.IntegrationTests.csproj

samples/
  Mammoth.LiteMapper.Samples.Basic/
    Mammoth.LiteMapper.Samples.Basic.csproj
  Mammoth.LiteMapper.Samples.Collections/
    Mammoth.LiteMapper.Samples.Collections.csproj
  Mammoth.LiteMapper.Samples.AspNetCore/
    Mammoth.LiteMapper.Samples.AspNetCore.csproj

benchmarks/
  Mammoth.LiteMapper.Benchmarks/
    Mammoth.LiteMapper.Benchmarks.csproj
```

The repository MUST contain both solution formats with these exact file names:

```text
Mammoth.LiteMapper.sln
Mammoth.LiteMapper.slnx
```

Every project file, assembly name, root namespace, and default package ID defined by this specification MUST use the `Mammoth.LiteMapper` prefix.

### 4.7 Repository control files

Implementation MUST create and maintain:

```text
AGENTS.md
IMPLEMENTATION_PLAN.md
DECISIONS.md
STATUS.md
```

`AGENTS.md` MUST state at least:

- this specification is authoritative;
- runtime reflection is forbidden;
- undocumented public APIs are forbidden;
- milestones are test-first;
- focused and full tests must run;
- diagnostics must not be suppressed merely to pass tests;
- semantic decisions must not be changed silently;
- generated output must remain deterministic;
- status and decisions must be updated after each milestone.

---

## 5. Public API

All public abstraction types MUST live directly in namespace `Mammoth.LiteMapper`.

The library MUST NOT provide a `[Mapper]` alias. The mapper attribute is `[LiteMapper]`.

All attribute classes MUST be sealed and use ordinary `set` accessors for named properties. Consumers MUST NOT derive custom attributes for generator interpretation.

### 5.1 Configuration enums

Every configurable enum MUST reserve `Unspecified = 0` for configuration inheritance.

```csharp
namespace Mammoth.LiteMapper;

public enum NameMatching
{
    Unspecified = 0,
    Exact = 1,
    ExactThenIgnoreCase = 2,
    IgnoreCase = 3
}

public enum UnmappedMemberPolicy
{
    Unspecified = 0,
    Ignore = 1,
    Info = 2,
    Warning = 3,
    Error = 4
}

public enum NullableMismatchPolicy
{
    Unspecified = 0,
    Error = 1,
    Throw = 2
}

public enum NullCollectionStrategy
{
    Unspecified = 0,
    Error = 1,
    Preserve = 2,
    Empty = 3
}

public enum NumericConversion
{
    Unspecified = 0,
    ImplicitOnly = 1,
    Checked = 2,
    Unchecked = 3
}

public enum EnumMappingStrategy
{
    Unspecified = 0,
    ByName = 1,
    ByValue = 2
}

public enum EnumNumericConversion
{
    Unspecified = 0,
    Checked = 1,
    Unchecked = 2
}

public enum UnmatchedEnumValuePolicy
{
    Unspecified = 0,
    Error = 1,
    Throw = 2,
    ByValue = 3
}

public enum ReferenceHandling
{
    Unspecified = 0,
    None = 1,
    ThrowOnCycle = 2
}

public enum OptionState
{
    Unspecified = 0,
    Disabled = 1,
    Enabled = 2
}
```

### 5.2 `LiteMapperAttribute`

```csharp
namespace Mammoth.LiteMapper;

[AttributeUsage(
    AttributeTargets.Class,
    AllowMultiple = false,
    Inherited = false)]
public sealed class LiteMapperAttribute : Attribute
{
    public NameMatching NameMatching { get; set; }

    public UnmappedMemberPolicy UnmappedTargetMembers { get; set; }

    public UnmappedMemberPolicy UnmappedSourceMembers { get; set; }

    public NullableMismatchPolicy NullableMismatch { get; set; }

    public NullCollectionStrategy NullCollections { get; set; }

    public NumericConversion NumericConversion { get; set; }

    public bool AllowExplicitOperators { get; set; }

    public EnumMappingStrategy EnumMapping { get; set; }

    public EnumNumericConversion EnumNumericConversion { get; set; }

    public UnmatchedEnumValuePolicy UnmatchedEnumValues { get; set; }

    public ReferenceHandling ReferenceHandling { get; set; }

    public bool GuardNonNullSource { get; set; }

    public bool IgnoreNullSourceMembers { get; set; }
}
```

Boolean properties on `LiteMapperAttribute` default to `false`. They do not participate in assembly-level inheritance because assembly defaults do not expose those options.

### 5.3 `MappingOptionsAttribute`

```csharp
namespace Mammoth.LiteMapper;

[AttributeUsage(
    AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = false)]
public sealed class MappingOptionsAttribute : Attribute
{
    public NameMatching NameMatching { get; set; }

    public UnmappedMemberPolicy UnmappedTargetMembers { get; set; }

    public UnmappedMemberPolicy UnmappedSourceMembers { get; set; }

    public NullableMismatchPolicy NullableMismatch { get; set; }

    public NullCollectionStrategy NullCollections { get; set; }

    public NumericConversion NumericConversion { get; set; }

    public OptionState AllowExplicitOperators { get; set; }

    public EnumMappingStrategy EnumMapping { get; set; }

    public EnumNumericConversion EnumNumericConversion { get; set; }

    public UnmatchedEnumValuePolicy UnmatchedEnumValues { get; set; }

    public ReferenceHandling ReferenceHandling { get; set; }

    public OptionState GuardNonNullSource { get; set; }

    public OptionState IgnoreNullSourceMembers { get; set; }
}
```

### 5.4 `LiteMapperDefaultsAttribute`

```csharp
namespace Mammoth.LiteMapper;

[AttributeUsage(
    AttributeTargets.Assembly,
    AllowMultiple = false,
    Inherited = false)]
public sealed class LiteMapperDefaultsAttribute : Attribute
{
    public NameMatching NameMatching { get; set; }

    public UnmappedMemberPolicy UnmappedTargetMembers { get; set; }

    public UnmappedMemberPolicy UnmappedSourceMembers { get; set; }

    public NullableMismatchPolicy NullableMismatch { get; set; }

    public NullCollectionStrategy NullCollections { get; set; }
}
```

Assembly defaults MUST be limited to stable cross-cutting behavior. Numeric conversion, explicit operators, enum mapping, reference handling, and patch behavior MUST NOT be assembly-level options in 1.0.

### 5.5 `UseMapperAttribute`

```csharp
namespace Mammoth.LiteMapper;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Assembly,
    AllowMultiple = true,
    Inherited = false)]
public sealed class UseMapperAttribute : Attribute
{
    public UseMapperAttribute(Type mapperType)
    {
        MapperType = mapperType;
    }

    public Type MapperType { get; }
}
```

A referenced type MAY contain generated mapping methods, handwritten mapping methods, `[MappingConverter]` methods, or any combination.

Externally registered mapper or converter-container types MUST be static in 1.0.

### 5.6 Mapping-selection attributes

```csharp
namespace Mammoth.LiteMapper;

[AttributeUsage(
    AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = false)]
public sealed class DefaultMappingAttribute : Attribute
{
}

[AttributeUsage(
    AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = false)]
public sealed class MappingConverterAttribute : Attribute
{
}

[AttributeUsage(
    AttributeTargets.Constructor,
    AllowMultiple = false,
    Inherited = false)]
public sealed class MappingConstructorAttribute : Attribute
{
}
```

`MappingConstructorAttribute` is the only LiteMapper attribute permitted on source or destination model declarations in 1.0.

### 5.7 `MapPropertyAttribute`

```csharp
namespace Mammoth.LiteMapper;

[AttributeUsage(
    AttributeTargets.Method,
    AllowMultiple = true,
    Inherited = false)]
public sealed class MapPropertyAttribute : Attribute
{
    public string? Source { get; set; }

    public string? Target { get; set; }

    public string? Use { get; set; }

    public Type? ConverterType { get; set; }
}
```

Rules:

- `Target` MUST be supplied and MUST identify one direct target member.
- `Target` MUST NOT contain a dot.
- `Source` MAY be omitted only when `Use` identifies a converter that consumes the complete root source object.
- When `Source` is present, it MAY be a dotted source member path.
- `Source` MUST NOT contain method-call syntax.
- `Use` names a converter or mapping method.
- `ConverterType` is required when `Use` selects an external method not available through normal local or registered resolution.
- The generator MUST validate all strings to exact Roslyn symbols and report errors at the corresponding attribute argument when possible.

Example:

```csharp
[MapProperty(
    Source = "Email.Value",
    Target = nameof(CustomerDto.Email),
    Use = nameof(MapEmail))]
```

Root-source example:

```csharp
[MapProperty(
    Target = nameof(CustomerDto.FullName),
    Use = nameof(MapFullName))]
```

### 5.8 Ignore and target-default attributes

```csharp
namespace Mammoth.LiteMapper;

[AttributeUsage(
    AttributeTargets.Method,
    AllowMultiple = true,
    Inherited = false)]
public sealed class IgnoreTargetAttribute : Attribute
{
    public IgnoreTargetAttribute(string member)
    {
        Member = member;
    }

    public string Member { get; }
}

[AttributeUsage(
    AttributeTargets.Method,
    AllowMultiple = true,
    Inherited = false)]
public sealed class IgnoreSourceAttribute : Attribute
{
    public IgnoreSourceAttribute(string member)
    {
        Member = member;
    }

    public string Member { get; }
}

[AttributeUsage(
    AttributeTargets.Method,
    AllowMultiple = true,
    Inherited = false)]
public sealed class UseTargetDefaultAttribute : Attribute
{
    public UseTargetDefaultAttribute(string member)
    {
        Member = member;
    }

    public string Member { get; }
}
```

`IgnoreSource` excludes the named source member from strict source-member reporting. When effective `UnmappedSourceMembers` is `Ignore`, the attribute is still validated but has no additional runtime or generation effect.

`UseTargetDefault` MUST preserve a real declared target default, such as:

- a property or field initializer that remains active when the member is not assigned;
- an optional constructor parameter default used by the selected constructor.

When a selected constructor parameter corresponds to the named target member under the effective name-matching policy, `UseTargetDefault` instructs LiteMapper to omit that optional argument and use its declared default.

If no real target default exists, generation MUST fail. LiteMapper MUST NOT generate `default!`.

### 5.9 Cycle exception

```csharp
namespace Mammoth.LiteMapper;

public sealed class LiteMapperCycleException : InvalidOperationException
{
    public LiteMapperCycleException(
        Type sourceType,
        Type destinationType,
        string mappingMethod,
        string memberPath)
        : base(CreateMessage(
            sourceType,
            destinationType,
            mappingMethod,
            memberPath))
    {
        SourceType = sourceType;
        DestinationType = destinationType;
        MappingMethod = mappingMethod;
        MemberPath = memberPath;
    }

    public Type SourceType { get; }

    public Type DestinationType { get; }

    public string MappingMethod { get; }

    public string MemberPath { get; }

    private static string CreateMessage(
        Type sourceType,
        Type destinationType,
        string mappingMethod,
        string memberPath)
    {
        // Exact punctuation is not public API, but all values must be present.
        return $"A mapping cycle was detected while mapping " +
            $"'{sourceType}' to '{destinationType}' in " +
            $"'{mappingMethod}' at '{memberPath}'.";
    }
}
```

The public constructor signature, exposed properties, base class, and namespace are part of the public API. The exact punctuation of the generated message is not public API, but the message MUST contain all four values. The exception MUST NOT retain the source object or arbitrary object graph.

---

## 6. Configuration precedence and defaults

### 6.1 Precedence

Effective configuration MUST be resolved in this order:

1. method-level `MappingOptionsAttribute` value when not `Unspecified`;
2. mapper-level `LiteMapperAttribute` value when not `Unspecified`;
3. assembly-level `LiteMapperDefaultsAttribute` value when available and not `Unspecified`;
4. library default.

For method-level Boolean options:

- `Enabled` resolves to `true`;
- `Disabled` resolves to `false`;
- `Unspecified` falls through to mapper-level Boolean configuration.

### 6.2 Library defaults

The 1.0 library defaults MUST be:

| Option | Default |
|---|---|
| Name matching | `ExactThenIgnoreCase` |
| Ordinary writable nullable/value target member | `Warning` |
| Unmapped source member | `Ignore` |
| Nullable mismatch | `Error` |
| Null collection | contextual, as defined below |
| Numeric conversion | `ImplicitOnly` |
| Explicit operators | disabled |
| Enum mapping | `ByName` |
| Enum numeric conversion | `Checked` |
| Unmatched enum values | `Error` |
| Reference handling | `None` |
| Ignore null source members | disabled |

### 6.3 Contextual null-collection default

If no explicit null-collection strategy is configured:

- a nullable source collection to a nullable target collection MUST preserve null;
- a nullable source collection to a non-null target collection MUST produce a compile-time error;
- a non-null source collection MUST map normally.

Explicit strategies behave as follows:

- `Error`: nullable source collection is rejected unless null is already impossible by flow/type contract;
- `Preserve`: null is assigned only when the target type is nullable; otherwise generation fails;
- `Empty`: null produces an empty destination collection.

### 6.4 Target-member severity refinement

Target members MUST be evaluated independently rather than classifying an entire destination type as mutable or immutable.

Regardless of the ordinary target-member policy:

- an unsatisfied `required` member is an error;
- an unassigned non-nullable reference member with no constructor binding or approved target default is an error;
- a member that cannot be assigned after construction and is not constructor-bound or covered by `UseTargetDefault` is an error when leaving it uninitialized would violate its declared contract;
- ordinary writable value-type members, nullable reference members, and members with an explicitly approved target default follow the configured target-member policy.

### 6.5 Hard semantic errors

Some conditions remain errors regardless of configurable unmapped-member severity, including:

- unsatisfied C# `required` members;
- inaccessible construction;
- nullable-to-non-nullable assignment under `Error` policy;
- invalid mapping signatures;
- unsupported generated types;
- ambiguous constructor, converter, member, or mapping selection;
- invalid attribute configuration;
- missing target default requested through `UseTargetDefault`.

---

## 7. Mapper declarations

### 7.1 Supported mapper types

A mapper MUST be either:

- a static partial class; or
- a non-abstract instance partial class.

The mapper MAY be sealed.

The following mapper declarations are unsupported:

- interfaces;
- structs;
- records used as mapper containers;
- abstract classes;
- generic mapper classes;
- mappers nested inside generic containing types.

### 7.2 Nested mapper classes

A mapper MAY be nested in a non-generic containing type.

Every containing type in the nesting chain MUST be partial so generated source can reproduce the declaration chain.

LiteMapper MUST NOT relocate a nested mapper to an external helper type.

### 7.3 Inheritance

Instance mapper classes MAY use ordinary C# inheritance.

Generated mapping discovery MUST be scoped to the current mapper declaration and its explicitly registered static mapper/container types. LiteMapper MUST NOT implement inherited mapping-profile composition.

Ordinary inherited handwritten methods MAY be used when normal C# accessibility and resolution permit them, but their presence MUST NOT create implicit mapper configuration inheritance.

### 7.4 Instance state

Instance mappers MAY use fields, properties, constructor-injected dependencies, and instance converter methods.

LiteMapper provides no thread-safety guarantee for user-owned state. Static generated mappers MUST contain no mutable generated state and MUST be safe for concurrent use.

### 7.5 Handwritten coexistence

A mapper MAY contain:

- generated bodyless partial mapping methods;
- handwritten mapping methods;
- helper methods;
- converter methods;
- unrelated members.

The generator MUST only implement eligible bodyless partial methods. It MUST NOT rewrite or duplicate handwritten method bodies.

---

## 8. Mapping method signatures

### 8.1 New-object mapping

A new-object mapping method MUST:

- be a bodyless partial method;
- have exactly one source parameter;
- return a non-`void` destination type;
- be synchronous;
- use an unmodified by-value source parameter;
- use `public`, `internal`, or `private` accessibility;
- be static when declared in a static mapper;
- be an instance method or static method when declared in an instance mapper.

Examples:

```csharp
[LiteMapper]
public static partial class CustomerMapper
{
    public static partial CustomerDto ToDto(
        this Customer source);

    internal static partial CustomerSummary ToSummary(
        Customer source);

    private static partial AddressDto MapAddress(
        Address source);
}
```

Instance mapper:

```csharp
[LiteMapper]
public sealed partial class ProductMapper
{
    public partial ProductDto ToDto(Product source);
}
```

Extension methods MUST NOT be declared in instance mapper classes because C# requires extension methods to be static members of static classes.

### 8.2 Existing-target mapping

Supported reference-target signatures are:

```csharp
public static partial void ApplyTo(
    Source source,
    Destination destination);
```

```csharp
public static partial Destination ApplyTo(
    Source source,
    Destination destination);
```

For a non-null reference destination, the destination-returning form MUST return the same destination reference.

A nullable destination parameter is invalid for a `void` update because creation or replacement could not be observed.

A nullable reference destination MAY be accepted by a destination-returning update method when:

- the method returns a non-null destination type;
- LiteMapper can construct a new destination using the normal constructor algorithm;
- a null input destination causes creation and mapping of a new destination;
- a non-null input destination is updated and returned.

### 8.3 Struct update

Existing-target mapping into a value type MUST use `ref`:

```csharp
public static partial void ApplyTo(
    Source source,
    ref Destination destination);
```

A destination-returning value-type update without `ref` is not supported in 1.0. Implementation MUST NOT claim mutation of a struct passed by value.

### 8.4 Unsupported method forms

The generator MUST reject:

- `async` methods;
- `Task<T>` or `ValueTask<T>` mapping return conventions;
- source parameters using `in`, `ref`, or `out` for new-object mapping;
- arbitrary additional context parameters;
- generic mapping methods;
- open generic source or destination declarations;
- top-level array update methods;
- bodyless partial methods whose shape cannot be classified unambiguously.

### 8.5 Extension ambiguity

LiteMapper MAY allow multiple extension methods with the same name and source type in different mapper classes. Normal C# overload and namespace ambiguity rules apply. The generator MUST NOT attempt compilation-wide extension-method policing.

---

## 9. Member discovery and matching

### 9.1 Automatic member candidates

Automatic mapping MUST consider:

- public instance properties with accessible public getters on the source;
- public instance fields on the source;
- public instance properties with a construction-time or update-time legal assignment path on the target;
- public instance fields on the target when assignment is legal;
- accessible public inherited members.

Automatic mapping MUST exclude:

- static members;
- constants;
- events;
- indexers;
- compiler-generated backing fields;
- non-public members;
- source methods;
- destination methods.

### 9.2 Inheritance and hidden members

When inherited and declared members share a name:

1. the most-derived accessible member wins;
2. when a property and field are otherwise tied, the property wins;
3. LiteMapper MUST emit a warning that a hidden member was selected.

### 9.3 Name matching

`Exact`:

- uses ordinal case-sensitive equality only.

`ExactThenIgnoreCase`:

1. attempts exact ordinal equality;
2. when no exact match exists, attempts a unique ordinal case-insensitive match;
3. reports ambiguity when multiple candidates match case-insensitively.

`IgnoreCase`:

- performs unique ordinal case-insensitive matching without exact-match priority;
- reports ambiguity when multiple candidates match.

LiteMapper MUST NOT normalize underscores, prefixes, suffixes, acronyms, or PascalCase segments.

### 9.4 Source paths

`MapProperty.Source` MAY contain a dotted path such as:

```text
Address.Country.Code
```

Each path segment MUST resolve to a readable public property or field.

The generator MUST:

- validate every segment at compile time;
- report the exact failing segment;
- evaluate each path segment no more than once when repeated evaluation could occur;
- generate temporaries for nullable traversal or repeated use;
- preserve exceptions thrown by getters;
- apply nullability policy at each segment.

Source paths MUST NOT contain:

- method calls;
- indexer access;
- array indexing;
- conditional syntax;
- target references;
- arbitrary C# expressions.

### 9.5 Target paths

Target names MUST identify direct members. Dotted target paths are unsupported in 1.0.

### 9.6 Source methods

Source methods do not participate automatically and cannot appear in source path syntax.

A source method MAY be used inside a handwritten root-source converter:

```csharp
[MapProperty(
    Target = nameof(CustomerDto.DisplayName),
    Use = nameof(MapDisplayName))]
private static string MapDisplayName(Customer source) =>
    source.GetDisplayName();
```

### 9.7 Duplicate configuration

Multiple attributes or conventions assigning the same target member MUST produce a fatal diagnostic unless one is merely the automatically discovered candidate replaced by one explicit configuration.

Attribute order MUST NOT select a winner.

---

## 10. Object construction

### 10.1 Eligible constructors

A constructor is eligible when it is accessible from the generated mapper code under ordinary C# accessibility rules.

Record copy constructors MUST be excluded from automatic selection.

Private constructors MUST NOT be invoked unless ordinary C# accessibility from the generated mapper makes the call legal, such as a mapper nested within the same declaring type. Reflection is forbidden.

### 10.2 Constructor selection algorithm

For a new-object mapping, LiteMapper MUST apply this algorithm:

1. Find eligible constructors marked `[MappingConstructor]`.
2. If more than one eligible constructor is marked, report a fatal diagnostic.
3. If exactly one eligible constructor is marked, select it if every required parameter is satisfiable.
4. Otherwise collect eligible, fully satisfiable parameterized constructors.
5. If exactly one fully satisfiable parameterized constructor exists, select it.
6. If multiple exist, select the unique constructor with the greatest number of parameters.
7. If multiple constructors remain tied, report ambiguity.
8. If no parameterized constructor is selected, use an eligible parameterless constructor and legal member initialization.
9. If no construction path exists, report a fatal diagnostic.

An optional constructor parameter is satisfiable using its declared default when no source/configuration maps it.

### 10.3 Constructor parameter matching

Constructor parameters use the effective name-matching policy.

Exact matching is attempted first under `ExactThenIgnoreCase`; a unique case-insensitive fallback is permitted.

A parameter MAY be satisfied by:

- explicit member configuration;
- automatic source-member matching;
- a converter;
- an optional parameter default;
- another conversion supported by the normal conversion pipeline.

### 10.4 Constructor-bound members

A target member bound through the selected constructor MUST NOT be assigned again by default.

LiteMapper 1.0 has no declarative option to force duplicate assignment. A user requiring setter execution after construction MUST write the mapping manually.

### 10.5 Records, `init`, and required members

- Records and record structs MUST be supported.
- `init` properties MAY be assigned during new-object creation through an object initializer.
- `init` properties MUST NOT be assigned during existing-target updates.
- Every C# `required` member MUST be satisfied by the selected constructor according to language metadata or by generated object initialization.
- A constructor annotated with `SetsRequiredMembersAttribute` MUST be treated according to the C# language contract.
- LiteMapper MUST NOT assign `default!` to satisfy a required member.

### 10.6 Read-only members and fields

- A read-only target property or field MAY be populated only through a selected constructor.
- A `readonly` destination field MAY be constructor-bound.
- An unbound required read-only member is a fatal error.
- An ordinary unbound read-only member is reported according to target-member policy, but it MUST NOT be assigned illegally.

### 10.7 Target initializers

A target initializer is not automatically considered a deliberate mapping default.

To preserve it intentionally, the mapping method MUST declare:

```csharp
[UseTargetDefault(nameof(Target.Member))]
```

Without `UseTargetDefault`, the member is analyzed as any other target member.

---

## 11. Mapping resolution

### 11.1 Resolution precedence

For each target value, LiteMapper MUST resolve mapping in this exact order:

1. explicit `MapProperty.Use` selection;
2. explicit source path selection, completing immediately when directly assignable and otherwise continuing through the remaining conversion stages with the selected source value;
3. local method marked `[MappingConverter]`;
4. local `Map{TargetMember}` member converter;
5. explicit local mapping method;
6. explicitly registered external converter;
7. explicitly registered external mapping method;
8. mapping marked `[DefaultMapping]` for the source/destination pair;
9. identity or implicit C# conversion;
10. enabled explicit operator or numeric conversion policy;
11. enum conversion policy;
12. collection mapping;
13. generated private structural nested mapping;
14. diagnostic for unresolved or ambiguous mapping.

Explicit user intent MUST outrank language and structural convenience.

### 11.2 Mapping methods and defaults

Multiple mappings with the same source and destination types are allowed.

Exactly one MAY be marked `[DefaultMapping]` within a visible resolution scope.

When nested mapping requires a source/destination pair:

- use the unique default mapping when present;
- use a unique mapping when only one visible mapping exists;
- report ambiguity when multiple mappings exist and none is the unique default.

### 11.3 Converter discovery

LiteMapper MUST NOT select arbitrary compatible helper methods solely by signature.

Eligible custom converters are:

- explicitly selected methods;
- methods marked `[MappingConverter]`;
- methods named `Map{TargetMember}` for a specific target member;
- explicit mapping methods used according to the resolution hierarchy.

### 11.4 Converter signatures

A converter MUST:

- be synchronous;
- be non-generic;
- have exactly one source parameter;
- return a value assignable or validly convertible to the required target type;
- obey nullable-target rules.

Static mapper classes may use static converters only.

Instance mapper classes may use static or instance converters and MAY access their own fields and dependencies.

A converter selected for a normal source member receives that member value.

A converter selected by a `MapProperty` with omitted `Source` receives the complete root source object.

When `Use` names an overloaded method, LiteMapper MUST select a unique overload using the expected source value type and required target type. Identity parameter compatibility outranks implicit parameter compatibility. Equal candidates are ambiguous.

Exceptions thrown by converters MUST propagate unchanged. LiteMapper MUST NOT wrap them in a general mapping exception.

### 11.5 External registrations

Class-level `[UseMapper]` registrations take precedence over assembly-level registrations when both expose otherwise equal candidates.

Every external registered type MUST be static.

LiteMapper MUST validate that a referenced type exists and exposes usable methods. It MUST NOT instantiate an external mapper or resolve it from dependency injection.

### 11.6 Identical reference types

For a non-collection reference type mapping `T -> T`:

- use an explicit declared mapping when one resolves under the normal precedence;
- otherwise assign the same reference.

LiteMapper MUST NOT automatically deep-copy identical reference types.

Mutable collections are a deliberate exception and MUST be copied.

---

## 12. Language and built-in conversions

### 12.1 Identity and implicit conversions

LiteMapper MUST support normal C# identity and implicit conversions, including:

- identity conversion;
- implicit reference conversion;
- boxing where legal;
- implicit numeric conversions;
- nullable lifting where semantically valid;
- implicit user-defined operators selected by normal C# overload resolution.

### 12.2 Explicit operators

Explicit user-defined operators are disabled by default.

When enabled, LiteMapper MUST emit a normal explicit cast and use C# overload-resolution semantics.

If multiple explicit conversions are ambiguous, LiteMapper SHOULD report a diagnostic at the mapping member rather than relying only on a downstream compiler error.

### 12.3 Numeric conversion

`ImplicitOnly`:

- permits only identity and implicit numeric conversion;
- rejects narrowing conversions.

`Checked`:

- permits explicit numeric conversions in a checked context;
- overflow MUST throw the standard `OverflowException`.

`Unchecked`:

- permits explicit numeric conversions in an unchecked context.

String parsing, formatting, `Parse`, `TryParse`, `ToString`, culture-dependent conversion, date/string conversion, and `Guid`/string conversion are not automatic and require a custom converter.

### 12.4 Nullable mismatch

For a potentially null source value mapped to a non-null target:

`Error`:

- reports a compile-time error;
- omits the invalid implementation;
- never emits a null-forgiving operator merely to silence diagnostics.

`Throw`:

- emits a runtime null check;
- throws an appropriate standard exception, normally `ArgumentNullException` for a root source and `InvalidOperationException` for an invalid nested/member value unless a more specific normal C# exception is appropriate;
- MUST preserve the member path in the exception message.

Nullable reference type annotations that are oblivious because nullable context is disabled MUST not produce nullable mismatch diagnostics.

### 12.5 Root source null behavior

For a non-null source parameter, generated code MUST NOT emit a runtime null guard by default.

When the effective `GuardNonNullSource` option is enabled for a mapping method, generated code MUST reject a runtime null source value using `ArgumentNullException`.

For a nullable source parameter and nullable return type, null source MUST return null.

A nullable source parameter with a non-null return MUST follow effective nullable-mismatch policy.

An emitted root-source null check MAY use `ArgumentNullException.ThrowIfNull` only when that exact API exists in the consumer compilation; otherwise it MUST emit a portable explicit check.

---

## 13. Enum mapping

### 13.1 By-name mapping

By-name mapping MUST match declared enum member names exactly.

The effective `UnmatchedEnumValuePolicy` controls declared source members that have no target name:

- `Error`: generation fails when any required declared source member lacks a target name;
- `Throw`: generation succeeds and the generated switch throws `ArgumentOutOfRangeException` when an unmatched declared or unknown runtime value is encountered;
- `ByValue`: an unmatched declared name falls back to the enum numeric-conversion policy; overflow remains an error or runtime overflow according to that policy.

The library default is `Error`.

Even when every declared member is mapped, generated code MUST include a runtime default case that throws `ArgumentOutOfRangeException` for unknown numeric values not represented by a valid mapping.

### 13.2 By-value mapping

By-value mapping MUST convert the underlying numeric value according to `EnumNumericConversion`.

- `Checked` uses a checked underlying conversion and throws the standard `OverflowException` at runtime for an out-of-range runtime value.
- `Unchecked` uses an unchecked underlying conversion.

The library default is `Checked`.

Enum numeric conversion is independent from general `NumericConversion` configuration.

### 13.3 Zero values

For by-name mapping, a declared zero-valued source member MUST have a matching target member.

LiteMapper MUST NOT assume all zero values map to target numeric zero when name mapping was requested.

### 13.4 Flags enums

For `[Flags]` enums mapped by name:

- every atomic non-zero source flag MUST have a matching target flag;
- declared composite aliases need not have matching names when their atomic components all map consistently;
- zero is handled by the zero-member rule;
- runtime values MUST be decomposed and reconstructed without losing unmatched bits;
- values containing unknown bits MUST throw.

### 13.5 Aliases

Source aliases sharing one numeric value are allowed only when every alias resolves to target members with the same target numeric value.

Target aliases are allowed when name mapping selects one unique target member name, even if multiple target names share the same numeric value.

---

## 14. Nested object mapping

### 14.1 Automatic nested helpers

When source and target member types require structural object mapping and no higher-precedence mapping exists, LiteMapper MUST generate a private nested helper.

Automatic helpers MUST:

- remain private implementation details;
- use exact closed source and destination types;
- never introduce generic helper methods or open generic templates;
- reuse an explicit or default mapping when one is visible;
- participate in recursive type analysis;
- receive and forward cycle-tracking state only when required.

### 14.2 Closed generic models

A closed constructed type such as:

```text
PagedResult<Customer> -> PagedResultDto<CustomerDto>
```

is treated as an ordinary concrete type pair.

LiteMapper MUST NOT infer or cache a reusable open generic mapping template.

### 14.3 Nullable nested objects

Nested null behavior follows the same source/target nullability policy as scalar members.

For nullable target members, a null nested source maps to null.

For non-null targets, the default policy is a compile-time error unless the user chooses `Throw` or supplies a converter/fallback through ordinary code.

### 14.4 Existing-target nested objects

For a writable nested target property during update:

- replacement with a newly mapped nested object is the default;
- mutation of the existing nested object is permitted only when an explicit compatible existing-target nested mapping method is declared and selected.

For a get-only nested target object:

- automatic update is permitted only when an explicit compatible existing-target nested mapping method is declared;
- otherwise generation fails.

LiteMapper MUST NOT infer nested mutation from the mere presence of a non-null object.

---

## 15. Collection and dictionary mapping

### 15.1 Mandatory collection shapes

LiteMapper 1.0 MUST support automatic element mapping for:

- one-dimensional arrays;
- jagged arrays;
- `IEnumerable<T>`;
- `ICollection<T>`;
- `IList<T>`;
- `IReadOnlyCollection<T>`;
- `IReadOnlyList<T>`;
- `List<T>`;
- `ISet<T>`;
- `HashSet<T>`;
- `IReadOnlySet<T>` when that symbol exists in the consumer compilation;
- `Dictionary<TKey, TValue>`;
- `IDictionary<TKey, TValue>`;
- `IReadOnlyDictionary<TKey, TValue>`.

LiteMapper 1.0 MUST reject automatic mapping for:

- rectangular arrays such as `T[,]`;
- queues and stacks;
- immutable collections;
- arbitrary custom collection types;
- `IAsyncEnumerable<T>`;
- `Span<T>` and `ReadOnlySpan<T>`;
- collection builders requiring runtime discovery.

### 15.2 Public collection mappings

A public top-level collection mapping method MUST be explicitly declared.

LiteMapper MUST NOT automatically generate public `ToList`, `ToArray`, or pluralized extension methods for every element mapping.

Private collection helpers required by nested member mapping MAY be generated automatically.

### 15.3 Interface target defaults

When a target is an interface, LiteMapper MUST use these defaults:

| Target | Concrete generated result |
|---|---|
| `IEnumerable<T>` | `T[]` |
| `IReadOnlyCollection<T>` | `T[]` |
| `IReadOnlyList<T>` | `T[]` |
| `ICollection<T>` | `List<T>` |
| `IList<T>` | `List<T>` |
| `ISet<T>` | `HashSet<T>` |
| `IReadOnlySet<T>` | `HashSet<T>` |
| `IDictionary<TKey,TValue>` | `Dictionary<TKey,TValue>` |
| `IReadOnlyDictionary<TKey,TValue>` | `Dictionary<TKey,TValue>` |

Read-only static interfaces do not guarantee immutable runtime implementations. LiteMapper MUST NOT add wrappers merely to simulate deep immutability.

### 15.4 Ordering

LiteMapper MUST preserve source enumeration order when the destination shape is ordered.

Sets and dictionaries follow their container semantics.

Dictionary insertion/enumeration order is preserved only to the extent provided by the selected runtime implementation. LiteMapper does not establish a stronger cross-runtime guarantee.

### 15.5 Enumeration count

A source enumerable MUST be enumerated exactly once unless it exposes indexed access or another direct path that avoids enumeration.

LiteMapper MUST NOT call `Count()` on an arbitrary enumerable merely to preallocate capacity.

### 15.6 Capacity allocation

Capacity SHOULD be preallocated when count is cheaply available from:

- arrays;
- `ICollection<T>`;
- `IReadOnlyCollection<T>`;
- known concrete collections;
- another symbol-proven constant-time count API.

### 15.7 Mutable copying

Mutable source collections MUST be copied even when source and target collection types and element types are identical.

LiteMapper MUST NOT alias a mutable collection into a destination object.

Known immutable/value-like collection types are outside 1.0 automatic support.

### 15.8 Elements

Every source element MUST produce exactly one target element for sequence and array mappings.

Null elements MUST NOT be dropped or replaced silently. Nullable-to-non-nullable element mapping follows the scalar nullable mismatch policy.

Jagged arrays are mapped recursively and preserve each nested array length.

### 15.9 Sets

Set mappings follow target comparer semantics. Duplicate mapped values MAY collapse naturally.

When source and destination element mapping is identity-compatible and the source comparer is type-compatible, LiteMapper SHOULD preserve the comparer.

Otherwise LiteMapper MUST use the destination's default comparer.

### 15.10 Dictionaries

Dictionary keys and values MUST independently use the complete conversion-resolution pipeline.

When key conversion is identity-compatible and the source comparer is type-compatible, LiteMapper SHOULD preserve the comparer. Otherwise it MUST use the destination's default comparer.

Entries MUST be inserted with normal `Add` semantics. If multiple source keys map to one equal target key, the destination dictionary MUST throw rather than silently use first-wins or last-wins behavior.

### 15.11 Null collections

Null source collections obey the effective null-collection strategy defined in section 6.

For update mappings with `IgnoreNullSourceMembers` enabled, a null collection source member skips the assignment and leaves the destination member unchanged, including when null is encountered through a configured source path.

### 15.12 Existing-target collection behavior

For a writable collection property during update:

- LiteMapper MUST create a new mapped collection and replace the target value.

LiteMapper MUST NOT automatically:

- clear and repopulate a get-only collection;
- merge by index;
- merge by key;
- retain existing elements;
- partially update an array.

A get-only collection target is unsupported for generated update mapping. A user requiring population semantics must implement that mapping manually.

Top-level array update methods are unsupported. A normal new-object collection mapping may return a replacement array.

---

## 16. Existing-target and patch mapping

### 16.1 Scalar assignments

Writable scalar members are assigned after conversion using the normal resolution pipeline.

`init` members, read-only fields, and inaccessible setters cannot be updated.

### 16.2 Null destination

For a non-null destination parameter, a runtime null value MUST throw `ArgumentNullException`.

For a nullable destination in a destination-returning method, LiteMapper MUST create a replacement destination when construction is possible and return it.

### 16.3 `IgnoreNullSourceMembers`

When enabled for an existing-target mapping:

- any null direct source member skips assignment;
- a null encountered at any segment in a configured source path skips assignment;
- the destination member remains unchanged;
- non-null values map normally;
- the behavior applies to scalars, nested objects, and collections.

It MUST NOT apply to new-object mappings. Configuring it on a new-object mapping SHOULD produce a diagnostic.

### 16.4 Return semantics

For reference targets, a destination-returning update MUST return the same input reference unless the input was nullable and null, in which case it returns the newly created destination.

For struct targets, the required mutation form is `ref`.

---

## 17. Recursive graphs and cycle detection

### 17.1 Recursive type support

Recursive type mappings are supported.

With `ReferenceHandling.None`:

- finite runtime trees map recursively;
- cyclic runtime object graphs may result in stack overflow;
- no tracker allocation is permitted.

The generator SHOULD emit an informational diagnostic when a recursive type graph uses `None`, making the runtime risk visible without rejecting valid finite trees.

### 17.2 `ThrowOnCycle`

With `ReferenceHandling.ThrowOnCycle`:

- the generator MUST detect recursive mapping components at compile time;
- only mapping paths within recursive strongly connected components require tracking;
- one tracking state MUST be created at the public mapping entry point for a recursive mapping call;
- the same tracker MUST be forwarded through nested helpers and collection element mappings;
- non-recursive public mappings MUST NOT allocate a tracker.

### 17.3 Active-path semantics

Cycle detection MUST use reference identity, not `Equals`.

The tracker MUST represent the active recursion path:

1. before recursively descending into a tracked source object, attempt to add that exact reference;
2. if the reference is already active, throw `LiteMapperCycleException`;
3. after the recursive mapping finishes, remove the reference in a `finally` block.

A source reference appearing in two separate non-overlapping branches is not a cycle. It MUST be mapped independently in each branch.

### 17.4 Value types

Only reference-type instances participate in cycle tracking. Value types are copied normally and MUST NOT be boxed merely for tracking.

### 17.5 Exception details

`LiteMapperCycleException` MUST expose:

- source type;
- destination type;
- public mapping method name;
- member path at which the cycle was detected.

The message SHOULD include all four values in a deterministic, readable format.

The exception MUST NOT retain the offending source instance.

### 17.6 Tracker implementation

The tracker MAY be:

- emitted as a private mapper-specific helper; or
- provided by an internal shared runtime helper.

The choice MUST be based on benchmarks and package constraints and MUST NOT alter public API, allocation guarantees, path semantics, or thread safety.

---

## 18. Unsupported and special types

### 18.1 Interfaces

An interface MAY be a source type. LiteMapper maps its accessible public properties and fields from the declared interface contract.

Default interface property declarations participate normally. Default interface methods do not become automatic source members.

An interface or abstract class MUST NOT be an automatically constructed destination. A handwritten mapping or explicit converter must return a concrete implementation.

### 18.2 `object`

A concrete type may map to `object` using normal implicit conversion.

`object` to a concrete destination requires an explicitly selected custom converter. LiteMapper MUST NOT generate runtime type dispatch.

### 18.3 `dynamic`

`dynamic` is unsupported for generated mapping.

### 18.4 Ref-like, pointer, and function-pointer types

Automatic structural mapping is unsupported for:

- `ref struct` types;
- `Span<T>`;
- `ReadOnlySpan<T>`;
- pointer types;
- function pointers.

A handwritten converter or handwritten mapping method MAY use these types when legal C# allows it. LiteMapper does not generate the unsafe or ref-like behavior.

### 18.5 Tuples

Tuple-to-tuple mapping is supported when the source and destination tuple arity is equal and each element at the same ordinal position can be converted under the normal pipeline. Tuple element names do not change positional mapping semantics.

Tuple-to-object and object-to-tuple structural mapping are unsupported in 1.0.

Anonymous types receive no explicit support.

---

## 19. Generated source requirements

### 19.1 Generator API

The generator MUST implement `IIncrementalGenerator` directly.

It MUST NOT implement the deprecated classic `ISourceGenerator` pipeline as its primary architecture.

The generator instance MUST be stateless. It MUST NOT store compilation state in instance fields.

### 19.2 Incremental isolation

The pipeline MUST be partitioned so changing one mapper regenerates only that mapper and any mappings whose relevant model/configuration inputs changed, where possible.

A change to assembly-wide defaults or assembly-wide external mapper registrations MAY invalidate all affected mappers.

Incrementality MUST be tested through observable tracked generator steps, not assumed from the interface name.

### 19.3 Discovery

Mapper discovery SHOULD use attribute-targeted syntax filtering, such as `SyntaxProvider.ForAttributeWithMetadataName`, or an equivalent precise incremental pipeline.

The pipeline MUST avoid scanning every syntax node semantically when a cheaper syntax predicate can eliminate it.

### 19.4 Capability model

The generator MUST build an immutable capability model from:

- effective C# language version;
- preprocessor symbols;
- required framework types and members;
- nullable context and annotations;
- analyzer/MSBuild options;
- relevant compilation references.

Changing language version, relevant symbols, or relevant analyzer options MUST invalidate affected generated output.

### 19.5 Source style

Generated source MUST contain:

```csharp
// <auto-generated/>
```

It MUST enable nullable analysis when supported and appropriate.

It MAY use targeted warning pragmas only for warnings generated by mechanically safe code. It MUST NOT globally suppress consumer warnings or suppress semantic problems.

Generated declarations SHOULD carry deterministic `GeneratedCodeAttribute` metadata containing the LiteMapper generator version.

Version metadata MUST NOT include timestamps or machine information.

### 19.6 User documentation

User-authored XML documentation on partial method declarations remains the documentation for the public method.

The generator MUST NOT hide user-declared public mapping methods from IntelliSense.

Generated private helpers remain private.

### 19.7 File layout

LiteMapper MUST emit one generated source file per mapper class.

Hint names MUST be based on the fully qualified metadata name plus a stable deterministic hash, for example:

```text
Mammoth.LiteMapper.MyNamespace.CustomerMapper.A1B2C3D4.g.cs
```

The hash algorithm and normalized input MUST be stable within a generator version.

### 19.8 Source ordering

Generated members, mappings, diagnostics, and helpers MUST be ordered deterministically by stable symbol identity rather than syntax-tree enumeration accidents.

### 19.9 Portable optimizations

Generated code MAY use newer framework APIs only when the exact symbols and signatures exist.

Examples include:

- `ArgumentNullException.ThrowIfNull`;
- collection constructors accepting capacity;
- target-specific collection helpers.

A portable fallback MUST exist for every required supported consumer target.

### 19.10 No runtime framework

Generated code MUST NOT depend on:

- runtime reflection;
- `dynamic`;
- expression compilation;
- runtime mapping registries;
- service locators;
- assembly scanning;
- emitted IL;
- `System.Linq` for hot collection loops when direct loops are clearer and more allocation-efficient.

---

## 20. Diagnostics

### 20.1 Diagnostic policy

Diagnostic IDs are public and stable immediately when introduced.

The prefix is `LITEMAPPER`.

Category ranges are:

| Range | Category |
|---|---|
| `LITEMAPPER0xxx` | mapper declarations, configuration, generator infrastructure |
| `LITEMAPPER1xxx` | members, paths, constructors, object creation |
| `LITEMAPPER2xxx` | nullability and conversions |
| `LITEMAPPER3xxx` | mapping and nested-resolution ambiguity |
| `LITEMAPPER4xxx` | collections and dictionaries |
| `LITEMAPPER5xxx` | existing-target mappings |
| `LITEMAPPER6xxx` | recursive graphs and cycle configuration |
| `LITEMAPPER7xxx` | enum mapping |
| `LITEMAPPER9xxx` | internal generator failures |

Standard `.editorconfig` severity overrides MUST work for configurable diagnostics.

Fatal semantic diagnostics MUST use `WellKnownDiagnosticTags.NotConfigurable` or equivalent behavior so suppressing them cannot create an invalid missing implementation contract.

Messages SHOULD use concise type names. Diagnostic properties SHOULD contain fully qualified metadata names. When concise names are ambiguous in the message, fully qualified names MUST be used.

Location selection priority is:

1. exact relevant attribute argument or model member;
2. mapping method;
3. mapper class.

### 20.2 Required diagnostic catalogue

The implementation MUST define and test at least the following stable diagnostics.

#### Declaration and generator diagnostics

| ID | Default severity | Name | Meaning |
|---|---:|---|---|
| `LITEMAPPER0001` | Error, non-configurable | MapperMustBePartial | Mapper or required containing type is not partial. |
| `LITEMAPPER0002` | Error, non-configurable | UnsupportedMapperType | Mapper is an interface, struct, record container, or another unsupported kind. |
| `LITEMAPPER0003` | Error, non-configurable | GenericMapperNotSupported | Mapper or containing type is generic. |
| `LITEMAPPER0004` | Error, non-configurable | AbstractMapperNotSupported | Instance mapper is abstract. |
| `LITEMAPPER0005` | Error, non-configurable | InvalidMappingMethodSignature | Partial method cannot be classified as a supported mapping. |
| `LITEMAPPER0006` | Error, non-configurable | AsyncMappingNotSupported | Mapping method or converter is asynchronous. |
| `LITEMAPPER0007` | Error, non-configurable | UnsupportedMappingAccessibility | Mapping accessibility is unsupported. |
| `LITEMAPPER0008` | Error, non-configurable | InvalidMapperConfiguration | Mapper-wide configuration contains an invalid value or combination. |
| `LITEMAPPER0009` | Error, non-configurable | InvalidMethodConfiguration | Method configuration contains an invalid value or combination. |
| `LITEMAPPER0010` | Error, non-configurable | InvalidUseMapperType | Registered external type is missing, invalid, or exposes no usable methods. |
| `LITEMAPPER0011` | Error, non-configurable | ExternalMapperMustBeStatic | External registered mapper/container is not static. |
| `LITEMAPPER0012` | Error, non-configurable | UnsupportedLanguageVersion | Consumer language version is older than C# 9. |
| `LITEMAPPER0013` | Error, non-configurable | DuplicateConfiguration | A non-repeatable configuration is declared more than once. |

#### Member and constructor diagnostics

| ID | Default severity | Name | Meaning |
|---|---:|---|---|
| `LITEMAPPER1001` | Warning, configurable | UnmappedTargetMember | Ordinary target member is not mapped. |
| `LITEMAPPER1002` | Error, non-configurable | RequiredTargetMemberNotMapped | Required or semantically mandatory target member is unsatisfied. |
| `LITEMAPPER1003` | Ignore, configurable | UnmappedSourceMember | Source member is unused while strict source checking is enabled. |
| `LITEMAPPER1004` | Error, non-configurable | AmbiguousMemberMatch | Multiple source members match one target member. |
| `LITEMAPPER1005` | Warning | HiddenMemberSelected | Most-derived hidden member was selected. |
| `LITEMAPPER1006` | Error, non-configurable | InvalidSourcePath | Source path or one segment cannot be resolved. |
| `LITEMAPPER1007` | Error, non-configurable | TargetPathNotSupported | Target contains a dotted path. |
| `LITEMAPPER1008` | Error, non-configurable | DuplicateTargetMapping | Multiple configurations assign the same target member. |
| `LITEMAPPER1009` | Error, non-configurable | TargetMemberNotWritable | Target member cannot be assigned in this mapping mode. |
| `LITEMAPPER1010` | Error, non-configurable | ConstructorNotFound | No legal target construction path exists. |
| `LITEMAPPER1011` | Error, non-configurable | AmbiguousConstructor | Constructor-selection algorithm has a tie. |
| `LITEMAPPER1012` | Error, non-configurable | MultipleMappingConstructors | More than one eligible constructor is marked. |
| `LITEMAPPER1013` | Error, non-configurable | MappingConstructorNotSatisfiable | Marked constructor cannot be satisfied. |
| `LITEMAPPER1014` | Error, non-configurable | TargetDefaultMissing | `UseTargetDefault` names a member with no real default. |
| `LITEMAPPER1015` | Error, non-configurable | InvalidIgnoredMember | Ignore attribute names a missing or invalid member. |
| `LITEMAPPER1016` | Error, non-configurable | IndexerNotSupported | Configuration attempts to map an indexer. |

#### Nullability and conversion diagnostics

| ID | Default severity | Name | Meaning |
|---|---:|---|---|
| `LITEMAPPER2001` | Error, configurable only to documented policy | NullableToNonNullable | Potential null maps to a non-null target. |
| `LITEMAPPER2002` | Error, non-configurable | InvalidNullCollectionMapping | Null collection strategy cannot satisfy target nullability. |
| `LITEMAPPER2003` | Error, non-configurable | NullableElementMismatch | Nullable collection element maps to a non-null element target. |
| `LITEMAPPER2004` | Error, non-configurable | ConversionNotFound | No valid conversion or mapping exists. |
| `LITEMAPPER2005` | Error, non-configurable | AmbiguousConversion | Multiple conversion candidates have equal precedence. |
| `LITEMAPPER2006` | Error | ExplicitOperatorDisabled | Mapping requires an explicit operator that is disabled. |
| `LITEMAPPER2007` | Error | NarrowingNumericConversionDisabled | Mapping requires narrowing numeric conversion under `ImplicitOnly`. |
| `LITEMAPPER2008` | Error, non-configurable | UnsupportedGeneratedType | Type requires dynamic, pointer, ref-like, or another unsupported generated behavior. |
| `LITEMAPPER2009` | Error, non-configurable | InvalidConverterSignature | Selected converter has an unsupported signature. |
| `LITEMAPPER2010` | Error, non-configurable | ConverterNullabilityMismatch | Converter result cannot satisfy target nullability. |
| `LITEMAPPER2011` | Error, non-configurable | AmbiguousConverter | Multiple converters have equal resolution precedence. |
| `LITEMAPPER2012` | Error, non-configurable | RuntimeObjectDispatchNotSupported | `object` or abstract runtime dispatch would be required. |

#### Mapping-resolution diagnostics

| ID | Default severity | Name | Meaning |
|---|---:|---|---|
| `LITEMAPPER3001` | Error, non-configurable | AmbiguousMapping | Multiple mappings exist for a required type pair. |
| `LITEMAPPER3002` | Error, non-configurable | DuplicateDefaultMapping | More than one default exists for a type pair. |
| `LITEMAPPER3003` | Error, non-configurable | DefaultMappingNotUsable | Selected default mapping is inaccessible or incompatible. |
| `LITEMAPPER3004` | Error, non-configurable | StructuralNestedMappingFailed | Automatic nested mapping cannot be generated. |
| `LITEMAPPER3005` | Error, non-configurable | AbstractDestinationNotSupported | Destination cannot be constructed because it is abstract or an interface. |

#### Collection diagnostics

| ID | Default severity | Name | Meaning |
|---|---:|---|---|
| `LITEMAPPER4001` | Error, non-configurable | UnsupportedCollectionShape | Collection shape is outside 1.0 support. |
| `LITEMAPPER4002` | Error, non-configurable | RectangularArrayNotSupported | Mapping uses a multidimensional rectangular array. |
| `LITEMAPPER4003` | Error, non-configurable | CollectionTargetCannotBeConstructed | No legal concrete destination collection can be created. |
| `LITEMAPPER4004` | Error, non-configurable | GetOnlyCollectionUpdateNotSupported | Update targets a get-only collection. |
| `LITEMAPPER4005` | Error, non-configurable | TopLevelArrayUpdateNotSupported | Existing-target array mapping was declared. |
| `LITEMAPPER4006` | Error, non-configurable | CustomCollectionNotSupported | Custom collection convention would be required. |

#### Existing-target diagnostics

| ID | Default severity | Name | Meaning |
|---|---:|---|---|
| `LITEMAPPER5001` | Error, non-configurable | InvalidUpdateSignature | Existing-target method has an unsupported signature. |
| `LITEMAPPER5002` | Error, non-configurable | NullableVoidDestinationNotSupported | Nullable destination cannot be replaced through a void method. |
| `LITEMAPPER5003` | Error, non-configurable | StructUpdateRequiresRef | Value-type destination is passed by value. |
| `LITEMAPPER5004` | Error, non-configurable | InitOnlyMemberCannotBeUpdated | Update attempts to assign an init-only member. |
| `LITEMAPPER5005` | Error, non-configurable | NestedUpdateMappingRequired | Get-only/existing nested mutation lacks explicit update mapping. |
| `LITEMAPPER5006` | Warning | IgnoreNullOnlyValidForUpdate | Patch null-skipping was configured on a new-object mapping. |

#### Recursive graph diagnostics

| ID | Default severity | Name | Meaning |
|---|---:|---|---|
| `LITEMAPPER6001` | Info | RecursiveMappingWithoutCycleDetection | Recursive type graph uses `ReferenceHandling.None`. |
| `LITEMAPPER6002` | Error, non-configurable | InvalidReferenceHandling | Reference handling is unsupported for the declared mapping. |
| `LITEMAPPER6003` | Error, non-configurable | CyclePathCannotBeTracked | Required recursive path cannot use reference identity. |

#### Enum diagnostics

| ID | Default severity | Name | Meaning |
|---|---:|---|---|
| `LITEMAPPER7001` | Error, non-configurable | EnumMemberNotMapped | Source enum member has no target mapping. |
| `LITEMAPPER7002` | Error, non-configurable | EnumAliasConflict | Aliases map inconsistently. |
| `LITEMAPPER7003` | Error, non-configurable | EnumZeroMemberNotMapped | Required zero member has no match. |
| `LITEMAPPER7004` | Error, non-configurable | EnumFlagNotMapped | Atomic flag has no target match. |
| `LITEMAPPER7005` | Error, non-configurable | EnumValueOverflow | By-value mapping cannot fit destination underlying type. |

#### Internal diagnostic

| ID | Default severity | Name | Meaning |
|---|---:|---|---|
| `LITEMAPPER9001` | Error, non-configurable | InternalGeneratorFailure | Unexpected failure while processing one mapper. |

### 20.3 Internal failures

Unexpected exceptions MUST be caught per mapper where possible.

The generator MUST:

- emit one sanitized `LITEMAPPER9001` at the mapper;
- avoid including absolute paths or sensitive environment details;
- continue processing unrelated mapper classes;
- expose full exception detail only through generator-development/test configuration.

When mapper-wide configuration is invalid, every generated method in that mapper fails, while unrelated mapper classes continue.

When one mapping method has a fatal method-specific error, valid independent methods in the same mapper MUST still generate unless the invalid method corrupts shared mapper configuration.

---

## 21. Incremental architecture requirements

### 21.1 Pipeline stages

The generator SHOULD separate these incremental stages:

1. mapper syntax candidate discovery;
2. mapper symbol validation;
3. assembly defaults and assembly registrations;
4. parse/language capability extraction;
5. framework capability extraction;
6. mapper configuration model;
7. mapping method model creation;
8. dependency graph and recursive-component analysis;
9. source rendering;
10. diagnostic emission.

### 21.2 Equality and caching

Intermediate models used as incremental values MUST have structural equality based only on relevant semantic inputs.

The implementation SHOULD avoid retaining entire `Compilation`, `SemanticModel`, or syntax trees in final incremental models when a compact immutable representation suffices.

### 21.3 Analyzer options

LiteMapper 1.0 MAY consume a small documented set of build properties:

```text
LiteMapper_EmitDebugMetadata
LiteMapper_IncludeGeneratedSourceComments
LiteMapper_TreatInternalGeneratorErrorsAsExceptions
```

These options are intended for diagnostics, development, and tests. They MUST NOT alter mapping semantics.

### 21.4 Required incremental tests

Tests MUST prove:

- unchanged inputs are cached;
- changing one mapper does not regenerate unrelated mappers;
- changing a relevant model symbol regenerates affected mappings;
- changing an unrelated model does not regenerate unaffected mappings;
- language-version changes invalidate emitted source when capability selection changes;
- relevant analyzer-option changes invalidate only affected outputs;
- output ordering remains deterministic.

---

## 22. Testing requirements

### 22.1 Test framework

All standard test projects MUST use MSTest with built-in assertion APIs.

The repository MUST NOT require FluentAssertions, Shouldly, or another assertion library.

### 22.2 Roslyn test infrastructure

The test suite MUST use both:

- official Roslyn source-generator testing packages for diagnostics and source-generation cases;
- a custom compilation harness for incremental-step inspection, in-memory execution, capability matrices, and targeted source inspection.

### 22.3 Snapshot testing

Representative generated-source snapshots MUST be checked in as ordinary expected `.g.cs` files.

Snapshots MUST cover representative algorithms, not every combinatorial test case.

Snapshot frameworks are not required.

### 22.4 Runtime testing

Runtime behavior MUST be tested through both:

- in-memory generated compilation and invocation, where practical;
- normally compiled fixture/consumer projects.

Reflection MAY be used by test infrastructure to invoke generated methods. The zero-reflection guarantee applies to shipped generated mapping paths.

### 22.5 Consumer target matrix

Pull-request CI MUST test at least:

- `netstandard2.0`;
- `net8.0`;
- `net10.0`.

Release CI MUST test:

- `netstandard2.0`;
- `net8.0`;
- `net9.0`;
- `net10.0`.

### 22.6 Language matrix

CI MUST test:

- C# 9;
- the language version associated with the current supported LTS SDK used by the project;
- `latest` supported by the current CI SDK.

### 22.7 Roslyn matrix

CI MUST test:

- Roslyn 4.8.0;
- the current stable Roslyn version pinned by the repository.

The generator package MUST reference Roslyn 4.8.0 for its compile-time API baseline, with `PrivateAssets="all"` for build dependencies.

### 22.8 Operating systems

CI MUST run on Windows and Linux.

macOS is not a required 1.0 CI platform.

### 22.9 Declaration tests

Tests MUST include valid and invalid forms for:

- static and instance mapper classes;
- non-partial mapper;
- nested mapper with non-partial parent;
- generic mapper or generic containing type;
- abstract mapper;
- unsupported mapper kind;
- invalid mapping signatures;
- async methods;
- unsupported accessibility;
- duplicate configuration;
- invalid `UseMapper` registrations.

### 22.10 Constructor tests

Tests MUST include:

- parameterless constructor;
- one parameterized constructor;
- multiple satisfiable constructors;
- constructor ambiguity;
- `[MappingConstructor]`;
- optional parameters;
- records and record structs;
- record copy-constructor exclusion;
- `init` properties;
- `required` members;
- `SetsRequiredMembersAttribute`;
- read-only fields;
- constructor accessibility.

### 22.11 Nullability tests

Tests MUST include:

- null/non-null root source;
- enabled and disabled `GuardNonNullSource` behavior;
- nullable/non-null return;
- nullable member to nullable target;
- nullable member to non-null target under `Error` and `Throw`;
- nullable source paths;
- nullable collections;
- nullable elements;
- nullable-oblivious projects;
- patch null skipping.

### 22.12 Collection tests

Tests MUST cover every mandatory shape and behavior:

- arrays;
- jagged arrays;
- sequence interfaces;
- mutable collection interfaces;
- read-only interfaces;
- lists;
- sets;
- dictionaries;
- nested collections;
- collections of nested objects;
- empty collections;
- null collections;
- nullable elements;
- comparer preservation;
- dictionary key collision;
- single enumeration;
- capacity preallocation when count is cheap;
- mutable-copy behavior;
- unsupported rectangular arrays and custom collections.

### 22.13 Existing-target tests

Tests MUST include:

- reference target;
- void update;
- destination-returning update;
- `ref` struct target;
- null destination;
- writable nested replacement;
- explicit nested mutation;
- collection replacement;
- get-only collection rejection;
- `IgnoreNullSourceMembers` behavior;
- init-only rejection;
- top-level array-update rejection.

### 22.14 Cycle tests

Tests MUST include:

- finite recursive tree;
- direct self-cycle;
- indirect cycle;
- cycle through a collection;
- shared non-cyclic reference;
- exception source/destination properties;
- mapping method and member path;
- no tracker allocation for acyclic type graphs;
- one tracker per recursive public call;
- concurrent calls.

### 22.15 Enum tests

Tests MUST include:

- by-name mapping;
- by-value mapping;
- unmatched members;
- unknown runtime numeric values;
- source aliases;
- target aliases;
- zero members;
- flags and composite flags;
- numeric overflow;
- custom converter precedence.

### 22.16 Resolution tests

Tests MUST verify the complete precedence chain, including conflicts among:

- explicit `Use`;
- local `[MappingConverter]`;
- `Map{TargetMember}`;
- local mapping method;
- external converter;
- external mapping method;
- default mapping;
- identity and implicit operator;
- enabled explicit operator;
- numeric conversion;
- enum mapping;
- collection mapping;
- structural nested mapping.

### 22.17 Portability tests

Representative generated source MUST compile against every supported consumer TFM and language baseline.

### 22.18 Native AOT and trimming

Pull-request CI MUST publish and execute at least one Native AOT sample.

Release CI MUST publish and execute Native AOT samples covering:

- static mapper;
- instance mapper;
- nested collections;
- cycle detection.

A trimmed sample MUST be published and executed. LiteMapper-generated trimming or AOT warnings MUST fail CI.

### 22.19 Package-consumer tests

Automated packaging tests MUST:

1. pack all packages;
2. create clean consumer projects;
3. install packages from a local feed;
4. compile and run representative mappings;
5. verify one-package normal installation;
6. verify generator assets do not become runtime dependencies;
7. verify Roslyn assemblies are not copied to consumer output;
8. verify `Mammoth.LiteMapper.Abstractions` can be consumed independently;
9. verify deterministic package content.

### 22.20 Public API tests

CI MUST use Roslyn public API analyzers with:

```text
PublicAPI.Shipped.txt
PublicAPI.Unshipped.txt
```

It MUST also perform package-level API compatibility validation.

---

## 23. Performance requirements

### 23.1 Benchmark comparison

Benchmarks MUST compare pinned versions of:

- manual mapping;
- LiteMapper;
- Mapperly;
- Mapster;
- AutoMapper.

Results are contextual and MUST record runtime, SDK, CPU, OS, library versions, and benchmark configuration.

### 23.2 Generated structure

For straightforward mappings, generated code MUST be structurally equivalent to reasonable handwritten C#.

The specification does not impose a universal nanosecond ratio to manual mapping because tiny benchmark ratios are unstable.

### 23.3 Allocation budgets

Expected allocations are:

| Feature | Allowed generated overhead |
|---|---|
| Flat new-object mapping | destination object only |
| Nested mapping | destination graph only |
| Collection mapping | destination collection and mapped destination elements only |
| Non-recursive mapping | no tracker allocation |
| Recursive `ThrowOnCycle` | one tracker state plus required active-path entries |

User converters may allocate independently.

### 23.4 Regression policy

Controlled release benchmarks MUST enforce:

- allocation regressions block release unless explicitly approved and documented;
- statistically significant throughput regressions greater than 10% require review;
- statistically significant throughput regressions greater than 20% block release by default.

Shared pull-request runners MUST NOT use fragile fixed nanosecond thresholds.

---

## 24. Packaging, security, and release policy

### 24.1 Reflection, AOT, and trimming guarantees

LiteMapper guarantees zero runtime reflection for all supported generated features.

All supported generated features MUST be compatible with Native AOT and trimming.

User-provided converter code and instance dependencies remain the consumer's responsibility.

### 24.2 Telemetry and network behavior

LiteMapper MUST NOT perform:

- telemetry;
- analytics;
- network calls;
- update checks;
- runtime reporting;
- build usage collection.

### 24.3 Logging

LiteMapper MUST NOT inject runtime logging or callbacks into generated mapping code.

### 24.4 Versioning

LiteMapper follows Semantic Versioning.

Compatibility includes:

- public types and members;
- attribute semantics;
- diagnostic IDs and meaning;
- supported mapping signatures;
- configuration precedence;
- observable generated behavior;
- exception types and properties.

Exact generated formatting, local names, and helper organization are not public API and may change between releases.

### 24.5 Diagnostic stability

Diagnostic IDs are stable from the moment they are introduced, including pre-1.0 releases.

### 24.6 Reproducible packages

Release artifacts MUST use:

- deterministic builds;
- Source Link;
- repository URL and commit metadata;
- symbol packages;
- no machine-specific paths;
- no content-changing timestamps;
- repeatable NuGet packaging.

### 24.7 Strong naming

LiteMapper assemblies MUST NOT be strong-name signed in 1.0.

### 24.8 License

All source and packages MUST use the MIT license.

---

## 25. Implementation milestones

Implementation MUST follow this order:

1. repository, solution, package, and CI skeleton;
2. public abstractions API and public API baselines;
3. generator discovery, incremental infrastructure, and declaration diagnostics;
4. flat mapping;
5. constructors, records, `init`, `required`, and value types;
6. explicit member configuration and converters;
7. nullable behavior;
8. nested object mapping;
9. arrays, collections, sets, and dictionaries;
10. existing-target mapping and patch behavior;
11. enum mapping;
12. recursive type analysis and `ThrowOnCycle`;
13. capability-based emitted optimization;
14. packaging, trimming, and Native AOT validation;
15. benchmarks and compiling samples;
16. usage documentation assembled from compiling samples.

### 25.1 Test-first rule

Every feature milestone MUST begin with failing tests and diagnostic cases.

Implementation-first followed by retrofitted tests is not accepted for required features.

### 25.2 Milestone completion

A milestone is complete only when:

- focused tests pass;
- the full test suite passes;
- generated source has been reviewed;
- diagnostics are documented and tested;
- public API baselines are deliberately updated when required;
- `STATUS.md` and `DECISIONS.md` are updated;
- no unrelated TODO or disabled test was introduced.

### 25.3 Specification changes

The implementation agent MAY propose a specification change in `DECISIONS.md`.

It MUST NOT implement semantic changes before explicit approval.

Any unresolved decision or specification ambiguity stops the whole implementation project until resolved.

The agent MUST NOT continue unaffected milestones while a normative ambiguity remains open.

---

## 26. Compiling samples and documentation

Before implementation stabilizes, this specification is the sole source of truth.

During implementation, sample projects become the authoritative source for usage examples.

The eventual usage guide MUST:

- be assembled from or directly reference real compiling sample source;
- be validated in CI;
- use package references rather than project references in release validation;
- never document an API that does not compile;
- distinguish required 1.0 behavior from deferred features.

A standalone handwritten usage guide MUST NOT be created in advance and treated as a parallel specification.

---

## 27. Explicitly deferred features

The following are deferred and MUST NOT appear as partially functional public APIs in 1.0:

- EF Core expression projections;
- expression-tree converter inlining;
- runtime polymorphism;
- reference preservation;
- hooks;
- object factories;
- mapping context propagation;
- open generic mappings;
- generic mapper containers;
- external instance mapper resolution;
- DI registration extensions;
- automatic flattening;
- naming-strategy plugins;
- custom collections;
- immutable collections;
- queues and stacks;
- rectangular arrays;
- async mapping;
- code fixes;
- runtime logging;
- telemetry.

A deferred feature MUST either:

- produce an explicit diagnostic when a generated declaration requests it; or
- remain absent from public API so normal C# rejects the attempted usage.

---

## 28. Critical algorithm pseudocode

### 28.1 Configuration resolution

```text
resolveOption(methodValue, mapperValue, assemblyValue, libraryDefault):
    if methodValue != Unspecified:
        return methodValue

    if mapperValue != Unspecified:
        return mapperValue

    if assemblyValue exists and assemblyValue != Unspecified:
        return assemblyValue

    return libraryDefault
```

For method Boolean overrides:

```text
resolveBoolean(methodState, mapperBoolean):
    if methodState == Enabled:
        return true

    if methodState == Disabled:
        return false

    return mapperBoolean
```

### 28.2 Member matching

```text
matchTargetMember(target, sourceCandidates, policy):
    explicit = explicit configuration for target
    if explicit exists:
        validate and return explicit

    candidates = readable public instance properties and fields

    if policy == Exact:
        return unique ordinal exact name match or none/error

    if policy == ExactThenIgnoreCase:
        exact = ordinal exact matches
        if exact count == 1:
            return exact[0]
        if exact count > 1:
            error

        insensitive = ordinal-ignore-case matches
        return unique insensitive match or none/error

    if policy == IgnoreCase:
        insensitive = ordinal-ignore-case matches
        return unique insensitive match or none/error
```

Before name comparison, hidden-member resolution selects the most-derived member, then property over field, and records a warning when hiding occurred.

### 28.3 Constructor selection

```text
selectConstructor(targetType, mapping):
    eligible = accessible constructors excluding record copy constructors

    marked = eligible with MappingConstructor
    if marked count > 1:
        error
    if marked count == 1:
        if fully satisfiable(marked[0]):
            return marked[0]
        error

    parameterized = fully satisfiable eligible constructors with parameter count > 0

    if parameterized count == 1:
        return parameterized[0]

    if parameterized count > 1:
        maxCount = maximum parameter count
        best = parameterized with maxCount
        if best count == 1:
            return best[0]
        error

    parameterless = eligible constructors with zero parameters
    if parameterless count == 1:
        return parameterless[0]

    if parameterless count > 1:
        error

    error
```

### 28.4 Value conversion

```text
resolveValue(sourceValue, targetType, targetMember):
    try explicit Use
    select explicit source path when configured
    if selected source value is directly assignable, use it
    otherwise continue with that selected source value
    try local MappingConverter
    try local Map{TargetMember}
    try explicit local mapping method
    try registered external converter
    try registered external mapping method
    try default mapping
    try identity or implicit C# conversion
    try enabled explicit operator / numeric policy
    try enum strategy
    try collection mapping
    try generated private structural nested mapping
    otherwise error
```

At every stage, equal-precedence ambiguity is an error.

### 28.5 Collection construction

```text
mapCollection(source, targetShape):
    if source is null:
        apply effective null collection strategy

    determine destination concrete type
    determine count only through cheap count/index capability
    allocate destination with capacity when available

    enumerate source exactly once:
        map key and value independently for dictionary
        otherwise map one element to one element
        add using normal destination semantics

    return destination
```

### 28.6 Recursive component detection

```text
build directed graph:
    node = closed source/destination mapping pair
    edge A -> B = A requires nested or element mapping B

compute strongly connected components

component is recursive when:
    component contains multiple nodes
    OR one node has a self-edge

only recursive components receive cycle-tracker plumbing
```

### 28.7 Runtime cycle tracking

```text
mapTracked(source, tracker, memberPath):
    if source is null:
        apply null behavior

    if tracker.activeReferences already contains source by reference identity:
        throw LiteMapperCycleException

    add source to tracker.activeReferences
    try:
        perform mapping and recursive calls
    finally:
        remove source from tracker.activeReferences
```

---

## 29. Definition of done for LiteMapper 1.0

LiteMapper 1.0 is complete only when all of the following are true:

- every required feature in this specification is implemented;
- every required diagnostic exists and has focused tests;
- all supported consumer TFMs compile and pass runtime tests;
- all required C# language-version tests pass;
- minimum, intermediate, and current Roslyn versions pass;
- generator declaration, runtime, snapshot, incremental, packaging, trimming, and Native AOT tests pass;
- Windows and Linux CI pass;
- public API compatibility checks pass;
- allocation and benchmark acceptance criteria are met;
- NuGet packages are reproducible;
- clean consumer projects install from a local feed and run successfully;
- Roslyn and generator assemblies do not become runtime dependencies;
- no supported feature uses reflection, dynamic dispatch, runtime scanning, or runtime code generation;
- every deferred feature is absent or explicitly diagnosed;
- samples compile from clean package references;
- the usage guide is assembled from compiling samples;
- no unresolved P0 or P1 issue exists;
- no unresolved normative ambiguity exists;
- `STATUS.md` declares every milestone complete with evidence.

Compilation alone is not completion.

---

## 30. Authoritative-source policy

This file supersedes every earlier MinimalMapper or LiteMapper draft, audit bundle, README, usage guide, ZIP archive, chat example, or provisional design note.

During implementation:

- approved updates MUST modify this file first;
- `DECISIONS.md` MUST record the approved reason;
- tests and samples MUST be updated in the same change;
- generated documentation MUST follow the implementation and compiling samples.

No other document may silently redefine LiteMapper behavior.
