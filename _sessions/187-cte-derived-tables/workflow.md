# Workflow: 187-cte-derived-tables

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: REMEDIATE
status: active
issue: #187
pr: #208
session: 7
phases-total: 9
phases-complete: 8

## Problem Statement
Support CTEs (WITH ... AS) and derived tables (subqueries in FROM clause) in the Quarry query builder. Both introduce "query-as-entity" — a subquery result set whose columns are determined by the inner query's projection, not by a schema entity class. Requires significant IR extension to compose inner query SQL as CTE prefix or FROM-clause subquery.

Baseline: 2779 tests passing (65 analyzer + 2714 main), 0 pre-existing failures.

## Decisions
- 2026-04-05: DTO class required for CTE/derived table column shape (not type inference from anonymous projections)
- 2026-04-05: Scope includes both CTEs and derived tables
- 2026-04-05: API: Option A+G — `db.With<TDto>(innerQuery)` before main table (mirrors SQL syntax); derived tables unified as CTEs via `.FromCte<TDto>()` instead of separate IR path
- 2026-04-05: CTE name derived from DTO class name (no user-specified string parameter)
- 2026-04-05: DTO columns inferred from public properties — no attribute required
- 2026-04-05: Inner CTE queries support captured variable parameters from the start
- 2026-04-05: Inner chain handling: nested chain analysis — generator analyzes inner chain, captures SQL, suppresses standalone interception. Outer carrier includes CTE SQL as a prebuilt string.
- 2026-04-05: Multiple CTEs supported from the start — `db.With<A>(a).With<B>(b).Users()...`

## Suspend State
(Cleared — pass #2 remediation complete; awaiting user FINALIZE confirmation.)

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Issue #187 selected, worktree created, baseline green |
| 1 | DESIGN | PLAN | Design decisions confirmed: DTO-based, CTEs+derived, Option A+G API |
| 1 | PLAN | IMPLEMENT | 9-phase plan approved |
| 1 | IMPLEMENT | IMPLEMENT | Phases 1-3 committed, suspended at phase 4 (context exhaustion) |
| 2 | IMPLEMENT | IMPLEMENT | Resumed — phases 4-8 committed, suspended at phase 9 (context exhaustion) |
| 3 | IMPLEMENT | IMPLEMENT | Resumed — Phase 9 pipeline fixes (6 fixes), SQLite CTE test passing, suspended (multi-dialect inner chain detection issue) |
| 4 | IMPLEMENT | REMEDIATE | Fixed multi-dialect detection, 4-dialect FromCte test, post-CTE discovery infra, review completed, (A)/(B) fixes applied, (C) issues #205/#206/#207 created. Suspended mid-rebase. |
| 5 | REMEDIATE | REMEDIATE | Resumed — rebased on origin/master (5 commits, resolved conflicts in InterceptorKind, QueryPlan, RawCallSite, UsageSiteDiscovery, ChainAnalyzer, manifests). All 2879 tests passing. PR #208 created, CI passed. Suspended awaiting merge confirmation. |
| 6 | REMEDIATE | REVIEW | Resumed — verified PR #208 still CLEAN/MERGEABLE, CI green (run 24030927995). User requested "go back to REVIEW": deleted review.md for fresh analysis pass; preserved all prior REMEDIATE outcomes. Found and fixed 3 critical bugs: (1) CTE inner captured params silently dropped at runtime, (2) PG dialect fallback corrupted MySQL/SS WITH names for non-entity DTOs, (3) FK heuristic NRE on tuple projections of properties ending in "Id". Plus 8 smaller remediation fixes. Added Cte_FromCte_CapturedParam + Cte_FromCte_DedicatedDto tests. Committed b02a13e, PR updated, CI run 24036554463 SUCCESS, 2960 tests passing. User requested ANOTHER REVIEW pass — pass #2 found 16 items (1 Medium global:: helper mismatch, 1 Medium QRY900 misclassification, 1 Medium negative-test gap, 13 Lower). User requested "fix all". Mid-pass-#2: WIP commit 61a0ee5 covers 7 of the 8 remediation tasks; new tests + final commit + push remain. SUSPENDED at user request before running full test suite. |
| 7 | REMEDIATE | REMEDIATE | Resumed pass #2: PR #208 CI verified GREEN at HEAD 243034d (run 24043045000) confirming WIP source changes work. Local baseline 2960 tests green. Added 4 new tests: Cte_With_NonInlineInnerArgument_EmitsQRY080, Cte_FromCte_WithoutPrecedingWith_EmitsQRY081, Cte_With_GlobalNamespaceDto_StripsGlobalPrefix (CarrierGenerationTests.cs), Cte_FromCte_AllColumns (CrossDialectCteTests.cs). Reclassified Test Quality #2 from (A)→(D) — Prepare in this codebase is a snapshot at chain construction (no Bind/SetParameter API), so re-execution of the same prepared instance with mutated captured value isn't possible; the lt2 pattern is correct. Commented Cte_FromCte_CapturedParam to document the snapshot semantics. Final test totals: 2782+103+79=2964 all green. WIP commit 61a0ee5 + suspend commit 243034d to be squashed at FINALIZE. Next: create non-WIP commit with new tests, push, update PR body, re-prompt for FINALIZE. |
