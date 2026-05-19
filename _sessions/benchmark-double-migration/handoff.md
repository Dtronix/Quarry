# Work Handoff: benchmark-double-migration

## Key Components

- **Generator core fix:** `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs`
  — `ResolveAggregateClrType` reordered (column lookup first, gated SemanticModel
  fallbacks second/third) and Sum/Avg call sites changed from the `"decimal"`
  default to `"object"` so `ChainAnalyzer.BuildProjection` enrichment kicks in.
- **Generator detector contract:** `src/Quarry.Generator/Utilities/TypeClassification.cs`
  — already recognizes `"?"` as an unresolved-type sentinel in both
  `IsUnresolvedTypeName` (strict) and `IsUnresolvedTypeNameLenient` (lenient).
  No detector changes have been made yet; the typed-marker rename is the
  next step.
- **Stage 4 enrichment:** `src/Quarry.Generator/Parsing/ChainAnalyzer.cs`
  (`BuildProjection`, `TryResolveAggregateTypeFromSql`) — already handles
  aggregate-type enrichment correctly for any sentinel that `IsUnresolvedTypeName`
  accepts. No changes needed here.
- **Regression tests:** `src/Quarry.Tests/Generation/AggregateTypeResolutionTests.cs`
  (NEW) — five tests covering Sum over `Col<double>`/`Col<decimal>`/`Col<int>`/
  `Col<long>` and Avg over `Col<double>`. All passing.
- **Benchmark migration files:** un-applied. Schema/entity/DTO/seed changes
  for the decimal→double migration are NOT yet on this branch; they were
  reverted during INTAKE so Phase 1 could be implemented in isolation.

## Completions (This Session)

