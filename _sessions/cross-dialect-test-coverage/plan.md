# Plan: cross-dialect-test-coverage

## Overview

Two tracks of work, executed in order.

**Track A — Analyzer / generator hardening (Phases 1–3).** Tighten dialect-specific rules that
currently emit Warnings for cases that physically cannot execute, and close the explicit
GeneratorTests.cs:1366-1370 testing gap for QRY070 / QRY071. After this track lands, the invariant
"if it compiles for a dialect, it executes on that dialect" is enforced at compile time, so the
test conversion in Track B does not need any `if (supported)` branches in test bodies.

**Track B — Cross-dialect test conversion (Phases 4–12).** Mechanically convert each SQLite-only
`Integration/*.cs` file into a 4-dialect `SqlOutput/CrossDialect*.cs` test, using the verbatim
pattern from `CrossDialectSelectTests.cs` (lines 14–56): build a `Prepare()` per dialect, call
`AssertDialects(...)` for SQL-shape verification, then execute on each context with the same
data assertions. Delete the original Integration/* file as the last step of each phase.

Each phase is independently committable and ends with a green test run. The session directory is
staged with every commit per the workflow rule.

## Track A — Analyzer / generator hardening

### Phase 1 — Tighten QRA502 dialect rules

**Files:**
- `src/Quarry.Analyzers/Rules/Dialect/SuboptimalForDialectRule.cs` — modify rule logic
- `src/Quarry.Analyzers/AnalyzerDiagnosticDescriptors.cs` — add a separate Error-severity descriptor for the unexecutable cases
- `src/Quarry.Analyzers.Tests/DialectRuleTests.cs` — update existing unit tests

**Changes:**

The QRA502 descriptor is currently a single Warning-severity descriptor reused for all four
suboptimal cases. We need an Error-severity descriptor for the cases that produce SQL the
configured dialect cannot execute. Approach: add a sibling descriptor `UnsupportedForDialect`
(QRA503), and split the four checks across the two:

| Check | Old severity | New severity | Descriptor |
|---|---|---|---|
| SQLite RIGHT JOIN | Warning (QRA502) | **Removed** | n/a — SQLite ≥ 3.39 supports it |
| SQLite FULL OUTER JOIN | Warning (QRA502) | **Removed** | n/a — SQLite ≥ 3.39 supports it |
| MySQL RIGHT JOIN (perf hint) | Warning (QRA502) | Warning (QRA502) | unchanged — SQL still executes |
| MySQL FULL OUTER JOIN | Warning (QRA502) | **Error (QRA503)** | new descriptor |
| SQL Server OFFSET without ORDER BY | Warning (QRA502) | **Error (QRA503)** | new descriptor |

QRA503 ID rationale: the QRA5xx range is "Dialect"; QRA501 = optimization hint, QRA502 = perf
warning, QRA503 = unsupported (Error). This keeps perf hints and capability errors visually
distinct in IDE output.

**Tests modified:**
- Delete `QRA502_SqliteRightJoin_Reports` (line 107) and `QRA502_SqliteFullOuterJoin_Reports`
  (line 128) — rules removed.
- Update `QRA502_MysqlFullOuterJoin_Reports` (line 139) → `QRA503_MysqlFullOuterJoin_Reports`,
  assert new descriptor ID and Error severity.
- Update `QRA502_SqlServerOffsetWithoutOrderBy_Reports` (line 170) → `QRA503_SqlServerOffsetWithoutOrderBy_Reports`,
  same updates.
- Negative tests (`_NoReport`) on PG/SqlServer for FULL OUTER stay as-is — they still pass
  because we don't emit any diagnostic.

**Deps:** none. **Tests:** rebuild + `dotnet test src/Quarry.Analyzers.Tests`.

### Phase 2 — QRA502 / QRA503 full-pipeline integration tests

**Files:**
- `src/Quarry.Analyzers.Tests/DialectRuleTests.cs` — add a new region "QRA503 pipeline tests"
  using `AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(source)`.

**Tests added:**

Each compiles a real source string with `[QuarryContext(Dialect = …)]` + a chain using the
offending construct and asserts the analyzer reports the expected diagnostic with the expected
severity. Source skeleton mirrors existing `Generator_*` tests in `GeneratorTests.cs`.

| Test | Dialect | Construct | Expected |
|---|---|---|---|
| `QRA503_Pipeline_MysqlFullOuterJoin_EmitsError` | MySQL | `.FullOuterJoin<…>()` | QRA503 Error, message contains "MySQL" + "FULL OUTER JOIN" |
| `QRA503_Pipeline_PostgresFullOuterJoin_NoDiagnostic` | PostgreSQL | `.FullOuterJoin<…>()` | no QRA5xx |
| `QRA503_Pipeline_SqlServerFullOuterJoin_NoDiagnostic` | SqlServer | `.FullOuterJoin<…>()` | no QRA5xx |
| `QRA503_Pipeline_SqliteFullOuterJoin_NoDiagnostic` | SQLite | `.FullOuterJoin<…>()` | no QRA5xx |
| `QRA503_Pipeline_SqlServerOffsetWithoutOrderBy_EmitsError` | SqlServer | `.Offset(10).ExecuteFetchAllAsync()` | QRA503 Error, message contains "OFFSET/FETCH" |
| `QRA503_Pipeline_SqlServerOffsetWithOrderBy_NoDiagnostic` | SqlServer | `.OrderBy(...).Offset(10).Execute…()` | no QRA5xx |
| `QRA503_Pipeline_SqliteOffsetWithoutOrderBy_NoDiagnostic` | SQLite | `.Offset(10).Execute…()` | no QRA5xx |
| `QRA503_Pipeline_PostgresOffsetWithoutOrderBy_NoDiagnostic` | PostgreSQL | `.Offset(10).Execute…()` | no QRA5xx |
| `QRA503_Pipeline_MysqlOffsetWithoutOrderBy_NoDiagnostic` | MySQL | `.Offset(10).Execute…()` | no QRA5xx |

**Deps:** Phase 1. **Tests:** new tests must pass.

### Phase 3 — QRY070 / QRY071 full-pipeline generator integration tests

**Files:**
- `src/Quarry.Tests/GeneratorTests.cs` — add tests in the Set Operation Diagnostics region;
  remove the explanatory NOTE comment block at lines 1366–1370 because the gap is closed.

**Tests added:**

Each test compiles a real source string with a `[QuarryContext(Dialect = …)]` and a chain calling
`.IntersectAll` / `.ExceptAll`, runs through `RunGeneratorWithDiagnostics(compilation)`, and
asserts the QRY id appears with `Severity == Error`. Source skeleton matches
`Generator_WithUnresolvableNavigationAggregateInSelect_ReportsQRY074` (lines 1407–1465).

| Test | Dialect | Operation | Expected |
|---|---|---|---|
| `Generator_SqliteIntersectAll_ReportsQRY070_AsError` | SQLite | `.IntersectAll(…)` | QRY070 Error, message contains "SQLite" |
| `Generator_MysqlIntersectAll_ReportsQRY070_AsError` | MySQL | `.IntersectAll(…)` | QRY070 Error, message contains "MySQL" |
| `Generator_SqlServerIntersectAll_ReportsQRY070_AsError` | SqlServer | `.IntersectAll(…)` | QRY070 Error, message contains "SqlServer" |
| `Generator_PostgresIntersectAll_NoQRY070` | PostgreSQL | `.IntersectAll(…)` | no QRY070 |
| `Generator_SqliteExceptAll_ReportsQRY071_AsError` | SQLite | `.ExceptAll(…)` | QRY071 Error |
| `Generator_MysqlExceptAll_ReportsQRY071_AsError` | MySQL | `.ExceptAll(…)` | QRY071 Error |
| `Generator_SqlServerExceptAll_ReportsQRY071_AsError` | SqlServer | `.ExceptAll(…)` | QRY071 Error |
| `Generator_PostgresExceptAll_NoQRY071` | PostgreSQL | `.ExceptAll(…)` | no QRY071 |

**Deps:** Phase 1. **Tests:** new tests must pass.

## Track B — Cross-dialect test conversion

**Pattern (verbatim, applied uniformly).** Each existing SQLite-only test becomes:

```csharp
await using var t = await QueryTestHarness.CreateAsync();
var (Lite, Pg, My, Ss) = t;

var lt = Lite.{accessor}().{chain}.Prepare();
var pg = Pg.{accessor}().{chain}.Prepare();
var my = My.{accessor}().{chain}.Prepare();
var ss = Ss.{accessor}().{chain}.Prepare();

QueryTestHarness.AssertDialects(
    lt.ToDiagnostics(), pg.ToDiagnostics(),
    my.ToDiagnostics(), ss.ToDiagnostics(),
    sqlite: "...", pg: "...", mysql: "...", ss: "...");

var lite = await lt.ExecuteFetchAllAsync();
Assert.That(lite, …); // assertions

var pgResults = await pg.ExecuteFetchAllAsync();
Assert.That(pgResults, …); // identical assertions

var myResults = await my.ExecuteFetchAllAsync();
Assert.That(myResults, …);

var ssResults = await ss.ExecuteFetchAllAsync();
Assert.That(ssResults, …);
```

The four executions and four identical assertion blocks are NOT extracted into a helper — match
existing CrossDialect* tests verbatim.

### Phase 4 — Convert ContainsIntegrationTests → CrossDialectWhereTests

**Source:** `src/Quarry.Tests/Integration/ContainsIntegrationTests.cs` (155 lines, ~6 tests covering
DELETE with WHERE, IN clause variations).

**Destination:** Append to `src/Quarry.Tests/SqlOutput/CrossDialectWhereTests.cs` as a new
"Contains / DELETE" region.

**Conversion notes:** DELETE chain uses `.Delete().Where(...).ExecuteNonQueryAsync()`. SQL-string
assertions need per-dialect quoting + parameter shape (`@p0` for Lite/Ss, `$1` for Pg, `?` for My).

**Final step:** Delete `src/Quarry.Tests/Integration/ContainsIntegrationTests.cs`.

**Deps:** Phase 3. **Tests:** all 4 dialects pass.

### Phase 5 — Convert CollectionScalarIntegrationTests → CrossDialectWhereTests

**Source:** `src/Quarry.Tests/Integration/CollectionScalarIntegrationTests.cs` (137 lines, ~6 tests
covering empty collections, single-element collections, large collections, parameter slot mixing).

**Destination:** Append to `src/Quarry.Tests/SqlOutput/CrossDialectWhereTests.cs` as a new
"Collection + scalar parameter mixing" region.

**Conversion notes:** Each dialect's collection IN expansion produces a different SQL shape
(`@p0,@p1,…` for Lite/Ss vs `$1,$2,…` for Pg vs `?,?,…` for My). The verbatim pattern surfaces
this naturally via per-dialect `AssertDialects` arguments.

**Final step:** Delete `src/Quarry.Tests/Integration/CollectionScalarIntegrationTests.cs`.

**Deps:** Phase 4. **Tests:** all 4 dialects pass.

### Phase 6 — Convert JoinedCarrierIntegrationTests → CrossDialectJoinTests

**Source:** `src/Quarry.Tests/Integration/JoinedCarrierIntegrationTests.cs` (219 lines, ~8 tests
covering 2-table inner joins, tuple/entity projections, COUNT terminal).

**Destination:** Append to `src/Quarry.Tests/SqlOutput/CrossDialectJoinTests.cs`. Most
single-dialect tests already have a SQL-shape sibling there — the new tests bring the matching
4-dialect execution.

**Final step:** Delete `src/Quarry.Tests/Integration/JoinedCarrierIntegrationTests.cs`.

**Deps:** Phase 5. **Tests:** all 4 dialects pass.

### Phase 7 — Convert JoinNullableIntegrationTests → JoinNullableProjectionTests

**Source:** `src/Quarry.Tests/Integration/JoinNullableIntegrationTests.cs` (212 lines, ~6 tests
covering LEFT JOIN nullable propagation, IsDBNull-guarded reads).

**Destination:** Append to existing `src/Quarry.Tests/SqlOutput/JoinNullableProjectionTests.cs`
(which already covers SQL-shape across 4 dialects).

**Conversion notes:** SQLite's loose typing tolerates many cases that strict providers reject —
this phase is the highest-payoff for catching dialect-specific reader bugs.

**Final step:** Delete `src/Quarry.Tests/Integration/JoinNullableIntegrationTests.cs`.

**Deps:** Phase 6. **Tests:** all 4 dialects pass.

### Phase 8 — Convert DateTimeOffsetIntegrationTests → CrossDialectTypeMappingTests

**Source:** `src/Quarry.Tests/Integration/DateTimeOffsetIntegrationTests.cs` (78 lines, ~3 tests
covering DTO round-trip, timezone preservation).

**Destination:** Append to existing `src/Quarry.Tests/SqlOutput/CrossDialectTypeMappingTests.cs`.

**Conversion notes:** Each provider parameterizes timestamps differently. PostgreSQL coerces
incoming DateTimeOffset values to UTC; MySQL stores as DATETIME without offset; SQL Server uses
`datetimeoffset` natively. The data assertion may need to be a tolerance comparison rather than
strict equality, or the seed data may need to use UTC-only values to keep all four dialects in
agreement. Resolve by inspecting how the existing seed (`events` table at QueryTestHarness.cs:621)
is materialized on each dialect during a pilot test before locking the assertion shape.

**Final step:** Delete `src/Quarry.Tests/Integration/DateTimeOffsetIntegrationTests.cs`.

**Deps:** Phase 7. **Tests:** all 4 dialects pass.

### Phase 9 — Convert PrepareIntegrationTests → PrepareTests

**Source:** `src/Quarry.Tests/Integration/PrepareIntegrationTests.cs` (222 lines, ~8 tests
covering single-terminal Select/FetchFirst, multi-terminal Select+Update, Delete, batch insert).

**Destination:** Append to existing `src/Quarry.Tests/SqlOutput/PrepareTests.cs` (which already
verifies SQL-shape across dialects).

**Conversion notes:** Multi-terminal Prepare needs to execute each terminal kind on each dialect.
Order each terminal's row-count assertions so they're independent of execution order across
dialects.

**Final step:** Delete `src/Quarry.Tests/Integration/PrepareIntegrationTests.cs`.

**Deps:** Phase 8. **Tests:** all 4 dialects pass.

### Phase 10 — Convert EntityReaderIntegrationTests → CrossDialectEntityReaderTests

**Source:** `src/Quarry.Tests/Integration/EntityReaderIntegrationTests.cs` (236 lines, ~15 tests
covering custom EntityReader materialization, identity projection, tuple/single-column projection
fallback).

**Destination:** New file `src/Quarry.Tests/SqlOutput/CrossDialectEntityReaderTests.cs`. ProductSchema
already has `[EntityReader(typeof(ProductReader))]`, so the harness gives us free 4-dialect coverage —
the existing `_db = new TestDbContext(_connection)` setup is replaced with `QueryTestHarness.CreateAsync()`
and per-dialect Prepare + execute.

**Final step:** Delete `src/Quarry.Tests/Integration/EntityReaderIntegrationTests.cs`.

**Deps:** Phase 9. **Tests:** all 4 dialects pass.

### Phase 11 — Convert RawSqlIntegrationTests → CrossDialectRawSqlTests

**Source:** `src/Quarry.Tests/Integration/RawSqlIntegrationTests.cs` (328 lines, ~15 tests
covering RawSqlAsync as IAsyncEnumerable, RawSqlScalarAsync, RawSqlNonQueryAsync, partial column
projection, error propagation).

**Destination:** New file `src/Quarry.Tests/SqlOutput/CrossDialectRawSqlTests.cs`.

**Conversion notes:** RawSql tests pass literal SQL strings that include parameter placeholders
(`@p0`, `$1`, `?`). Each dialect needs its own SQL string passed to its own `RawSqlAsync<T>` call.
This means each test will have four different SQL strings rather than one shared string — but the
assertion blocks remain identical. The verbatim pattern adapts naturally:
```csharp
var liteRows = await Lite.RawSqlAsync<User>("SELECT * FROM \"users\" WHERE \"UserId\" = @p0", 1).ToListAsync();
var pgRows   = await Pg.RawSqlAsync<User>("SELECT * FROM \"users\" WHERE \"UserId\" = $1", 1).ToListAsync();
var myRows   = await My.RawSqlAsync<User>("SELECT * FROM `users` WHERE `UserId` = ?", 1).ToListAsync();
var ssRows   = await Ss.RawSqlAsync<User>("SELECT * FROM [users] WHERE [UserId] = @p0", 1).ToListAsync();
// identical assertions on all four
```

**Final step:** Delete `src/Quarry.Tests/Integration/RawSqlIntegrationTests.cs`.

**Deps:** Phase 10. **Tests:** all 4 dialects pass.

### Phase 12 — Convert LoggingIntegrationTests → CrossDialectLoggingTests

**Source:** `src/Quarry.Tests/Integration/LoggingIntegrationTests.cs` (760 lines, ~30 tests
covering Query/RawSql/Modify/Connection/Parameters/Execution log categories, Sensitive() redaction,
opId correlation, slow-query warning).

**Destination:** New file `src/Quarry.Tests/SqlOutput/CrossDialectLoggingTests.cs`. Marked
`[NonParallelizable]` because `LogsmithOutput.Logger` is process-wide.

**Conversion notes:** Within one test, executing on Lite → Pg → My → Ss sequentially against a
single `RecordingLogsmithLogger` produces interleaved log lines. Easiest pattern: clear the
logger after each dialect's execution and assert independently:
```csharp
_logger.Clear();
var liteRows = await lt.ExecuteFetchAllAsync();
AssertLogShape(_logger.Entries, dialect: "SQLite");

_logger.Clear();
var pgRows = await pg.ExecuteFetchAllAsync();
AssertLogShape(_logger.Entries, dialect: "PostgreSQL");
// …
```

The `AssertLogShape` helper is internal to the test class (not a global helper — keeps with the
"verbatim pattern" decision). `RecordingLogsmithLogger.Clear()` may need to be added if it doesn't
already exist; check `RecordingLogsmithLogger.cs`.

**Final step:** Delete `src/Quarry.Tests/Integration/LoggingIntegrationTests.cs`.

**Deps:** Phase 11. **Tests:** all 4 dialects pass.

## Dependencies

Phase 1 → Phase 2 → Phase 3 (Track A is sequential).
Phases 4–12 each depend on the previous (Track B, sequential to keep diffs small).
Track A must complete before Track B starts (the conversion relies on the
"compiles ⇒ executes" invariant).

## phases-total: 12
