# Workflow: 187-cte-derived-tables

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: REMEDIATE
status: suspended
issue: #187
pr: #208
session: 6
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
- Current phase: REMEDIATE — second-pass-#2 mid-flight
- WIP commit: 61a0ee5 (`[WIP] Pass #2 review remediation: short-name consolidation, dedicated CTE diagnostics, EntityRef hardening`)
- Build status: green (Quarry.Generator builds clean). Full test suite NOT yet run after the WIP changes — must run before non-WIP commit.
- **PR**: #208 open on Dtronix/Quarry. Last green CI was on commit b02a13e (run 24036554463). After WIP push the CI will run again on 61a0ee5; the WIP changes should not regress anything but they have not been verified.
- **Branch state at suspend**:
  - Most recent non-WIP commit: b02a13e (pass #1 remediation — committed and CI'd)
  - WIP commit on top: 61a0ee5 (pass #2 mid-flight)
  - 19 commits ahead of origin/master before WIP, 20 with WIP
- **Pass #2 review classifications** (recorded in `_sessions/187-cte-derived-tables/review.md`):
  - **(A) Fix all** — user explicitly said "fix all"
  - **(D) Verifications**: 2 entries — FK-narrowing consumer audit, EmitCteDefinition `chain` parameter naming
  - **(C) Deferred**: 1 entry — cross-dialect execution (project-wide harness pattern, not in scope)
- **Pass #2 completed in this WIP commit**:
  - **Correctness #1** (Medium, biggest finding): consolidated short-name helpers into `CteNameHelpers.ExtractShortName` in `IR/CteDef.cs`. Strips both `global::` and namespace prefix. Both `ChainAnalyzer.AnalyzeChainGroup` (CteDefinition + FromCte branches) and `TransitionBodyEmitter.EmitCteDefinition` use the shared helper. Local helpers deleted from both files.
  - **Codebase Consistency #4** (Medium): added `QRY080 CteInnerChainNotAnalyzable` and `QRY081 FromCteWithoutWith` descriptors in `DiagnosticDescriptors.cs`. Registered in `s_deferredDescriptors` (`QuarryGenerator.cs`). Replaced both `PipelineErrorBag.Report(...)` calls in `ChainAnalyzer` with `diagnostics?.Add(new DiagnosticInfo(...))`.
  - **Correctness #2/#3**: tightened `ProjectionAnalyzer.BuildColumnInfoFromTypeSymbol` FK detection to (a) verify `ContainingNamespace == Quarry` via new `IsQuarryNamespace` helper, (b) unwrap `Nullable<T>` so `EntityRef<X,Y>?` still resolves as FK.
  - **Correctness #4**: `CallSiteBinder` adds single-context fallback when `ContextClassName` is null AND `AllContexts.Length == 1`.
  - **Correctness #5/#6 + Codebase #5**: documented `cteInnerResults` span-key uniqueness invariant; documented multi-CTE first-match limitation in `EmitCteDefinition`; replaced `chain.ClauseSites[0]` IsCteInnerChain check with full-scan loop in `FileEmitter`.
  - **Codebase Consistency #2** (Low): added `using System.Collections.Generic;` to `TransitionBodyEmitter.cs` and switched to short-name `Dictionary<,>?` parameter type.

- **Pass #2 REMAINING work** (next session):
  1. **Test Quality #1 (Medium) — Add diagnostic tests** for QRY080 and QRY081 in `src/Quarry.Tests/Generation/CarrierGenerationTests.cs` (or new file). Pattern is `RunGeneratorWithDiagnostics(compilation)` then assert `diagnostics.FirstOrDefault(d => d.Id == "QRY080")` is non-null. Source samples need to construct an unanalyzable `With(...)` call and a `FromCte<T>()` without preceding `With<T>()`.
  2. **Plan Compliance #1 (Low) — Add `Cte_FromCte_AllColumns` test** in `src/Quarry.Tests/SqlOutput/CrossDialectCteTests.cs` that does NOT use a tuple projection — exercises identity / all-columns FromCte. (No test currently covers this.)
  3. **Test Quality #2 (Low) — Reuse-prepared assertion** in existing `Cte_FromCte_CapturedParam`: re-execute the SAME prepared `lt` after mutating `cutoff` (currently re-creates a new `lt2`).
  4. **Correctness #1 verification** — Add a regression test using a global-namespace DTO (no namespace declaration) to prove the consolidated helper handles the global:: prefix end-to-end.
  5. **Run full test suite** (`dotnet test src/Quarry.Tests src/Quarry.Analyzers.Tests src/Quarry.Migration.Tests`) — must be all green.
  6. **Convert WIP commit 61a0ee5 to a non-WIP commit**: do NOT use `--amend` (per Suspend rules — amending WIP can lose work). Instead, create a NEW commit with the test additions and any final fixes, then squash/reword by adding another commit on top, or accept the two-commit history. **Recommended**: stage tests + write a final commit message that summarizes ALL of pass #2 (the WIP commit message is informal). Push as the new HEAD; CI will run.
  7. **Update PR #208 body** to mention pass #2 in the Review Remediation section. The current body already mentions "second-review remediation"; needs updating to reflect "pass #2" and the new fixes.
  8. **Re-prompt user for FINALIZE/merge** once CI is green.

- **Files modified in WIP commit 61a0ee5**:
  - `src/Quarry.Generator/IR/CteDef.cs` — added `CteNameHelpers.ExtractShortName`
  - `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — use shared helper, switch to `diagnostics?.Add` for QRY080/QRY081, span-key invariant comment
  - `src/Quarry.Generator/CodeGen/TransitionBodyEmitter.cs` — use shared helper, `using System.Collections.Generic`, multi-CTE comment, deleted local `ExtractDtoShortName`
  - `src/Quarry.Generator/CodeGen/FileEmitter.cs` — full-scan IsCteInnerChain
  - `src/Quarry.Generator/IR/CallSiteBinder.cs` — single-context dialect fallback
  - `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs` — namespace-checked + nullable-unwrapped EntityRef detection, `IsQuarryNamespace` helper
  - `src/Quarry.Generator/DiagnosticDescriptors.cs` — added QRY080, QRY081
  - `src/Quarry.Generator/QuarryGenerator.cs` — registered new descriptors

- **Prior session 5 context** (kept for reference):
  - Rebased on origin/master at session 5
  - PR #208 created and CI'd
  - Issues #205 (CTE+Join), #206 (multi-CTE carrier conflict), #207 (discovery boilerplate) created and remain open
  - Pass #1 remediation committed at b02a13e and CI'd successfully (run 24036554463)

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
