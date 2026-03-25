# Implementation Plan: Unified Test Infrastructure (#68)

## 1. Overview

Replace the dual test infrastructure — `CrossDialectTestBase` (inheritance-based, mock-only SQL verification) and `SqliteIntegrationTestBase` (separate real-database execution tests) — with a single `QueryTestHarness` composition pattern. Every SQL output test gains execution verification via `.Prepare()` multi-terminal support. Integration tests are absorbed into existing CrossDialect test classes and deleted.

During implementation, three pre-existing generator bugs were discovered and fixed as prerequisites. These are documented in Section 3.

### Depends On
- `.Prepare()` multi-terminal support (completed in PR #70)

### Branch
`feature/unified-test-harness`

---

## 2. Core Concepts

### 2.1 QueryTestHarness

A self-contained, disposable object that provides all dialect contexts and database connections. Tests create a harness as a local variable — no shared mutable state, fully parallelizable.

**Location**: `src/Quarry.Tests/QueryTestHarness.cs`

**Key properties**:
- `Lite` — `TestDbContext` on a real SQLite in-memory connection. Supports both `.ToDiagnostics()` (SQL verification) and `.ExecuteFetchAllAsync()` (execution verification).
- `Pg` — `PgDb` on `MockDbConnection`. SQL verification only.
- `My` — `MyDb` on `MockDbConnection`. SQL verification only.
- `Ss` — `SsDb` on `MockDbConnection`. SQL verification only.
- `MockConnection` — shared `MockDbConnection` backing Pg/My/Ss. Exposed for tests that inspect executed SQL via `LastCommand.CommandText`.

**Signatures**:
```csharp
internal sealed class QueryTestHarness : IAsyncDisposable
{
    public TestDbContext Lite { get; }
    public Pg.PgDb Pg { get; }
    public My.MyDb My { get; }
    public Ss.SsDb Ss { get; }
    public MockDbConnection MockConnection { get; }

    public static Task<QueryTestHarness> CreateAsync();
    public void Deconstruct(out TestDbContext lite, out Pg.PgDb pg, out My.MyDb my, out Ss.SsDb ss);
    public static void AssertDialects(QueryDiagnostics sqliteDiag, QueryDiagnostics pgDiag, QueryDiagnostics mysqlDiag, QueryDiagnostics ssDiag, string sqlite, string pg, string mysql, string ss);
    public static void AssertDialects(string sqliteActual, string pgActual, string mysqlActual, string ssActual, string sqlite, string pg, string mysql, string ss);
    public Task SqlAsync(string sql);
    public ValueTask DisposeAsync();
}
```

### 2.2 CreateAsync Algorithm

1. Create `SqliteConnection("Data Source=:memory:")` and open it
2. Set `PRAGMA foreign_keys = OFF` — FK enforcement disabled by default so DELETE tests don't need dependent-row ordering. Tests that need FK behavior opt in via `SqlAsync("PRAGMA foreign_keys = ON")`.
3. Create schema: `users`, `orders` (with `Priority` column), `order_items` tables + `Order` view
4. Seed data: 3 users (Alice/active, Bob/active, Charlie/inactive), 3 orders, 3 order items
5. Create `MockDbConnection` and construct `Pg`, `My`, `Ss` on it
6. Return assembled harness

### 2.3 Deconstruct Pattern

C# duck-typed deconstruction — the compiler matches `Deconstruct(out T1, out T2, out T3, out T4)` by arity without requiring an interface.

```csharp
await using var t = await QueryTestHarness.CreateAsync();
var (Lite, Pg, My, Ss) = t;
```

This is **required** (not just convenient) because of a generator bug where property-chained context access (`t.Pg.Users()`) causes cross-context type references in generated interceptor files. Assigning to local variables first (`var Pg = t.Pg; Pg.Users()`) works correctly. The Deconstruct pattern makes this ergonomic.

### 2.4 Test Method Pattern

Every test follows a three-phase pattern:

**Phase 1 — Build and prepare all dialect chains**:
All 4 dialects use `.Prepare()` for visual consistency and identical query shapes.

```csharp
var lite = Lite.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
var pg   = Pg.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
var my   = My.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
var ss   = Ss.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
```

**Phase 2 — Assert SQL output from all 4 dialects**:
```csharp
QueryTestHarness.AssertDialects(
    lite.ToDiagnostics(), pg.ToDiagnostics(),
    my.ToDiagnostics(), ss.ToDiagnostics(),
    sqlite: "...", pg: "...", mysql: "...", ss: "...");
```

**Phase 3 — Execute against real SQLite**:
```csharp
var results = await lite.ExecuteFetchAllAsync();
Assert.That(results, Has.Count.EqualTo(2));
Assert.That(results[0], Is.EqualTo((1, "Alice")));
```

### 2.5 Exceptions to the Standard Pattern

**Insert execution tests**: `.Prepare()` + `.ExecuteNonQueryAsync()` on insert chains has a parameter binding bug (parameters not populated). Insert execution tests use direct execution on Lite instead of Prepare. Mock-based Pg/My/Ss capture SQL via `t.MockConnection.LastCommand.CommandText`.

**Batch insert execution tests**: Lite execution must include all NOT NULL columns. The mock-based Pg/My/Ss tests can use partial column sets since `MockDbConnection` doesn't enforce schema constraints.

### 2.6 Seed Data Reference

| Table | ID | Key Fields |
|---|---|---|
| users | 1 | Alice, active, email=alice@test.com, CreatedAt=2024-01-15 |
| users | 2 | Bob, active, email=NULL, CreatedAt=2024-02-20 |
| users | 3 | Charlie, inactive, email=charlie@test.com, CreatedAt=2024-03-10 |
| orders | 1 | UserId=1(Alice), Total=250, Status=Shipped, Priority=2(High) |
| orders | 2 | UserId=1(Alice), Total=75.50, Status=Pending, Priority=1(Normal) |
| orders | 3 | UserId=2(Bob), Total=150, Status=Shipped, Priority=3(Urgent) |
| order_items | 1 | OrderId=1, Widget, Qty=2, UnitPrice=125, LineTotal=250 |
| order_items | 2 | OrderId=2, Gadget, Qty=1, UnitPrice=75.50, LineTotal=75.50 |
| order_items | 3 | OrderId=3, Widget, Qty=3, UnitPrice=50, LineTotal=150 |

### 2.7 Entity Column Lists

After the SELECT * elimination (Section 3.3), all queries emit explicit columns. These are the canonical column lists per entity:

- **Users**: `"UserId", "UserName", "Email", "IsActive", "CreatedAt", "LastLogin"`
- **Orders**: `"OrderId", "UserId", "Total", "Status", "Priority", "OrderDate", "Notes"`
- **OrderItems**: `"OrderItemId", "OrderId", "ProductName", "Quantity", "UnitPrice", "LineTotal"`

Each dialect quotes differently:
- SQLite / PostgreSQL: `"col"`
- MySQL: `` `col` ``
- SQL Server: `[col]`

---

## 3. Generator Bugs Discovered and Fixed

### 3.1 Join + Prepare + ExecuteFetchAllAsync Receiver Type

**File**: `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs`

**Bug**: `EmitJoinReaderTerminal` did not check `site.IsPreparedTerminal`. For prepared joined queries, it emitted `IJoinedQueryBuilder<User, Order, TResult>` as the receiver type instead of `PreparedQuery<TResult>`, causing CS9144 signature mismatch.

**Fix**: Added `IsPreparedTerminal` check at the top of the receiver type logic in `EmitJoinReaderTerminal`, matching the existing pattern in `EmitReaderTerminal`. When `IsPreparedTerminal` is true, emits `PreparedQuery<TResult>` as the `this` parameter.

**How it was found**: The `OrderBy_Joined_RightTableColumn` test used `.Prepare()` on a join chain and called `.ExecuteFetchAllAsync()`. The build failed with CS9144.

**Status**: Fixed and verified.

### 3.2 Enum Captured Variable Parameter Logging

**File**: `src/Quarry.Generator/CodeGen/CarrierEmitter.cs`

**Bug**: `IsNonNullableValueType(string typeName)` could not distinguish enum type names (e.g., `OrderPriority`) from class names by string inspection alone — it conservatively returned `false`. This caused the logging code to emit `__c.P0?.ToString()` which is invalid on a non-nullable value type (CS0023).

**Fix**: Added `|| param.IsEnum` to the condition at the call site (line 747). The `QueryParameter` already carries `IsEnum` metadata, so no new plumbing was needed.

**How it was found**: `CrossDialectEnumTests.Where_EnumCapturedVariable` was converted to use `.Select(o => (o.OrderId, o.Total)).Prepare()`, which routed through the carrier-optimized execution path for the first time, triggering the logging codegen.

**Status**: Fixed and verified.

### 3.3 SELECT * Elimination

**File**: `src/Quarry.Generator/Parsing/ChainAnalyzer.cs`

**Bug**: When no `.Select()` clause was present, the `SqlAssembler` emitted `SELECT *` because the identity projection enrichment (which expands `*` to explicit column names) was gated by `hasSelectClause`. This meant `Db.Users().Where(u => u.IsActive).ExecuteFetchAllAsync()` produced `SELECT * FROM "users" ...` instead of `SELECT "UserId", "UserName", ... FROM "users" ...`.

**Fix**: Removed the `hasSelectClause &&` guard from the identity projection enrichment condition (line 577). The enrichment now always runs for identity projections, using authoritative entity column metadata from `EntityRef`. This is the implementation of "Option C" from the design analysis — zero API breaking change, fully predictable SQL.

**How it was found**: During test migration, Lite chains used `.Select()` for executability but Pg/My/Ss chains didn't, producing different SQL shapes across dialects. The root cause was that `SELECT *` should never have been emitted.

**Status**: Fixed. 60 test assertions need mechanical updates to replace `SELECT *` with explicit column lists.

---

## 4. Implementation Phases

### Phase 0: Proof-of-Concept [DONE]

Created `QueryTestHarness` skeleton and a single POC test (`HarnessProofOfConceptTests.Select_Tuple_TwoColumns_SqlAndExecution`) to validate:
- Real SQLite Lite + mock Pg/My/Ss in one harness
- `.Prepare()` enabling both `.ToDiagnostics()` and `.ExecuteFetchAllAsync()` on the same chain
- The Deconstruct/local-variable pattern for avoiding the generator cross-context bug

**Commit**: `b286345`

### Phase 1: Finalize QueryTestHarness [DONE]

Added `Deconstruct()`, `MockConnection` property, string overload of `AssertDialects()`, `PRAGMA foreign_keys = OFF`, `Priority` column in orders schema.

### Phase 2: Migrate CrossDialect Classes Without Integration Counterparts [DONE]

Migrated all 10 classes. Each test: removed `: CrossDialectTestBase` inheritance, made async, added harness creation with Deconstruct, converted to `.Prepare()` pattern, added execution assertions.

| Class | Tests | Execution Coverage |
|---|---|---|
| CrossDialectOrderByTests | 4 | Full — verifies sort order including joined OrderBy |
| CrossDialectDeleteTests | 6 | Full — verifies affected row counts |
| CrossDialectInsertTests | 7 | Partial — ToDiagnostics tests are SQL-only; ExecuteNonQuery/ExecuteScalar tests verify real inserts |
| CrossDialectBatchInsertTests | 8 | Partial — Lite execution uses full-column inserts for NOT NULL compliance |
| CrossDialectSubqueryTests | 21 | Full — every subquery verifies result count and content |
| CrossDialectStringOpTests | 9 | Partial — Contains/StartsWith with Select verify results |
| CrossDialectMiscTests | 7 | SQL-only — Sql.Raw, ToLower, etc. can't execute custom functions on SQLite |
| CrossDialectEnumTests | 4 | Partial — WHERE enum + UPDATE enum verify execution |
| CrossDialectComplexTests | 9 | Full — Where+Select, Join+Where+Select, pagination verify results |
| CrossDialectSchemaTests | 9 | Partial — SingleColumn select, Delete All verify execution; schema-qualified tests are mock-only |

**Commits**: `24adf84`, `39cabdb`

### Phase 2.5: Fix SELECT * Assertions [IN PROGRESS]

60 test assertions contain `SELECT *` in expected SQL strings. The generator now emits explicit column lists. Each assertion needs mechanical replacement:

**Pattern**: Replace `SELECT * FROM "users"` with `SELECT "UserId", "UserName", "Email", "IsActive", "CreatedAt", "LastLogin" FROM "users"` (and equivalent for other entities/dialects).

**Affected files** (mix of already-migrated and not-yet-migrated):
- `CrossDialectSubqueryTests.cs` — Pg/My/Ss chains that don't have `.Select()` (Lite already has it)
- `CrossDialectMiscTests.cs` — all 4 chains use WHERE without Select
- `CrossDialectStringOpTests.cs` — some tests without Select
- `CrossDialectSchemaTests.cs` — schema-qualified tests, Ref FK test
- `CrossDialectSelectTests.cs` — Distinct, Pagination tests (not yet migrated)
- `CrossDialectWhereTests.cs` — all WHERE-only tests (not yet migrated)
- `PrepareTests.cs` — conditional chain tests (not yet migrated)
- `CrossDialectDiagnosticsTests.cs` — diagnostics metadata tests (not yet migrated)
- `CrossDialectCompositionTests.cs` — (not yet migrated)
- `VariableStoredChainTests.cs` — (not yet migrated)
- `TracedFailureDiagnosticTests.cs` — (not yet migrated)
- `EndToEndSqlTests.cs` — standalone

**Approach**: For already-migrated files, add `.Select()` to all 4 dialect chains so the query shape is identical. For not-yet-migrated files, the SELECT * fix will be applied during their Phase 3/4 migration.

### Phase 3: Merge Overlapping CrossDialect + Integration Pairs [NOT STARTED]

For each pair: migrate CrossDialect class to harness, fold Integration execution assertions into corresponding test methods, delete Integration test class.

| CrossDialect Class | Integration Class | Merge Strategy |
|---|---|---|
| CrossDialectSelectTests | SelectIntegrationTests | Fold Select_Tuple, Select_Dto, Pagination execution into CrossDialect tests |
| CrossDialectJoinTests | JoinIntegrationTests + LeftJoinIntegrationTests | Fold inner join + left join execution. Note: RIGHT JOIN not supported by SQLite. |
| CrossDialectWhereTests | WhereIntegrationTests | Fold boolean, comparison, null check execution |
| CrossDialectAggregateTests | AggregateIntegrationTests | Fold COUNT, SUM, AVG, MIN, MAX execution |
| CrossDialectCompositionTests | ComplexIntegrationTests (partial) | Fold composition-pattern execution |
| CrossDialectUpdateTests | UpdateIntegrationTests | Fold UPDATE execution with affected-row verification |
| CrossDialectTypeMappingTests | TypeMappingIntegrationTests | Fold custom type mapping execution. May need `accounts` table in harness schema. |

**Migration algorithm per pair**:
1. Read both files to identify corresponding test methods
2. Remove `: CrossDialectTestBase` from CrossDialect class
3. Add harness creation with Deconstruct to each test
4. Convert to `.Prepare()` pattern on all 4 dialects
5. Add execution assertions from the Integration test into the CrossDialect test
6. Run tests to verify
7. Delete the Integration test file

**Schema additions needed**: The `TypeMappingIntegrationTests` may require an `accounts` table with `Balance` (Money type mapping) and `credit_limit` (MapTo column). The `products` table may be needed for `EntityReaderIntegrationTests` (kept standalone per issue plan).

### Phase 4: Migrate Remaining Classes [NOT STARTED]

| Class | Notes |
|---|---|
| CrossDialectDiagnosticsTests | Migrate to harness. No execution phase needed — tests verify diagnostic metadata (tier, clauses, parameters). Harness still used for consistency. |
| TracedFailureDiagnosticTests | Migrate to harness. Tests intentionally invalid chains — execution phase may assert expected failures/diagnostics. |
| VariableStoredChainTests | Migrate to harness. Tests `IQueryBuilder<T>` variable storage pattern with conditional branches. |

### Phase 5: Cleanup [NOT STARTED]

1. **Delete `CrossDialectTestBase`** (`src/Quarry.Tests/SqlOutput/CrossDialectSqlTests.cs`) — no longer inherited by any class
2. **Delete `SqliteIntegrationTestBase`** (`src/Quarry.Tests/Integration/SqliteIntegrationTestBase.cs`) — no longer inherited
3. **Consolidate Prepare tests**: Decide whether `PrepareTests.cs` and `PrepareIntegrationTests.cs` should be merged into the harness pattern or kept standalone (they test `.Prepare()` itself, not general query behavior)
4. **Handle standalone integration tests** that are NOT absorbed:
   - `EntityReaderIntegrationTests` — custom schema, tests `[EntityReader]` materialization
   - `LoggingIntegrationTests` — `[NonParallelizable]`, tests Logsmith singleton sink
   - `RawSqlIntegrationTests` — raw SQL bypasses builder chain
5. **Delete `HarnessProofOfConceptTests.cs`** and **`JoinPrepareExecutionTest.cs`** — POC/validation tests that are superseded by the full migration
6. **Run full test suite** and verify no coverage regression
7. **Final commit** with cleanup

---

## 5. Known Limitations and Open Issues

### 5.1 Generator Cross-Context Bug
**Symptom**: `t.Pg.Users()` (property-chained access) causes the generator to emit PgDb/SsDb interceptors inside MyDb's generated file, producing CS0246 or CS9144 errors.
**Workaround**: Always assign to local variables first via `var (Lite, Pg, My, Ss) = t;`.
**Root cause**: Not investigated. Likely in the generator's file-routing logic that determines which context's interceptor file receives a chain.
**Impact**: Low — the Deconstruct pattern is clean and the workaround is documented.

### 5.2 Prepare + Insert + ExecuteNonQuery Parameter Binding
**Symptom**: `.Prepare()` on an insert chain followed by `.ExecuteNonQueryAsync()` fails with "Must add values for the following parameters".
**Workaround**: Use direct execution (no Prepare) for insert ExecuteNonQuery/ExecuteScalar tests.
**Root cause**: Not investigated. The Prepare codepath for inserts may not propagate parameter values into the command.
**Impact**: Medium — insert execution tests can't use the standard Prepare pattern.

### 5.3 Sql.Raw Functions Not Executable on SQLite
Tests using `Sql.Raw<bool>("custom_func({0})", ...)` verify SQL generation but can't execute on SQLite because the custom functions don't exist. These tests remain SQL-verification-only.

---

## 6. File Inventory

### New Files
| File | Purpose |
|---|---|
| `src/Quarry.Tests/QueryTestHarness.cs` | Composition-based harness |
| `src/Quarry.Tests/SqlOutput/HarnessProofOfConceptTests.cs` | POC (delete in Phase 5) |
| `src/Quarry.Tests/SqlOutput/JoinPrepareExecutionTest.cs` | Join+Prepare validation (delete in Phase 5) |
| `handoff.md` | Session handoff document |
| `impl-plan.md` | This document |

### Modified Generator Files
| File | Change |
|---|---|
| `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs` | Join+Prepare receiver fix |
| `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` | Enum parameter logging fix |
| `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` | SELECT * elimination |

### Migrated Test Files (Phase 2 — complete)
All in `src/Quarry.Tests/SqlOutput/`:
`CrossDialectOrderByTests.cs`, `CrossDialectDeleteTests.cs`, `CrossDialectInsertTests.cs`, `CrossDialectBatchInsertTests.cs`, `CrossDialectSubqueryTests.cs`, `CrossDialectStringOpTests.cs`, `CrossDialectMiscTests.cs`, `CrossDialectEnumTests.cs`, `CrossDialectComplexTests.cs`, `CrossDialectSchemaTests.cs`

### To Be Migrated (Phases 3–4)
`CrossDialectSelectTests.cs`, `CrossDialectJoinTests.cs`, `CrossDialectWhereTests.cs`, `CrossDialectAggregateTests.cs`, `CrossDialectCompositionTests.cs`, `CrossDialectUpdateTests.cs`, `CrossDialectTypeMappingTests.cs`, `CrossDialectDiagnosticsTests.cs`, `TracedFailureDiagnosticTests.cs`, `VariableStoredChainTests.cs`

### To Be Deleted (Phases 3 + 5)
`CrossDialectSqlTests.cs` (base class), `SqliteIntegrationTestBase.cs` (base class), `SelectIntegrationTests.cs`, `JoinIntegrationTests.cs`, `LeftJoinIntegrationTests.cs`, `WhereIntegrationTests.cs`, `AggregateIntegrationTests.cs`, `ComplexIntegrationTests.cs`, `UpdateIntegrationTests.cs`, `TypeMappingIntegrationTests.cs`

### Kept As-Is (not absorbed)
`EntityReaderIntegrationTests.cs`, `LoggingIntegrationTests.cs`, `RawSqlIntegrationTests.cs`, `PrepareIntegrationTests.cs` (decision pending)