- IMPLEMENT Phase 1 (complete):
  - Added `TypeClassification.UnresolvedTypeMarker = "?"` with XML-doc
    explaining the Stage 1 → Stage 4 enrichment contract.
  - Replaced all 8 Sum/Avg `"object"` defaults at the aggregate call sites
    (`GetSqlAggregateInfo` Sum/Avg, `GetJoinedAggregateInfo` Sum/Avg,
    `GetWindowFunctionInfo` Sum/Avg, `GetJoinedWindowFunctionInfo` Sum/Avg)
    with the named constant.
  - Min/Max defaults left as `"object"` (deferred per plan Known Follow-Ups #1).
  - All 5 `AggregateTypeResolutionTests` still green; full suite green
    (146 + 201 + 3143 = 3490).

## Previous Session Completions

- INTAKE: worktree created from `master@08d8323`; baseline tests green
  (3477/3477); workflow.md initialized with problem statement, baseline,
  and decisions.
- DESIGN: investigated `ProjectionAnalyzer.ResolveAggregateClrType` and the
  Stage 1 / Stage 4 division of work; identified that Roslyn's overload
  resolution against Error-typed arguments was producing a fabricated
  `decimal` return type; ratified the reorder + stricter-gate approach.
- PLAN: 5 phases written.
- IMPLEMENT Phase 1 (partial):
  - Applied reorder + gate to `ResolveAggregateClrType`.
  - Changed 8 Sum/Avg default arguments from `"decimal"` to `"object"`
    across regular aggregates, joined aggregates, window aggregates, and
    joined window aggregates.
  - Added `AggregateTypeResolutionTests.cs` (5 tests).
  - Full suite green.

## Progress

| Phase | Status | Notes |
|-------|--------|-------|
| 1 — generator fix + tests | **Complete** | Reorder/gate + typed-marker `UnresolvedTypeMarker` landed; all 5 unit tests + full suite green. |
| 2 — schema/entity/DTO/seed → double | **Complete** | OrderSchema/OrderItemSchema → `Col<double>`; EfOrder/EfOrderItem → `double`; 5 DTOs migrated + `DapperOrderLagDto` deleted; DatabaseSetup seed literals → `double`. Quarry.Benchmarks does not compile until Phase 3 (expected per plan). |
| 3 — reader call updates + restore disabled files | In progress | Depends on Phase 2. |
| 4 — remove obsolete comments | Not started | Depends on Phase 3. |
| 5 — full suite validation | Not started | Depends on Phases 1–4. |

## Current State

Phase 1 is complete and ready to commit:

- `src/Quarry.Generator/Utilities/TypeClassification.cs` — added
  `UnresolvedTypeMarker = "?"` constant with XML-doc.
- `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs` — reorder/gate
  on `ResolveAggregateClrType` (prior WIP) + 8 Sum/Avg call sites now
  reference `TypeClassification.UnresolvedTypeMarker` instead of `"object"`.
- `src/Quarry.Tests/Generation/AggregateTypeResolutionTests.cs` — 5 tests.

The Phase 1 work spans two prior WIP commits (`892312d`, `3d9eb46`) plus
the rename made in session 2. The rename will be a follow-up commit on
top of the WIP; the squash merge in FINALIZE collapses them all.

**Failed approaches** noted from the session:

- *Original DESIGN: "reorder only"* — applied first, didn't make tests
  pass. Showed that `ResolveAggregateClrType`'s priority is moot when
  `columnLookup` is empty (Stage 1 syntax-only); the real bug is that
  the `"decimal"` default suppresses Stage 4 enrichment.
- *Stricter gate Try 3* — applied, but functionally inert in Stage 1
  (argResolved is always false there). Kept anyway as future-proofing.

## Known Issues / Bugs

The fixed bug itself: schema with any non-`decimal` numeric column type
for an aggregated property (e.g. `Col<double> Total` + `Sql.Sum(o.Total)`)
silently miscompiled the carrier interface as `IQueryBuilder<Order, decimal>`
and emitted `Func<Order, decimal>` interceptor signature, producing
`CS9144 signature mismatch` against the user's `Func<Order, double>` call
site. No runtime data corruption — compile error at downstream consumers.

## Dependencies / Blockers

None.

## Architecture Decisions

- **Stage 1 stays syntax-only.** Passing EntityRegistry into Stage 1 would
  remove the need for the unresolved-type sentinel entirely but would
  broaden the incremental-pipeline cache invalidation. Deferred to
  follow-up (plan.md Known Follow-Ups #4).
- **Typed marker is a constant, not a discriminated union.** The full
  refactor to a `ResolvedClrType` type-safe representation is the right
  end state but touches `ProjectedColumn`, `ProjectionInfo`, and every
  consumer. Out of scope for this branch (plan.md Known Follow-Ups #2).
- **Reorder + gate kept even though sentinel default is sufficient for
  the current bug.** Future-proofs against (a) joined contexts where
  column lookup IS populated and (b) Roslyn changing its overload-resolution
  heuristics against Error-typed arguments.
- **Cross-dialect SQL output test deferred.** Bug is dialect-independent
  (CLR type only); test infrastructure cost (DDL changes across 4
  containers) outweighs marginal coverage gain.

## Open Questions

- Should `TypeClassification.UnresolvedTypeMarker` also replace the bare
  `"?"` usages elsewhere in TypeClassification.cs? Quick scan during
  DESIGN suggests yes, but verify exhaustively during the rename pass.
- Min/Max also default to `"object"` (and work correctly via enrichment).
  Should they migrate to the typed marker now or wait for the broader
  follow-up #1? Recommendation: leave them; their consumers are wider
  than the aggregate-type system and a careful audit is the right
  follow-up scope.

## Next Work (Priority Order)

1. **Apply the typed-marker rename** to complete Phase 1:
   - Add `public const string UnresolvedTypeMarker = "?";` to
     `TypeClassification.cs` with XML-doc explaining usage and the
     "Stage 1 → Stage 4 enrichment" contract.
   - Replace `"object"` at the 6 Sum/Avg call sites in `ProjectionAnalyzer.cs`
     with `TypeClassification.UnresolvedTypeMarker`. Line refs to verify
     by Grep before editing:
     - `GetSqlAggregateInfo` — Sum (line ~1811), Avg (line ~1821)
     - `GetJoinedAggregateInfo` — Sum (line ~2565), Avg (line ~2574)
     - `GetWindowFunctionInfo` — Sum (line ~2780), Avg (line ~2783)
     - `GetJoinedWindowFunctionInfo` — Sum (line ~2842), Avg (line ~2845)
   - Rerun `Quarry.Tests` filter `AggregateTypeResolutionTests` — must
     remain green.
   - Run full suite — must remain at 3482/3482.
   - Commit Phase 1 with message:
     `Quarry.Generator: fix aggregate CLR-type resolution for non-decimal columns`
2. **Phase 2 — schema/entity/DTO/seed migration to double.** Follow
   plan.md Phase 2 file list. The stashed work from INTAKE was reverted;
   re-apply by editing files directly (the changes are small and the
   plan lists them all). Build benchmark project; expect Phase 1
   restoration to be required for the 3 disabled files to come back —
   but since Phase 1 is already committed, the benchmark project should
   build with the new schema.
3. **Phase 3 — reader call updates + restore disabled files.**
   - 7 reader call updates (`GetDecimal` → `GetDouble`).
   - Restore `AggregateSumBenchmarks.cs`, `AggregateAvgBenchmarks.cs`,
     `WindowRunningSumBenchmarks.cs` from `git show` of an earlier
     commit (they no longer exist in the working tree; the `.cs.disabled`
     versions were dropped during INTAKE cleanup). Each needs the
     decimal→double conversion in its body.
   - Smoke-test one benchmark from each category.
4. **Phase 4 — remove obsolete documentation comments.** 7+1 files.
5. **Phase 5 — full suite validation.** `dotnet test -c Release` and
   benchmark smoke pass.
