# Work Handoff — #140 Collection Parameter Collision Fix

## Key Components

| File | Role |
|------|------|
| `src/Quarry/Internal/ParameterNames.cs` | **New** — Pre-computed `@pN`/`$N` string arrays (256 entries per dialect) |
| `src/Quarry/Internal/CollectionSqlCache.cs` | **New** — Immutable cache entry for expanded collection SQL |
| `src/Quarry.Generator/CodeGen/TerminalEmitHelpers.cs` | **Modified** — Segment parser, inline builder emitter, per-param shift diagnostics |
| `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` | **Modified** — SQL dispatch rewrite, per-param shift command binding |
| `src/Quarry.Tests/Integration/CollectionScalarIntegrationTests.cs` | **New** — 7 SQLite integration tests |
| `src/Quarry.Tests/SqlOutput/CollectionParameterCollisionTests.cs` | **New** — 5 cross-dialect SQL output tests |

## Completions (This Session)

1. **ParameterNames + CollectionSqlCache runtime types** — zero-alloc param name lookup, thread-safe cache
2. **SQL segment parser** (`ParseSqlSegments`) — tokenizes `{__COL_PN__}` and `@pN`/`$N` for all 4 dialects
3. **Inline builder emission** (`EmitInlineSqlBuilder`, `EmitCollectionPartsPopulation`) — StringBuilder.Append per segment
4. **EmitCarrierSqlDispatch rewrite** — cache check + inline builder for collection-bearing carriers, `_sqlCache` field
5. **EmitCarrierCommandBinding fix** — `__bindShift` incremental variable per-param (not global `__colShift`)
6. **Diagnostic emission fix** — `__diagShift` incremental + `ComputeShiftExprForIndex` for clause diagnostics
7. **Removed EmitCollectionExpansion** — dead code after dispatch rewrite
8. **12 new tests** — all passing, full suite 2495 tests with zero regressions

## Previous Session Completions

- Design discussion transcript (`6cc73e9`)
- Implementation plan (`impl-plan.md`)

## Progress

**All 12 implementation steps from the plan are complete.** The fix is functionally done and all tests pass.

## Current State

The branch `fix/140-collection-param-collision` has 8 commits (excluding the design discussion). Ready for review/PR.

## Known Issues / Bugs

- **Diagnostic clause SQL fragments still use `string.Replace`**: Section 1 of `EmitDiagnosticClauseArray` (lines ~290-340 in TerminalEmitHelpers.cs) still uses `string.Replace` for expanding collection tokens within per-clause diagnostic SQL fragments. This is display-only (diagnostics, not execution) and has no collision risk since the fragment names are correct. Converting to inline builder would be a polish item, not a correctness fix.

- **MySQL __colShift computed but unused in SQL**: For MySQL, `__colShift` accumulates correctly but scalar SQL rendering doesn't use it (MySQL uses positional `?`). The shift is used for the cache hash and cache entry storage. No bug, just dead computation.

## Dependencies / Blockers

None. The fix is self-contained.

## Architecture Decisions

- **Per-parameter incremental shift (`__bindShift`) vs global `__colShift`**: The impl plan proposed using a single `__colShift` for all command binding. This was **wrong** when scalars appear before collections in the expression (scalar at GlobalIndex 0, collection at GlobalIndex 1 → scalar needs shift 0, not the total shift). Fixed by introducing `__bindShift` that accumulates during the command binding loop, and `__diagShift` for diagnostics. The `__colShift` from the preamble is still used for pagination params (which always come after all chain params) and the cache entry.

- **Cache per (carrier, mask)**: Each carrier has a `_sqlCache` array indexed by mask value. Single entry per slot (MRU). Benign race on store — no locks needed. This matches the plan.

- **Parts population before builder**: Collection `__col{N}Parts` arrays are populated before the switch/builder, using the correct accumulated shift at each collection's position. The builder then uses these pre-computed parts via `string.Join`. This avoids re-computing parts in the builder while keeping shift arithmetic correct.

## Open Questions

- **PR scope**: Should `impl-plan.md` be committed to the branch or excluded from the PR?
- **Follow-up**: Should diagnostic clause fragment expansion be converted from `string.Replace` to inline builder for consistency? (No correctness impact.)

## Next Work (Priority Order)

1. **Create PR** for `fix/140-collection-param-collision` → `master`
2. **Optional polish**: Convert diagnostic clause fragment expansion (Section 1 of `EmitDiagnosticClauseArray`) from `string.Replace` to inline builder for consistency
3. **Optional**: Add tests for multiple collections in a single Where clause (e.g., `ids1.Contains(u.UserId) && ids2.Contains(u.OrderId)`)
