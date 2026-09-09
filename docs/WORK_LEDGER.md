# All-defects work ledger

The specification is authoritative. Goal: complete whole-project conformance review, repair every discovered defect after failing MSTest regressions, and verify the integrated result. Preserve all dirty work, especially user AGENTS.md and .tokensave/config.json. No commits, pushes, publication, external messages, or unapproved semantic decisions. PENDING-0002 through PENDING-0007 are approved/incorporated. Local W01-W10 acceptance is complete; remote CI remains unexecuted.

## Acceptance and dependencies

Each repair requires valid-input failing tests, minimal implementation, focused green tests, and accurate documentation. Local completion evidence includes the complete three-host Windows generator matrix, Windows consumers/packaging/trimming, controlled performance, and required Linux Native AOT. Remote CI remains unexecuted and is not described as local evidence. Any new normative ambiguity stops implementation and is recorded with exact references.

| Unit | Goal and class | Dependencies | Exclusive ownership/status | Acceptance |
|---|---|---|---|---|
| W01 | Complete nested updates; deep | None | Complete | Explicit selection, precedence, null/patch behavior, getter counts, exceptions, generated recursion/tracker forwarding, writable replacement proved. |
| W02 | Handwritten converter registration edge cases; scoped | None for tests; root integration after W01 | Complete | Legal ref-like/unsafe handwritten methods follow18.4 without enabling unsupported generated declarations. |
| W03 | Collection/nullability gaps; scoped | None for tests; root integration serialized | Complete | IReadOnlySet/IDictionary, dictionary comparer, Preserve policy, source-path exceptions and update collection behavior evidenced. |
| W04 | Recursive mapping gaps; deep | W01 | Complete | Legal mixed class/struct graph, indirect cycles, tracker reuse and exception paths. |
| W05 | Enum/tuple/operator gaps; scoped | W01 integration | Complete | Runtime unmatched Throw, positional tuples, custom mapping precedence and required unsupported cases. |
| W06 | Complete contract/diagnostic audit; verify | W01–W05 | Complete | Requirement-by-requirement review converted concrete defects into red-first repair units. |
| W07 | Benchmark scenarios/checks; scoped | Stable W01–W05 | Complete | 32-case plan, correctness fixtures and tested allocation/statistical comparison. |
| W08 | Controlled performance evidence; deep | W07 | Complete | Affinity-pinned baseline/candidate passed five scenarios with unchanged allocation and no review-threshold regression. |
| W09 | Documentation/skill reconciliation; scoped | W06/W08 | Complete | Consumer-only USAGE retained; decisions/plan/status/review/skill synchronized. |
| W10 | Final integrated validation; verify | W01–W09 | Complete locally | Three Roslyn hosts, full Windows suite with 12 consumers, and required Linux Native AOT passed; one Windows linker-prerequisite skip recorded. |

Final integrated checkpoint: 535 generator tests pass on each Roslyn 4.8/4.14/5.9 host. The Windows solution passes 602 with one Native AOT prerequisite skip; Linux Native AOT passes 1/1. The controlled affinity-pinned comparison passes five scenarios. Final read-only subagent checks found no remaining configured-path/nested defect and accepted the exact/fallback default-pair correction.

The ten planned packages were an estimate of known work; later regressions expanded them. Historical checkpoints below retain their original incomplete-state wording and revision boundaries.

## Delegation plan

Two independent test authors are sufficient while root owns shared generator changes. Both use scoped route, gpt-5.6-luna, medium effort, fork none: bounded new test files with explicit contracts and no execution. Escalate only after correcting context/ownership or identifying deeper uncertainty. No subagent may build, restore, edit shared files, or spawn further agents.

| Agent | Model/effort/fork | Unit | Accepted configuration/status |
|---|---|---|---|
| /root/handwritten_registration_edges | gpt-5.6-luna / medium / none | W02 | Test file returned; root red run exposed2 failures; implementation pending |
| /root/collection_contract_edges | gpt-5.6-luna / medium / none | W03 | Test file returned;3 cases passed/one diagnostic expectation under review; bounded same-file follow-up active |

