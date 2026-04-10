# Work Handoff: 222-parameterize-window-function-args

## Key Components
- `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs` — Core changes: `ResolveScalarArgSql`, `FormatConstantForSql` helpers; updated `BuildNtileSql`, `BuildLagLeadSql`, `BuildJoinedLagLeadSql`; `List<ParameterInfo>` threaded through entire analysis call chain (both single-entity and joined paths).
- `src/Quarry.Generator/Models/ProjectionInfo.cs` — Added `ProjectionParameters` property (`IReadOnlyList<Translation.ParameterInfo>?`).
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — Added `EnrichmentLambda` setting for Select sites with captured projection params; passed `semanticModel` to `AnalyzeJoinedSyntaxOnly`.
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — **NOT YET MODIFIED** — needs Phase 3 (parameter merging).
- `src/Quarry.Generator/CodeGen/CarrierAnalyzer.cs` — **NOT YET MODIFIED** — needs Phase 4 (extraction plans).
- `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` — **NOT YET MODIFIED** — needs Phase 4 (Select interceptor capture).

## Completions (This Session)
- **Phase 1**: Added `ResolveScalarArgSql` / `FormatConstantForSql` to ProjectionAnalyzer. Updated build methods to use constant extraction instead of `.ToString()`. Fixed `0m` → `0` in test assertions. Added `ProjectionParameters` to `ProjectionInfo`.
- **Phase 2**: Threaded `List<ParameterInfo>? projectionParams` through entire analysis call chain (AnalyzeCore → AnalyzeExpression → AnalyzeInvocation → GetAggregateInfo → GetWindowFunctionInfo → Build methods). Same for joined path. Passed SemanticModel to `AnalyzeJoinedSyntaxOnly`. Set `EnrichmentLambda` on Select sites when projection has captured params.

## Previous Session Completions
None — first session.

## Progress
- Phase 1: COMPLETE (commit 67dceae)
- Phase 2: COMPLETE (commit e7c1445)
- Phase 3: NOT STARTED
- Phase 4: NOT STARTED
- Phase 5: NOT STARTED

## Current State
Phases 1-2 lay all the groundwork. The projection analyzer now:
1. Correctly formats compile-time constants as SQL literals (stripping C# suffixes)
2. Creates `ParameterInfo` objects for non-constant expressions (captured variables)
3. Stores them in `ProjectionInfo.ProjectionParameters`
4. Sets `EnrichmentLambda` so `DisplayClassEnricher` can resolve display class info

What remains is the **consumer side** — making the rest of the pipeline actually USE these parameters.

## Known Issues / Bugs
None — all 3090 tests pass.

## Dependencies / Blockers
None.

## Architecture Decisions
- **Placeholder format**: `@__proj{N}` for local projection parameter indices, remapped to `@p{globalIndex}` in ChainAnalyzer. Avoids collision with `@p` WHERE params.
- **Reuse `Translation.ParameterInfo`**: Instead of a new class, projection params use the same `ParameterInfo` class as WHERE params. This lets the extraction plan builder handle them uniformly.
- **SemanticModel threading**: `AnalyzeJoinedSyntaxOnly` now accepts optional `SemanticModel?` for `GetConstantValue` support in joined queries.
- **`ResolveScalarArgSql` fallback**: When `projectionParams` is null OR expression is complex (not a simple variable), returns null → runtime fallback. Only simple `ILocalSymbol` / `IParameterSymbol` identifiers are captured.

## Open Questions
None.

## Next Work (Priority Order)

### Phase 3: ChainAnalyzer parameter merging
In `ChainAnalyzer.AnalyzeChainGroup()`, after clause params are collected (~line 984) and before pagination (~line 1270):
1. Find the Select clause site's `ProjectionInfo.ProjectionParameters`
2. Call `RemapParameters(projectionParams, ref paramGlobalIndex)` to create `QueryParameter` objects with global indices
3. Replace `@__proj{N}` → `@p{globalIndex}` in `ProjectedColumn.SqlExpression` strings
4. `ProjectedColumn` is a record with `init` properties — use `with { SqlExpression = ... }` to create copies
5. Add remapped params to the `parameters` list

### Phase 4: Carrier extraction and Select interceptor
1. In `CarrierAnalyzer.BuildExtractionPlans()` (~line 260): add condition for `cs.Kind == InterceptorKind.Select` with projection parameters. Create `CapturedVariableExtractor` objects using `cs.DisplayClassName` and `cs.CapturedVariableTypes`.
2. In `CarrierEmitter`: when Select has an extraction plan, generate the interceptor to capture the lambda (not `_`) and extract values from `projection.Target`.

### Phase 5: Tests
1. Add test for variable parameterization: `int n = 3; Sql.Ntile(n, ...)` → SQL has `NTILE(@p0)`.
2. Add test for const variable: `const int n = 3; Sql.Ntile(n, ...)` → SQL has `NTILE(3)` (inlined).
3. Add carrier generation test verifying UnsafeAccessor and carrier fields.
