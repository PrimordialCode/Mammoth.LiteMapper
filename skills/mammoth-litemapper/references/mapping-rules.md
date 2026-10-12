# Mapping rules

Consumer mapping rules for the current Mammoth.LiteMapper release.

Nested mapper classes may live in non-generic partial records, record structs, or interfaces; every containing type must be partial and the mapper itself must remain a class. For example, a partial record Container can contain a static partial Mapper class. Attribute enum constants are validated by value: `(NameMatching)3` means IgnoreCase and maps source value to target Value; undefined999 is rejected, including in assembly defaults.

## Members and construction

Populate read-only fields/properties through a selected constructor. For example, Target(int value) can initialize readonly Value from source Value3. An ordinary unbound read-only value field follows UnmappedTargetMembers and retains its value; do not emit an illegal initializer assignment. Required/non-nullable obligations remain enforced.

Public inherited source members participate, including inherited interface properties and configured source paths. For example, `ISource : IBase` with inherited `Value == 3` maps to target `Value == 3`. Shared diamond members count once; unrelated same-name members are ambiguous, while a more-derived declaration wins with `LITEMAPPER1005`. Ignore/default configuration can name inherited members.

Put mapping configuration on the mapping method, not model properties:

```csharp
[MapProperty(Source = "Email.Value", Target = nameof(CustomerDto.Email))]
[MapProperty(Target = nameof(CustomerDto.DisplayName), Use = nameof(MapDisplayName))]
public static partial CustomerDto Map(Customer source);

private static string MapDisplayName(Customer source) =>
    source.FirstName + " " + source.LastName;
```

Adapt the model names and ensure every destination member is satisfiable. Source paths contain readable public fields/properties only, with no calls, indexing, or expressions. Targets are direct member names, never dotted paths. Omitting `Source` with explicit `Use` selects the root source before automatic member matching; no same-name source member is required.

`[IgnoreTarget(nameof(Target.Member))]` excludes a target assignment. `[IgnoreSource(nameof(Source.Member))]` controls unused-source reporting, not target assignment. `[UseTargetDefault(nameof(Target.Member))]` deliberately preserves a real initializer or optional constructor default; it cannot invent a default or satisfy an unsatisfied required contract.

Construction prefers an accessible `[MappingConstructor]`, then a unique fully satisfiable parameterized constructor with the most parameters, then a parameterless constructor. Ties fail. Records, structs, and record structs use legal constructors and initializers. `init` is creation-only; required members must be satisfied. Constructor-bound members are not assigned a second time.

Constructor arguments accept explicit source configuration and converters, including checked numeric, enum, and nested conversions. Nullability follows the actual parameter annotation. A converter from `Source.Raw` to `Target.Value` can supply `Target(int value)` without a setter in a new-object mapping. A destination-returning update with a nullable destination also uses normal constructor argument mapping when it must create a replacement.

`IgnoreTarget` does not discharge required or non-nullable target obligations. `UseTargetDefault` on an optional constructor-bound member omits its argument even when a matching source value exists. A required member still needs C# construction satisfaction, including `SetsRequiredMembers` when relying on a constructor; an initializer alone is insufficient. Combining explicit `MapProperty` with an ignore/default for the same member is a fatal conflict regardless of attribute order.

## Converters and options

Converters are synchronous, non-generic methods with one source argument and a compatible return. Use explicit `MapProperty.Use`, `[MappingConverter]`, or `Map{TargetMember}`. Do not assume an arbitrary compatible helper will be discovered. Register external static containers with `[UseMapper]`; use `ConverterType` for explicit external selection outside local/registered resolution. Use `[DefaultMapping]` to disambiguate a source/destination pair within the local or registered external mapping stage being considered. Converter exceptions propagate unchanged.

Assembly-level defaults and `[UseMapper]` registrations are compile-time configuration. Rebuild after changing them; `[UseMapper]` does not create a runtime registry.

An explicit `Use = nameof(Parse)` can select an accessible inherited handwritten converter, following normal C# member lookup. For example, a protected base `int Parse(string value) => int.Parse(value) + 1` maps `"12"` to `13`. Instance converters require an instance mapping method; private base methods are inaccessible. Mapper configuration is not implicitly inherited.

