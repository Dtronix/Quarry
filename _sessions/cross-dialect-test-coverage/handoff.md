# Work Handoff: cross-dialect-test-coverage

## Key Components

- `src/Quarry.Analyzers/Rules/Dialect/SuboptimalForDialectRule.cs` — emits QRA502 (perf Warning) and QRA503 (capability Error). Reference for dialect-rule patterns.
- `src/Quarry.Analyzers.Tests/AnalyzerTestHelper.cs` — `GetAnalyzerDiagnosticsAsync(source)` runs the analyzer end-to-end; pattern used in Phase 2 pipeline tests.
- `src/Quarry.Tests/GeneratorTests.cs` — `RunGeneratorWithDiagnostics(compilation)` runs the source generator end-to-end; pattern used in Phase 3 generator tests.
- `src/Quarry.Tests/QueryTestHarness.cs` — `CreateAsync()` produces 4 dialect contexts (`Lite`, `Pg`, `My`, `Ss`) all backed by real DBs (in-memory SQLite + Testcontainers PG/MySQL/MsSQL). Cross-dialect data parity is in place: PG/My/Ss containers seed identical Alice/Bob/Charlie/orders/etc. baseline.
- `src/Quarry.Tests/SqlOutput/CrossDialectSelectTests.cs` lines 14–56 — canonical example of the "verbatim 4-dialect Prepare + AssertDialects + 4× ExecuteFetchAllAsync + 4× assertions" pattern.
- `src/Quarry.Generator/QuarryGenerator.cs` line 754 — `s_deferredDescriptors` registry. Any new generator-emitted diagnostic must be added here or it gets silently dropped at line 524 (this was the Phase 3 bug fix).

## Completions (This Session)

