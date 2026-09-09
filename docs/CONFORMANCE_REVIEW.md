# Specification conformance review

`SPECIFICATION.md` is authoritative. This ledger records review evidence and local completion state; it does not alter product semantics or establish release readiness.

## Scope and evidence

- Review completed locally: 2026-09-09. Final generator suites pass 535 tests on each Windows Roslyn 4.8/4.14/5.9 host. The Windows solution passes 602 tests with one linker-prerequisite Native AOT skip; required isolated Linux Native AOT passes 1/1. Controlled affinity-pinned performance passes five scenarios. Remote CI remains unexecuted.
- A sections 1-30 static audit and final targeted re-audits are complete. Concrete findings were converted into failing MSTest regressions before repair. STATUS records exact revision boundaries and TRX evidence.
- The generator, diagnostic catalogue, generator declaration/capability tests, package-consumer tests, benchmark harness, project settings, and active `.github/workflows/ci.yml` were inspected. File line references below identify the inspected revision and may move during fixes.
- TokenSave status/context was consulted initially; current files were used to verify findings. Context7 did not identify a matching Mammoth.LiteMapper library, so unrelated library documentation was not used.
- The approved section 11.3 eligibility definition and section 23.3 unknown-count buffering exception are present in the specification. These previously reported questions are not open blockers.

## Final implementation findings

PENDING-0004 was approved and incorporated into sections 11.1/11.2/28.4: defaults select within the current mapping-method stage, so local mappings precede external converters. Implementation resumed with competing-method regressions. The usage guide contains consumer guidance only; this ledger owns implementation tracking.

| ID | Contract | Evidence and required correction |
|---|---|---|
| CR-017 | 14.4/22.13: explicit nested updates | Resolved: get-only/writable selection, source/root paths, registration precedence and ambiguity, overload identity preference, patch/Error/Throw nullability, returned nullable replacement, get-only rejection, and recursive tracker/path forwarding have runtime/diagnostic coverage. |
| CR-018 | 6.3/9.4/11.2/11.4/14.3/15.11/16.2/16.3: configured nullable paths | Resolved under approved PENDING-0007 Option A. Nullable converter inputs receive null; non-null inputs diagnose or check the full path. Nested and collection paths preserve policy, patch guards evaluate once, required construction uses valid null-preserving/non-null expressions, generated identifiers avoid user parameters, and duplicate defaults are checked within exact and unwrapped source pairs. |

Both final findings are resolved and included in the final validation evidence. Earlier findings are recorded separately below.

