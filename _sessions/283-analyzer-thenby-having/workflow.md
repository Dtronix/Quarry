# Workflow: 283-analyzer-thenby-having

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: #283
pr:
session: 1
phases-total: 2
phases-complete: 0

## Problem Statement
Issue #283: Add analyzer warnings for two semantically-dubious fluent chains that became newly legal after PR #281:

1. `.ThenBy(...)` called without a preceding `.OrderBy(...)` in the same fluent chain. Since `IEntityAccessor<T>` now exposes `ThenBy`, code like `db.Users().ThenBy(...)` or `db.With<T>(...).FromCte<T>().ThenBy(...)` compiles, but it emits `ORDER BY <key>` — semantically equivalent to `OrderBy`. The API name implies "next sort key" but there is no first.
2. `.Having(...)` called without a preceding `.GroupBy(...)` in the same fluent chain. Now legal on both `IEntityAccessor<T>` and `IQueryBuilder<T>`. Emits `HAVING <pred>` against the whole result; almost always not what the user intended.

Set-op forms (`Union`, `Intersect`, `Except`, ...) are explicitly out of scope — they have a defensible implicit `SELECT * FROM <cte>` semantic.

Suggested fix: add two analyzer diagnostics in `src/Quarry.Analyzers/`. Both rules need fluent-chain backward-walk (look at the receiver expression's chain). Pre-CTE chains (`db.Users().ThenBy(...)`) and post-CTE chains (`db.With<T>(...).FromCte<T>().ThenBy(...)`) should both be flagged. Tests live in `src/Quarry.Analyzers.Tests/`.

### Baseline Test Status (recorded at INTAKE)
- `Quarry.Analyzers.Tests`: 128/128 passing.
- `Quarry.Tests`: cannot build — pre-existing generator failure on master (`error QRY900: Internal error in Quarry generator: Failed to generate interceptors: An item with the same key has already been added. Key: 34f95b8c`). This is unrelated to our work and predates this branch (HEAD c3f3f01). We will not be required to make `Quarry.Tests` build, but we should not regress it further.
- Other test projects (`Quarry.Migration.Tests`) build cleanly; not exercised here.

## Decisions
- **2026-04-29 — Diagnostic IDs and category.** Use `QRA403` (ThenBy without OrderBy) and `QRA404` (Having without GroupBy), placed in the existing QRA4xx Patterns category alongside `QRA401 QueryInsideLoop` and `QRA402 MultipleQueriesSameTable`. The issue text said "QRY***" but the codebase consistently uses the `QRA` prefix for analyzer rules; `QRY` is reserved for migration / project-setup diagnostics (QRY042, QRY044). Patterns is the right category because these are "probable misuse" semantic foot-guns, not simplifications and not wasted work.
- **2026-04-29 — Severity: Warning** for both diagnostics. Matches the issue spec and aligns with peer rules `QRA201 UnusedJoin` and `QRA205 CartesianProduct` which are also "probable mistake" patterns. The emitted SQL is technically valid, so Error would be wrong; Info would understate that the user almost certainly did not intend this.
- **2026-04-29 — Set-op chains are flagged.** A chain like `q1.Union(q2).ThenBy(...)` (no preceding OrderBy in the receiver chain) is still flagged. The issue's "set-op forms are fine" carve-out applies only to set-op calls themselves having no preceding Select — it does not exempt a `ThenBy` or `Having` that sits *after* a set op. Implementation: backward receiver walk simply looks for OrderBy / GroupBy by method name; nothing special for set ops.
- **2026-04-29 — Code fix scope: ThenBy → OrderBy only.** Provide a CodeFix that renames `ThenBy` → `OrderBy` and `ThenByDescending` → `OrderByDescending`. No code fix for the Having case because adding `.GroupBy(...)` requires knowing the grouping key expression, which the analyzer cannot infer. Code fix will live in `src/Quarry.Analyzers.CodeFixes/CodeFixes/` alongside existing fixes.
- **2026-04-29 — Receiver-chain backward walk algorithm.** Mirror `UnsupportedForDialectRule`'s pattern: starting from `context.InvocationSyntax`, descend through `MemberAccessExpressionSyntax.Expression` repeatedly, scanning method names for the anchor (OrderBy/OrderByDescending or GroupBy). Walk is transparent to intervening calls (Where, Select, Distinct, Trace, Limit, set ops, etc.). The rule fires only when `Site.Kind` matches (`ThenBy` or `Having`), so the rule never runs on non-query chains.
- **2026-04-29 — Diagnostic location.** Report the diagnostic on the `ThenBy` / `Having` method-name identifier (the `Name` of the `MemberAccessExpressionSyntax`), not the whole invocation. Matches Roslyn analyzer convention and produces a tighter squiggle.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-04-29 | | INTAKE: created worktree/branch, recorded baseline (analyzer tests green; Quarry.Tests has pre-existing QRY900 build failure on master). DESIGN: confirmed QRA403/QRA404 IDs, Warning severity, set-op chains flagged, ThenBy→OrderBy code fix only. Rebased on origin/master to pick up #287 (now at 3be1cf2). Entering PLAN. |