Automatic nested and collection mappings preserve the mapper instance for selected instance converters and mappings. Static entry methods use only static converters and mappings. User-owned mutable state still requires consumer synchronization.

Register static containers, including closed constructed generic types, exposing an accessible supported synchronous converter or mapping, including two-parameter updates. A container with `int Parse(string value)` is usable even for unrelated mapping pairs; one with only `void Ping()` reports `LITEMAPPER0010`. Unbound generic containers and generic methods are unsupported. Missing types also report0010, while existing non-static containers report0011. Normal C# accessibility permits private converters from a nested mapper; assembly registration does not make inaccessible methods callable.

Handwritten `ref`/`ref readonly` converter results may be copied into ordinary value targets: copying3 and then changing the source to7 leaves the target3.

Effective numeric and explicit-operator options also govern a selected converter's result. For example, a `long` result of `2147483648` targeting `int` throws `OverflowException` under `Checked` and becomes `int.MinValue` under `Unchecked`. Result conversion evaluates the converter once and preserves its exceptions; nullable results are checked before narrowing, or preserved for nullable targets. Method-level options override mapper options.

A nullable reference or value converter result targeting a non-null member reports `LITEMAPPER2010` under `Error`; `Throw` checks the result once and names the target path in `InvalidOperationException`. For example, a selected `int? ReadValue(string value) => value == "missing" ? null : 12` supplying a `long Value` member produces `12L` for a non-null result and throws for `"missing"`; a `long? Value` target preserves null. An unusable default for the requested pair reports `LITEMAPPER3003`; do not rely on silently falling back to another method.

Generated eligible partial mappings are discovered automatically. Handwritten local methods require one of those explicit mechanisms or `[DefaultMapping]`; names such as `ToDto` or a matching scalar/structural signature alone do not make a helper eligible. Explicit registration opts in compatible methods from a static external container. Mark converters used for dictionary keys/values or collection elements with `[MappingConverter]` when they should participate automatically; marked converters outrank identity conversion.

Generated nested and collection helpers are private; do not depend on their names or signatures. Each mapping retains the calling entry's effective options and nullable shape, including when multiple entries use the same model types.

Resolve explicit `MapProperty.Use` first, then an explicit source path (completing immediately if directly assignable), local marked converters, the local member convention, local mappings, registered external converters, and registered external mappings, before language/enum/collection/structural conversion. A unique default selects among mapping methods within its stage; it never skips an earlier stage.

For example, suppose a nested member needs `SourceValue -> TargetValue`:

```csharp
// In the local mapper:
[DefaultMapping]
private static TargetValue MapDefault(SourceValue source) =>
    new() { Value = source.Value + 10 };

// In a static container registered through UseMapper:
[MappingConverter]
public static TargetValue Convert(SourceValue source) =>
    new() { Value = source.Value + 20 };
```

For `source.Value == 3`, ordinary nested mapping returns `13`: local mappings precede registered external converters. Explicitly selecting `Convert` with `MapProperty.Use` and its `ConverterType` returns `23`. Local `[MappingConverter]` and `Map{TargetMember}` methods also precede the local default. Do not treat defaults as a separate final stage or a global override.

Do not mark multiple visible mappings for the requested pair as defaults, including across local and registered external methods. For example, marking both local `MapDefault(SourceValue)` and an external `OtherDefault(SourceValue)` returning `TargetValue` with `[DefaultMapping]` reports `LITEMAPPER3002`. Keep one default or remove duplicate default attributes and select explicitly; stage precedence does not authorize duplicate defaults.

String parsing/formatting, dates, GUID strings, culture decisions, and domain conversions require handwritten converters. Numeric narrowing needs deliberate `NumericConversion.Checked` or `Unchecked`; the default is `ImplicitOnly`. Explicit user-defined operators require opt-in `AllowExplicitOperators`.

