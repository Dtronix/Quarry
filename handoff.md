# Work Handoff

## Key Components
- `src/Quarry.Tests/QueryTestHarness.cs` — composition-based test harness (real SQLite Lite + mock Pg/My/Ss), Deconstruct pattern
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — SELECT * elimination (identity projection enrichment)
- `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs` — Join+Prepare execution fix, Insert+Prepare receiver fix
- `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` — enum parameter logging fix, InsertInfo from AssembledPlan
- `src/Quarry.Generator/IR/AssembledPlan.cs` — top-level InsertInfo property for Prepare chain support
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — prepared insert terminal kind remap
- `src/Quarry.Tests/SqlOutput/` — all CrossDialect test files migrated to harness
- `src/Quarry.Tests/Integration/` — 7 integration test files deleted (absorbed), 5 kept standalone

## Completions (This Session)
- **Phase 2.5**: Fixed 60 SELECT * assertions in 5 already-migrated files — mechanical replacement with explicit column lists per entity/dialect
- **Phase 3**: Migrated 7 CrossDialect + Integration pairs to harness. Folded execution assertions from corresponding Integration tests. Added new aggregate and left-join tests.
- **Phase 4**: Migrated 4 remaining classes — CrossDialectDiagnosticsTests (fixed column-name-vs-predicate assertion ambiguity), TracedFailureDiagnosticTests, VariableStoredChainTests, PrepareTests
- **Phase 5**: Deleted CrossDialectTestBase, 7 absorbed integration tests, POC files. Removed unused `hasSelectClause` variable from ChainAnalyzer.cs.
- **Prepare + Insert bug fix**: Two-layer fix. (1) Added `InsertInfo` as top-level property on `AssembledPlan` so `EmitCarrierInsertTerminal` reads from chain.InsertInfo instead of chain.ExecutionSite.InsertInfo (which was null for Prepare chains). (2) Added kind remap in `UsageSiteDiscovery` step 8b so prepared insert terminals get `InsertExecuteNonQuery` kind instead of generic `ExecuteNonQuery`. Also fixed `EmitInsertDiagnosticsTerminal` receiver type for prepared terminals.

## Previous Session Completions
- PR #70: `.Prepare()` multi-terminal support (prerequisite for issue #68)
- Phase 0: QueryTestHarness POC with single proof-of-concept test
- Phase 1: Finalized QueryTestHarness — Deconstruct, MockConnection, AssertDialects overloads, FK OFF, Priority column
- Phase 2: Migrated 10 CrossDialect classes to harness (OrderBy, Delete, Insert, BatchInsert, Subquery, StringOp, Misc, Enum, Complex, Schema)
- Generator fixes: SELECT * elimination, Join+Prepare receiver type, enum parameter logging

## Progress
- All 5 phases complete (0→1→2→2.5→3→4→5)
- 20/20 CrossDialect test classes migrated to QueryTestHarness
- 7/7 Integration test classes absorbed and deleted
- Prepare + Insert parameter binding bug fixed
- 1851 tests pass, 0 failures, 1 skipped

## Current State
- Branch: `feature/unified-test-harness` (8 commits on top of master)
- All work described in `impl-plan.md` is complete
- Prepare + Insert bug is fixed (was a known limitation, now resolved)
- Ready for PR to master

## Known Issues / Bugs
- **Generator cross-context bug**: `t.Pg.Users()` causes cross-context type references in generated interceptor files. Workaround: use Deconstruct pattern `var (Lite, Pg, My, Ss) = t;`. Root cause not investigated.
- **Sql.Raw functions not executable on SQLite**: Tests using `Sql.Raw<bool>("custom_func(...)")` are SQL-verification-only.
- **TypeMappingIntegrationTests kept standalone**: Has custom `accounts` schema setup. CrossDialectTypeMappingTests is SQL-only (accounts table not in harness).

## Dependencies / Blockers
- None.

## Architecture Decisions
- **QueryTestHarness composition over inheritance**: Each test creates its own harness via `await using var t = await QueryTestHarness.CreateAsync()` — no shared mutable state, fully parallelizable. Replaces both `CrossDialectTestBase` (mock-only) and `SqliteIntegrationTestBase` (execution-only).
- **Deconstruct pattern required**: `var (Lite, Pg, My, Ss) = t;` avoids generator cross-context bug with property-chained access. Duck-typed C# pattern.
- **SELECT * eliminated at generator level**: ChainAnalyzer always expands identity projections to explicit columns using EntityRef metadata. Zero breaking API change.
- **FK enforcement OFF by default**: `PRAGMA foreign_keys = OFF` in harness. Tests needing FK behavior opt in.
- **SqliteIntegrationTestBase kept**: Still used by PrepareIntegrationTests and RawSqlIntegrationTests (not absorbed).
- **Assertion specificity after SELECT * elimination**: Tests checking conditional WHERE activity now assert `Does.Contain("\"IsActive\" = 1")` instead of `Does.Contain("IsActive")` to avoid false matches with the SELECT column list.
- **InsertInfo on AssembledPlan**: InsertInfo is resolved once during assembly (from executionSite, clause sites, or prepareSite) and stored as a top-level property. All downstream emitters read `chain.InsertInfo` instead of hunting through sites. This fixed the Prepare+Insert parameter binding bug where `chain.ExecutionSite.InsertInfo` was null for prepared terminals.

## Open Questions
- Should PrepareIntegrationTests/RawSqlIntegrationTests be migrated to harness in a follow-up?
- Should the accounts table be added to QueryTestHarness to enable TypeMapping execution tests?

## Next Work (Priority Order)
1. **Refactor CrossDialectInsertTests + CrossDialectBatchInsertTests to use `.Prepare()` pattern** — now that the Prepare+Insert parameter binding bug is fixed, these tests should use the standard `.Prepare().ToDiagnostics()` pattern for SQL verification instead of the `MockConnection.LastCommand` workaround. This eliminates `t.MockConnection` usage from insert tests and makes them consistent with all other CrossDialect tests. The `ExecuteNonQueryAsync`/`ExecuteScalarAsync` tests should use `.Prepare()` on the Lite chain with real execution, and `.Prepare().ToDiagnostics()` on Pg/My/Ss for SQL verification.
2. **Create PR** for `feature/unified-test-harness` → `master`
3. **Optional**: Migrate PrepareIntegrationTests and RawSqlIntegrationTests to harness (would allow deleting SqliteIntegrationTestBase)
4. **Optional**: Add accounts table to QueryTestHarness for TypeMapping execution tests
