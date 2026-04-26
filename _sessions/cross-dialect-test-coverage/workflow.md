# Workflow: cross-dialect-test-coverage

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
phases-total: 12
phases-complete: 4

## Problem Statement

The Quarry test suite has rich SQLite-only execution coverage in `src/Quarry.Tests/Integration/*.cs`
(joins, GroupBy, set ops, window funcs, CTEs, navigation subqueries, pagination, DateTimeOffset,
RawSql, Logging, EntityReader, Prepare). The PostgreSQL / MySQL / SQL Server containers introduced
by PRs #266, #270, #271, #275, #276 are exercised by only a narrow regression-guard slice
(single-entity INSERT, batch INSERT, IN(collection)) — leaving every other feature untested at
runtime on the three non-SQLite dialects.

The existing `src/Quarry.Tests/SqlOutput/CrossDialect*.cs` pattern already runs `Prepare()` +
`AssertDialects(...)` + `ExecuteFetchAllAsync()` on all four dialects in one method. Adopting the
same pattern across the gap closes the coverage difference.

A prerequisite for "compiles ⇒ executes on every dialect" (which removes the need for any
`if (supported)` skip logic in test bodies) is tightening dialect-specific analyzer rules:
`SuboptimalForDialectRule` (QRA502) currently emits Warnings for cases that can never execute
(MySQL FULL OUTER JOIN, SQL Server OFFSET/FETCH without ORDER BY) and emits warnings for cases
that DO execute on modern engines (SQLite ≥ 3.39 supports RIGHT/FULL OUTER JOIN — needs
verification against the version `Microsoft.Data.Sqlite` ships).

A second gap noted at `GeneratorTests.cs:1366-1370`: QRY070 / QRY071 (INTERSECT ALL / EXCEPT ALL
not supported) have no end-to-end pipeline tests — only descriptor-existence tests.

### Baseline
- Build: succeeds.
- Tests: 3340/3340 passing (Quarry.Analyzers.Tests 117, Quarry.Migration.Tests 201, Quarry.Tests 3022).
- No pre-existing failures.

### Scope
1. Promote QRA502 cases that produce unexecutable SQL to Error (MySQL FULL OUTER JOIN,
   SQL Server OFFSET-no-ORDERBY). Verify SQLite ≥ 3.39 capability and remove or downgrade the
   stale SQLite RIGHT/FULL OUTER rules accordingly.
2. Add full-pipeline analyzer integration tests for QRA502 using
   `AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(source)`.
3. Add full-pipeline generator integration tests for QRY070 / QRY071 using
   `RunGeneratorWithDiagnostics(compilation)` — closes the explicit gap noted at
   `GeneratorTests.cs:1366-1370`.
4. Convert SQLite-only `Integration/*.cs` files into 4-dialect `SqlOutput/CrossDialect*.cs` tests
   using the existing verbatim pattern from `CrossDialectSelectTests.cs`. New file mapping:
   - `JoinedCarrierIntegrationTests.cs` → extend `CrossDialectJoinTests.cs`
   - `JoinNullableIntegrationTests.cs` → extend `JoinNullableProjectionTests.cs` or `CrossDialectJoinTests.cs`
   - `ContainsIntegrationTests.cs` → extend `CrossDialectWhereTests.cs`
   - `CollectionScalarIntegrationTests.cs` → extend `CrossDialectWhereTests.cs`
   - `DateTimeOffsetIntegrationTests.cs` → extend `CrossDialectTypeMappingTests.cs`
   - `PrepareIntegrationTests.cs` → extend `PrepareTests.cs`
   - `EntityReaderIntegrationTests.cs` → new `CrossDialectEntityReaderTests.cs`
   - `RawSqlIntegrationTests.cs` → new `CrossDialectRawSqlTests.cs`
   - `LoggingIntegrationTests.cs` → new `CrossDialectLoggingTests.cs`
5. Delete the now-redundant `Integration/*.cs` files.
6. Keep narrow provider-regression guards: `PostgresIntegrationTests.cs`,
   `MySqlIntegrationTests.cs`, `SqlServerIntegrationTests.cs`, `NpgsqlParameterBindingTests.cs`,
   `MsSqlContainerSmokeTests.cs`, and the `*TestContainer.cs` plumbing.

### Invariants
- Pattern: explicit verbatim pattern (no helper) — matches the existing 1000+ tests.
- Divergence: no `if (supported)` skips. Dialect features that can't execute are analyzer errors
  at compile time; valid chains execute on every configured dialect.
- Layout: cross-dialect tests live in `SqlOutput/CrossDialect*.cs`, not `Integration/*`.