For `int` to `byte`, checked conversion of `256` throws `OverflowException`; unchecked conversion yields `0`. Method options override mapper options. Disabled narrowing reports `LITEMAPPER2007`, disabled explicit operators `2006`, and ambiguous language conversions `2005`. Nullable numeric conversion and unwrapping nullable booleans, GUIDs, dates, enums, and custom structs under `Throw` check null before conversion.

Nullable boxing follows the target annotation: `int?` to non-null `object` reports2001 under Error (2003 for collection elements), or checks null under Throw. `object?` preserves null; oblivious reference targets do not introduce nullable mismatch diagnostics. Nullable enum mappings retain the selected enum strategy: with `From.Ready=1` and `To.Ready=9`, `To? Map(From? source)` maps Ready to9 and null to null. The same applies to members/elements; a non-null target requires Error/Throw policy, unknown numeric values still throw under by-name mapping, and patch-null values skip assignment.

Options resolve method `[MappingOptions]`, mapper `[LiteMapper]`, assembly `[LiteMapperDefaults]`, then library defaults. `Unspecified` inherits. Mapper Boolean options use `true`/`false`; method Boolean overrides use `OptionState.Enabled`/`Disabled`. Assembly defaults expose only name matching, unmapped source/target policies, nullable mismatch, and null collections. The library default for ordinary unmapped target members is `Error`; explicit `Ignore`, `Info`, `Warning`, or `Error` target policy overrides it. Unmapped source members remain `Ignore` by default.

For an opt-in strict source-completeness boundary, set `UnmappedSourceMembers` to `Error` at mapper, method, or assembly scope. This reports unused readable source members as configurable `LITEMAPPER1003` diagnostics naming the member; use `Warning` or `Info` to stage adoption and `IgnoreSource` for intentional exclusions. The default remains `Ignore` because source models can contain fields that are not part of a given destination contract. Source completeness does not weaken target completeness or mandatory target errors.

Name matching uses ordinal comparisons. `Exact` accepts only an exact name; `ExactThenIgnoreCase` prefers exact and otherwise requires a unique case-insensitive match. `IgnoreCase` requires a unique case-insensitive match without exact priority: source `Value` plus `value` targeting `Value` reports non-configurable `LITEMAPPER1004`, including updates and nested structural mappings. Explicit `MapProperty.Source` can select the intended member. Constructor parameters follow the same policy; hidden members are resolved before matching.

## Nulls and updates

- Nullable-to-non-null values default to a compile-time error. `NullableMismatchPolicy.Throw` requests runtime validation. Nullable source and nullable result preserve root null.
- Preserve nullable collection element types during mapping. In patch updates, nullable value members also skip assignment when null under `IgnoreNullSourceMembers`; for example, null `bool?` preserves an existing `true`, and non-null `false` replaces it.
- Non-null root parameters have no runtime guard by default. Enable `GuardNonNullSource` when a guard is intended.
- For collections, the specified contextual default preserves null only for nullable destinations. `NullCollectionStrategy.Empty` explicitly materializes an empty destination; `Preserve` requires nullable targets.
- Reference updates mutate and return the same destination when a destination-returning signature is used. Runtime null passed to a non-null destination parameter throws. A nullable destination is allowed only in a destination-returning form capable of constructing a replacement; a nullable `void` destination is invalid.
- Existing-target updates are non-transactional and deterministic: target members are evaluated and assigned in ordinal target-member name order. A later getter, converter, nested mapping, collection operation, or setter failure leaves earlier assignments applied and skips later members. A per-member source capture may avoid repeated getter/path evaluation but does not provide rollback. New-object construction remains separate.
- For `Target Update(Source source, Target? destination)` with `Target(int value)` and writable `Value`, source `Value = 12` and destination null create `Target(12)` without assigning the constructor-bound member again. A subsequent call with that target and source `Value = 17` updates it and returns the same reference. Existing-target members must still be writable; successful construction alone does not make a read-only update legal.
- `IgnoreNullSourceMembers` is update-only. Null direct values or null path segments leave the existing member unchanged, including collections. At method scope use `OptionState.Enabled`.
- Configured nullable paths are evaluated once per segment. With `[MapProperty(Source = "Address.Code", Use = nameof(Normalize), Target = nameof(Target.Code))]`, a `string? Normalize(string?)` converter receives null when `Address` is null. A `string Normalize(string)` parameter instead reports `LITEMAPPER2001` under `Error` or receives a full-path runtime check under `Throw`, even for a nullable target.
- Null-collection policy includes null introduced by an intermediate configured path. For nullable `Data` in path `Data.Items`, the contextual default rejects a non-null collection target, explicit `Error` rejects nullable traversal for either target annotation, `Preserve` requires a nullable target, and `Empty` materializes an empty destination.
- Writable nested members and collections are replaced by default. Do not infer merging or get-only collection population. Nested in-place mutation needs an explicit compatible update mapping.
- A non-null get-only child can use a declared `void ApplyChild(ChildSource source, ChildTarget target)`: source Value3 updates the existing child to3 without replacing it. Explicit `Use` with a configured source selects a handwritten updater; a configured path such as `Other.Item` supplies that nested source. Class-level external registration precedes assembly-level registration: an updater adding20 wins over an assembly updater adding10, producing23 for source3. Equally eligible updaters in the same registration scope require disambiguation or report `LITEMAPPER3001`.

