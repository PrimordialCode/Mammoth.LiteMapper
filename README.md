# Mammoth.LiteMapper

Mammoth.LiteMapper is a convention-first, compile-time object mapping library for C#.

Consumers declare partial mapping methods on classes marked with `[LiteMapper]`. A Roslyn incremental source generator emits direct C# implementations using constructors, assignments, loops, casts, and ordinary method calls. LiteMapper is not a runtime mapping container and does not provide runtime mapper registration, assembly scanning, reflection fallback, or dynamic dispatch.

## Quickstart

Install the primary package:

```xml
<PackageReference Include="Mammoth.LiteMapper" Version="1.0.0" />
```

Declare a mapper:

```csharp
using Mammoth.LiteMapper;

[LiteMapper]
public static partial class CustomerMapper
{
    public static partial CustomerDto Map(Customer source);
}
```

Use the generated method like ordinary C#:

```csharp
CustomerDto dto = CustomerMapper.Map(customer);
```

See the compiling samples under `samples/` for static mappers, instance mappers, collections, recursive mappings, and ASP.NET Core usage.

## Documentation

- `SPECIFICATION.md` is the sole authoritative product contract.
- `docs/USAGE.md` is the usage guide and must stay synchronized with the specification, samples, tests, and other project documents.
