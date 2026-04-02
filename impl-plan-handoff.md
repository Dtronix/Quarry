# Work Handoff — #140 Collection Parameter Collision Fix

## Key Components

| File | Role |
|------|------|
| `src/Quarry/Internal/ParameterNames.cs` | **New** — Pre-computed `@pN`/`$N` string arrays (256 entries per dialect) |
| `src/Quarry/Internal/CollectionSqlCache.cs` | **New** — Immutable cache entry for expanded collection SQL |
| `src/Quarry.Generator/CodeGen/TerminalEmitHelpers.cs` | **Modified** — Segment parser, inline builder emitter, per-param shift diagnostics |
| `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` | **Modified** — SQL dispatch rewrite, per-param shift command binding |
| `src/Quarry.Tests/Integration/CollectionScalarIntegrationTests.cs` | **New** — 7 SQLite integration tests |
| `src/Quarry.Tests/SqlOutput/CollectionParameterCollisionTests.cs` | **New** — 8 cross-dialect SQL output tests |

## Completions (This Session)

1. Code review fixes (commit `24403f3`):
   - Removed dead `colOrd` variable and `GetCollectionOrdinal` method
   - Diagnostic clause SQL fragments now shift scalar `@pN` references to match expanded SQL
   - Hash primes fixed: replaced `1099511628211L` and `2654435761L` with int-fitting primes (prevented compile error for 3+ collection chains)
   - Pagination binding switched from `__colShift` to `__bindShift` for self-contained coupling
   - Shift variable invariant documented in `EmitCarrierCommandBinding`
   - Added cross-dialect scalar-before-collection test (PG/MySQL/SS)
   - Added multi-collection test (two `Contains()` in same `Where`)
   - Added multi-collection execution test

## Previous Session Completions

1. **ParameterNames + CollectionSqlCache runtime types** — zero-alloc param name lookup, thread-safe cache
2. **SQL segment parser** (`ParseSqlSegments`) — tokenizes `{__COL_PN__}` and `@pN`/`$N` for all 4 dialects
3. **Inline builder emission** (`EmitInlineSqlBuilder`, `EmitCollectionPartsPopulation`) — StringBuilder.Append per segment
4. **EmitCarrierSqlDispatch rewrite** — cache check + inline builder for collection-bearing carriers, `_sqlCache` field
5. **EmitCarrierCommandBinding fix** — `__bindShift` incremental variable per-param (not global `__colShift`)
6. **Diagnostic emission fix** — `__diagShift` incremental + `ComputeShiftExprForIndex` for clause diagnostics
7. **Removed EmitCollectionExpansion** — dead code after dispatch rewrite
8. **12 new tests** — all passing, full suite 2495 tests with zero regressions
9. Design discussion transcript (`6cc73e9`) and implementation plan (`impl-plan.md`)

## Progress

**All implementation steps complete. Code review fixes applied.** Full suite: 2498 tests, zero failures.

## Current State

Branch `fix/140-collection-param-collision` has 10 commits (excluding design discussion). Ready for PR.

## Known Issues / Bugs

- **MySQL `__colShift` computed but unused in SQL**: For MySQL, `__colShift` accumulates correctly but scalar SQL rendering doesn't use it (MySQL uses positional `?`). The shift is used for the cache hash and cache entry storage. No bug, just dead computation.

## Dependencies / Blockers

None. The fix is self-contained.

## Architecture Decisions

- **Three shift variables (`__colShift`, `__bindShift`, `__diagShift`)**: The impl plan proposed a single `__colShift` for everything. This was wrong when scalars appear before collections. `__bindShift` accumulates during command binding (per-param, incremental). `__diagShift` does the same for diagnostic parameter arrays. `__colShift` is set by the inline SQL builder and used for the cache entry. Invariant: all three equal the same total after processing all chain params. Pagination uses `__bindShift` (self-contained, no cross-method dependency).

- **Cache per (carrier, mask)**: 1-entry MRU per mask slot. Hash is `len * prime` (single collection, bijective — zero collisions) or `XOR of (len * prime_i)` (multi-collection, 48 collisions in 1M-pair space — failure mode is crash, not silent corruption). Benign race on store.

- **Diagnostic clause fragments shift scalar refs**: After collection token expansion, scalar `@pN` references are replaced with shifted names via `ComputeShiftExprForIndex`. This ensures diagnostic SQL matches the actual expanded SQL.

## Open Questions

- **PR scope**: Should `impl-plan.md` be committed to the branch or excluded from the PR?

## Next Work (Priority Order)

1. **Create PR** for `fix/140-collection-param-collision` → `master`