When explicitly selecting an overloaded nested updater with `MapProperty.Use`, identity source compatibility wins. For example, for a `ChildSource` member, `Chosen(ChildSource source, ChildTarget target)` wins over `Chosen(object source, ChildTarget target)`. If the selected overload adds `10`, source value `3` becomes target value `13`.

For a writable non-null child, select the updater explicitly with `[MapProperty(Source = nameof(Source.Child), Target = nameof(Target.Child), Use = nameof(ApplyChild))]`. With `void ApplyChild(ChildSource source, ChildTarget target)`, source value `3` updates the original child to `3` and preserves its identity. Without explicit updater selection, writable children use replacement by default.

Omitting `Source` in `[MapProperty(Target = nameof(Target.Child), Use = nameof(ApplyChild))]` passes the root source to `void ApplyChild(Source source, ChildTarget target)`. No source member named `Child` is required: the updater can copy root `Value == 3` into `target.Child.Value`.

Patch guards and nested updater calls share one captured source value, so direct nullable getters are read once and null skips the updater. Nullable value captures preserve the selected overload for automatic direct members and configured direct or dotted source paths.

Nested updater calls obey patch and nullability rules. With `IgnoreNullSourceMembers`, null `source.Child` skips the updater and preserves the destination. Under the default mismatch policy, nullable child to a non-null updater parameter reports `LITEMAPPER2001`; `Throw` identifies the member path. A returning updater may create and return a nullable writable child, and the parent assigns it. A nullable get-only child cannot store a returned replacement and reports `LITEMAPPER5005`.

## Collections, enums, and cycles

Arrays and lists can map to `HashSet<T>` or `ISet<T>` on `netstandard2.0` through parameterless or comparer-only construction when capacity constructors are unavailable. `IReadOnlySet<T>` requires that interface to exist. Capacity is used only when the exact accessible constructor is available; constructor selection does not change the mapped contents.

Enum numeric policy applies to declared values and unmatched-name ByValue fallback: Unchecked300 to a byte-backed enum yields44; Checked retains overflow validation. Missing composite flag names may map through their atoms (target Read4 OR Write8 gives12), including signed high-bit source flags; unknown source bits throw. A matching composite name must equal the mapped atomic OR: target Both12 is valid, Both16 reports LITEMAPPER7002 and omits the mapping. Every ordinary source alias must resolve consistently: Known/Alias1 may map to Known/Alias9.

Declare public top-level collection mappings explicitly, for example `public static partial List<CustomerDto> MapCustomers(Customer[] source);` inside a static mapper. Supported shapes include one-dimensional/jagged arrays, common generic sequence/list interfaces, `List<T>`, sets, and dictionaries; `IReadOnlySet<T>` requires the symbol in the compilation.

