# Work Handoff

## Key Components

- **TraceCapture** (`IR/TraceCapture.cs`): New `[ThreadStatic]` side-channel dictionary accumulating trace messages keyed by site UniqueId. Methods: `Log`, `LogFormat`, `Get`, `Clear`. Cleared at start of each analysis pass in `PipelineOrchestrator.AnalyzeAndGroupTranslated`.
- **InterceptorKind.Trace**: New enum value for `.Trace()` method calls. Detected in discovery, excluded from clause processing in ChainAnalyzer, does not generate an interceptor.
- **IsTraced flag**: Flows through `AnalyzedChain.IsTraced` → `AssembledPlan.IsTraced`. Set when any chain member has `Kind == InterceptorKind.Trace`.
- **QUARRY_TRACE gating**: `QuarryGenerator.HasQuarryTrace(Compilation)` checks consumer project preprocessor symbols. When defined + chain is traced → inline `// [Trace]` comments emitted. When `.Trace()` present but symbol missing → QRY034 warning.
- **TraceExtensions** (`src/Quarry/Query/TraceExtensions.cs`): 14 per-interface no-op extension methods returning the same builder type.
- **QRY034 diagnostic**: Warning when `.Trace()` found but `QUARRY_TRACE` not defined.
- **Old trace system removed**: `PipelineOrchestrator.TraceLog` StringBuilder and `__Trace.*.g.cs` file emission are fully removed.

## Completions (This Session)

1. aaf2b98: Add TraceCapture side-channel static class for per-site trace accumulation.
2. b95e084: Add InterceptorKind.Trace and discovery trace logging.
3. de30281: Add trace logging to CallSiteBinder and CallSiteTranslator.
4. 7481e62: Add IsTraced to AnalyzedChain and detect .Trace() in chain groups.
5. aa70d39: Add trace logging to SqlAssembler and CarrierAnalyzer.
6. 68bef58: Propagate IsTraced through pipeline, read QUARRY_TRACE, emit trace comments, add QRY034.
7. e2fa37a: Add .Trace() extension methods to runtime library.
8. 4605fd7: Remove old PipelineOrchestrator.TraceLog and __Trace file emission.

## Previous Session Completions

1. 80f00c8: Add Contains/IN expansion for runtime collection parameters.
2. 38f98f1: Add multi-entity join ON clause resolution for 3+ table joins.
3. 34b45ea–6549892: (30 prior commits) See handoff.md for full list.

## Progress

- Build: Clean (0 errors, warnings only)
- Tests: 2936 passed, 10 failed, 1 skipped out of 2947 (99.7% pass rate)
- Session start: 2934 passed, 13 failed (99.6%)
- Improvement: +2 tests fixed, 3 failures resolved (net -3 failures from trace work)
- Branch: feature/compiler-architecture (42 commits ahead of origin)

## Current State

10 remaining failures — all pre-existing, none caused by trace work:

- **Batch insert (3)**: ExecuteNonQueryAsync_BatchUsers, ExecuteNonQueryAsync_InsertMany_Users, InsertMany_MultipleEntities. Values() method not tracked as InterceptorKind. NOT YET ATTEMPTED.
- **Carrier generation (2)**: CarrierGeneration_EntityAccessorToDiagnostics_NoPrebuiltDispatch, CarrierGeneration_ForkedChain_EmitsDiagnostic. NOT YET ATTEMPTED.
- **Join issues (2)**: Join_Where_InClause (join + Contains/IN combo), NavigationJoin_InferredType (tuple type inference for joined result). Partially attempted — ON clause resolution works but WHERE rendering and type inference remain.
- **SetPoco (1)**: Update_SetPoco_UpdatesMultipleColumns. Integration test, parameter binding issue. NOT YET ATTEMPTED.
- **MutuallyExclusiveOrderBy (1)**: Select_MutuallyExclusiveOrderBy_ElseBranch. Else branch mask logic. NOT YET ATTEMPTED.
- **CountSubquery enum (1)**: Where_CountSubquery_WithEnumPredicate. Enum constant renders as CapturedValueExpr instead of integer literal. NOT YET ATTEMPTED.

## Known Issues / Bugs

- FileEmitter still contains inline TRACE comments in generated .g.cs output (lines 272-290). These are debug-era traces baked into the emitter output, separate from the new `// [Trace]` system. Should be cleaned up but are harmless.
- The trace comment emission in FileEmitter uses a chain index counter (`chainIdx`) that assumes chain groups and `_chains` are aligned 1:1. If standalone sites are interleaved, trace data may attach to wrong chains. Low risk since standalone sites are appended last.

## Dependencies / Blockers

None for the trace system. Remaining test failures are independent of trace work.

## Architecture Decisions

- **Side-channel over pipeline types**: Trace data is stored in `TraceCapture` (ThreadStatic dictionary) rather than on `RawCallSite`/`BoundCallSite`/`TranslatedCallSite`. This avoids breaking IEquatable caching in the incremental pipeline. The side-channel is always written (cheap dictionary puts) and only read at emission time for traced chains.
- **QRY034 instead of QRY030**: The plan specified QRY030, but that ID was already in use for `ChainOptimizedTier1`. Used QRY034 instead.
- **Always-capture, gate-at-emission**: TraceCapture.Log runs for ALL sites regardless of .Trace() presence. The filtering happens at Stage 6 (emission) where IsTraced and QUARRY_TRACE are checked. This keeps Stages 2-4 simple and avoids threading trace flags through the incremental pipeline.
- **Old trace system fully removed**: `PipelineOrchestrator.TraceLog` and `__Trace.*.g.cs` files are gone. Debug visibility now comes from the new TraceCapture system (opt-in per chain) rather than global trace files.

## Open Questions

- Should the TraceCapture side-channel be gated behind a static boolean to avoid dictionary allocations when no chains use .Trace()? Currently it writes for every site. For 500-site projects this is ~500 small lists — probably fine, but could be optimized.
- The FileEmitter's debug TRACE comments (lines 272-290) should be removed in a cleanup pass — they were useful during migration but now the new trace system supersedes them.

## Next Work (Priority Order)

### 1. Fix batch inserts (3 failures)
Values() method not tracked as InterceptorKind. Options: (a) make batch insert chains carrier-ineligible by detecting Values() calls during discovery, (b) add Values as an InterceptorKind and generate interceptors.

### 2. Fix carrier generation tests (2 failures)
CarrierGeneration_EntityAccessorToDiagnostics_NoPrebuiltDispatch, CarrierGeneration_ForkedChain_EmitsDiagnostic. Carrier-related diagnostic/generation differences.

### 3. Fix remaining join issues (2 failures)
- Join_Where_InClause: join + Contains/IN combination.
- NavigationJoin_InferredType: tuple type inference for joined result.

### 4. Fix remaining 3 failures
- SetPoco: Update_SetPoco_UpdatesMultipleColumns (parameter binding).
- MutuallyExclusiveOrderBy_ElseBranch (else branch mask logic).
- CountSubquery_WithEnumPredicate (enum as CapturedValueExpr).

### 5. Clean up FileEmitter debug traces
Remove inline TRACE comments from generated .g.cs output (lines 272-290 in FileEmitter.cs).
