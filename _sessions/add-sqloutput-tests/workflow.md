# Workflow: add-sqloutput-tests

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: REMEDIATE
status: active
issue: discussion
pr: 295
session: 2
phases-total: 4
phases-complete: 4

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
- 2026-05-03: Phase 2 conditional-Having test deferred. Pattern `var grouped = q.GroupBy(...); if (true) grouped = grouped.Having(...);` triggers a generator misattribution where the chain binds to `Cte.CteDb` instead of `TestDbContext` because both expose `IEntityAccessor<Order>`. Single-line GroupBy chains work fine (see CrossDialectAggregateTests). Filed as follow-up; non-blocking. Test left as a comment in CrossDialectConditionalMaskTests.cs explaining the deferral.
- 2026-05-03: Discovered SQL renderer behavior worth recording — multiple conditional `Where` predicates are wrapped in parentheses (`WHERE (a) AND (b) AND (c)`); `OrderBy` without explicit direction renders as `ASC` explicitly. Both are stable conventions the new tests now lock in.
- 2026-05-03: Phase 3 SQL conventions discovered — nested subquery EXISTS clauses (depth ≥ 2) are wrapped in parentheses, e.g. `AND (EXISTS (...))`; nested predicate comparisons inside such EXISTS bodies are also parenthesized; literal string constants are inlined (`'urgent'` rather than `@p0`); captured variables are parameterized as expected; sibling projection-side scalar subqueries each maintain their own alias namespace and reuse `sq0` (not monotonic across columns).
- 2026-05-06: Phase 4 QRY075 audit — the 341c895 commit added a 2-arg typed-lambda Set form `Set(p => p.X, value)` that doesn't exist in `IUpdateBuilder<T>`/`IExecutableUpdateBuilder<T>` (only `Set(T entity)` and `Set(Action<T>)` exist). The QRY075 typed-lambda test always failed; CI never ran on the branch so it wasn't caught. Eliminated the phantom form: removed the `else { kind = InterceptorKind.UpdateSet; }` discovery branch, the unreachable single-set `else` branch in ChainAnalyzer, the `EmitComputedColumnSetDiagnosticForSingleColumn` helper, the `InterceptorKind.UpdateSet` enum value and all table references, the `EmitUpdateSet` emitter, and the failing test. QRY075 retained for `UpdateSetAction` and `SetActionAssignments` paths. Action-lambda `Set(p => p.X = v)` covers every legitimate use case.
- 2026-05-06: Phase 4 Gap A — streaming SQL is asserted via parallel `.ToDiagnostics()` chain (same clauses, different terminal); cancellation is exercised via `break` inside `await foreach` rather than CancellationToken (loop control is enough to verify short-circuit). Discovered `WHERE bool-col` renders dialect-specific: SQLite/MySQL/SS use `= 1`, PostgreSQL uses `= TRUE`.
- 2026-05-06: Phase 4 Gap C — `FileInterceptorGroup` keying on (ContextClassName, FilePath) confirmed by emitting two `[QuarryContext]` partial classes in a single synthesized syntax tree and asserting two interceptor `.g.cs` files emit. Carrier classes are `file sealed`, so `Chain_0` in each file is naturally non-colliding.
- 2026-05-06: REMEDIATE — addressed REVIEW findings #1, #3/9, #7, #8 (2A + 2B from 2A/3B/0C/11D classification). Discovered during Finding #1 remediation: the generator's projection-type resolver does not propagate `int` correctly through nested aggregate subqueries (e.g., `Sum(o => o.Items.Count())` resolves the projection element type as decimal instead of int, causing interceptor signature mismatch CS9144). Worked around by holding the new projection test at decimal-typed nested Sums (`Sum(o => o.Items.Sum(i => i.LineTotal))`); the int-aggregate path is a follow-up.

## Suspend State

## Session Log

| # | Phase Start | Phase End | Summary |
|---|-------------|-----------|---------|
| 1 | 2026-05-03 INTAKE | — | Worktree + branch created. Baseline skipped (no Docker). Moved to DESIGN. |
| 2 | 2026-05-06 IMPLEMENT | 2026-05-06 IMPLEMENT | Resumed from remote (no local worktree existed). Worktree recreated from origin/add-sqloutput-tests at 341c895. Audited 341c895 and discovered the QRY075 typed-lambda Set form was phantom; eliminated InterceptorKind.UpdateSet and dead hooks in 1abdd63. Added Gap A (CrossDialectStreamingTests) and Gap C (MultiContextPerFileTests) in ceb2b03. All 3125 tests pass. Transitioning to REVIEW. |
