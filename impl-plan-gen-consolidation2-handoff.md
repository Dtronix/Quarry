# Work Handoff

## Key Components
- `src/Quarry.Generator/IR/PipelineErrorBag.cs` — side-channel error bag for Stage 3 Bind failures
- `src/Quarry.Generator/IR/TranslatedCallSite.cs` — copy methods now forward PipelineError
- `src/Quarry.Generator/QuarryGenerator.cs` — pipeline error reporting with accurate locations
- `src/Quarry.Generator/Utilities/TypeClassification.cs` — type classification (IsUnresolvedTypeName split, nested tuple fix)
- `src/Quarry.Generator/CodeGen/TerminalEmitHelpers.cs` — shared terminal helpers (return type, executor, insert binding)
- `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` — FormatCarrierFieldAssignment helper
- `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs` — uses consolidated helpers
- `src/Quarry.Tests/Utilities/TypeClassificationTests.cs` — 135 unit tests
- `src/Quarry.Tests/Integration/DateTimeOffsetIntegrationTests.cs` — 3 integration tests
- `src/Quarry.Tests/Samples/EventSchema.cs` — DateTimeOffset test entity

## Completions (This Session)
- **Phase A**: Pipeline error reliability fixes (PipelineErrorBag, ChainMemberSites check, PipelineError forwarding in copy methods, error location fix, dead overload removal)
- **Phase B**: Split `IsUnresolvedTypeName` into strict and lenient variants, all 9 call sites updated
- **Phase C**: Depth-aware tuple parsing in `IsUnresolvedResultType` with `SplitTupleElements` and `FindMatchingCloseParen` helpers; recursive nested tuple support
- **Phase D**: DateTimeOffset integration tests — EventSchema entity, 3 round-trip tests on SQLite confirming `GetFieldValue<DateTimeOffset>` works
- **Phase E**: Emitter consolidation — `FormatCarrierFieldAssignment`, `ResolveTerminalReturnType`, `ResolveCarrierExecutorMethod`, `GetInsertColumnBinding` helpers
- **Phase F**: 135 TypeClassification unit tests covering all public methods

## Previous Session Completions
- Phase 1 generator consolidation (7 commits): IR pipeline stages 2-5, pipeline orchestrator, carrier emitter, terminal emitter, dead code removal

## Progress
6 of 6 phases complete. All 2462 tests pass (2401 Quarry.Tests + 61 Analyzers.Tests).

## Current State
All planned work is implemented and committed on `feat/generator-consolidation`. Working tree is clean.

## Known Issues / Bugs
- **E.2 (EmitCarrierClauseBody consolidation) deferred**: The `delegateParamName` parameter cannot be removed because SetAction callers pass `"_"` when there are no captured params, but the extraction plan's `DelegateParamName` is always `"action"`. Reconciling this requires either: (a) making `CarrierAnalyzer.GetDelegateParamName` aware of the `hasCapturedParams` state, or (b) threading the actual param name through the extraction plan.
- **Pipeline error location accuracy**: The `PipelineErrorBag` errors use `LinePositionSpan` with zero-length spans. The diagnostic will point to the correct line/column but won't highlight a range. This is acceptable for internal error diagnostics (QRY900).

## Dependencies / Blockers
None.

## Architecture Decisions
- **PipelineErrorBag uses `[ThreadStatic]`** instead of `ConcurrentBag`: The incremental generator pipeline runs on a single thread per compilation. `[ThreadStatic]` follows the same pattern as `TraceCapture` and avoids the overhead of concurrent collections. The bag is drained in `EmitFileInterceptors`, which is the first output stage that runs after all binding is complete.
- **IsUnresolvedTypeName split** instead of enum parameter: Two explicitly named methods (`IsUnresolvedTypeName` + `IsUnresolvedTypeNameLenient`) are clearer than a boolean or enum parameter. The strict version is the "default" for chain analysis; lenient is only for projection analysis where `"object"` is a valid placeholder.
- **E.2 deferred rather than forced**: The plan flagged this as risky and gated on verification. The `delegateParamName` mismatch between callers (`"_"` for uncaptured SetAction) and the extraction plan (`"action"`) would silently break generated code. Deferring avoids a regression with minimal cost — the duplication is localized.
- **GetInsertColumnBinding consolidates call args only**: The single-insert and batch-insert loops differ in parameter naming (`__p{i}` vs `__p`), indentation, and counter management. Only the 9-argument `GetColumnValueExpression` call was extracted as a helper, keeping the loops readable.

## Open Questions
- Should `CarrierAnalyzer.GetDelegateParamName` be made context-aware (passing `hasCapturedParams`) to enable E.2 consolidation?
- Should TimeSpan/DateOnly/TimeOnly integration tests be added? SQLite support for these via `GetFieldValue<T>` is less well-tested. The generator produces correct code, but provider behavior may vary.

## Next Work (Priority Order)
1. **E.2 EmitCarrierClauseBody consolidation** — requires reconciling `delegateParamName` with extraction plan. Suggested approach: update `CarrierAnalyzer.GetDelegateParamName` to accept a `hasCapturedParams` flag, or always use `"action"` and adjust the method signature emission to match.
2. **TimeSpan/DateOnly/TimeOnly integration tests** — extend EventSchema or create a new schema to verify these `GetFieldValue<T>` types round-trip correctly on SQLite.
