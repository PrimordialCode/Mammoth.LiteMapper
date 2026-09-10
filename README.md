# Mammoth.LiteMapper

Mammoth.LiteMapper is a convention-first, compile-time object mapping library for C#.

Consumers declare partial mapping methods on classes marked with `[LiteMapper]`. A Roslyn incremental source generator emits direct C# implementations using constructors, assignments, loops, casts, and ordinary method calls. LiteMapper is not a runtime mapping container and does not provide runtime mapper registration, assembly scanning, reflection fallback, or dynamic dispatch.

## Compilation Status

- main [![CI](https://github.com/PrimordialCode/Mammoth.LiteMapper/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/PrimordialCode/Mammoth.LiteMapper/actions/workflows/ci.yml)
- develop [![CI](https://github.com/PrimordialCode/Mammoth.LiteMapper/actions/workflows/ci.yml/badge.svg?branch=develop)](https://github.com/PrimordialCode/Mammoth.LiteMapper/actions/workflows/ci.yml)


## Quickstart

Install the primary package:

```xml
<PackageReference Include="Mammoth.LiteMapper" />
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

- [SPECIFICATION.md](SPECIFICATION.md) is the sole authoritative product contract.
- [USAGE.md](docs/USAGE.md) is the usage guide and must stay synchronized with the specification, samples, tests, and other project documents.

## Agent skill

The [mammoth-litemapper skill](skills/mammoth-litemapper/SKILL.md) provides consumer mapping guidance for compatible coding agents using the [Skills CLI](https://skills.sh/docs).

Once this skill is published on the repository's default branch, install it from your consuming project:

```sh
npx skills add PrimordialCode/Mammoth.LiteMapper --skill mammoth-litemapper
```

Append `--agent codex` to target Codex or `--global` for user-level installation. To preview this checkout's discoverable skills without installing, run `npx skills add . --list` from the repository root. The skill contains its own supporting reference; consumers do not need a LiteMapper source checkout. Local validation does not establish a skills.sh directory listing.