Root combined red run:7 tests,3 failed/4passed (TRX13:45:51). Do not count all failures as product defects until assertion/fixture review completes. W01 explicit Use/path cases separately began2 failures, now10 focused passes (13:44:08); complete nested-update semantics remain open. Default4.8 selected; no live build process.

Earlier legacy converter_result_tests reported exhausted workspace credits; matrix_review was interrupted before writing its CR017 file. Root created the four initial cases locally. This does not establish that subsequent delegation can run; record actual outcomes.

## Latest focused repair checkpoint (2026-09-09)

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
### W01 nullability checkpoint (2026-09-09)

Nested updater patch/nullability and returned replacement cases are repaired and focused-green (51 tests, 14:08:04). Remaining W01 work includes recursion/tracker forwarding and any defects found by the full audit. W03 still includes broader source-path and collection update coverage.

### Delegation layer: W04 and W05 (2026-09-09)

Two agents are sufficient because these are the two independent, non-overlapping regression-audit surfaces now unblocked; root retains generator integration, documents, and all builds/restores. W04 is `deep`, routed to `gpt-5.6-terra` at medium effort, fork none, owning only new `RecursiveContractEdgeTests.cs`. W05 is `scoped`, routed to `gpt-5.6-luna` at medium effort, fork none, owning only new `EnumTupleOperatorEdgeTests.cs`. Both may inspect source/spec/tests and add valid MSTest regressions, but must not modify production/docs/existing tests, run builds/restores/tests, commit, push, publish, or spawn agents. Root accepts only cases tied to exact specification clauses and independently runs red/green validation.

Default Roslyn 4.8 full generator checkpoint after W01 nullability repairs: 452 passed, 0 failed/skipped (`w01-nullability-full-roslyn-4.8.0.trx`). The first sandboxed attempt was blocked by NuGet network policy despite `--no-restore`; the approved rerun passed. This is not final multi-host/platform evidence.

W04 agent `/root/recursive_contract_edges` returned its exclusive test file; root accepted and strengthened it with declared-mapping runtime coverage. W05 agent `/root/enum_tuple_operator_edges` returned its exclusive test file; root corrected invalid tuple/enum fixture shapes before accepting evidence. Both used their recorded model/effort/fork configuration and made no forbidden side effects. W04 and W05 implementation repairs are default-host green: 469 full generator tests, 0 failed/skipped. Final platform acceptance remains W10.

Next delegation route: W06 is an independent requirement-by-requirement verification pass, class `verify`, routed to `gpt-5.6-sol` at high effort with fork none because it spans the complete normative specification and must distinguish direct proof from indirect/missing evidence. One verifier is sufficient; root retains integration and will turn each confirmed implementation defect into a test-first repair unit. The verifier is read-only and may not build, restore, edit, commit, push, publish, or spawn agents.

W07 benchmark-scenario work is independent of the read-only W06 audit and production generator integration. It is `scoped`, routed to `gpt-5.6-luna` at medium effort with fork none. Exclusive ownership is new `NestedCycleAllocationBenchmarks.cs` and new `BenchmarkScenarioContractTests.cs`; the agent may not edit existing benchmark/tests/projects/docs or run builds/restores/tests. Acceptance is 15 additional scenarios (3 nested and 12 finite-recursive combinations) that bring the existing 17 to the planned 32, with setup-time output equivalence and contract tests that count/execute the scenario shape. Root retains comparison-policy implementation and all validation.

W06 returned a complete sections 1-30 static audit. It confirmed seven implementation candidates: nullable reference roots/elements, top-level null-collection Error, once-only ordinary source paths, converter-path null handling, escaped identifiers, and closed-type helper collisions. It also identified five catalogue diagnostics without direct tests. `3004` and `5001` have defined generator paths and remain repair work. `4003`, `6002`, and `6003` have no specification-defined valid triggers, producing PENDING-0006 and pausing further product implementation.

W07 is accepted after root corrected two test-shape assertions and a benchmark fixture name collision. The benchmark project builds with zero warnings/errors; six scenario/comparison-policy tests pass; an unrestricted Dry run discovered and executed all 15 new scenarios. The comparison gate has direct tests for scenario/environment mismatch, allocation blocking, statistical significance, and the 10%/20% throughput thresholds. W08 started a controlled Medium flat baseline from tag 1.0.0, which uses the same five competitors and pinned versions; the run was already in flight when PENDING-0006 was confirmed and is allowed to finish as validation evidence.

