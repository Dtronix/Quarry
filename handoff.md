# Work Handoff

## Key Components
- `src/Quarry/Query/QueryDiagnostics.cs` — Expanded diagnostic types (QueryDiagnostics, DiagnosticParameter, ClauseDiagnostic + 5 new types)
- `src/Quarry.Generator/CodeGen/TerminalEmitHelpers.cs` — Shared helpers for SQL dispatch, parameter locals, collection expansion, diagnostic emission. Now emits full DiagnosticParameter metadata and ClauseDiagnostic metadata (SourceLocation, BitIndex, BranchKind).
- `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` — Delegates to TerminalEmitHelpers; EmitCarrierToDiagnosticsTerminal uses shared path
- `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs` — Non-carrier diagnostic paths removed; EmitDiagnosticsTerminal now carrier-only
- `src/Quarry.Generator/CodeGen/CarrierAnalyzer.cs` — Trivial ToDiagnostics gate removed
- `src/Quarry.Generator/Generation/InterceptorCodeGenerator.cs` — EmitDiagnosticParameterArray/ClauseArray delegate to TerminalEmitHelpers; EmitNonCarrierDiagnosticClauseArray deleted
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — `RemapParameters` needs enrichment for IsEnum/IsSensitive (see Next Work #1)

## Completions (This Session)
- **Expanded DiagnosticParameter emission**: `EmitDiagnosticParameterArray` and per-clause parameter emissions now emit full constructor args: `typeName`, `typeMappingClass`, `isSensitive`, `isEnum`, `isCollection`, `isConditional`, `conditionalBitIndex`. Added `BuildParamConditionalMap` and `FormatParamMetadata` helpers.
- **Expanded ClauseDiagnostic emission**: `EmitDiagnosticClauseArray` now emits `sourceLocation` (project-relative path via `ComputeCommonDirectory`/`MakeProjectRelativePath`), `conditionalBitIndex`, and `branchKind` for each clause.
- **llm.md updated**: Removed `BatchInsertToSql` from interceptor kinds list.
- **Tests added**: 10 new tests for expanded metadata — TypeName, IsConditional, ConditionalBitIndex on params; SourceLocation, BitIndex, BranchKind on clauses. 2 tests pending (IsEnum, IsSensitive) awaiting column metadata propagation fix.

## Previous Session Completions
- **Phase 1**: Removed `ToSql()` from 16 public interfaces, all carrier bases, runtime builders, generator (`InterceptorKind.BatchInsertToSql` eliminated). Test call sites changed to `.ToDiagnostics().Sql`.
- **Phase 2**: Removed trivial ToDiagnostics carrier gate — all PrebuiltDispatch chains (including bare `db.Users().ToDiagnostics()`) now get carrier classes.
- **Phase 3**: Created `TerminalEmitHelpers.cs` consolidating 6 shared helpers (EmitSqlDispatch, EmitParameterLocals, EmitCollectionExpansion, EmitDiagnosticParameterArray, EmitDiagnosticClauseArray, EmitDiagnosticsConstruction). CarrierEmitter and InterceptorCodeGenerator delegate to these.
- **Phase 4**: Deleted non-carrier diagnostic code paths (non-carrier fallback in EmitDiagnosticsTerminal, EmitInsertDiagnosticsTerminal, EmitNonCarrierDiagnosticClauseArray).
- **Phase 5**: Expanded `QueryDiagnostics` with 17 new properties, added 5 new diagnostic types (SqlVariantDiagnostic, ProjectionColumnDiagnostic, JoinDiagnostic, ClauseSourceLocation, DiagnosticBranchKind). Expanded DiagnosticParameter with 7 new fields, ClauseDiagnostic with 3 new fields. EmitDiagnosticsConstruction now emits all metadata.
- **Phase 6**: Added 13 new tests covering TierReason, SqlVariants, ConditionalBitCount, CarrierClassName, IsDistinct, Limit/Offset, AllParameters.

## Progress
All original 6 phases complete. Expanded DiagnosticParameter/ClauseDiagnostic emission implemented. 2815 existing tests pass + 8 new tests pass. 2 new tests fail pending column metadata propagation fix. 0 errors, 2 pre-existing warnings.

## Current State
The emit-side work is complete — `EmitDiagnosticParameterArray` and `EmitDiagnosticClauseArray` emit all expanded fields. However, the **data flow** for `IsEnum` and `IsSensitive` on clause-level parameters is broken: these values are always `false` because `ParameterInfo` (created in `SqlExprClauseTranslator`) never populates them, and `ChainAnalyzer.RemapParameters` passes them through as-is.

## Known Issues / Bugs
- **IsEnum/IsSensitive not propagated for clause parameters**: `ParameterInfo` objects created in `SqlExprClauseTranslator.ExtractParameters` do not carry `IsEnum`, `EnumUnderlyingType`, or `IsSensitive`. The `RemapParameters` method in `ChainAnalyzer` copies these false values into `QueryParameter`. As a result, `DiagnosticParameter.IsEnum` and `DiagnosticParameter.IsSensitive` are always `false` for WHERE/OrderBy/etc. clause parameters. Only SET-clause parameters (from `UpdateInfo.Columns`) get correct values.
  - Impact: Medium — the diagnostic metadata surface reports incorrect values for these fields.
  - 2 tests fail: `ToDiagnostics_EnumParameter_HasIsEnumTrue`, `ToDiagnostics_SensitiveParameter_HasIsSensitiveTrue`.
- **RuntimeBuild emitter not updated**: `EmitRuntimeDiagnosticsTerminal` does not pass `disqualifyReason`. Runtime builders are scheduled for removal so this was intentionally skipped.
- **Multi-row batch insert diagnostics**: `ToDiagnostics()` on batch insert returns single-row template SQL, not expanded multi-row SQL.

## Dependencies / Blockers
None.

## Architecture Decisions
- **All diagnostic paths through carrier**: After removing the trivial gate (Phase 2), every PrebuiltDispatch chain gets a carrier class, even bare `db.Users().ToDiagnostics()`. This adds ~50 bytes per carrier but eliminates the non-carrier diagnostic code path entirely, preventing drift.
- **TerminalEmitHelpers as shared source of truth**: SQL dispatch, parameter locals, and collection expansion are now in one place. Both execution terminals and diagnostic terminals call the same methods. This is the core architectural change that prevents the diagnostic/execution output drift.
- **Project-relative source paths**: `ClauseSourceLocation.FilePath` emits relative paths computed via common directory prefix of all clause file paths, avoiding leaking absolute user directory structure.
- **Additive public API**: All new constructor parameters have defaults. Existing `QueryDiagnostics` construction call sites (runtime builders) remain source-compatible.
- **EmitDiagnosticsConstruction centralization**: The `new QueryDiagnostics(...)` call is now emitted in exactly one place (`TerminalEmitHelpers.EmitDiagnosticsConstruction`).

## Open Questions
- Should `ClauseSourceLocation` data come from `TranslatedCallSite.Bound.Raw.Location` or from `AssembledPlan.ClauseSites`? Currently using `clause.Site.FilePath/Line/Column`.

## Next Work (Priority Order)
1. **Fix IsEnum/IsSensitive propagation for clause parameters** — The data flow gap is in `ChainAnalyzer`. The fix requires matching clause parameters to their corresponding entity columns via the expression tree. For a WHERE clause like `u.Priority == priority`, the resolved expression tree contains `BinaryOpExpr(ResolvedColumnExpr("Priority"), "=", ParamSlotExpr(0))`. Walk the tree to find column-param pairs, then look up the column in `site.Bound.Entity.Columns` to get `IsEnum`/`IsSensitive`/`EnumUnderlyingType`. Implementation approach:
   - Add a helper method `EnrichParametersFromExpression(List<QueryParameter>, SqlExpr, EntityRef)` in `ChainAnalyzer`
   - Walk `BinaryOpExpr` nodes where one side is `ResolvedColumnExpr` and the other involves `ParamSlotExpr`
   - Match the `ResolvedColumnExpr.QuotedColumnName` (strip quotes) against `EntityRef.Columns[].ColumnName`
   - Copy `IsEnum`, `EnumUnderlyingType`, `IsSensitive` from the matched `ColumnInfo` to the `QueryParameter`
   - Call this after `RemapParameters` at line ~321 in `AnalyzeChainGroup`
   - Note: `QueryParameter` is currently immutable (readonly properties). Either make a `WithColumnMetadata()` copy method or change `RemapParameters` to accept column metadata.
2. **Update RuntimeBuild emitter** (deferred — runtime builders are being removed soon)