Build dependency repaired: three regressions proved affected Build.Tasks.Git10.0.300 in all shipping projects. SourceLink.GitHub now pins10.0.303 and resolves patched Build.Tasks.Git10.0.303; all three tests pass without suppressing audit. [Microsoft advisory](https://github.com/dotnet/sourcelink/security/advisories/GHSA-23fw-v26w-5fgq). Final local validation is recorded in STATUS.

Historical CR-016 checkpoint:29 registration cases began16 failures/13 controls; focused97 passed. Two follow-up ref-return regressions caught an overly strict new check and passed after correction. Its 429-test evidence is superseded by the final 535-test host runs in STATUS. Supported two-parameter mappings, unrelated converters and actual nested/private accessibility remain legal.

### Superseded source-audit checkpoint: sections 22.11–22.16

This checkpoint originally identified the get-only nested update path. Subsequent CR-017 and CR-018 regressions and repairs cover local/registered/default updater selection, identity retention, writable replacement, get-only rejection, and configured nullable paths.

CR-017 is implemented and final-suite green for eligible local/registered/default updaters, explicit Use, writable replacement, patch/Error/Throw behavior, returned replacements, get-only rejection, exception propagation, and recursive tracker forwarding. Get-only collections retain `4004`.

The invalid self-containing struct fixture was replaced with legal class-to-struct-to-class traversal. Update, collection, recursive, enum, tuple, converter-precedence, and configured-path regressions now cover the listed cases. Option A for PENDING-0006 removed `6003` because legal value traversal cannot trigger it.

## Resolved implementation findings

- CR-015: declaration cases began11 failures/nine controls; final22 new cases plus isolation/incremental/declaration suites pass58 after semantic enum validation, preserving enclosing type kinds, and fatal assembly-default gating. Fixed deficient M3 references instead of retaining syntax-based rejection. Constructor fixtures now compile before generation; runtime checks cover records/init/required/defaults/private access, greatest-arity/marked selection, fields and record-copy exclusion. Four read-only-field failures exposed illegal root/nested assignments; they now follow target policy and retain legal generated code. Its final integrated evidence is in STATUS.

- Approved PENDING-0005: seven new cases produced four failures/three controls before adding named-composite OR consistency validation. Final enum/nullable suites pass39 tests, including signed targets and independent healthy mappings. Target Read4/Write8 accepts Both12 and rejects Both16 with7002/no implementation. The specification was updated before implementation. Current full/platform evidence is recorded in STATUS.

- CR-014 edge cases:14 regressions produced12 failures/two controls before repair. Missing composite names, unchecked declared overflow/fallback, signed flag bit widths, ordinary alias conflicts, generated variable collisions, and once-only unknown-value path evaluation are repaired. Two unchecked alias-normalization controls also pass. Existing positive alias fixture now has matching source/target names and a runtime assertion. PENDING-0005 was subsequently approved and incorporated; final evidence is in STATUS.

- CR-010 nullable boxing and cross-enum follow-up: corrected boxing fixtures produced six failures/two controls; enum cases produced12 failures/three controls. Two later regressions caught oblivious-target annotation handling and a generated parameter-name collision. Boxing honors Error/Throw for explicitly non-null targets; nullable/oblivious reference targets preserve allowed null. Nullable enums preserve null/lift target types and map From.Ready=1 to To.Ready=9 by name at root/member/element boundaries, with Throw paths, unknown99 rejection, patch skipping, and once-only configured-path evaluation.

- CR-013: inherited interface properties and class/interface source paths, inherited ignore/default names, diamond deduplication, ambiguity and hiding, and explicitly selected inherited converters now have 16 passing cases. Initial 11 cases produced eight failures/three controls; three ambiguity/hiding cases also failed. An additional overload nullability control failed after initial repair and now passes with normal C# invocation binding. Inherited methods remain subject to accessibility/static restrictions and do not implicitly contribute mapper configuration.
- CR-004 converter-result numeric/operator follow-up: 14 new cases initially produced 12 failures/two disabled-policy controls. Eligibility now uses effective numeric/operator options; result emission reuses language conversion after a once-only null check. All 33 new and existing converter-contract cases pass, including checked overflow, unchecked wrapping, method overrides, nullable lifting/Error/Throw, operator invocation counts, root/element/dictionary boundaries, and exception identity.

- Package consumer provenance: two actual restore regressions failed when an alternate feed satisfied missing local packages. Source mapping now restricts Mammoth.LiteMapper* to LocalPackages, with isolated caches; both regressions and all 12 framework/language consumers pass. Final Windows suite: 340 passes, zero failures, one local AOT prerequisite skip; required Linux AOT: one pass, zero skips. Full evidence is in STATUS.

- Roslyn 5.9 async declaration compatibility: an existing 0006 assertion failed in the full matrix run. Three focused cases produced two failures/one pass before checking the syntax async modifier alongside IsAsync; all pass afterward, with no invalid implementation and an independent healthy mapping retained. Complete post-fix generator suites pass 290 tests each on Windows under Roslyn 4.8/4.14/5.9, zero skips. Shipping baseline remains 4.8; no public semantics changed.

- CR-012 visible-scope default follow-up: root/member/element regressions initially failed because local and external defaults were considered independently. A shared exact-pair check now reports 3002 across registered/local containers while independent healthy mappings still generate. All 17 precedence cases pass; later nullable exact/fallback pair regressions also pass.
- CR-004 nullable value and unsupported-type follow-up: ten nullable value converter cases initially produced nine failures/one control pass; all 19 converter-contract cases now pass. Seven unsupported-member/ref-like update cases failed before repair, and all 24 type-diagnostic cases now pass, including a handwritten Span member converter control. Selected nullable results are checked once before implicit widening; nullable targets retain null.

- Nullable update CR-011 repair: retain and render selected constructor arguments, preserve approved optional defaults, initialize required members legally, and avoid repeating constructor-bound setters when creating a destination. Existing destinations are mutated and returned unchanged in identity. Six added cases yielded two initial compilation failures/one passing default control, then three failing required-obligation cases; all now pass, including the later configured-path construction cases.

- CR-006/CV-001: per-candidate output avoids aggregate positional invalidation across files; successful equality ignores irrelevant declaration locations. All 21 tracked cases pass, including exact Array.Empty capability, population changes, and fresh-driver per-file determinism. Same-file mapper insertion/removal recreates Roslyn candidate identity in both production and isolated attribute-targeted probes. Under section 19.2's explicit "where possible" qualification, these cases require identical hints/content; other edits retain strict caching assertions. See DECISIONS for official Roslyn evidence and test-scope rationale. No product semantics changed.
- New-object CR-011 repair: constructor parameters reuse configuration/conversion planning with isolated candidate helpers/diagnostics, actual parameter nullability, compiler accessibility, and correct IgnoreCase matching. Ordinary self-typed constructors are no longer mistaken for record copy constructors. Constructor expressions participate in cycle tracking. Fourteen tests pass after initial 6 failures/3 passes plus recursive and nullable numeric follow-up failures. Nullable update creation is now covered by the follow-up repair above.
- CV-005/CV-006/CV-008: nine fingerprint fixtures pass after eight mutation regressions failed; actual all-six-package content comparison, public package API compatibility, and all three unchanged samples in clean primary-package projects pass in the packaging suite. NuGet-generated IDs/metadata filenames are normalized while payload and metadata content remain hashed. CI invokes these tests through its existing solution test job; no remote CI execution is claimed.
- CR-006/CV-001 initial checkpoint: 11 tracked tests failed before per-mapper emission existed, then all passed. Immutable MapperEmission values retain no compiler symbols or syntax trees. Diagnostic publication stays separate; option-rendering and output exceptions retain mapper-local sanitization/development rethrow. Later population-change and capability regressions are included in the resolved CR-006/CV-001 evidence above.
- Partial CR-010 and collection/type rendering repair: nullable-value tests initially yielded 12 failures/1 pass; follow-up collection cases all 4 failed because construction stripped nullable element types. Language conversion now unwraps nonnumeric nullable values under Error/Throw, patch updates skip null, and construction preserves nullable element types. Guid/DateTime cases also exposed unqualified generated types; namespace/containing types and collection elements now use qualified names. A root collection's explicit Empty strategy reaches collection handling instead of an earlier scalar null guard. Final combined validation is recorded in STATUS.
- Partial CR-004: `UnsupportedTypeDiagnosticTests` covers 1016 explicit indexers, 2008 dynamic/pointer/function-pointer signatures and ref-like structural creation, and 0006 named async converters. Initial 15 failures/1 pass included a positive Span converter blocked by the type-qualification defect. After repairs all 16 pass; valid handwritten ref-like converter and synchronous overload controls remain supported.
- CR-009 (6.4/6.5/10.3): explicit ignores no longer bypass mandatory targets; required members must satisfy C# construction obligations. Approved optional constructor defaults omit the argument even with a matching source. MapProperty conflicts with ignore/default settings produce 1008 in either attribute order. `MandatoryTargetConfigurationTests`: 13 failures/8 passes before repair, 21 passes afterward.
- CR-003 (12.1-12.3/20.2): canonical 2005/2006/2007 now have actual ambiguous-language/disabled-operator/disabled-narrowing behavior. Shared language conversion honors mapper/method options at root, member, element, and dictionary boundaries. Nullable numeric cases check null before casting, with 2001/2003 under Error. `NumericOperatorConversionTests`: 13 passes after test-first repairs, including 4 nullable cases that initially failed. Full constructor/general-nullable/precedence compliance remains separately open under CR-010/CR-011/CR-012.
- CR-005 (6.2/20.1): exact source/target severity policies and configurable Error descriptors preserve valid generated methods through Roslyn severity overrides; hard semantic errors remain non-configurable. Initial 17 tests yielded 6 failures/11 passes; 4 mandatory-nullability/default cases added 2 failures/2 passes. All 21 pass after repair, including actual tree-scoped compiler severity options. Ordinary unmapped non-nullable members now report 1002 even with an unapproved initializer; explicit IgnoreTarget remains tracked separately as CR-009.
- Partial CR-003/CR-004 repair: 16 canonical configuration/converter/update cases failed before repair and pass afterward. Named registered external converter lookup no longer emits a premature local failure; invalid named selections report 2009 once. Array destination updates reject every source shape; get-only collections, init-only members, and scalar read-only members retain distinct diagnostics. Existing milestone expectations were corrected only after these regressions failed.
- CR-002 (20.3): mapper planning now runs inside the failure boundary after reading the documented development option. An internal, stateless fault-injection hook proves real exceptions are sanitized to one mapper diagnostic while unrelated mappers generate; development mode exposes the original exception. `InternalFailureIsolationTests`: 1 failure/1 pass before repair, 2 passes afterward.
- CR-007 (19.8): mapping methods are ordered by full method identity during planning/rendering. The reversed syntax-tree overload regression failed before repair and passes afterward. Mapper output ordering remains covered by the declaration suite.
- CR-008: packaging/sample/usage tests share `TestProcess`, which drains both streams concurrently and applies one deadline to process exit and pipe closure. Dotnet children disable reusable build servers. Four bounded fixture cases (stderr pressure, sleeping child, exited parent retaining descendant pipe handles, environment settings) failed before repair and pass afterward. A descendant reparented after its direct parent exits cannot be terminated through that parent; retained pipes still cannot block the caller beyond the deadline. Cross-platform/final-suite evidence is recorded in `STATUS.md`.
- CR-001 (20.3): separated mapper-wide validation from method validation, planning, and recursive-graph diagnostics. Only valid method models are rendered; mapper-wide errors still suppress all methods in that mapper. `MethodFailureIsolationTests` covers invalid signatures, method configuration, conversion failures, and invalid mapper-wide configuration with an unrelated healthy mapper. Test-first evidence: 3 failures/1 pass before repair, 4 passes afterward. Tests also require that the only compiler error is the deliberately unimplemented invalid partial method. Final suite evidence is recorded in `STATUS.md`.

## Required validation gaps

| ID | Contract | Current evidence and missing proof |
|---|---|---|
| CV-003 | 22.5/22.17/29: consumer framework matrix | All12 clean-package combinations passed on Windows and Linux: netstandard2.0/net8/net9/net10 with C#9/14/latest, matching runtime execution and local package source mapping. Linux retry packaging56 passes includes the matrix after warning-helper repair. SDK/runtime/TRX evidence is in artifacts/validation/linux-20260909-cr016. Remote CI remains unexecuted. |
| CV-004 | 22.6/22.7/29: language and Roslyn matrices | Final current Windows generator535 tests pass on Roslyn4.8/4.14/5.9, zero failures/skips; default4.8 restored. Earlier Linux host/consumer matrices passed on their recorded revision, and current required Linux Native AOT passes. CI configuration exists but remote runs remain unexecuted. |
| CV-007 | 23.1/23.3/23.4/29: benchmark acceptance | Resolved locally. The 32-scenario harness and comparison-policy tests are present. The final back-to-back affinity-pinned Medium artifacts compare five flat scenarios: Manual 4.2054 ns to 4.1690 ns (-0.87%), LiteMapper 4.8502 ns to 4.8219 ns (-0.58%), unchanged 40-byte allocation; the comparison script passes all five. |
| CV-009 | 20.2/22.9-22.16/29: complete behavior/diagnostic coverage | Resolved by the sections 1-30 audit, red-first feature/diagnostic units, final 535-test host suites, and targeted re-audits. Descriptor existence or milestone labels alone were not used as proof. |

## Historical evidence inventory, superseded by final validation

### Declaration and construction follow-up (22.9/22.10)

Read-only audit after PENDING-0005 approval; no additional tests executed for this inventory. CR-015 owns the two source-identified defects. Remaining evidence gaps must not be inferred from existing green counts:

Subsequent verified update: CR-015 is repaired with22 new declaration cases. Milestone5ConstructionTests now validates input compilation and executes records/init/structs/private constructors; legal ambiguity/required fixtures replace malformed ones. Greatest-arity/marked selection, read-only fields, and accessible record-copy exclusion are covered. The original inventory below is historical for those completed items; remaining signature/registration breadth still needs review.

| Requirement | Existing evidence and next check |
|---|---|
| Static/instance declarations | ValidStaticAndInstanceMappersProduceDeterministicDeclarationSources checks text; consumers execute both. Add compilation to the declaration fixture. |
| Generic enclosing types, record/interface mapper rejection | InvalidDeclarationsReportStableDiagnostics covers generic mapper, abstract class, struct, non-partial declarations. Add omitted containing/type forms. |
| Invalid signatures | InvalidMappingMethodsReportDeclarationDiagnostics needs legal instance protected, in/out, and extra-parameter cases. Async isolation already has focused coverage. |
| Configuration/registration | Existing diagnostics cover empty/non-static registrations. Add valid enum-cast controls (CR-015), generic containers, and missing registration-type evidence. |
| Constructor selection | ParameterizedConstructorIsSelectedAndBoundMembersAreNotAssignedAgain executes one constructor. Add greatest-parameter-count and successful MappingConstructor override among satisfiable constructors. |
| Ambiguous constructors | ConstructorDiagnosticsUseStableIds uses duplicate Target(int) signatures; replace with a legal ambiguous shape and validate input compilation. |
| Records/init/structs | OptionalParametersRecordsInitRequiredAndValueTypesAreSupported checks text; compile/execute. Add a record copy constructor that would otherwise compete. |
| Required/read-only members | RequiredReadOnlyMembersRequireConstructorBindingUnlessConstructorSetsRequiredMembers uses invalid required get-only C#. Replace misleading evidence; retain valid SetsRequiredMembers tests. Add constructor-bound/unbound read-only fields. |
| Accessibility | ReferencedAssemblyConstructorAccessibilityUsesTheCompilation covers external access; execute PrivateConstructorIsUsableWhenMapperIsNestedInTargetType rather than checking text only. |

Optional-default and required-construction tests already provide runtime/fatal evidence; preserve them while closing these gaps.

- CV-002 resolved: `OfficialGeneratorTests` uses `CSharpSourceGeneratorTest<LiteMapperGenerator, MstestVerifier>` for exact diagnostic location/arguments and exact generated source/hint/compilation. Both cases pass with built-in MSTest assertions and local framework references. Final platform evidence is recorded in `STATUS.md`.
- Generator directly implements stateless `IIncrementalGenerator`; declaration tests assert the interface and absence of instance fields. Discovery filters attributed type syntax before semantic inspection (19.1/19.3).
- Generated-source snapshots exist for flat, explicit configuration, nested, enum, and update mappings. Custom harnesses compile/invoke generated code; normally compiled samples and package consumers exercise runtime mapping (22.3/22.4).
- Active CI runs Windows and Linux and requires Native AOT rather than allowing a prerequisite skip. Package publish tests use warnings-as-errors, execute trimmed/AOT outputs, and cover static/instance mappers, nested collections, and cycles (22.8/22.18).
- Package tests pack all three packages, install a clean one-package consumer, verify no generator/Roslyn runtime DLLs, and exercise independent abstractions consumption (22.19, except reproducibility gap above).
- Build settings enable deterministic builds, Source Link, repository metadata, symbol packages, and MIT package licensing; public API analyzer/baseline checks exist (22.20/24.6/24.8). These settings require artifact verification and do not independently prove reproducibility.

## Completion state

Whole-project conformance and the locally required section 29 evidence are complete on the reviewed revision. Remote Windows/Linux CI was not executed and must not be inferred from local validation; publication readiness still requires any separate release authorization and remote process the project chooses to require.

## Diagnostic repair map

This map records the initial audit. The current open/resolved lists above supersede its statements about missing descriptors and unexecuted regressions.

Bounded CR-003/CR-004 source review on 2026-09-08 found no additional normative blocker. Section 20.2 supplies the required meanings. The following are source-confirmed repair targets, not newly executed failing tests; add canonical regression cases and observe failures before fixing implementation. Function names identify sites because line numbers change during concurrent repairs. All numeric IDs below have the `LITEMAPPER` prefix. `3002 DuplicateDefaultMapping` has already been added and emitted; it is no longer a missing descriptor.

| Current site or behavior | Required diagnostic and regression |
|---|---|
| `ResolveSourcePath` uses `2005 InvalidMemberConfiguration` | `1006 InvalidSourcePath`: missing/empty/inaccessible/unreadable segment or prohibited method-call syntax; identify the failing segment. |
| `ParseExplicitConfigurations` combines invalid target cases under 2005 | `1007 TargetPathNotSupported` for dotted target; `0009 InvalidMethodConfiguration` for omitted/missing direct target or omitted Source without Use. |
| Duplicate target assignment uses 0013 | `1008 DuplicateTargetMapping`: repeated MapProperty target and conflicting explicit target configuration. Separate IgnoreTarget/UseTargetDefault sets must not conceal conflicts. |
| Requested target default absent uses 2005 | `1014 TargetDefaultMissing`: existing member has neither an applicable initializer nor an optional constructor-parameter default. |
| `ParseMemberNames` uses 2005 for invalid ignore | `1015 InvalidIgnoredMember`: invalid IgnoreSource/IgnoreTarget member. A nonexistent UseTargetDefault member is invalid method configuration; 1014 describes the absence of a real default. |
| Configuration lookup filters indexers out | `1016 IndexerNotSupported`: explicitly selecting an actual indexer must be distinguished from a missing member. |
| Illegal assignment lacks a specific descriptor | `1009 TargetMemberNotWritable`: explicit target with no legal setter/constructor binding. Keep 5004 for init-only updates and 5005 for unsupported nested mutation. |
| `ResolveNamedConverter`/`ResolveConverterSet` use `2006 InvalidConverter` | `2009 InvalidConverterSignature`: selected generic/void/ref-parameter/wrong-arity/type-incompatible method. Use 0006 for asynchronous converters. |
| Converter ambiguity uses 2007 | `2011 AmbiguousConverter`: equally ranked compatible converters or named overloads. |
| `IsUsableConverter` ignores nullable result and returned conversion is marked non-null | `2010 ConverterNullabilityMismatch`: selected nullable converter result cannot satisfy the target under the effective policy. This requires behavior validation, not just renaming. |
| `ResolveElementExpression` lacks nullable-element validation | `2003 NullableElementMismatch`: nullable element to non-null element under Error, including dictionary key/value conversion. Throw policy needs runtime coverage. |
| Unsupported generated types lack a dedicated validation branch | `2008 UnsupportedGeneratedType`: dynamic/ref-like/pointer/function-pointer generated behavior. Keep more specific collection diagnostics where applicable. |
| Visible mapping resolution filters unusable defaults before selecting defaults | `3003 DefaultMappingNotUsable`: intended default for the requested pair is inaccessible/incompatible. Inspect intended default candidates before silently discarding them. |
| Get-only collection update emits 5005 | `4004 GetOnlyCollectionUpdateNotSupported`. |
| Top-level array update emits 5001 | `4005 TopLevelArrayUpdateNotSupported`. |

Reclaim 2005 for `AmbiguousConversion` (for example equally applicable language operators), 2006 for `ExplicitOperatorDisabled`, and 2007 for `NarrowingNumericConversionDisabled`. Converter/mapping ambiguities remain 2011/3001. General numeric/explicit-operator options were absent from `EffectiveMappingOptions` at this inspection, so the last two need conversion-pipeline behavior as well as descriptors.

Existing `Milestone6ConfigurationAndConverterTests` expectations encode incorrect 2005/2006/2007/0013 uses, and `Milestone10ExistingTargetMappingTests` encodes 5005/5001 for the collection cases. Change those expectations with canonical failing regressions, not as isolated test adjustments. No runtime or diagnostic test was executed by this bounded audit.

## Historical focused repair checkpoints (2026-09-09)

W02: the two valid handwritten registration regressions failed before repair. Registration now permits legal pointer/function-pointer and ref-like handwritten signatures without relaxing generated declaration restrictions. HandwrittenRegistrationEdgeTests, ExternalRegistrationTests and UnsupportedTypeDiagnosticTests: 58 passed, 0 failed/skipped (13:47:28 TRX).

W03: Preserve nullable-source to non-nullable-target collection mapping previously omitted required diagnostic 2002. The root collection path now checks this contract. CollectionContractEdgeTests, Milestone9CollectionMappingTests, CrossTypeNullableEnumTests and NullableValueMappingTests: 69 passed, 0 failed/skipped (13:48:12 TRX). Example: a nullable source list with Preserve cannot map to a non-nullable target list and must report 2002.

Both delegated test files have returned and their focused checks passed after root fixes. Remaining W03 source-path/update coverage and W01 selection, null/patch and recursion behavior remain open. No final-revision full platform matrix has run after these changes. All-defects goal remains active; ten packages remain the planning structure, not ten completed tasks.

### Nested updater registration checkpoint (2026-09-09)

Sections11.2/11.5: two new valid-input MSTest regressions failed before repair (13:53:10 TRX): equal external registrations omitted3001; assembly registration produced13 instead of class-registration23. ResolveNestedUpdater now groups local/class/assembly candidates before default/ambiguity resolution. NestedUpdateMappingTests, Milestone10ExistingTargetMappingTests and ExternalRegistrationTests:43 passed,0 failed/skipped (13:53:39 TRX). USAGE and skill now include this verified example. W01 remains partial: null/patch, writable explicit selection, overload and recursive tracker cases remain. No normative change, no final full matrix after this repair. TokenSave status failed with Transport closed; direct source inspection used.

### Explicit nested updater overload checkpoint (2026-09-09)

Section11.4: ExplicitNestedUpdaterPrefersIdentitySourceOverload failed with3001 before repair (13:55:05 TRX). Explicit Use now prefers identity source compatibility before resolving remaining candidates. Example: Chosen(ChildSource, ChildTarget) adds10 to source3 and wins over Chosen(object, ChildTarget), producing13. Focused nested/update/registration suite:44 passed,0 failed/skipped (13:55:26 TRX). W01 remains partial; no final full-platform validation after this change. TokenSave remains unavailable (Transport closed), so source inspection was used.

### Writable nested updater checkpoint (2026-09-09)

Section14.4: explicit Use of ApplyChild on a writable child failed with2009 before repair (13:56:30 TRX). Writable reference properties now attempt explicitly selected updaters, retaining replacement by default and normal converter fallback when no updater resolves. Source Child.Value3 updates the original child to3 without changing identity. NestedUpdateMappingTests, Milestone10ExistingTargetMappingTests and Milestone6ConfigurationAndConverterTests:19 passed,0 failed/skipped (13:56:52 TRX). W01 null/patch and recursion remain open; final integrated platform checks are pending.

### Root-source nested updater checkpoint (2026-09-09)

Section11.4: omitted Source with explicit Use incorrectly reported1001 when the root had no same-name child (red13:57:57). Matching now allows the root parameter in the update path. Intermediate edits targeted the wrong block: one compile failure CS0103 and two20-test runs with1failure/19passes (13:59:05,13:59:30); these are not acceptance evidence. Corrected focused suite:20 passed,0 failed/skipped (13:59:56 TRX). Example: root Value3 supplies ApplyChild(Source, ChildTarget), setting target.Child.Value3. W01 remains partial; full integrated validation pending.
### Nested updater nullability evidence (2026-09-09)

Sections 12.4, 14.3, 14.4, and 16.3 now have runtime/diagnostic regressions for null patch skipping, Error/Throw source mismatch, writable nullable replacement, and get-only nullable rejection. Red evidence: 14:02:34, 14:05:56, and 14:07:43 TRX. Green evidence: 51 tests at 14:08:04, no failures/skips. Final integrated platform validation is pending.

### W04/W05 recursion and tuple evidence (2026-09-09)

Historical pre-Option-A regressions proved three defects: legal class-to-struct-to-class recursion incorrectly emitted the then-catalogued `6003`; tuple/object boundaries emitted member diagnostics rather than `2004`; mutually recursive declared mappings emitted direct calls without tracking. The old `struct Node { Node? Child; }` test was invalid C# and was removed. Repairs forward the tracker through value helpers without entering them, diagnose unsupported structural tuple boundaries, and generate tracked overloads for declared recursive components. Option A for PENDING-0006 later removed `6003` from the active catalogue. A runtime follow-up initially reported path `A`; path composition was corrected to `B.A`. Focused suites pass 74 tests for the first batch and 24 recursion tests after runtime strengthening. Full default Roslyn 4.8 generator suite: 469 passed, 0 failed/skipped (`w04-w05-full-roslyn-4.8.0.trx`). Enum Throw, positional tuples, marked-converter precedence, reference identity, separate branches, and indirect cycle details were conforming controls.
