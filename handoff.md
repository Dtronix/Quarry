# Work Handoff

## Key Components
- `src/Quarry.Generator/Utilities/TypeClassification.cs` — Single source of truth for all CLR type classification
- `src/Quarry.Generator/CodeGen/TerminalEmitHelpers.cs` — Shared `ResolveSiteParams` and `GetClauseParamCount` helpers
- `src/Quarry.Generator/IR/RawCallSite.cs` — `NestingContext` (renamed from `ConditionalInfo`)
- `src/Quarry.Generator/IR/TranslatedCallSite.cs` — Added `PipelineError` field for error surfacing
- `src/Quarry.Generator/CodeGen/CarrierParameter.cs` — Extracted from deleted `CarrierStrategy.cs`
- `src/Quarry.Generator/Models/InsertInfo.cs` — Added `EnumUnderlyingType` to `InsertColumnInfo`

## Completions (This Session)
- Wrote `impl-plan-gen-consolidation2.md` — 6-phase follow-up plan addressing code review findings

## Previous Session Completions
- **Phase 1**: Consolidated `TypeClassification` — unified 4 ValueType HashSets, 4 GetReaderMethod implementations, 3 IsUnresolvedTypeName implementations, 3 BuildTupleTypeName implementations. ~200 lines removed across 11 files.
- **Phase 2**: Extracted `ResolveSiteParams` helper — replaced 7 duplicated parameter offset loops. Added comprehensive 3-source offset counting to diagnostic methods.
- **Phase 3**: Renamed `ConditionalInfo` → `NestingContext` and `DetectConditionalAncestor` → `DetectNestingContext` across 7 files.
- **Phase 4**: Fixed enum underlying type in insert path. Extracted `EnrichParameterFromColumn` helper.
- **Phase 5**: Deleted duplicate `JoinBodyEmitter.GetJoinedBuilderTypeName`. Extracted `IsBrokenTupleType` helper.
- **Phase 6**: Added `PipelineError` to `TranslatedCallSite`. Replaced silent catch blocks with exception capture.
- **Phase 7**: Deleted dead `CarrierStrategy`/`CarrierField`/`CarrierStaticField`. Merged `EmitCarrierNonQueryTerminal`.

## Progress
Phase 1 consolidation: 7/7 phases complete (8 commits on `feat/generator-consolidation`). Phase 2 plan written, 0/6 phases implemented.

## Current State
Branch `feat/generator-consolidation` has all Phase 1 work. Phase 2 plan is in `impl-plan-gen-consolidation2.md` and covers 6 follow-up phases (A–F) addressing code review findings.

## Known Issues / Bugs
- **Stage 3 (Bind) silent drop**: Binding failures return `Empty` with only `Debug.WriteLine`. No QRY900 diagnostic. User sees runtime `InvalidOperationException`. Plan Phase A fixes via side-channel error bag.
- **`WithResolvedResultType` drops `PipelineError`**: Copy method constructs new `TranslatedCallSite` without forwarding `pipelineError`. Plan Phase A.2.3 fixes.
- **`ChainMemberSites` not checked for pipeline errors**: Error reporting loop only iterates `group.Sites`. Plan Phase A.2.2 fixes.
- **`IsUnresolvedResultType` nested tuple parsing**: `string.Split(',')` fails on `(int, (string, object))`. Plan Phase C fixes with depth-aware splitter.
- **`GetFieldValue<DateTimeOffset>` provider compatibility**: `ColumnInfo.GetReaderMethodByTypeName` changed from `GetValue` to `GetFieldValue<T>`. Needs integration test. Plan Phase D.
- **`IsUnresolvedTypeName` boolean parameter trap**: `treatObjectAsUnresolved` default caused identity-Select regression during Phase 1 development. Plan Phase B renames to separate methods.
- **Dead `GetReaderMethod(string, out bool)` overload**: Zero callers. Plan Phase A.2.5 removes.

## Dependencies / Blockers
- None. All work is self-contained within the generator project.

## Architecture Decisions
- **Side-channel error bag for Stage 3**: Incremental pipeline `SelectMany` cannot carry per-site errors because failed Bind produces no `BoundCallSite`. A static `ConcurrentBag` scoped by generation is the pragmatic solution. The bag is global (not per-file), so errors may be reported in the first file's output stage call.
- **`IsUnresolvedTypeName` vs `IsUnresolvedTypeNameLenient`**: Separate methods replace boolean parameter. Self-documenting names prevent the identity-Select regression class of bugs.
- **`BuildTupleTypeName` always wraps in parens**: The plan suggested returning raw type for single columns, but callers depend on tuple syntax. Optimization removed.
- **`ColumnInfo.GetReaderMethodByTypeName` uses `GetFieldValue<T>`**: Correctness fix per ADO.NET spec. Pending integration test verification.
- **Phase 6 captures exceptions on `TranslatedCallSite` only**: Stage 4 (Translate) carries errors through the pipeline. Stage 3 (Bind) uses a side-channel bag since there's no site to attach to.

## Open Questions
- Should `ColumnInfo.GetReaderMethodByTypeName` be dialect-aware for `DateTimeOffset`? Current implementation is dialect-agnostic. Only needed if a provider fails with `GetFieldValue<DateTimeOffset>`.
- Phase 7.1 (ChainResultTypeResolver extraction) and 7.2 (subquery parameter walker unification) — continue deferring or include in a future plan?

## Next Work (Priority Order)
1. **Phase A: Pipeline error reliability fixes** — Side-channel error bag for Stage 3, forward PipelineError in copy methods, fix error location, check ChainMemberSites, remove dead overload.
2. **Phase B: Rename IsUnresolvedTypeName** — Split into `IsUnresolvedTypeName` + `IsUnresolvedTypeNameLenient`. Mechanical rename, 8 files.
3. **Phase C: Fix nested tuple parsing** — Depth-aware comma splitting in `IsUnresolvedResultType`.
4. **Phase D: DateTimeOffset integration test** — Verify `GetFieldValue<DateTimeOffset>` round-trips correctly across providers.
5. **Phase E: Remaining Phase 5 emitter sub-steps** — Compound wrapping helper, EmitCarrierClauseBody consolidation, terminal return type helpers, insert column binding helper.
6. **Phase F: TypeClassification unit tests** — ~80-100 unit tests covering all consolidated methods.
