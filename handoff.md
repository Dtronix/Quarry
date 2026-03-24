# Work Handoff

## Key Components
- `src/Quarry/Query/QueryDiagnostics.cs` — Expanded diagnostic types (QueryDiagnostics, DiagnosticParameter, ClauseDiagnostic + 5 new types)
- `src/Quarry.Generator/CodeGen/TerminalEmitHelpers.cs` — Shared helpers for SQL dispatch, parameter locals, collection expansion, diagnostic emission. Emits full DiagnosticParameter metadata and ClauseDiagnostic metadata (SourceLocation, BitIndex, BranchKind).
- `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` — Delegates to TerminalEmitHelpers; EmitCarrierToDiagnosticsTerminal uses shared path
- `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs` — Non-carrier diagnostic paths removed; EmitDiagnosticsTerminal now carrier-only
- `src/Quarry.Generator/CodeGen/CarrierAnalyzer.cs` — Trivial ToDiagnostics gate removed
- `src/Quarry.Generator/Generation/InterceptorCodeGenerator.cs` — EmitDiagnosticParameterArray/ClauseArray delegate to TerminalEmitHelpers; EmitNonCarrierDiagnosticClauseArray deleted
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — `RemapParameters` + `EnrichParametersFromColumns` propagate IsEnum/IsSensitive from entity column metadata to clause parameters

## Completions (This Session)
- **Fixed IsEnum/IsSensitive propagation for clause parameters**: Added `EnrichParametersFromColumns` in `ChainAnalyzer` that walks the resolved expression tree (BinaryOpExpr, InExpr, LikeExpr) to find column-parameter pairs, then looks up column metadata from `EntityRef.Columns` to enrich `QueryParameter` with `IsEnum` and `IsSensitive`. Called after `RemapParameters` at line ~320 in `AnalyzeChainGroup`. The 2 previously failing tests (`ToDiagnostics_EnumParameter_HasIsEnumTrue`, `ToDiagnostics_SensitiveParameter_HasIsSensitiveTrue`) now pass.

## Previous Session Completions
- **Phase 1**: Removed `ToSql()` from 16 public interfaces, all carrier bases, runtime builders, generator (`InterceptorKind.BatchInsertToSql` eliminated). Test call sites changed to `.ToDiagnostics().Sql`.
- **Phase 2**: Removed trivial ToDiagnostics carrier gate — all PrebuiltDispatch chains (including bare `db.Users().ToDiagnostics()`) now get carrier classes.
- **Phase 3**: Created `TerminalEmitHelpers.cs` consolidating 6 shared helpers (EmitSqlDispatch, EmitParameterLocals, EmitCollectionExpansion, EmitDiagnosticParameterArray, EmitDiagnosticClauseArray, EmitDiagnosticsConstruction). CarrierEmitter and InterceptorCodeGenerator delegate to these.
- **Phase 4**: Deleted non-carrier diagnostic code paths (non-carrier fallback in EmitDiagnosticsTerminal, EmitInsertDiagnosticsTerminal, EmitNonCarrierDiagnosticClauseArray).
- **Phase 5**: Expanded `QueryDiagnostics` with 17 new properties, added 5 new diagnostic types (SqlVariantDiagnostic, ProjectionColumnDiagnostic, JoinDiagnostic, ClauseSourceLocation, DiagnosticBranchKind). Expanded DiagnosticParameter with 7 new fields, ClauseDiagnostic with 3 new fields. EmitDiagnosticsConstruction now emits all metadata.
- **Phase 6**: Added 13 new tests covering TierReason, SqlVariants, ConditionalBitCount, CarrierClassName, IsDistinct, Limit/Offset, AllParameters. Added 10 tests for expanded DiagnosticParameter/ClauseDiagnostic metadata.
- **Expanded DiagnosticParameter emission**: `EmitDiagnosticParameterArray` and per-clause parameter emissions now emit full constructor args: `typeName`, `typeMappingClass`, `isSensitive`, `isEnum`, `isCollection`, `isConditional`, `conditionalBitIndex`.
- **Expanded ClauseDiagnostic emission**: `EmitDiagnosticClauseArray` now emits `sourceLocation`, `conditionalBitIndex`, and `branchKind` for each clause.
- **llm.md updated**: Removed `BatchInsertToSql` from interceptor kinds list.

## Progress
All 6 original plan phases complete. IsEnum/IsSensitive propagation fix complete. 2882 tests pass (2826 main + 56 analyzer), 0 failures, 0 errors, 2 pre-existing warnings.

## Current State
All planned work from `impl-plan-to-diagnostics-unification.md` is complete. The IsEnum/IsSensitive data flow gap has been fixed. No items are in progress or blocked.

## Known Issues / Bugs
- **RuntimeBuild emitter not updated**: `EmitRuntimeDiagnosticsTerminal` does not pass `disqualifyReason`. Runtime builders are scheduled for removal so this was intentionally skipped.
- **Multi-row batch insert diagnostics**: `ToDiagnostics()` on batch insert returns single-row template SQL, not expanded multi-row SQL.
- **EnumUnderlyingType not enriched from columns**: The `EnrichParametersFromColumns` method sets `IsEnum` and `IsSensitive` from column metadata but does not populate `EnumUnderlyingType` since `ColumnInfo` doesn't store it as a string. This doesn't affect diagnostic correctness — `IsEnum` is correctly reported. The inline cast optimization in the carrier emitter won't fire for these parameters, but the default `(object?)` boxing path handles enum values correctly at runtime.

## Dependencies / Blockers
None.

## Architecture Decisions
- **All diagnostic paths through carrier**: After removing the trivial gate (Phase 2), every PrebuiltDispatch chain gets a carrier class, even bare `db.Users().ToDiagnostics()`. This adds ~50 bytes per carrier but eliminates the non-carrier diagnostic code path entirely, preventing drift.
- **TerminalEmitHelpers as shared source of truth**: SQL dispatch, parameter locals, and collection expansion are now in one place. Both execution terminals and diagnostic terminals call the same methods. This prevents diagnostic/execution output drift.
- **Project-relative source paths**: `ClauseSourceLocation.FilePath` emits relative paths computed via common directory prefix, avoiding leaking absolute user directory structure.
- **Additive public API**: All new constructor parameters have defaults. Existing `QueryDiagnostics` construction call sites remain source-compatible.
- **Expression tree walking for column metadata enrichment**: Rather than modifying `ParameterInfo` or `SqlExprClauseTranslator`, the enrichment happens post-`RemapParameters` by walking the resolved expression tree. This keeps the extraction and enrichment concerns separate — extraction doesn't need entity context, enrichment matches parameters to columns via the expression structure.

## Open Questions
- Should `ClauseSourceLocation` data come from `TranslatedCallSite.Bound.Raw.Location` or from `AssembledPlan.ClauseSites`? Currently using `clause.Site.FilePath/Line/Column`.

## Next Work (Priority Order)
1. **Update RuntimeBuild emitter** (deferred — runtime builders are being removed soon)
2. **Populate EnumUnderlyingType from column metadata** — Would require either storing the underlying type name in `ColumnInfo` or reverse-mapping from `ReaderMethodName`. Low priority since the diagnostic metadata already reports `IsEnum` correctly and the execution path handles enum values via boxing.
