# Workflow: scope-trest-tuple-projection

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: REMEDIATE
status: active
issue: discussion
pr: 282
session: 1
phases-total: 3
phases-complete: 3

## Problem Statement

Quarry's tuple-projection support has never been exercised at or beyond the C# `ValueTuple` flattening boundary (7 elements). The widest tuple projection in the test suite is 6 elements (`CrossDialectCompositionTests.cs:685`, a GroupBy/Having chain selecting 6 aggregate columns). At 8+ elements, the C# compiler nests via `ValueTuple<T1..T7, TRest>` where `TRest` is itself another `ValueTuple`, and member access (`tuple.Item8`) is rewritten to `tuple.Rest.Item1`. Neither `ProjectionAnalyzer` nor `ReaderCodeGenerator` contain explicit logic for this nesting; the gap has been masked by `JoinArityHelpers.MaxJoinArity = 6`, which caps the natural "join everything and project" path below the boundary.

This becomes load-bearing for an upcoming feature direction: an analyzer code-fix that rewrites anonymous-type projections (`new { ... }`) to named tuples. Anonymous types have no arity cap, so a simple single-table `.Select(u => new { 8+ cols })` rewrite would land users directly in `TRest` territory, surfacing whatever latent issues exist in projection analysis and reader emission.

This workflow's deliverable is **scope-only + demonstrative tests**:
1. A scoping document (`scope.md` in the session directory) enumerating the work `ProjectionAnalyzer` and `ReaderCodeGenerator` would need to perform to handle `TRest` correctly.
2. New tests at 7-, 8-, and 10-element tuple projections that demonstrate current behavior (passing where supported, failing/diagnostic-emitting where not).
3. **No generator changes.** Implementation of TRest support is out of scope and would be a follow-up workflow.

### Baseline (master @ aebf88d)
- Quarry.Tests: 3022 passed, 0 failed (53s).
- Pre-existing warnings: NU1903 (System.Security.Cryptography.Xml 9.0.0 vulnerability), CS0649 on `Chain_6.P1` in generated CrossDialectDistinctOrderBy interceptors.

## Decisions

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-04-26 | Scope-only deliverable: doc + failing tests, no generator changes | Confirms the gap with executable evidence before committing to a fix; keeps the rewrite-anon-to-tuple path unblocked for design |
| 2026-04-26 | Test pattern: cross-dialect SQL assertion + SQLite execution (same as `CrossDialectCompositionTests`) | Catches both projection-side SQL emission bugs and reader-side materialization bugs at the `TRest` boundary |
| 2026-04-26 | Test arities: 7 (last flat), 8 (first nested), 10 (deeper into TRest) | 7 confirms boundary still works flat; 8 hits first nesting; 10 verifies behavior past Item8 |
| 2026-04-27 | Add a 16-element test on top of 7/8/10 | Crosses a second TRest nesting boundary (`(T1..T7, ValueTuple<T1..T7, ValueTuple<T1, T2>>)`) — guards against deep-nesting regressions in the rewrite path |
| 2026-04-27 | Test file: `CrossDialectWideTupleTests.cs` (new) | Self-contained file dedicated to TRest-boundary projections; avoids bloating CompositionTests |
| 2026-04-27 | Projection shape: joined Users×Orders (×OrderItems for 16) with mostly entity columns | Mirrors the realistic anon→tuple rewrite scenario (many columns from a join), not the GroupBy/aggregate shape already covered by the existing 6-element test |
| 2026-04-27 | If tests pass: keep as regression guards + document latent risks in scope.md | Passing tests are evidence the boundary works today; scope.md still enumerates audit areas (e.g., naive `Split(',')` in `IsValidTupleTypeName:1650`) that could regress it |

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE 2026-04-26 | DESIGN 2026-04-27 | Created branch `scope-trest-tuple-projection`, recorded baseline (3022 passing on master @ aebf88d), confirmed scope-only + failing-tests deliverable, explored ProjectionAnalyzer / ReaderCodeGenerator / TypeClassification, identified that reader emission is flat (C# folds to TRest) and likely works at runtime, found one inconsistency (`IsValidTupleTypeName:1650` uses naive `Split(',')` instead of depth-aware `SplitTupleElements`), transitioned to PLAN |
| 2 | PLAN 2026-04-27 | IMPLEMENT 2026-04-27 | Wrote plan.md with three phases (tests → scope.md → commit). Rebased on origin/master (picked up f1968dc — cross-dialect coverage, baseline now 3024). Implemented `CrossDialectWideTupleTests.cs` with 4 tests at arities 7/8/10/16; first run failed all 4 due to author-side `ORDER BY` assertion-string bug (omitted explicit `ASC`); fixup made all 4 pass. Wrote scope.md with audit table verdicts filled in. Verdict: **TRest works at runtime today** — the rewrite path is unblocked modulo a small follow-up to harden `IsValidTupleTypeName` against nested generic args |
| 3 | REVIEW 2026-04-27 | REMEDIATE 2026-04-27 | Agent review surfaced 24 findings: 6A/0B/0C/18D. As accepted: (#7) scope.md self-contradiction, (#13) tighten Pg/My/Ss assertions in 7-elem test, (#14) add nullable-inside-Rest test, (#16) add mid-Rest assertions in 16-elem test, (#17) add CTE wide-tuple test, (#24) update Integration framing once #17 lands |
| 4 | REMEDIATE 2026-04-27 | DESIGN 2026-04-27 | Implemented #7, #13, #14, #16. Adding #17 (`Tuple_PostCteWideProjection`) surfaced TWO pre-existing generator bugs in the post-CTE wide-projection path: (Bug A) empty table alias `""."col"` emitted on every column when projection is wide, where the simple 2-col CTE case correctly emits unaliased `"col"`; (Bug B) empty column name and unfilled cast type `()r.GetValue(N)` for FK `.Id` projection in CTE context. Both are real generator bugs surfaced for the first time by this test. Per user direction, expanding workflow scope from "scope-only" to "scope + bug fixes" — going back to DESIGN to investigate root causes |
| 5 | DESIGN 2026-04-27 | DESIGN 2026-04-28 | Investigation: Bug A root cause is `SqlAssembler.AppendProjectionColumnSql:1359` checking `col.TableAlias != null` instead of `IsNullOrEmpty` (the placeholder path in `ProjectionAnalyzer:213` deliberately sets `Alias=""` to mean "no alias"; `ReaderCodeGenerator` already used `IsNullOrEmpty`). Bug B turned out to be 3 intertwined issues across `BuildColumnInfoFromTypeSymbol`, `TryParseNavigationChain` (matches `o.UserId.Id` as if it were a navigation), and missing FK key-type extraction — much deeper than expected. Adding OrderBy to the CTE test also surfaced Bug C (`Order<Order>` malformed interceptor for `OrderBy` on `IEntityAccessor<T>`, which is invalid user-level C# anyway). Per user direction, scoped down: fix Bug A, defer B and C as separate issues |
| 6 | DESIGN 2026-04-28 | REMEDIATE 2026-04-29 | Implemented Bug A fix (`SqlAssembler.cs:1359`). Restructured `Tuple_PostCteWideProjection` to use `Echo: o.OrderId` (non-FK) for the 8th element and to sort in-memory (no OrderBy in chain). All 6 wide-tuple tests pass. Updated scope.md to reflect Bug A fix in this PR + B/C deferred. Bug B and Bug C will be filed as separate GitHub issues during REMEDIATE |