## Decisions

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-04-25 | Source: current discussion | User selected (1) Current discussion. |
| 2026-04-25 | Test home: SqlOutput/CrossDialect*.cs | One uniform home for SQL-shape verification + 4-dialect execution; delete the SQLite-only Integration/* files once mirrored. |
| 2026-04-25 | Pattern: explicit verbatim | Consistent with existing CrossDialect* tests; no new abstraction to learn. |
| 2026-04-25 | Divergence handling: none — promote to analyzer error | If Quarry can generate invalid SQL for a configured dialect that's a compile-time bug, not a runtime concern. |
| 2026-04-25 | PR scope: single large PR | One sweep, single review. |
| 2026-04-25 | Add full-pipeline analyzer integration tests for QRA502 | Existing DialectRuleTests.cs only unit-tests the rule with synthetic contexts. End-to-end through real Roslyn analysis is missing. |
| 2026-04-25 | Add full-pipeline generator integration tests for QRY070/QRY071 | GeneratorTests.cs:1366-1370 explicitly notes this gap; descriptor-existence tests are insufficient. |
| 2026-04-25 | Remove stale SQLite RIGHT/FULL OUTER QRA502 rules | Empirically verified Microsoft.Data.Sqlite 10.0.3 ships SQLite 3.49.1; both joins execute. The rules date from when the bundled SQLite was older. |
| 2026-04-25 | QRA502: keep MySQL RIGHT JOIN as Warning | MySQL fully supports RIGHT JOIN — the warning is a perf hint about the query planner, not a capability issue. SQL still executes. |
| 2026-04-25 | QRA502: promote MySQL FULL OUTER JOIN to Error | MySQL has never supported FULL OUTER JOIN; SQL physically cannot execute. |
| 2026-04-25 | QRA502: promote SqlServer OFFSET-no-ORDERBY to Error | SQL Server rejects OFFSET/FETCH without ORDER BY at parse time; SQL physically cannot execute. |

## Suspend State

## Session Log

| # | Phase Start | Phase End | Summary |
|---|-------------|-----------|---------|
| 1 | INTAKE | DESIGN | Confirmed problem from discussion. Created branch + worktree. Baseline 3340/3340 passing. Auto-transitioned to DESIGN. |
| 1 | DESIGN | PLAN | Verified SQLite 3.49.1 supports RIGHT/FULL OUTER via empirical probe. Locked rule decisions, conversion pattern, file mapping. User approved. |
| 1 | PLAN | IMPLEMENT | Wrote 12-phase plan in plan.md (Track A: phases 1–3 hardening; Track B: phases 4–12 conversion). User approved. |
| 1 | IMPLEMENT P1 | IMPLEMENT P1 | Phase 1 complete — added QRA503 (Error) descriptor; SuboptimalForDialectRule emits QRA502 (perf) for MySQL RIGHT JOIN, QRA503 (capability) for MySQL FULL OUTER + SqlServer OFFSET-no-ORDERBY; removed stale SQLite rules; updated DialectRuleTests; pruned MySQL clause from CrossDialectJoinTests.FullOuterJoin_OnClause + JoinNullableIntegrationTests.FullOuterJoin_SqlVerification. Tests: 3341/3341 (was 3340 — net +1 test). |
| 1 | IMPLEMENT P2 | IMPLEMENT P2 | Phase 2 complete — added 9 full-pipeline analyzer integration tests via AnalyzerTestHelper covering MySQL/PG/Ss/SQLite × FULL OUTER JOIN and SqlServer × OFFSET (with/without ORDER BY) plus three negative dialects. Tests: 3350/3350 (Analyzers 127). |
| 1 | IMPLEMENT P3 | IMPLEMENT P3 | Phase 3 complete — added 8 full-pipeline generator integration tests for QRY070/QRY071 covering INTERSECT ALL and EXCEPT ALL across 4 dialects. **Discovered + fixed silent diagnostic drop**: QuarryGenerator.s_deferredDescriptors was missing IntersectAllNotSupported/ExceptAllNotSupported/SetOperationProjectionMismatch — GetDescriptorById returned null and the diagnostics were dropped at QuarryGenerator.cs:524. Removed the obsolete "cannot test these" note. Tests: 3358/3358 (Quarry.Tests +8). |
| 1 | IMPLEMENT P4 | IMPLEMENT P4 | Phase 4 complete — converted ContainsIntegrationTests.cs to 7 cross-dialect tests in CrossDialectWhereTests (4 SELECT) + CrossDialectDeleteTests (3 DELETE). Exercise the runtime collection-expansion path (List<int>, IEnumerable<int>) on all 4 dialects. Original Integration file deleted. Tests: 3358/3358 (count unchanged: 7 deleted + 7 added; each new test runs on 4× dialects). |
