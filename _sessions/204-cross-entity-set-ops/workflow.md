# Workflow: 204-cross-entity-set-ops
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: REVIEW
status: suspended
issue: #204
pr: #210
session: 2
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
## Suspend State
- **Phase reset:** Moved back from REMEDIATE → REVIEW at user request. All REMEDIATE work preserved (PR #210 open, CI green, all commits on the branch).
- **PR #210 status:** Created, CI passing (1m35s on first run; second run for value-assertion commit not yet observed but build is green locally for all 2965 tests).
- **Branch state:** 4 new commits on top of master: `41c69d9` (CallSiteBinder fix), `7ec073d` (blank line cleanup), `8b220f3` (multi-dialect tests + manifests), `12bc318` (value assertions on Union/UnionAll/Except). Plus `2321bc4` ([WIP] session artifacts) which will be deleted in FINALIZE.
- **Test status:** All 2965 tests passing (79 migration + 103 analyzer + 2783 main).
- **Why back to REVIEW:** User wants to re-examine findings or potentially add new ones. Triggered after a discussion about Union TResult strictness vs SQL UNION permissiveness — that conversation may produce additional review items or doc/PR-description notes worth tracking.
- **Next step on resume:** Determine which review items (if any) need additions or re-classification. Possible follow-ups from the Union types discussion:
  - Document the strict-TResult design choice in the PR description (the tradeoff vs SQL UNION's column-compatible-types semantics, the explicit-projection escape hatch, and parity with EF Core / LINQ to SQL conventions).
  - Consider whether to add a doc/XML comment on `IQueryBuilder.Union<TOther>` explaining the constraint.
  - Re-evaluate whether QRY072's "defensive" classification still holds, given that the Union discussion identified one shape (asymmetric projection flattening) where it could legitimately fire.
- **Unresolved at suspend:** None — all merged code compiles and tests pass. The reset is purely a phase-state change for the user to add more REVIEW work.
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