Null-policy test authoring was routed as `scoped` to `gpt-5.6-luna` at medium effort with fork none. The agent returned eight cases in new `NullPolicyContractEdgeTests.cs`; root corrected expected catalogue IDs to `2001` for root reference mismatch and `2003` for element mismatch. These cases remain unexecuted because product work stopped at PENDING-0006.

PENDING-0006 Option A was approved on 2026-09-09 and incorporated before implementation resumed. `4003`, `6002`, and `6003` are removed from the 1.0 catalogue and generator descriptors; `4006`, `0008`, and the section 17.4 value-type rule retain their existing roles.

The next independent unit is generation-safety regression authoring, class `scoped`, routed to `gpt-5.6-terra` at medium effort with fork none because source-path evaluation, keyword escaping, and closed-type helper identity need careful valid Roslyn fixtures. One agent is sufficient; it owns only new `GenerationSafetyContractTests.cs`, may not run builds/tests, and may not edit the shared generator or documents. Root owns null-policy red/green work, production integration, and all serialized commands.

### PENDING-0007 Option A and configured-path delegation (2026-09-09)

Option A is approved and incorporated into specification section 11.4. Success requires red-before-green MSTest coverage for nullable-input and non-null-input explicit converters, nullable nested-path preservation and once-only evaluation, path-induced collection policies, and once-only patch/update paths. Root exclusively owns `SPECIFICATION.md`, shared project documents, `LiteMapperGenerator.cs`, converter-path tests, integration, and every build/test command.

Three bounded test-authoring follow-ups are sufficient because they own separate new files and independent contract surfaces. Each is class `scoped`; existing agent runtimes are reused through bounded follow-ups, so the collaboration API exposes no new model/effort selection. Fork mode is follow-up/no fork. Agents may inspect repository files and create only their assigned test file. They may not edit production/docs/existing tests, run builds/restores/tests, commit, push, publish, or spawn agents.

| Agent | Model/effort/fork | Unit | Acceptance/status |
|---|---|---|---|
| `/root/source_path_investigation` | existing runtime / existing effort / follow-up | New `ConfiguredPathNestedContractTests.cs` | Valid nested nullable preservation and once-only fixtures tied to sections 9.4 and 14.3; pending |
| `/root/helper_collision_investigation` | existing runtime / existing effort / follow-up | New `ConfiguredPathCollectionContractTests.cs` | Default/Error/Preserve/Empty path-induced null collection fixtures tied to sections 6.3, 9.4, and 15.11; pending |
| `/root/matrix_review` | existing runtime / existing effort / follow-up | New `ConfiguredPathPatchContractTests.cs` | Getter-counted patch and selected nested-updater path fixtures tied to sections 9.4 and 16.3; pending |

Combined red evidence: 20 cases, 15 failed and 5 controls passed in `configured-path-red.trx`. The first integrated repair attempt passes all 20 cases in `configured-path-green-attempt1.trx`. A bounded read-only verifier follow-up is class `verify`, using `/root/matrix_review`'s existing runtime and effort with no fork; it reviews only the integrated configured-path implementation against sections 6.3, 9.4, 11.4, 12.4, 14.3, 15.11, and 16.3. Root continues serialized broader tests and retains all edits.

The verifier found seven adjacent gaps. Root added red-first cases for captured converters, nullable value captures, and nullable traversal across a non-nullable struct in new `ConfiguredPathCaptureEdgeTests.cs`. Two more bounded `scoped` follow-ups reuse existing runtimes/effort with no fork: `/root/source_path_investigation` owns new `ConfiguredPathAdditionalPatchTests.cs` for nonnullable-path once-only evaluation, nullable-destination required initialization, and non-patch updater Throw capture; `/root/helper_collision_investigation` owns new `ConfiguredRootConverterIdentifierTests.cs` for an escaped root-source converter parameter. Root retains generator changes and all commands.