Mutable collections are copied; elements are not necessarily deep-cloned. Arbitrary enumerable sources are traversed once. Nullable structural elements preserve null without calling an object-mapping helper; null elements are not silently removed. Dictionary keys and values map separately; duplicate converted keys throw through normal `Add` semantics. Do not generate custom/immutable collection construction, queues, stacks, rectangular arrays, or async enumerables.

Concrete set sources preserve compatible comparers only for identity element mappings. A selected custom element converter uses the destination default comparer even for identical element types, so `x` and `X` both survive under default string equality while repeated `x` collapses. Concrete dictionary sources preserve compatible key comparers independently of value conversion; selected custom key converters use the destination default comparer even for identical key types. Interface-declared sources do not expose a comparer for preservation.

Interface destinations use `T[]` for `IEnumerable<T>`, `IReadOnlyCollection<T>`, and `IReadOnlyList<T>`; `List<T>` for `ICollection<T>` and `IList<T>`; `HashSet<T>` for set interfaces; and `Dictionary<TKey, TValue>` for dictionary interfaces. The same result types apply to nested members and `NullCollections.Empty`.

An unknown-count enumerable-to-array mapping may allocate a temporary growing buffer plus its final array. Temporary storage/allocation is O(n); enumeration and element conversion happen once. With a cheap, reliable count, allocate the final array directly. This also applies to array-backed interfaces. Do not promise destination-only allocations for unknown-count arrays.

Enum defaults are `EnumMappingStrategy.ByName`, `UnmatchedEnumValuePolicy.Error`, and `EnumNumericConversion.Checked`. Names match exactly. `Throw` permits unmatched names with runtime `ArgumentOutOfRangeException`; by-value conversion uses the independent enum numeric policy. Domain-specific enum semantics belong in a converter.

A missing reference intermediate in `Child.State` maps to null when the destination enum is nullable, even when `State` is non-nullable. By-name unknown values throw with the configured path and original value; by-value preserves null before numeric conversion. Explicit converters retain precedence, and a nullable-input converter receives a missing path as null.

`ReferenceHandling.ThrowOnCycle` detects active-path reference cycles in recursive generated mappings and throws `LiteMapperCycleException`. Shared references on separate branches are not cycles and identity is not preserved. Without tracking, recursive runtime cycles can overflow the stack. The exception exposes `SourceType`, `DestinationType`, `MappingMethod`, and `MemberPath`.

Recursive declared mappings share one tracker. For an `A -> B -> A` component implemented by generated `MapA` and `MapB`, the cycle reports `MapA` at `B.A`. Value helpers forward tracker state but are not entered or boxed; a finite class-to-struct-to-class path maps normally.

Equal-arity tuple mappings are positional. `(int Number, string Text)` can map to `(long Id, string Label)` as `(7L, "seven")`; element names do not affect matching. Automatic tuple-to-object and object-to-tuple structural mapping reports `LITEMAPPER2004`.

## Diagnostics

Use the emitted diagnostic message to identify the affected declaration or member. Families: `0xxx` declarations/configuration, `1xxx` members/construction, `2xxx` nullability/conversions, `3xxx` mapping resolution, `4xxx` collections, `5xxx` updates, `6xxx` recursion, `7xxx` enums, `9001` internal generator failure. For an internal failure, reduce the case and report it without including proprietary data.

A method-specific fatal error leaves that method unimplemented but does not suppress independent valid mappings in the same mapper. Invalid mapper-wide configuration suppresses that mapper; unrelated mappers continue. Fix the reported error before expecting the consumer build to succeed.

Async mapping declarations report `LITEMAPPER0006`. For example, change `async partial Target Map(Source source);` to `partial Target Map(Source source);` and perform asynchronous work before mapping. Task-returning mappings and async converters remain unsupported.

Ordinary unmapped policies support `Ignore`, `Info`, `Warning`, and `Error`. Standard severity overrides work for target diagnostic `LITEMAPPER1001` and source diagnostic `1003` when source checking is enabled. Reported diagnostics identify the exact source or target member. These configurable diagnostics preserve valid generated implementations: suppressing or downgrading an ordinary unmapped diagnostic does not suppress generation and is not proof of complete mapping. Required, non-nullable, inaccessible, invalid, and otherwise mandatory target obligations remain hard non-configurable errors even under `Ignore`; `IgnoreTarget` and `UseTargetDefault` retain their existing validation, and initializers need explicit `UseTargetDefault` approval.

