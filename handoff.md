# Work Handoff

## Key Components
- `src/Quarry.Tests/QueryTestHarness.cs` — new composition-based test harness (real SQLite Lite + mock Pg/My/Ss)
- `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs` — generator fix for Join + Prepare + execution
- `src/Quarry.Tests/SqlOutput/` — CrossDialect test files being migrated from inheritance to harness
- `src/Quarry.Tests/Integration/` — integration test files to be merged into CrossDialect tests (Phase 3)

## Completions (This Session)
- **QueryTestHarness created** with `CreateAsync()`, `Deconstruct()`, `AssertDialects()` (both string and QueryDiagnostics overloads), `SqlAsync()`, `MockConnection` property
- **Generator bug fixed**: `EmitJoinReaderTerminal` in `TerminalBodyEmitter.cs` was not handling `IsPreparedTerminal` — emitted `IJoinedQueryBuilder<...>` as receiver instead of `PreparedQuery<TResult>`. Added `IsPreparedTerminal` check matching the pattern in `EmitReaderTerminal`.
- **Phase 2 partially complete** — 7 of 10 CrossDialect classes migrated to harness:
  - `CrossDialectOrderByTests` — migrated with execution (including Join+Prepare execution)
  - `CrossDialectDeleteTests` — migrated with execution (FK enforcement off in harness)
  - `CrossDialectInsertTests` — migrated; execution uses direct calls (not Prepare) for insert
  - `CrossDialectBatchInsertTests` — migrated; Lite execution uses full-column inserts to satisfy NOT NULL
  - `CrossDialectStringOpTests` — migrated with execution on Contains/StartsWith tests
  - `CrossDialectMiscTests` — migrated (SQL verification only — Sql.Raw can't execute on SQLite)
  - `HarnessProofOfConceptTests` — POC test validating full pattern
  - `JoinPrepareExecutionTest` — validates the generator fix for Join+Prepare+Execution
- **FK enforcement disabled** in harness via `PRAGMA foreign_keys = OFF` so DELETE tests work without dependent-row ordering

## Previous Session Completions
- PR #70 implemented `.Prepare()` multi-terminal support (prerequisite for issue #68)

## Progress
Phase 2: 7 of 10 CrossDialect classes migrated. 3 remaining: Subquery, Enum, Complex, Schema.
Phases 3–5 not started.

## Current State
- All 1895 tests passing (1894 passed, 1 skipped)
- Branch: `feature/unified-test-harness`
- 1 commit so far (Phase 0 POC). Uncommitted work includes generator fix + all Phase 2 migrations.

### Remaining Phase 2 files to migrate:
- `CrossDialectSubqueryTests.cs` — 21 tests, all SQL-only (subqueries use `u.Orders.Any()` navigation which works for SQL gen but subquery execution against SQLite needs the "Order" view and FK relationships)
- `CrossDialectEnumTests.cs` — 4 tests, includes Insert/Update with enum columns
- `CrossDialectComplexTests.cs` — 9 tests, mix of Where+Select, Join+Where+Select, pagination
- `CrossDialectSchemaTests.cs` — 9 tests, uses schema-qualified contexts (SchemaPgDb, SchemaMyDb, SchemaSsDb) and Account/Product/Widget entities

## Known Issues / Bugs
- **Generator cross-context bug**: Accessing contexts via property chains (`t.Pg.Users()`) causes the generator to emit interceptors for PgDb/SsDb inside MyDb's generated file. **Workaround**: assign to local variables first via `var (Lite, Pg, My, Ss) = t;` (Deconstruct pattern).
- **Prepare + Insert + ExecuteNonQuery parameter binding**: `.Prepare()` on an insert chain followed by `.ExecuteNonQueryAsync()` fails with "Must add values for the following parameters". Direct execution (without Prepare) works. Execution tests for inserts use direct calls, not Prepare.
- **Batch insert partial columns**: Mock-based tests can insert partial columns (e.g., `(UserName, IsActive)` without `CreatedAt`), but real SQLite enforces NOT NULL. Lite execution assertions use full required column sets.

## Dependencies / Blockers
- None — all work is self-contained on the feature branch.

## Architecture Decisions
- **Lite = real SQLite, Pg/My/Ss = MockDbConnection**: Only Lite gets execution verification. Pg/My/Ss are SQL-output-only (for now). When Docker-based databases are added, those switch to real connections with zero test code changes.
- **Deconstruct pattern**: `var (Lite, Pg, My, Ss) = t;` is required (not just convenient) because the generator has a bug with property-chained context access. This duck-typed pattern works without an interface.
- **All 4 contexts use `.Prepare()`**: For visual consistency and future-proofing (when Pg/My/Ss get real connections). Exception: Insert execution tests use direct calls due to Prepare+Insert parameter binding issue.
- **FK enforcement OFF by default**: Simplifies DELETE tests. Tests needing FK behavior can opt in via `t.SqlAsync("PRAGMA foreign_keys = ON")`.
- **MockConnection exposed**: Tests that need to capture executed SQL (Insert ExecuteNonQuery/ExecuteScalar) use `t.MockConnection.LastCommand` for Pg/My/Ss.

## Open Questions
- Should the Prepare + Insert + ExecuteNonQuery parameter binding issue be fixed in the generator before completing the migration? Currently worked around by using direct execution for Lite inserts.
- Should `PrepareTests.cs` and `PrepareIntegrationTests.cs` be merged into the harness pattern (Phase 5), or kept as standalone since they test `.Prepare()` itself?

## Next Work (Priority Order)
1. **Commit current work** — generator fix + harness + Phase 2 migrations (7 files)
2. **Finish Phase 2** — migrate remaining 3 files: CrossDialectSubqueryTests, CrossDialectEnumTests, CrossDialectComplexTests, CrossDialectSchemaTests
3. **Phase 3** — merge 7 CrossDialect+Integration pairs: Select, Join/LeftJoin, Where, Aggregate, Composition/Complex, Update, TypeMapping
4. **Phase 4** — migrate CrossDialectDiagnosticsTests, TracedFailureDiagnosticTests, VariableStoredChainTests
5. **Phase 5** — delete CrossDialectTestBase, SqliteIntegrationTestBase, consolidate Prepare tests, final verification
