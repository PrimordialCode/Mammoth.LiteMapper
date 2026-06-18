# Mammoth.LiteMapper Usage

This guide is assembled from compiling sample projects. The source of truth for each example is the referenced sample file.

Validated samples:

- `samples/Mammoth.LiteMapper.Samples.Basic/Program.cs`
- `samples/Mammoth.LiteMapper.Samples.Collections/Program.cs`
- `samples/Mammoth.LiteMapper.Samples.AspNetCore/Program.cs`

## Basic static mapper

From `samples/Mammoth.LiteMapper.Samples.Basic/Program.cs`:

```csharp
[LiteMapper]
public static partial class StaticMapper
{
    public static partial StaticTarget Map(StaticSource source);
}
```

LiteMapper generates the partial mapping implementation at compile time. The sample maps `StaticSource` to `StaticTarget`, including nested child objects and an array-to-list collection member.

## Instance mapper

From `samples/Mammoth.LiteMapper.Samples.Basic/Program.cs`:

```csharp
[LiteMapper]
public sealed partial class InstanceMapper
{
    public partial InstanceTarget Map(InstanceSource source);
}
```

Instance mapper classes are supported. The generated implementation is still direct C# and does not use runtime reflection or a mapper registry.

## Cycle detection

From `samples/Mammoth.LiteMapper.Samples.Basic/Program.cs`:

```csharp
[LiteMapper(ReferenceHandling = ReferenceHandling.ThrowOnCycle)]
public static partial class CycleMapper
{
    public static partial NodeTarget Map(NodeSource source);
}
```

`ReferenceHandling.ThrowOnCycle` enables generated cycle tracking for recursive type graphs. A detected cycle throws `LiteMapperCycleException`.

## Collections

From `samples/Mammoth.LiteMapper.Samples.Collections/Program.cs`:

```csharp
[LiteMapper]
public static partial class CollectionMapper
{
    public static partial OrderTarget Map(OrderSource source);
}
```

The sample maps arrays to lists, nested collection elements, sets, and dictionaries. Supported collection mappings are generated as ordinary loops.

## ASP.NET Core

From `samples/Mammoth.LiteMapper.Samples.AspNetCore/Program.cs`:

```csharp
[LiteMapper]
public static partial class UserMapper
{
    public static partial UserDto Map(User source);
}
```

The ASP.NET Core sample maps a domain object to a DTO before writing JSON from a minimal API endpoint.

## Diagnostics

LiteMapper reports compile-time diagnostics for unsupported declarations, invalid configuration, and unsupported mapping shapes. Diagnostic IDs are stable once introduced.

Examples of implemented diagnostic ranges:

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
- Runtime polymorphic mapping.
- Runtime mapper registration or assembly scanning.
- Dependency-injection registration extensions.
- Object factories, hooks, or mapping context propagation.
- Open generic mappings or generic mapper containers.
- Custom collections, immutable collections, queues, stacks, and rectangular arrays.
- Async mapping.
- Code fixes.
- Runtime logging, telemetry, or network behavior.

Deferred features are either absent from the public API or diagnosed when requested through generated mapping declarations.
