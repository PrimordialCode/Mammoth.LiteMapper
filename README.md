# Mammoth.LiteMapper

Mammoth.LiteMapper is a convention-first, compile-time object mapping library for C#.

Consumers declare partial mapping methods on classes marked with `[LiteMapper]`. A Roslyn incremental source generator emits direct C# implementations using constructors, assignments, loops, casts, and ordinary method calls. LiteMapper is not a runtime mapping container and does not provide runtime mapper registration, assembly scanning, reflection fallback, or dynamic dispatch.

Assembly-wide defaults and external mapper registrations are compile-time configuration. Rebuild after changing them. Generated helpers are private; call only the mapping methods you declare and do not depend on helper names or signatures.

## Compilation Status

- main [![CI](https://github.com/PrimordialCode/Mammoth.LiteMapper/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/PrimordialCode/Mammoth.LiteMapper/actions/workflows/ci.yml)
- develop [![CI](https://github.com/PrimordialCode/Mammoth.LiteMapper/actions/workflows/ci.yml/badge.svg?branch=develop)](https://github.com/PrimordialCode/Mammoth.LiteMapper/actions/workflows/ci.yml)


## Quickstart

Install the published stable primary package, `3.0.0`, in a project without central package management:

```xml
<PackageReference Include="Mammoth.LiteMapper" Version="3.0.0" />
```

For central package management, put the version in `Directory.Packages.props` instead; see [installation](docs/USAGE.md#installation). The [version notes](docs/USAGE.md#versions-and-upgrading) distinguish `3.0.0` from unreleased fixes in this checkout.

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

Ordinary unmapped target members are reported as `LITEMAPPER1001` errors by default. Explicit mapper or method `UnmappedMemberPolicy` values and standard compiler severity configuration may intentionally relax this configurable diagnostic; doing so preserves valid generation but is not proof of complete target coverage. Required, non-nullable, inaccessible, invalid, and otherwise mandatory target obligations remain hard errors. Projects requiring mandatory unmapped-target coverage should treat `LITEMAPPER1001` as an error in CI and audit or avoid project-wide suppression.

## Documentation

- [SPECIFICATION.md](SPECIFICATION.md) is the sole authoritative product contract.
- [USAGE.md](docs/USAGE.md) is the usage guide and must stay synchronized with the specification, samples, tests, and other project documents.

## Agent skill

The [mammoth-litemapper skill](skills/mammoth-litemapper/SKILL.md) provides consumer mapping guidance for compatible coding agents using the [Skills CLI](https://skills.sh/docs).

Install the skill from the explicit `develop` branch, which includes guidance for `3.0.0` and clearly marked unreleased corrections:

```sh
npx skills add https://github.com/PrimordialCode/Mammoth.LiteMapper/tree/develop/skills/mammoth-litemapper --skill mammoth-litemapper
```

Append `--agent codex` to target Codex or `--global` for user-level installation. To preview this checkout without installing, run `npx skills add ./skills/mammoth-litemapper --list` from the repository root. The skill includes its supporting reference and needs no source checkout. Installing the skill does not upgrade the NuGet package; verify the package version before using an unreleased correction.
