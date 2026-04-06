# Workflow: 204-cross-entity-set-ops
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: REVIEW
status: active
issue: #204
pr: #210
session: 3
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
## Suspend State
(none — session 3 is active)
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
