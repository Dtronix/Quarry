# Work Handoff: carrier-codegen-efficiency

## Key Components
- `src/Quarry.Generator/IR/SqlExprBinder.cs` — Boolean negation fix (Phase 1)
- `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` — Dead cast removal (Phase 2), `_sqlCache` readonly (Phase 3), reader field extraction (Phase 5)
- `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs` — Batch param names (Phase 4), reader field reference (Phase 5)
- `src/Quarry/Internal/ParameterNames.cs` — Existing cache to integrate (Phase 4)

## Completions (This Session)
- Full codebase scan across 194 generated files identifying 8 efficiency items + 1 SQL bug
- Design decisions made for all items (see workflow.md Decisions)
- Plan written with 5 implementation phases

## Previous Session Completions
(none — first session)

## Progress
| Phase | Status | Description |
|-------|--------|-------------|
| 1 | Not started | SQL Server boolean negation fix in SqlExprBinder.cs |
| 2 | Not started | Remove dead `var __c` in CarrierEmitter.cs line 268 |
| 3 | Not started | Add `readonly` to `_sqlCache` emission in CarrierEmitter.cs line 1191 |
| 4 | Not started | Replace `"@p" + __paramIdx` with `ParameterNames.AtP()` in TerminalBodyEmitter.cs line 576 |
| 5 | Not started | Extract reader lambda to static carrier field |

## Current State
Implementation not yet started. Plan is approved and ready.

## Known Issues / Bugs
- Phase 1 will change expected test output for all dialects (NOT(col) → col = 0/FALSE)
- Phase 5 requires threading `ResultTypeName` into `EmitCarrierClass` — may need to add parameter

## Dependencies / Blockers
None. All phases are independent except Phase 5 depends on understanding the carrier class emission pipeline.

## Architecture Decisions
- **Bool negation:** Fix at binder level (not renderer) — match existing bare-boolean handling pattern
- **`var __c`:** Guard with `(clauseParams?.Count > 0) || clauseBit.HasValue`
- **Param names:** Always use `ParameterNames.AtP()` regardless of dialect (ADO.NET ParameterName is always @p format)
- **Reader dedup:** Static readonly field on carrier class, referenced by name from terminals
- **Deferred:** Carrier deduplication (union interfaces approach) → tracking issue
- **Deferred:** Incremental SQL mask rendering (split shared/varying) → tracking issue

## Open Questions
- Phase 5: Need to verify `chain.ResultTypeName` is the correct fully-qualified type for the `Func<DbDataReader, T>` field declaration. May need `chain.ProjectionInfo.ResultTypeName` instead.

## Next Work (Priority Order)
1. Phase 1: Edit SqlExprBinder.cs lines 139-145 (UnaryOpExpr case). Add NegateBoolean helper. Update test expectations.
2. Phase 2: Wrap line 268 in CarrierEmitter.cs with conditional. Run tests.
3. Phase 3: Add `readonly` to line 1191. Run tests.
4. Phase 4: Replace line 576 in TerminalBodyEmitter.cs. Run tests.
5. Phase 5: Add reader field to EmitCarrierClass, update EmitCarrierExecutionTerminal line 747 to reference it. Run tests.
6. Create 2 tracking issues for deferred items.
