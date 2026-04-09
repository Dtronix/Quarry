# Workflow: 186-window-functions
## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master
## State
phase: IMPLEMENT
status: active
issue: #186
pr:
session: 1
phases-total: 5
phases-complete: 1
## Problem Statement
Add support for SQL window functions (ROW_NUMBER, RANK, DENSE_RANK, NTILE, LAG, LEAD, FIRST_VALUE, LAST_VALUE, SUM/COUNT/AVG/MIN/MAX OVER) in Select projections via new `Sql.*` methods with a lambda-based OVER clause (Approach D from design discussion).

Target API:
```csharp
.Select(o => (
    Name: o.Name,
    RowNum: Sql.RowNumber(over => over.OrderBy(o.Date)),
    Rank: Sql.Rank(over => over.PartitionBy(o.Category).OrderBy(o.Price))
))
```

The `over` parameter is an `OverBuilder` with `.PartitionBy()`, `.OrderBy()`, and future `.Rows()`/`.Range()` frame spec methods. `Sql.RowNumber(...)` returns `int`, so tuple types resolve correctly.

### Pre-existing test failures
None. All 3034 tests pass (97 migration + 103 analyzers + 2834 quarry).

### Supporting work already merged
- PR #208: CTE/derived table support
- PR #210: Cross-entity set operations
- PR #211: Discovery boilerplate refactored into shared helpers
- PR #212: Multi-CTE carrier fix
- PR #214: QuarryContext<TSelf> typed chains
- PR #218: Lambda-form CTE overloads + set-op lambda infrastructure
- PR #219: Set-op lambda context resolution fix
- PR #220: Lambda CTE column reduction
- PR #221: Lambda QRY080 diagnostic tests

## Decisions
- 2026-04-09: **Scope — all listed window functions**. ROW_NUMBER, RANK, DENSE_RANK, NTILE, LAG, LEAD, FIRST_VALUE, LAST_VALUE, plus aggregate OVER variants (SUM/COUNT/AVG/MIN/MAX OVER). Generator machinery is the same for all; incremental cost per function is minimal.
- 2026-04-09: **OrderBy direction — OrderBy + OrderByDescending**. Mirror existing IQueryBuilder pattern. `over.OrderBy(o.Date)` for ASC, `over.OrderByDescending(o.Price)` for DESC.
- 2026-04-09: **Frame specs deferred**. ROWS/RANGE BETWEEN deferred to follow-up. OverBuilder kept extensible but not implemented now.
- 2026-04-09: **Aggregate OVER — overloads on existing Sql methods**. `Sql.Sum(o.Total, over => over.PartitionBy(o.Category))`. Second parameter is `Func<IOverClause, IOverClause>`. Generator distinguishes by argument count.
- 2026-04-09: **PartitionBy — params array**. `over.PartitionBy(o.Category, o.Region)` single call with multiple columns.
- 2026-04-09: **Runtime type — IOverClause interface**. Consistent with IQueryBuilder pattern. Methods return IOverClause for chaining. Runtime implementation throws.
- 2026-04-09: **LAG/LEAD — three overloads each**. `Sql.Lag<T>(T col, over)`, `Sql.Lag<T>(T col, int offset, over)`, `Sql.Lag<T>(T col, int offset, T default, over)`. OVER lambda always last parameter.
## Suspend State
## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | | Starting window functions work |
