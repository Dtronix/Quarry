# Workflow: add-sqloutput-tests

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: discussion
pr:
session: 1
phases-total: 4
phases-complete: 0

## Problem Statement

`src/Quarry.Tests/SqlOutput/` is dense for standard query shapes but has gaps in four areas, identified by reviewing the suite against the Generator architecture in `src/Quarry.Generator/llm.md`:

1. **5/6-table explicit joins + new join kinds.** The T4-generated `IJoinedQueryBuilder5/6` and `JoinedCarrierBase5/6` paths are barely exercised. `CrossJoin` (no condition) and `FullOuterJoin` (analyzer-rejected on MySQL via QRA503) lack systematic 5/6-table coverage across dialects.
2. **Conditional-mask boundary stress.** Generator-side carrier emission has `ConditionalCarrierTests`, but `SqlOutput/` has no test that exercises mask combinations near the limits (close to `MaxConditionalBits = 8`, depth-2 `MaxIfNestingDepth = 2`, mutually-exclusive `if/else` groups, masks interacting with OrderBy/Limit/Having).
3. **Deeply nested navigation subqueries.** `CrossDialectSubqueryTests` covers single-level only. 3+ level nesting (`u.Orders.Any(o => o.Items.Any(i => i.Tags.Any(...)))`) tests `sq0/sq1/sq2` alias allocation, per-level correlation, and dialect-specific identifier quoting.
4. **Bundled smaller gaps:** `ToAsyncEnumerable` streaming terminal not covered in SqlOutput; `Computed<T>()` modifier exclusion from INSERT/UPDATE column lists not asserted; multi-context-per-file carrier isolation untested.

**Constraint (user-stated):** every test must run against all four dialects (SQLite, PostgreSQL, MySQL, SQL Server) when the dialect supports the feature. Skip a dialect only with explicit comment when it genuinely lacks support (e.g., MySQL has no `FULL OUTER JOIN`).

**Baseline:** Full test suite was not run as a baseline because Docker Desktop is not running and `QueryTestHarness` requires Testcontainers for PG/MySQL/SQL Server. CI on the PR will catch any pre-existing failures.

## Decisions

- 2026-05-03: Source = current discussion. Branch name = `add-sqloutput-tests`.
- 2026-05-03: Scope = all four batches in the listed order.
- 2026-05-03: Dialect-parity rule: every SqlOutput test must cover all four dialects that support the feature; explicit skip with comment otherwise.
- 2026-05-03: Skipped baseline test run — Docker not running. CI on PR is the safety net.
- 2026-05-03: Docker started mid-DESIGN; switched test execution from CI-only to local cross-dialect smoke after each phase.
- 2026-05-03: Phase 3 schema additions (`TagSchema`) and tests in a single commit (avoid orphaned-schema commit).
- 2026-05-03: Phase 4 expanded — adding compile-time `QRY075` diagnostic for `Update().Set` targeting a computed column. Reason: current behavior emits invalid SQL (DB rejects at runtime) while INSERT silently drops; QRY075 fixes the asymmetry at compile time, consistent with other QRY error codes. Includes diagnostic registration, translator detection, llm.md doc update, and tests.

## Suspend State

## Session Log

| # | Phase Start | Phase End | Summary |
|---|-------------|-----------|---------|
| 1 | 2026-05-03 INTAKE | — | Worktree + branch created. Baseline skipped (no Docker). Moved to DESIGN. |