**Track B (Phase 5):**
- Phase 5: Converted `Integration/CollectionScalarIntegrationTests.cs` (7 SQLite-only tests for runtime collection + scalar parameter mixing) → 7 cross-dialect execution tests appended to `SqlOutput/CrossDialectWhereTests.cs` in a new "Collection + scalar — runtime parameter mixing (4-dialect execution)" region. Skipped Prepare+AssertDialects per Phase 4 precedent — SQL-shape coverage already lives in `CollectionParameterCollisionTests.cs` (regression #140). Original Integration file deleted. Source-generator regenerated `ManifestOutput/quarry-manifest.{mysql,postgresql,sqlserver}.md`. Tests: 3030/3030 in Quarry.Tests (count unchanged; each new test now exercises 4 dialects).

## Previous Session Completions

**Track A (Phases 1–3):**
- Phase 1 commit `290b188`: Added QRA503 (Error) descriptor; promoted MySQL FULL OUTER + SqlServer OFFSET-no-ORDERBY to QRA503; removed stale SQLite RIGHT/FULL OUTER rules (verified Microsoft.Data.Sqlite 10.0.3 ships SQLite 3.49.1); pruned MySQL clauses from existing FULL OUTER tests in CrossDialectJoinTests + JoinNullableIntegrationTests.
- Phase 2 commit `8cc4fd4`: 9 full-pipeline analyzer integration tests for QRA503 via `AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync`.
- Phase 3 commit `3786d38`: 8 full-pipeline generator integration tests for QRY070 / QRY071 via `RunGeneratorWithDiagnostics`. **Caught + fixed bug** — `s_deferredDescriptors` was missing IntersectAllNotSupported/ExceptAllNotSupported/SetOperationProjectionMismatch, causing silent diagnostic drops. The original `// Note: cannot test these` comment at GeneratorTests.cs:1366 was rationalising this bug; comment removed.

**Track B (Phase 4):**
- Phase 4 commit `be7f748`: Converted `Integration/ContainsIntegrationTests.cs` (7 SQLite-only) → 7 cross-dialect tests split between `SqlOutput/CrossDialectWhereTests.cs` (4 SELECT) and `SqlOutput/CrossDialectDeleteTests.cs` (3 DELETE). Original Integration file deleted.

## Progress

- 5 / 12 plan phases complete.
- Track A (analyzer / generator hardening): **complete**.
- Track B (test conversion): 2 / 9 phases done.

## Current State

- Branch: `cross-dialect-test-coverage`, 5 commits ahead of `master`.
- Working tree: clean.
- Tests: 3358 / 3358 passing (Quarry.Analyzers.Tests 127, Quarry.Migration.Tests 201, Quarry.Tests 3030).
- No pre-existing failures from baseline.

## Known Issues / Bugs

None blocking. Phase 3 already fixed one pre-existing bug (silent QRY070/QRY071/QRY072 drop).

## Dependencies / Blockers

None.

## Architecture Decisions

Recorded in `workflow.md` Decisions table. Highlights:

- **QRA502 vs QRA503 split**: capability errors get a separate Error-severity descriptor rather than reusing the QRA502 Warning. Rationale: IDE filtering, severity ergonomics, and clearly distinguishing "your SQL won't run" from "your SQL is suboptimal."
- **Conversion home is `SqlOutput/CrossDialect*`**, not a dedicated `Integration/Cross*` directory. Rationale: existing CrossDialect tests already do exactly what we want (SQL-shape verification + 4-dialect execution); one home for one concern.
- **Verbatim pattern, no helper.** Each cross-dialect test repeats `var lt = …Prepare()` four times and the four assertion blocks. Matches the existing 1000+ tests; reviewers can diff line-for-line.
- **No `if (supported)` skips.** Capability gaps are analyzer Errors at compile time (QRA503 / QRY070 / QRY071). If a chain compiles for a dialect, it executes for that dialect. Period.
- **MySQL FULL OUTER tests**: existing tests had a `My.Users().FullOuterJoin(...)` line; promoted QRA503 makes that fail to compile. The line was removed and the test now only verifies SQL on Lite/Pg/Ss. Same in JoinNullableIntegrationTests.

## Open Questions

None. Plan is locked.

## Next Work (Priority Order)

Resume mid-IMPLEMENT. The remaining 7 phases (6–12) are sequential per the plan. Each is mechanical:

1. **Phase 6** — Convert `Integration/JoinedCarrierIntegrationTests.cs` (219 lines, ~8 tests; 2-table inner joins, tuple/entity projections, COUNT terminal). Target: extend `SqlOutput/CrossDialectJoinTests.cs`. Delete original.
2. **Phase 7** — Convert `Integration/JoinNullableIntegrationTests.cs` (212 lines, ~6 tests; LEFT JOIN nullable propagation). Target: extend `SqlOutput/JoinNullableProjectionTests.cs`. Delete original. *Highest dialect-divergence payoff.*
3. **Phase 8** — Convert `Integration/DateTimeOffsetIntegrationTests.cs` (78 lines, ~3 tests). Target: extend `SqlOutput/CrossDialectTypeMappingTests.cs`. **Caveat**: PG coerces incoming DateTimeOffset to UTC; MySQL stores DATETIME without offset; SQL Server has native datetimeoffset. May need tolerance comparison or UTC-only seed. Pilot one test before committing the assertion shape. Delete original.
4. **Phase 9** — Convert `Integration/PrepareIntegrationTests.cs` (222 lines, ~8 tests; Prepare single + multi-terminal). Target: extend `SqlOutput/PrepareTests.cs`. Delete original.
5. **Phase 10** — Convert `Integration/EntityReaderIntegrationTests.cs` (236 lines, ~15 tests; custom EntityReader materialization). Target: new `SqlOutput/CrossDialectEntityReaderTests.cs`. ProductSchema already has `[EntityReader(typeof(ProductReader))]` so the harness gives 4-dialect coverage for free. Replace the test's custom `_db = new TestDbContext(_connection)` setup with `QueryTestHarness.CreateAsync()`. Delete original.
6. **Phase 11** — Convert `Integration/RawSqlIntegrationTests.cs` (328 lines, ~15 tests). Target: new `SqlOutput/CrossDialectRawSqlTests.cs`. **Caveat**: each dialect needs its own SQL string with the correct parameter syntax (`@p0` for Lite/Ss, `$1` for Pg, `?` for MySQL). Verbatim pattern: four different SQL strings, four `RawSqlAsync` calls, four identical assertion blocks. Delete original.
7. **Phase 12** — Convert `Integration/LoggingIntegrationTests.cs` (760 lines, ~30 tests; Logsmith abstraction, Sensitive() redaction, opId correlation, slow-query Warning). Target: new `SqlOutput/CrossDialectLoggingTests.cs`. **Caveat**: `LogsmithOutput.Logger` is process-wide; mark `[NonParallelizable]`. Pattern: `_logger.Clear()` between each dialect's execution; assert log shape per dialect. Check whether `RecordingLogsmithLogger.Clear()` exists — add it if needed (file at `Integration/RecordingLogsmithLogger.cs`). Delete original.

After Phase 12: REVIEW (delegate the 6-section diff analysis to an agent producing `review.md`, then classify findings on the main context), REMEDIATE, REBASE on `origin/master`, open PR, FINALIZE.

### Known gotchas for Phase 5+ work

- **Schema parity**: every entity used in an Integration test (User, Order, OrderItem, Account, Address, Warehouse, Shipment, Product, Event) is already exposed via Pg.PgDb / My.MyDb / Ss.SsDb in `Samples/`. Need to type-qualify joined entities like `Pg.Order` / `My.Order` / `Ss.Order` (the type system can't infer them).
- **Identity counters**: PG/My/Ss baselines auto-generate identities continuing from the seed. Tests that assert `newId > 2` (since seed populates IDs 1–2) work correctly.
- **Test isolation**: PG/My/Ss tests run inside per-test transactions that roll back on dispose; deletes/inserts within a test are visible to subsequent reads but rolled back at harness dispose. SQLite uses fresh in-memory DB per test.
- **Static readonly arrays inline**: `private static readonly int[] _ids = {1, 2}` gets constant-folded to `IN (1, 2)` literal. Local `var ids = new List<int>{1,2}` and method-returned arrays go through runtime expansion. Phase 4 covered the runtime path; the existing `Delete_Where_Contains` test at CrossDialectDeleteTests.cs:189 covers the inlined path.
- **Build cycle is slow**: ~50s for `dotnet build`, ~60s for `dotnet test Quarry.Tests`. Use `--no-build` after the first `dotnet test`. Run only the affected feature when iterating: `--filter "FullyQualifiedName~CrossDialect..."`.

## Test status summary (resume baseline)

```
Quarry.Analyzers.Tests:  127 / 127  (was 117 in Track A baseline; +9 P2 + +1 P1 net)
Quarry.Migration.Tests:  201 / 201
Quarry.Tests:           3030 / 3030 (was 3022; +8 from P3 generator tests)
                        ─────
Total:                  3358 / 3358
```
