# Workflow: 204-cross-entity-set-ops
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: FINALIZE
status: active
issue: #204
pr: #210
session: 4
phases-total: 6
phases-complete: 6
## Problem Statement
Cross-entity set operation overloads (Union<TOther>, UnionAll<TOther>, Intersect<TOther>, IntersectAll<TOther>, Except<TOther>, ExceptAll<TOther>) are defined on IQueryBuilder but not wired in discovery or code generation. Users get QRY073 diagnostic or runtime InvalidOperationException. Same-entity set operations are fully implemented (#181/PR #201). Need to extend discovery, ChainAnalyzer, SetOperationBodyEmitter, and remove QRY073.

Baseline: 2957 tests pass (79 migration + 103 analyzer + 2775 main). No pre-existing failures.
## Decisions
- 2026-04-05: Extract TOther generic type arg during discovery, store on RawCallSite.operandEntityTypeName, propagate through SetOperationPlan to emitter
- 2026-04-05: Remove QRY073 (CrossEntitySetOperationNotSupported) entirely. Keep QRY072 (column count mismatch)
- 2026-04-05: Test using Users + Products projecting to same tuple type (int, string) across all 4 dialects
- 2026-04-06: Fix in this PR (vs separate issue) the pre-existing source generator bug where any entity with a user-written partial declaration in the schema's namespace pinned interceptor signatures to the wrong type for non-default contexts (Pg/My/Ss). Root cause: chain root discovery used `ToFullyQualifiedDisplayString()` on the resolved entity symbol; when the user supplies a partial class (e.g., `Quarry.Tests.Samples.Product` with `[EntityReader]`), Roslyn resolves to that class even though the source generator generates a separate per-context entity at `{contextNamespace}.Product`. Fix: in `CallSiteBinder.Bind`, after resolving the entity, rewrite `RawCallSite.EntityTypeName` (and `OperandEntityTypeName` for cross-entity set ops) to `global::{contextNamespace}.{entityName}` only when the discovery's namespace differs from the context namespace. Simple-name (Error type) and same-namespace cases are left untouched to preserve the existing carrier/interceptor output format.
- 2026-04-06: User direction: "Fix all" review findings rather than deferring multi-dialect to a separate issue. Ignored only the QRY072 negative test (D-class) — the C# type system rejects the column-count mismatch shapes that would trigger it, so it's covered at the descriptor level instead.
- 2026-04-06 (session 3): Add Union TResult docs only (not the QRY072 retest path). Added uniform `<remarks>` block to all six cross-entity overloads on `IQueryBuilder` documenting the strict-TResult constraint, the explicit-projection escape hatch, and EF Core / LINQ to SQL parity. Added a "Design Notes" section to PR #210 covering the same material plus QRY072's defensive retention.
- 2026-04-06 (session 3, follow-up): Reclassified QRY072 finding from (D) → (A) after investigation revealed the original "TResult pins column counts" claim only holds for tuples/required-init records, not DTOs with object initializers. ProjectionAnalyzer counts one column per assignment expression — so `Select(u => new MyDto { A, B })` vs `Select(u => new MyDto { A })` reaches the orchestrator with mismatched Columns.Count and triggers QRY072. Added unit-level negative test in PipelineOrchestratorTests.cs that constructs the AssembledPlan via reflection (bypassing the source-generator default-interface-method limitation). Corrected the misleading comment in GeneratorTests.cs.
## Suspend State
- **Phase:** REVIEW. PR #210 still open. Session 3 in progress on a second-pass review.
- **Done this session before suspend:**
  - Added uniform `<remarks>` blocks to all six cross-entity overloads on `IQueryBuilder` (commit `4c6aba6` — already pushed). Updated PR #210 description with a "Design Notes" section.
  - Investigated QRY072 reachability and proved the original (D) classification was wrong: the constraint "TResult pins column counts" only holds for tuples and required-init records, **not** for DTOs with object initializers. `ProjectionAnalyzer.AnalyzeInitializerExpressions` counts one column per assignment expression, so `Select(u => new MyDto { A, B })` (2 cols) vs `Select(u => new MyDto { A })` (1 col) both compile with `TResult=MyDto` but reach the orchestrator with mismatched `Columns.Count` and trigger QRY072.
  - Reclassified the QRY072 finding (D)→(A) in `review.md`.
  - Added two unit-level tests in `src/Quarry.Tests/IR/PipelineOrchestratorTests.cs` (`CollectPostAnalysisDiagnostics_SetOperationColumnCountMismatch_EmitsQRY072` and `..._NoDiagnostic`) that construct the `AssembledPlan` directly via reflection on the private `PipelineOrchestrator.CollectPostAnalysisDiagnostics`, bypassing the source-generator harness limitation with default interface methods.
  - Corrected the misleading comment in `src/Quarry.Tests/GeneratorTests.cs:1411` to acknowledge the DTO-init reachable shape and point at the new tests.
  - **All 2967 tests pass** (79 migration + 103 analyzer + 2785 main) — +2 from the new QRY072 tests.
  - Background agent produced `_sessions/204-cross-entity-set-ops/review-session3.md` (fresh review pass against the full branch). Three new low-severity findings (see Next step).
- **Next step on resume:** Decide classifications for the three new findings from `review-session3.md`. The user rejected the first AskUserQuestion prompt — re-present, possibly per-finding rather than bulk. Recommended classifications:
  1. `RawCallSite.cs:217-220` orphaned `<summary>` block — **(A) Fix.** Real doc regression introduced in session 2: when `WithOperandEntityTypeName` was inserted, the existing `WithResultTypeName` summary was left stranded above it; `WithResultTypeName` at line 346 now has no doc at all. Easy fix — move the 4-line block back down.
  2. `CrossDialectSetOperationTests.cs:738` `CrossEntity_Union_WithParameters` count-only assertion — **(A) Fix.** Strengthen with deterministic OrderBy + tuple sequence assertion. Expected rows: `(1, "Widget"), (2, "Bob"), (3, "Charlie"), (3, "Doohickey")` (sorted by Item1 then Item2). Matches the row-value strengthening already applied to `CrossEntity_Union_TupleProjection` in commit `12bc318`.
  3. `CallSiteBinder.cs:40-52` rebind loop break style — **(D) Ignore.** Functionally correct; the unique-context-name invariant is solid.
- **Open in conversation:** I had drafted an AskUserQuestion presenting the three findings with bulk-classification options when the user interrupted with "handoff and push." That question is unsent.
- **Branch state:** Commit `4c6aba6` is the latest pushed commit. About to add a WIP commit for session 3 follow-up work (QRY072 tests + session artifacts + review-session3.md + comment correction).
- **Test status:** All 2967 tests passing (79 + 103 + 2785).
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Issue #204 selected, worktree created, baseline green (2957 tests) |
| 1 | DESIGN | PLAN | 3 design decisions confirmed. Threading TOther through pipeline. |
| 1 | PLAN | IMPLEMENT | 6-phase plan approved. Starting implementation. |
| 1 | IMPLEMENT | REVIEW | All 6 phases complete. 2963 tests pass (79 migration + 103 analyzer + 2781 main). |
| 1 | REVIEW | REVIEW | Analysis pass complete (review.md). Suspended before classification approval. |
| 2 | REVIEW | REMEDIATE | Resumed session. User chose "Fix all". Discovered pre-existing source generator bug with per-context entity resolution. Fixed in CallSiteBinder. Added multi-dialect cross-entity assertions for all 4 set ops, plus IntersectAll/ExceptAll cross-entity tests. 2965 tests passing. |
| 2 | REMEDIATE | REVIEW | PR #210 created and CI green. Discussed Union TResult strictness with user. User reset phase back to REVIEW preserving all work, then suspended. |
| 3 | REVIEW | REVIEW | Resumed from suspend. Baseline 2965 green. User chose Union TResult docs only. Added uniform `<remarks>` block to all 6 cross-entity overloads on `IQueryBuilder`. Added "Design Notes" section to PR #210. Tests still 2965 green. |
| 3 | REVIEW | REVIEW | Continuing in REVIEW. User added two more tasks: parallel agent for fresh review pass + reclassify QRY072. Investigated QRY072 reachability — found it IS reachable via asymmetric DTO inits. Reclassified (D)→(A). Added unit-level test in PipelineOrchestratorTests.cs via reflection on the private orchestrator method. Corrected misleading comment in GeneratorTests.cs. 2967 tests green (+2). |
| 3 | REVIEW | REVIEW (suspended) | Background agent finished review-session3.md with 3 new low-severity findings (orphaned RawCallSite summary, count-only Union_WithParameters assertion, rebind loop style nit). User said "handoff and push" before the classifications question was answered. Suspended. |
| 4 | REVIEW | -- | Resumed from suspend. PR #210 still OPEN/MERGEABLE, CI green on `4c6aba6`. WIP commit `19c2c6a` (session 3 follow-up) is unpushed and will be amended on next real commit. Reading baseline. |