Projects requiring mandatory unmapped-target coverage should treat `LITEMAPPER1001` as an error in CI and audit or avoid project-wide suppression. The library cannot prevent an intentional consumer `.editorconfig` or other compiler severity override while this diagnostic remains configurable.

For configuration troubleshooting, use `1006` invalid source path, `1007` dotted target, `1008` duplicate target mapping, `1014` missing requested default, `1015` invalid ignored member, `1016` unsupported indexer selection, `2009` invalid selected converter signature, and `2011` ambiguous converters (all prefixed `LITEMAPPER`). Existing-target array updates report `4005`; get-only collection updates report `4004`. IDs `4003`, `6002`, and `6003` are not part of the diagnostic catalogue.

Unexpected planning failures normally report one sanitized `LITEMAPPER9001` per failing mapper. The documented `LiteMapper_TreatInternalGeneratorErrorsAsExceptions` development option exposes full exception details through compiler generator-failure reporting; redact local paths and proprietary details before sharing them.

To enable a diagnostic build property, also export it through a `CompilerVisibleProperty` item. For example, pair `<LiteMapper_EmitDebugMetadata>true</LiteMapper_EmitDebugMetadata>` in `PropertyGroup` with `<CompilerVisibleProperty Include="LiteMapper_EmitDebugMetadata" />` in `ItemGroup`. Apply the same pattern to `LiteMapper_IncludeGeneratedSourceComments` or `LiteMapper_TreatInternalGeneratorErrorsAsExceptions`; setting the property alone does not pass it to Roslyn.

## Breaking-change migration

- From 1.x or 2.x, check reliance on implicit root null guards, convention-only handwritten helpers, interface collection result types, tuple member names, former diagnostic IDs, and unmapped targets. Root guards require `GuardNonNullSource`, helpers require an explicit eligibility mechanism, equal-arity tuples map positionally, read-only sequence interfaces produce arrays, and ordinary unmapped targets default to `LITEMAPPER1001` errors.
- From 3.0.0, ambiguous `IgnoreCase` mappings that previously selected an exact member fail with `LITEMAPPER1004`. Select the source with `MapProperty.Source`, or use `Exact`/`ExactThenIgnoreCase` when exact-name priority is intended.
- From 3.0.0, corrected comparer selection can change collection equality behavior. Dictionary value-only conversions preserve compatible source key comparers; custom key or set-element conversions use destination default comparers. Verify lookups, membership, element counts, and duplicate-key exceptions.
- From 3.0.0, recheck entries sharing model pairs with distinct conversion, null, enum, or cycle policies. Corrected policy isolation can change results and expose previously masked `LITEMAPPER2001` or `LITEMAPPER2002` errors. Patch updates evaluate each guarded member getter once and preserve the selected nullable updater overload; review side-effectful getters and nullable/non-nullable updater overloads.
- From 3.0.0, explicit `MapProperty.Use` without `Source` consistently selects the root source. Recheck previously defaulted outputs, including under `UnmappedTargetMembers.Ignore`. In patch updates, an unrelated same-name nullable member no longer prevents the root converter from running.
- From 3.0.0, structural collection-element fallback preserves null for nullable element targets. With non-nullable element targets under `Throw`, that fallback rejects null with `InvalidOperationException` before helper/member access, rather than an incidental `NullReferenceException`; higher-precedence nullable-input converters still receive null. Nullable-root by-name enum failures now use the declared parameter name in `ParamName`. Recheck exception-type and `ParamName` assertions.

## Validation boundaries

- Representative package-consumer checks cover `netstandard2.0`, `net8.0`, `net9.0`, and `net10.0` with C# 9, C# 14, and `latest`. Compiler-host checks cover Roslyn 4.8, 4.14, and 5.9. These representative cases do not prove every feature on every compiler host.
- Native AOT is validated separately and requires platform linker prerequisites. For example, Linux publishing requires Clang and zlib development files.
