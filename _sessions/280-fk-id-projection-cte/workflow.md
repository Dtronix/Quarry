# Workflow: 280-fk-id-projection-cte

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: #280
pr:
session: 1
phases-total: 7
phases-complete: 0

## Problem Statement
Projecting `o.UserId.Id` (an `EntityRef<TEntity, TKey>.Id` access) inside a `Select(...)` lambda on a chain rooted at `FromCte<T>()` produces invalid SQL (empty quoted identifier `""`) and an unfilled cast type (`(?)`) in the generated reader. Both surface as compile errors in the generated interceptor.

Root layers (per the issue):
1. FK detection in discovery — `BuildColumnInfoFromTypeSymbol` may fail to mark the column `Kind == ForeignKey` when the generated entity's `EntityRef` type symbol is not yet fully resolved during generation.
2. `TryParseNavigationChain` falsely matches `o.UserId.Id` as a navigation chain (hops=["UserId"], finalProp="Id"), producing a `ProjectedColumn` with `columnName="Id"` and unresolved CLR types.
3. FK key-type extraction — even when the FK `.Id` branch fires, `refColumn.ClrType` is the full `EntityRef<T, K>` display string, not the wrapped key type `K`.

Baseline: master @ 0e80b50 — 3364 tests passing (Quarry.Tests 3035, Quarry.Migration.Tests 201, Quarry.Analyzers.Tests 128). Build clean. No pre-existing failures.

## Decisions

### 2026-04-29 — Scope: fix both CTE and non-CTE paths
The issue title focuses on CTE post-Select, but reproduction confirms the same
broken output (`columnName="Id"`, empty `clrType`, compile errors in generated
interceptor) for plain `Lite.Orders().Select(o => (..., UserKey: o.UserId.Id, ...))`.
The root layers differ:
- CTE: `ResolveJoinedColumnWithPlaceholder` placeholder branch produces a
  column with `ColumnName=""` and `IsForeignKey=true` — enrichment can't find
  the FK column to pull metadata from.
- Non-CTE: `AnalyzeProjectedExpression` FK `.Id` branch falls through because
  `refColumn.Kind != ColumnKind.ForeignKey` (BuildColumnInfoFromTypeSymbol is
  not detecting the EntityRef as a FK reliably). The navigation-chain fallback
  then matches `o.UserId.Id` as `hops=["UserId"], finalProp="Id"` and emits
  empty clrType.

Both paths are addressed in one coherent change.

### 2026-04-29 — Approach: explicit `IsRefKeyAccess` flag on ProjectedColumn
Discovery (both placeholder and semantic-model paths) sets `IsRefKeyAccess=true`
when projecting `o.FK.Id`. Enrichment honors the flag: looks up FK column by
ColumnName, copies its key type (registry already stores this as `int`/etc.,
not `EntityRef`), and explicitly suppresses the `EntityRef<…>` wrap that the
reader code generator emits for `IsForeignKey=true` columns. Pros/cons analysis
preferred this over sentinel ColumnName, navigation-hops marker, and implicit
"IsForeignKey=false convention" alternatives — the explicit flag is clearest and
matches existing `IsAggregateFunction`/`IsEnum`/`IsForeignKey` patterns.

### 2026-04-29 — Test coverage
- Extend `Tuple_PostCteWideProjection` to use `UserKey: o.UserId.Id` per the
  issue's own suggestion (replaces the `Echo: o.OrderId` workaround).
- Add a non-CTE single-entity test for `.Select(o => (..., o.UserId.Id, ...))`.
- Add a joined-chain test with `.Select((u, o) => (..., o.UserId.Id, ...))`.
- Add a SQL-only assertion test (no execution) for fast SQL/diagnostics
  regression coverage.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|-------------|-----------|---------|
| 1 | INTAKE 2026-04-29 | IMPLEMENT 2026-04-29 | Loaded issue #280, created worktree, baseline 3364 tests green, design + plan approved |
