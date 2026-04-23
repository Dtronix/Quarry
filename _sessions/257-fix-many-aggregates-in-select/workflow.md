# Workflow: 257-fix-many-aggregates-in-select

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: REMEDIATE
status: active
issue: #257
pr:
session: 1
phases-total: 5
phases-complete: 5

## Problem Statement
`Many<T>.Sum`, `Min`, `Max`, `Avg`/`Average` work in predicate positions (Where/Having/comparisons)
but silently fail when used inside a `Select` tuple projection. Generated SQL has empty columns
(`SELECT "UserName", "", "", ""`), the projected tuple type falls back to `(object, object, ...)`
in the `IQueryBuilder<T, ...>` declaration, and the reader lambda emits `(?)r.GetValue(N)` (cast to
unknown type) — producing CS0246/CS0501 build errors.

`Many<T>.Count()` in Select works (HasManyThrough correlated count) — the regression is specific to
the scalar-aggregate variants added in #195.

Baseline test suite: 3242 tests passing (Quarry.Analyzers.Tests 103, Quarry.Migration.Tests 201,
Quarry.Tests 2938). No pre-existing failures.

## Decisions

### 2026-04-22 — Root cause confirmed by reproduction
Added a single test using `.Select(u => (u.UserName, OrderTotal: u.Orders.Sum(o => o.Total)))`.
Generator emits exactly the broken artifacts the issue describes:
- `_sql = @"SELECT ""UserName"", """" FROM ""users"""`
- `IQueryBuilder<User, (object, object)>`
- `_reader = ... (UserName: r.GetString(0), OrderTotal: (?)r.GetValue(1))`

The generator does NOT raise a diagnostic — `ProjectionAnalyzer` silently emits a placeholder
`ProjectedColumn` with empty `ColumnName`, empty `SqlExpression`, empty `ClrType`. The downstream
SqlAssembler/reader emit then renders empty SQL and `(?)r.GetValue(N)`. Test reverted (the failing
test will be re-added as part of the fix in PLAN phase).

### 2026-04-22 — Design choices (user-confirmed)
- **Architecture:** ProjectionAnalyzer detects navigation-aggregate calls and stores the unbound
  `SqlExpr` (parsed via existing `SqlExprParser.ParseExpression`) on a new `ProjectedColumn`
  field. `ChainAnalyzer.BuildProjection` (already runs with full registry/dialect) does the
  binding (`SqlExprBinder.Bind`) and rendering (`SqlExprRenderer.Render`) into the existing
  `ProjectedColumn.SqlExpression` text field. SqlAssembler stays untouched.
- **Aggregate scope:** Sum/Min/Max/Avg/Average/Count handled uniformly via existing
  `SqlExprParser.IsSubqueryMethod` (covers all 6 names — Avg/Average alias to same kind).
- **Projection scope:** Both tuple and DTO-initializer projections; both single-entity and
  joined (multi-entity) Select.
- **Failure mode:** Emit a generator diagnostic (new QRY code) when navigation/binding fails,
  so the silent (?)r.GetValue(N) regression cannot recur.
- **Nullability:** Match existing Sql.* aggregate convention — emit non-nullable result type
  (Sum→decimal, Avg→double/decimal per Sql.cs, Count→int, Min/Max→selector type). Existing
  Where path doesn't mark these IsNullable either; consistency over SQL-NULL-on-empty semantics.
- **Test coverage:** Mirror existing CrossDialectSubqueryTests `Where_*` patterns
  (cross-dialect Select_Sum/Min/Max/Avg/Count) + 1 DTO test + 1 joined-context test +
  1 combined-tuple repro of the issue's example.

### 2026-04-22 — Two disjoint pipelines for SubqueryExpr
Codepath confirmed:
- **Where/Having path:** `SqlExprParser.ParseExpression` recognizes navigation-method calls
  via `IsSubqueryMethod`, builds `SubqueryExpr` (with `SubqueryKind`), then `SqlExprBinder.BindSubquery`
  resolves table/alias/correlation, and `SqlExprRenderer.RenderSubquery` produces correct SQL.
- **Select path:** `ProjectionAnalyzer.IsAggregateCall` only matches `Sql.*` invocations
  (line 553-558). Navigation-property aggregate calls fall through completely. The downstream
  pipeline (SqlAssembler.AppendSelectColumns at line 1071) renders aggregate columns by quoting
  `col.SqlExpression` as a plain text fragment — so any fix must produce already-rendered SQL
  text (or extend SqlAssembler to invoke Bind+Render for SubqueryExpr columns).

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | 2026-04-22 INTAKE |  | Loaded issue #257, created worktree+branch, baseline 3242 tests green |
| 1 | 2026-04-22 IMPLEMENT |  | Phases 1-3: ProjectedColumn.SubqueryExpression field, ProjectionAnalyzer detection of nav-aggregates, BuildProjection bind+render. SQLite repro test passes. 3243 tests green. |
| 1 | 2026-04-22 IMPLEMENT |  | Phase 4: cross-dialect Select_Many_{Count,Sum,Min,Max,Average} + multi-aggregate repro + DTO + joined-context tests. 3250 tests green. |
| 1 | 2026-04-23 IMPLEMENT |  | Phase 5: QRY073 sanity test (descriptor well-formed). End-to-end emission test deferred — requires constructing a source that compiles but has unresolvable navigation; non-trivial setup. 3251 tests green. |
| 1 | 2026-04-23 REVIEW |  | Agent review pass: 23 findings (1 High, 3 Medium, 11 Low + positives). Critical: QRY073 was plumbed but NOT registered in s_deferredDescriptors → silently dropped. End-to-end emission test (deferred in Phase 5) would have caught it. |
| 1 | 2026-04-23 REMEDIATE |  | All 5 A items + 2 B items addressed: QRY073 registration, equality fixes for ProjectedColumn/ProjectionInfo, joined Sum test, HasManyThrough Max test, empty-set assertion test, end-to-end QRY073 emission test (now exercises real bind failure via unregistered context entity). 3255 tests green. |
