# Workflow: 274-ss-window-function-int

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: DESIGN
status: active
issue: #274
pr:
session: 1
phases-total: 5
phases-complete: 0

## Problem Statement
SQL Server window functions (`ROW_NUMBER`, `DENSE_RANK`, `NTILE`, `COUNT`-over-partition, etc.) return `BIGINT`, but the Quarry generator emits `SqlDataReader.GetInt32(i)` for `int`-typed projections. `Microsoft.Data.SqlClient.SqlDataReader.GetInt32` does not auto-narrow from BIGINT and throws `InvalidCastException`. PostgreSQL silently narrows, so the bug manifests only on Ss execution.

PR #270 added cross-dialect window-function tests; nine sites in `CrossDialectWindowFunctionTests` and `CrossDialectSetOperationTests` execute on Lite/Pg/My only and skip Ss execution pending this fix.

Issue suggests three options:
1. Cast in SQL — emit `CAST(<window_fn> AS INT)` on Ss when projection target is `int`.
2. Read with `GetInt64` and narrow in C# — emit `(int)reader.GetInt64(i)` on Ss for window-function-projected `int` columns.
3. Recommend `long` projections — documentation only, breaks symmetry with existing tests.

Issue suggested approach: Option (1).

Baseline test status (this session): all green — Quarry.Tests 3035, Quarry.Analyzers.Tests 128, Quarry.Migration.Tests 201. (Ss execution paths for the nine listed tests are passively unrun, not skipped via attributes.)

## Decisions
- 2026-04-29: Started new branch `274-ss-window-function-int` from `master` at `0e80b50`.
- 2026-04-29: Approach — hybrid SQL CAST, dialect-conditional. Emit `CAST(<expr> AS INT)` only on `SqlDialect.SqlServer`. Other dialects unchanged. Reader emit (`GetInt32`) stays as-is.
- 2026-04-29: Scope — apply the cast to any window-function projection where the resolved `ClrType == "int"`: `ROW_NUMBER`, `RANK`, `DENSE_RANK`, `NTILE`, `COUNT(*) OVER`, `COUNT(col) OVER`, and `SUM/AVG/MIN/MAX OVER` when resolved type is int. Joined and single-entity paths both. **Excluded**: `LAG`, `LEAD`, `FirstValue`, `LastValue` — these inherit the source column's type and don't have the BIGINT-narrow problem.
- 2026-04-29: Mechanism — add flag `RequiresSqlServerIntCast` to `ProjectedColumn`. Set at window-function emit sites. Apply wrap at `ReaderCodeGenerator.GenerateColumnList` (where dialect is already threaded through).
- 2026-04-29: Manifest update — regenerate `src/Quarry.Tests/ManifestOutput/quarry-manifest.sqlserver.md` in the same PR (separate commit so the diff is reviewable). Other dialect manifests untouched.
- 2026-04-29: Note on partial coverage — only `ROW_NUMBER`/`RANK`/`DENSE_RANK`/`NTILE` actually return BIGINT on Ss. `COUNT`/`SUM`/`AVG`/`MIN`/`MAX` already return INT, so wrapping them is a defensive no-op. Accepted as a consequence of "uniform rule for all int-typed window projections" — matches the issue's suggested heuristic and keeps the rule simple.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|-------------|-----------|---------|
| 1 | 2026-04-29 INTAKE |  | Created worktree, loaded issue #274, ran baseline tests (all green). |
