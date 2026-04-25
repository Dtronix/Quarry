# Workflow: 267-fix-pg-distinct-orderby

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: REMEDIATE
status: active
issue: #267
pr:
session: 1
phases-total: 3
phases-complete: 3

## Problem Statement

Generator emits `SELECT DISTINCT <projection> ... ORDER BY <expr>` where `<expr>` is not part of `<projection>`. SQLite, MySQL, and SQL Server tolerate this. PostgreSQL rejects with `42P10: for SELECT DISTINCT, ORDER BY expressions must appear in select list`.

Test exposing the issue: `src/Quarry.Tests/SqlOutput/CrossDialectCompositionTests.cs::Join_Distinct_OrderBy_Limit` — Pg execution mirror is currently skipped with an inline comment referencing #267. Cross-dialect SQL-text assertion still verifies generator output is uniform across dialects.

The recommended approach (from the issue) is **Option (2)** — wrap in a subquery on PG when DISTINCT is combined with ORDER BY on expressions that aren't part of the SELECT projection. Per-dialect SQL emission is already supported.

### Pre-existing baseline failures (excluded from quality gates)

- `Quarry.Tests.Migration.PostgresMigrationRunnerTests.RunAsync_InsertsHistoryRow_OnPostgreSQL` — TearDown NRE, requires a live Postgres instance available locally. Unrelated to #267.

Otherwise: 2489 passed, 506 skipped (skipped are conditional/Trace tests that intentionally suppress at this build configuration).

## Decisions

### 2026-04-25 — Rewrite shape: subquery wrap (issue's option 2), applied to ALL dialects
When DISTINCT combines with ORDER BY on a column that is not in the SELECT projection, emit a derived-table wrap:

```
SELECT <d>.<proj_aliases> FROM (
  SELECT DISTINCT <proj_cols [AS proj_aliases]>, <orderby_cols [AS order_aliases]>
  FROM ... WHERE ... [GROUP BY ... HAVING ...]
) AS <d>
ORDER BY <d>.<alias> [ASC|DESC], ...
[LIMIT/OFFSET applied to outer]
```

Rationale: Quarry's identity is cross-dialect parity (`AssertDialects`, 4-dialect harness). Per-dialect dispatch on *result shape* (not just syntax) breaks that pitch and surprises users at runtime when they migrate dialects. The current SQLite/MySQL behavior on `OrderBy(non-proj).Distinct().Select(proj)` is implementation-defined leniency (picks one row per projection key arbitrarily, ordered by an undefined column value) — not a contract. PG correctly rejects the same query. Unifying to the wrap form gives all dialects deterministic, standard-SQL-compliant semantics. Row count for affected query shapes increases (e.g., 3 vs 2 in the existing test) because DISTINCT now applies to `(proj_cols, orderby_cols)` instead of just `proj_cols` — this is the correct semantic for an ambiguous query.

### 2026-04-25 — Dialect scope: ALL FOUR dialects (SQLite, PostgreSQL, MySQL, SQL Server)
Single rendering path. No per-dialect dispatch for this construct. Eliminates the per-mask fallback in `RenderSelectSqlBatch` and keeps `AssertDialects` cross-dialect tests with one expected SQL string instead of two flat + two wrapped.

### 2026-04-25 — Detection: rendered-string equality of ORDER BY expressions vs projection columns
Compare the dialect-rendered SQL string of each ORDER BY expression against each projection column's rendered column-reference string. If any ORDER BY expression doesn't match, wrap is needed. Handles bare column refs and complex expressions uniformly. Conservative: complex ORDER BY expressions that don't have a matching projection column trigger the wrap.

### 2026-04-25 — Aliasing scheme
- Inner derived alias: `__d`.
- Inner projection column aliases: `__c0`, `__c1`, ... (added when projection column lacks an existing alias).
- Inner ORDER BY column aliases: `__o0`, `__o1`, ... (added only for ORDER BY expressions not already in projection).
- Outer SELECT references inner aliases: `<d>.<alias>`.
- Reader code generation reads by ordinal — no impact from aliasing.

### 2026-04-25 — Excluded scopes
- Set operations (UNION/INTERSECT/EXCEPT): wrap not applied when `plan.SetOperations.Count > 0`. Existing post-union derived-table wrap and operand-level rendering already handle those paths.
- Conditional ORDER BY (mask-driven): per-mask detection — masks where no non-projected ORDER BY is active emit flat SQL; masks where at least one is active emit wrap. Both paths must be supported in `RenderSelectSql` and `RenderSelectSqlBatch`.

### 2026-04-25 — Test plan for `Join_Distinct_OrderBy_Limit`
- Cross-dialect SQL assertion: all four dialects emit wrapped form (same shape, dialect-specific quoting/pagination only).
- SQLite execute mirror: row-count assertion changes from 2 to 3 (Alice with order₁, Alice with order₂, Bob).
- PG execute mirror re-enabled with `Has.Count.EqualTo(3)`.
- Add a focused test verifying the wrap is NOT applied when ORDER BY column IS in projection (no regression for the common case).
- Behavior change documented in release notes: `OrderBy(non-projected).Distinct().Select(proj)` now returns one row per `(proj, orderby)` pair instead of one row per `proj` with an arbitrary `orderby` value.

## Suspend State

## Session Log

| # | Phase Start | Phase End | Summary |
|---|-------------|-----------|---------|
| 1 | 2026-04-25 INTAKE | 2026-04-25 DESIGN | Issue #267 loaded, branch + worktree created, baseline established (1 unrelated PG migration test failure recorded as baseline). |
| 1 | 2026-04-25 DESIGN | 2026-04-25 PLAN | Decisions captured: subquery wrap, all 4 dialects, rendered-string detection, alias scheme `__d`/`__c{i}`/`__o{i}`, set-ops excluded. Initial PG/SS-only scope revised to all dialects after user pushback to preserve cross-dialect parity. |
| 1 | 2026-04-25 PLAN | 2026-04-25 IMPLEMENT | 3-phase plan: detection+wrap+tests (P1), PG execute mirror (P2 — folded into P1 commit), verify (P3). |
| 1 | 2026-04-25 IMPLEMENT | 2026-04-25 IMPLEMENT | Phase 1+2 done: NeedsDistinctOrderByWrap / RenderProjectionColumnRef / GetInnerProjectionAlias / RenderSelectSqlWithDistinctOrderByWrap added in SqlAssembler.cs; dispatch wired in RenderSelectSql + Assemble batch fallback. Existing test SQL & SQLite count updated, PG mirror added; 5 new focused tests in CrossDialectDistinctOrderByTests.cs. Full Quarry.Tests + Analyzers.Tests green (3001 + 117). |
