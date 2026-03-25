# Work Handoff

## Key Components
- `src/Quarry.Tests/QueryTestHarness.cs` — composition-based test harness (real SQLite Lite + mock Pg/My/Ss), Deconstruct pattern
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — SELECT * elimination (identity projection enrichment), InsertInfo resolution priority fix
- `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs` — Join+Prepare execution fix, Insert+Prepare receiver fix, prepared insert/batch-insert scalar receiver fix
- `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` — enum parameter logging fix, InsertInfo from AssembledPlan
- `src/Quarry.Generator/IR/AssembledPlan.cs` — top-level InsertInfo property for Prepare chain support
- `src/Quarry.Generator/IR/SqlAssembler.cs` — InsertInfo resolution priority fix (prefer PrepareSite over execution site)
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — prepared insert terminal kind remap
- `src/Quarry.Tests/SqlOutput/` — all CrossDialect test files migrated to harness

## Completions (This Session)
- **Insert test refactor**: Refactored CrossDialectInsertTests (7 tests) and CrossDialectBatchInsertTests (8 tests) to use `.Prepare()` pattern with `AssertDialects` + real SQLite execution. Eliminated `t.MockConnection` usage from all insert tests.
- **Generator fix — Prepared insert/batch-insert scalar receiver**: `EmitInsertScalarTerminal` and `EmitBatchInsertScalarTerminal` did not check `IsPreparedTerminal`, emitting `IInsertBuilder<T>` instead of `PreparedQuery<TResult>` as receiver. Added `IsPreparedTerminal` branch with arity-2 generic signature (`<TResult, TKey>`) matching `PreparedQuery<TResult>.ExecuteScalarAsync<TKey>()`.
- **Generator fix — Prepared insert column resolution**: For `.Insert(entity).Prepare()` chains, the Prepare site's InsertInfo (derived from the object initializer) has the correct column subset, but the execution terminal's InsertInfo (derived without initializer context) includes ALL entity columns. Fixed resolution priority in both `ChainAnalyzer` (line ~608) and `SqlAssembler` (line ~61) to prefer `PrepareSite.InsertInfo` over `ExecutionSite.InsertInfo`.

## Previous Session Completions
- PR #70: `.Prepare()` multi-terminal support (prerequisite for issue #68)
- Phase 0: QueryTestHarness POC with single proof-of-concept test
- Phase 1: Finalized QueryTestHarness — Deconstruct, MockConnection, AssertDialects overloads, FK OFF, Priority column
- Phase 2: Migrated 10 CrossDialect classes to harness (OrderBy, Delete, Insert, BatchInsert, Subquery, StringOp, Misc, Enum, Complex, Schema)
- Phase 2.5: Fixed 60 SELECT * assertions — mechanical replacement with explicit column lists per entity/dialect
- Phase 3: Migrated 7 CrossDialect + Integration pairs to harness. Folded execution assertions from Integration tests.
- Phase 4: Migrated 4 remaining classes — DiagnosticsTests, TracedFailureDiagnosticTests, VariableStoredChainTests, PrepareTests
- Phase 5: Deleted CrossDialectTestBase, 7 absorbed integration tests, POC files
- Prepare + Insert parameter binding bug fix (InsertInfo on AssembledPlan, kind remap in UsageSiteDiscovery)
- Generator fixes: SELECT * elimination, Join+Prepare receiver type, enum parameter logging

## Progress
- All 5 phases complete (0→1→2→2.5→3→4→5)
- 20/20 CrossDialect test classes migrated to QueryTestHarness
- 7/7 Integration test classes absorbed and deleted
- Insert/BatchInsert tests refactored to Prepare pattern
- 1851 tests pass, 0 failures, 1 skipped

## Current State
- Branch: `feature/unified-test-harness` (9 commits on top of master)
- All work described in `impl-plan.md` is complete
- All insert/batch-insert tests use standard Prepare pattern
- Ready for PR to master

## Known Issues / Bugs
- **Generator cross-context bug**: `t.Pg.Users()` causes cross-context type references in generated interceptor files. Workaround: use Deconstruct pattern `var (Lite, Pg, My, Ss) = t;`. Root cause not investigated.
- **Sql.Raw functions not executable on SQLite**: Tests using `Sql.Raw<bool>("custom_func(...)")` are SQL-verification-only.
- **TypeMappingIntegrationTests kept standalone**: Has custom `accounts` schema setup. CrossDialectTypeMappingTests is SQL-only (accounts table not in harness).

## Dependencies / Blockers
- None.

## Architecture Decisions
- **QueryTestHarness composition over inheritance**: Each test creates its own harness via `await using var t = await QueryTestHarness.CreateAsync()` — no shared mutable state, fully parallelizable. Replaces both `CrossDialectTestBase` (mock-only) and `SqliteIntegrationTestBase` (execution-only).
- **Deconstruct pattern required**: `var (Lite, Pg, My, Ss) = t;` avoids generator cross-context bug with property-chained access.
- **SELECT * eliminated at generator level**: ChainAnalyzer always expands identity projections to explicit columns using EntityRef metadata.
- **FK enforcement OFF by default**: `PRAGMA foreign_keys = OFF` in harness. Tests needing FK behavior opt in.
- **SqliteIntegrationTestBase kept**: Still used by PrepareIntegrationTests and RawSqlIntegrationTests (not absorbed).
- **InsertInfo resolution priority**: For Prepare chains, PrepareSite.InsertInfo (initializer-derived) takes priority over ExecutionSite.InsertInfo (all-columns fallback). Fixed in both ChainAnalyzer and SqlAssembler.
- **InsertInfo on AssembledPlan**: InsertInfo is resolved once during assembly and stored as a top-level property. All downstream emitters read `chain.InsertInfo`.
- **Batch insert Lite execution uses extra columns**: CreatedAt added to Lite batch insert column selectors for NOT NULL compliance. Pg/My/Ss use mock connections that don't enforce schema constraints.

## Open Questions
- Should PrepareIntegrationTests/RawSqlIntegrationTests be migrated to harness in a follow-up?
- Should the accounts table be added to QueryTestHarness to enable TypeMapping execution tests?

## Next Work (Priority Order)
1. **Create PR** for `feature/unified-test-harness` → `master`
2. **Optional**: Migrate PrepareIntegrationTests and RawSqlIntegrationTests to harness (would allow deleting SqliteIntegrationTestBase)
3. **Optional**: Add accounts table to QueryTestHarness for TypeMapping execution tests
