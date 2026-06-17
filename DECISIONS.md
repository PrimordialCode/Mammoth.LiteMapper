# Mammoth.LiteMapper Decision Log

`SPECIFICATION.md` is authoritative. Accepted entries here are non-semantic implementation decisions unless explicitly stated otherwise.

## Accepted non-semantic implementation decisions

### DEC-0001

- Date: 2026-06-17
- Milestone: 1
- Status: Accepted
- Context: The specification requires exact project names and both solution formats.
- Decision: Use SDK-style projects under the exact section 4.6 paths and generate `Mammoth.LiteMapper.slnx` from `Mammoth.LiteMapper.sln` with `dotnet sln Mammoth.LiteMapper.sln migrate`.
- Specification references: 4.6, 25.
- Consequences: `.sln` remains the editable source solution; `.slnx` is regenerated after solution membership changes.

### DEC-0002

- Date: 2026-06-17
- Milestone: 1
- Status: Accepted
- Context: The skeleton needs repeatable dependency versions and deterministic project settings.
- Decision: Use central package management in `Directory.Packages.props` and shared deterministic defaults in `Directory.Build.props`.
- Specification references: 3.4, 4.1, 4.2, 22.1, 22.7, 24.6.
- Consequences: Package versions are controlled centrally; shipping assemblies target `netstandard2.0`.

### DEC-0003

- Date: 2026-06-17
- Milestone: 1
- Status: Accepted
- Context: Milestone 1 must compile without implementing Milestone 2 public abstractions or Milestone 3 generator behavior.
- Decision: Shipping assemblies contain internal marker types only. No public mapping or abstraction API is introduced.
- Specification references: 4.6, 5, 25.
- Consequences: Public API remains empty until Milestone 2.

### DEC-0004

- Date: 2026-06-17
- Milestone: 1
- Status: Accepted
- Context: The generator must use the Roslyn 4.0.1 API baseline.
- Decision: Pin `Microsoft.CodeAnalysis.CSharp` to `4.0.1` in central package management and reference it from the generator with `PrivateAssets="all"`.
- Specification references: 4.1, 22.7.
- Consequences: Generator code must avoid APIs introduced after Roslyn 4.0.1 unless a later approved specification change raises the baseline.

### DEC-0005

- Date: 2026-06-17
- Milestone: 1
- Status: Accepted
- Context: Milestone 1 requires MSTest with built-in assertions.
- Decision: Use `MSTest.TestFramework` `4.2.3`, `MSTest.TestAdapter` `4.2.3`, and `Microsoft.NET.Test.Sdk` `18.6.0`; do not add FluentAssertions, Shouldly, or another assertion library.
- Specification references: 22.1.
- Consequences: Tests use only MSTest assertion APIs.

### DEC-0006

- Date: 2026-06-17
- Milestone: 1
- Status: Accepted
- Context: Milestone 1 requires initial Windows and Linux CI.
- Decision: Add separate GitHub Actions workflows for Windows and Linux that restore, build, test, and list both solution formats.
- Specification references: 22.8, 25.
- Consequences: CI skeleton exists; release, trimming, AOT, package-consumer, and benchmark validation remain later milestones.

## Pending specification questions

None.

## Rejected alternatives

### DEC-0007

- Date: 2026-06-17
- Milestone: 1
- Status: Rejected
- Context: Public marker APIs could make test code easier to write.
- Decision or question: Do not expose public marker types in shipping assemblies during Milestone 1.
- Specification references: 3.2, 5, 25.
- Consequences: Tests load assemblies by name and validate that no public mapping API exists yet.

## Superseded decisions

None.
