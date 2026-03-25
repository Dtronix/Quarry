# Work Handoff

## Key Components
- `src/Quarry.Tests/QueryTestHarness.cs` — composition-based test harness (real SQLite Lite + mock Pg/My/Ss), Deconstruct pattern
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — SELECT * elimination (identity projection enrichment)
- `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs` — Join+Prepare execution fix
- `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` — enum parameter logging fix
- `src/Quarry.Tests/SqlOutput/` — CrossDialect test files migrated to harness
- `src/Quarry.Tests/Integration/` — integration test files to be merged (Phase 3)

## Completions (This Session)

### Generator fixes
- **SELECT * eliminated**: Removed `hasSelectClause` guard in `ChainAnalyzer.cs:577`. Identity projections now always enrich with entity columns, so `SELECT *` is never emitted. All queries produce explicit column lists like `SELECT "UserId", "UserName", "Email", ...`.
- **Join+Prepare+Execution**: `EmitJoinReaderTerminal` in `TerminalBodyEmitter.cs` now checks `IsPreparedTerminal` and emits `PreparedQuery<TResult>` as receiver instead of `IJoinedQueryBuilder<...>`.
- **Enum parameter logging**: `CarrierEmitter.cs` now checks `param.IsEnum` alongside `IsNonNullableValueType` so enum captured variables use `.ToString()` instead of invalid `?.ToString()`.

### Test infrastructure
- **QueryTestHarness**: `CreateAsync()`, `Deconstruct()`, `AssertDialects()` (string + QueryDiagnostics overloads), `SqlAsync()`, `MockConnection` property, FK enforcement OFF, Priority column in orders schema.
- **Phase 2 complete**: All 10 CrossDialect test classes migrated to harness: OrderBy, Delete, Insert, BatchInsert, StringOp, Misc, Subquery, Enum, Complex, Schema.
- **Execution verification**: Subquery, Complex, OrderBy, Delete, Enum tests verify actual SQLite query results.

## Previous Session Completions
- PR #70 implemented `.Prepare()` multi-terminal support (prerequisite for issue #68)

## Progress
- Phase 2: 10/10 CrossDialect classes migrated to harness (complete)
- **60 test assertions need updating**: Expected SQL strings still contain `SELECT *` but generator now emits explicit columns. Mechanical fix needed.
- Phases 3–5 not started.

## Current State
- Branch: `feature/unified-test-harness` (3 commits)
- **60 tests failing** — all due to `SELECT *` in expected SQL assertions that need updating to explicit column lists
- The failures are in these files (mix of already-migrated and not-yet-migrated files):
  - `CrossDialectSubqueryTests.cs` — already migrated, needs SQL assertion updates
  - `CrossDialectMiscTests.cs` — already migrated, needs SQL assertion updates
  - `CrossDialectStringOpTests.cs` — already migrated, needs SQL assertion updates
  - `CrossDialectSchemaTests.cs` — already migrated, needs SQL assertion updates
  - `CrossDialectSelectTests.cs` — NOT YET migrated (Phase 3)
  - `CrossDialectWhereTests.cs` — NOT YET migrated (Phase 3)
  - `PrepareTests.cs` — NOT YET migrated (Phase 5)
  - `CrossDialectDiagnosticsTests.cs` — NOT YET migrated (Phase 4)
  - `CrossDialectCompositionTests.cs` — NOT YET migrated (Phase 3)
  - `VariableStoredChainTests.cs` — NOT YET migrated (Phase 4)
  - `TracedFailureDiagnosticTests.cs` — NOT YET migrated (Phase 4)
  - `EndToEndSqlTests.cs` — standalone, may need update

### What the SELECT * fix changes
Before: `SELECT * FROM "users" WHERE "IsActive" = 1`
After: `SELECT "UserId", "UserName", "Email", "IsActive", "CreatedAt", "LastLogin" FROM "users" WHERE "IsActive" = 1`

For orders: `SELECT "OrderId", "UserId", "Total", "Status", "Priority", "OrderDate", "Notes" FROM "orders" ...`

The fix is mechanical: replace `SELECT *` in each expected SQL string with the entity's explicit column list.

## Known Issues / Bugs
- **Generator cross-context bug**: Accessing contexts via property chains (`t.Pg.Users()`) causes cross-context type references in generated interceptor files. **Workaround**: Use `var (Lite, Pg, My, Ss) = t;` (Deconstruct pattern). Root cause is in the generator's file-routing logic.
- **Prepare + Insert + ExecuteNonQuery parameter binding**: `.Prepare()` on insert chain + `.ExecuteNonQueryAsync()` fails with "Must add values for parameters". Direct execution works. Insert execution tests use direct calls.

## Dependencies / Blockers
- None.

## Architecture Decisions
- **Option C adopted for SELECT ***: Instead of requiring `.Select()` on every query or just discouraging `SELECT *` in tests, the generator now always expands identity projections to explicit columns. Zero breaking API change, fully predictable SQL.
- **Deconstruct pattern required**: `var (Lite, Pg, My, Ss) = t;` is required due to generator cross-context bug with property-chained access. Duck-typed C# pattern (no interface needed).
- **All 4 dialects use .Prepare()**: Consistent shape across all chains for visual verification and future Docker-based execution.
- **FK enforcement OFF**: `PRAGMA foreign_keys = OFF` in harness so DELETE tests work without ordering dependent rows.
- **Harness Priority column**: Added `Priority INTEGER NOT NULL DEFAULT 1` to orders schema (wasn't in original SqliteIntegrationTestBase). Seed values: Order1=2(High), Order2=1(Normal), Order3=3(Urgent).

## Open Questions
- Should the Prepare + Insert parameter binding issue be fixed before completing migration?
- Should PrepareTests/PrepareIntegrationTests be merged into harness (Phase 5) or kept standalone?

## Next Work (Priority Order)
1. **Fix 60 failing SELECT * assertions** — mechanical replacement in all affected test files. Use these column lists:
   - Users: `"UserId", "UserName", "Email", "IsActive", "CreatedAt", "LastLogin"`
   - Orders: `"OrderId", "UserId", "Total", "Status", "Priority", "OrderDate", "Notes"`
   - Each dialect uses its own quoting: SQLite/Pg use `"col"`, MySQL uses `` `col` ``, SqlServer uses `[col]`
2. **Phase 3** — merge 7 CrossDialect+Integration pairs: Select, Join/LeftJoin, Where, Aggregate, Composition/Complex, Update, TypeMapping
3. **Phase 4** — migrate Diagnostics, TracedFailure, VariableStoredChain
4. **Phase 5** — delete old base classes, consolidate Prepare tests, final verification
