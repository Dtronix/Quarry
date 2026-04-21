# Quarry v0.2.0

_Released 2026-03-29_

**A complete compile-time SQL builder for .NET** — This release represents a ground-up transformation of Quarry's internals, delivering a carrier-only architecture, a rewritten compiler pipeline, zero-dependency runtime, AOT compatibility, a full migration framework, and dramatically improved performance. 70+ PRs merged since v0.1.0.

---

## Highlights

- **Carrier-only architecture** — Runtime SQL builders removed entirely. Every query chain is now analyzed at compile time and executed through zero-allocation carrier interceptors. Non-analyzable chains are compile-time errors, not silent fallbacks.
- **Rewritten compiler pipeline** — Monolithic `UsageSiteInfo` replaced with a layered IR (`RawCallSite` → `BoundCallSite` → `TranslatedCallSite` → `QueryPlan` → `AssembledPlan` → `CarrierPlan`). ~15,000 lines deleted, ~12,000 lines of clean-room pipeline code added.
- **Zero-alloc captured variable extraction** — `Expression<Func<>>` parameters replaced with `Func<>` across all builder interfaces. `[UnsafeAccessor]` + `[UnsafeAccessorType]` replaces all runtime reflection for captured variable extraction.
- **Full migration framework** — Recovery, checksums, idempotent DDL, seed data, views, stored procedures, migration bundles, and CLI commands (`diff`, `script`, `status`, `squash`).
- **Zero runtime dependencies** — Logsmith switched to Abstraction mode; all logging types are source-generated into the `Quarry.Logging` namespace.
- **Native AOT support** — Verified with a dedicated AOT sample covering 15 scenarios. All reflection removed from hot paths.
- **Near-raw-ADO.NET performance** — Scalar aggregate overhead reduced from ~2x to ~1.1x vs raw ADO.NET. Interface dispatch eliminated from the execution hot path.
- **DocFX documentation site** — 13 comprehensive articles, API reference, and a benchmark suite comparing Raw ADO.NET, Dapper, EF Core, SqlKata, and Quarry.

---

## Breaking Changes

### API Changes

- **`Expression<Func<>>` → `Func<>`** — All 52 lambda parameters across 13 interfaces changed from `Expression<Func<...>>` to `Func<...>`. Inline lambdas work without changes. Pre-built `Expression<Func<>>` variables must be changed to `Func<>`. (#120)
- **`ToSql()` removed** — Removed from all 16 public builder interfaces and `PreparedQuery<TResult>`. Use `.ToDiagnostics().Sql` instead. (#67, #72)
- **`Set<TValue>(Expression, TValue)` overload removed** — Use `Set(Action<T>)` assignment syntax instead. (#58)
- **`InsertMany()` and `Values(T)` removed** — Replaced with `InsertBatch(u => (u.Col1, u.Col2)).Values(entities)` pattern that separates column declaration from data provision. (#62)
- **`Union/UnionAll/Except/Intersect` removed** — These methods always threw `NotImplementedException`. Use `RawSqlAsync` as a workaround until generator support is built. (#27)
- **`IQueryExecutionContext` interface removed** — Was in `Quarry.Internal` namespace. `QuarryContext.Connection`, `DefaultTimeout`, and `EnsureConnectionOpenAsync` changed from `protected` to `public`. (#101)
- **`ColumnDefBuilder.Default()` renamed to `DefaultValue()`** (#103)
- **Non-analyzable chains are now compile errors** — `QRY032` upgraded from Info to Error severity. Restructure queries to be statically analyzable. (#72)

### Diagnostic API Changes

- **`QueryPlanTier` enum removed** (#110)
- **`DiagnosticOptimizationTier` enum removed** (#110)
- **`QueryDiagnostics.Tier`, `.IsCarrierOptimized`, `.CarrierIneligibleReason` removed** (#110)
- **`QueryDiagnostics.ActiveMask` and `SqlVariants` key type changed from `ulong` to `int`** (#78)

### Logging Changes

- **Logsmith Shared mode → Abstraction mode** — `LogManager.Initialize()` no longer accessible. Implement `ILogsmithLogger` and assign to `LogsmithOutput.Logger`. All types now in `Quarry.Logging` namespace. (#76)

---

## New Features

### Query Engine

#### `.Prepare()` Multi-Terminal Support (#70)

Freeze a compiled query chain and execute multiple terminals without rebuilding:

```csharp
var prepared = db.Users()
    .Where(u => u.IsActive)
    .Select(u => (u.UserId, u.UserName))
    .Prepare();

var diagnostics = prepared.ToDiagnostics();    // inspect SQL
var users = await prepared.ExecuteFetchAllAsync(); // execute
```

Zero allocation for both single and multi-terminal cases via `Unsafe.As` reinterpret cast.

#### `Set(Action<T>)` Assignment Syntax (#33)

```csharp
db.Users().Update().Set(u => {
    u.IsActive = true;
    u.UserName = name;
}).Where(u => u.UserId == id).ExecuteNonQueryAsync();
```

Supports single assignment (expression lambda) and multi-assignment (statement lambda). The `Action<T>` delegate is never allocated or invoked on the carrier-optimized path.

#### Batch Insert API Redesign (#62)

```csharp
db.Users()
    .InsertBatch(u => (u.UserName, u.IsActive))
    .Values(users)
    .ExecuteNonQueryAsync();
```

Column selection is compile-time analyzable, enabling full carrier optimization. `MaxParameterCount` (2100) guard prevents oversized parameter lists.

#### Variable-Walking Chain Unification (#62)

Fluent chains split across local variables (up to 2 hops) are now unified into a single chain for analysis:

```csharp
var query = db.Users().Where(u => u.IsActive);
var results = await query.Select(u => u.UserName).ExecuteFetchAllAsync();
// Both sites receive the same ChainId — fully analyzed
```

#### Rich `QueryDiagnostics` (#18, #67)

`ToDiagnostics()` now returns compiler-known metadata: SQL variants, per-clause parameter ownership, clause source locations, `IsConditional`/`IsActive` flags, projection columns, join info, and parameter sensitivity metadata. 17 new properties on `QueryDiagnostics`, 7 new fields on `DiagnosticParameter`, 5 new diagnostic types.

#### Constant Inlining (#24, #114, #119)

- **Enum/const values** inlined as SQL literals — zero parameters.
- **Constant string LIKE patterns** inlined directly into SQL: `WHERE "Name" LIKE '%shipped%'` instead of `LIKE '%' || @p0 || '%'`.
- **Literal pagination** inlined: `LIMIT 10` instead of `LIMIT @p0`.
- **Constant collections** for IN clauses inlined: `IN ('pending', 'processing', 'shipped')` instead of `IN (@p0, @p1, @p2)`.
- Extends to subqueries and qualified member access (const fields, static readonly fields).

#### `Sql.Raw` Template Support (#58)

```csharp
db.Users().Where(u => Sql.Raw("{0} > {1}", u.Score, threshold))
```

With QRY029 diagnostic for placeholder mismatches.

#### Subquery Support (#58)

Navigation property collection methods (`.Any()`, `.Count()`, `.All()`) now generate correlated subqueries.

#### `.Trace()` Chain Debugging (#58)

Compile-time trace logging system gated behind `QUARRY_TRACE` symbol. Per-site and per-chain trace output with `// [Trace]` comments in generated code.

#### Joined Query Improvements (#8, #10, #114)

- `ExecuteScalarAsync<TScalar>()` added to all joined builder interfaces.
- Joined entity projection: `.Select((s, u) => u)` works in joined queries.
- Pre-join WHERE clause table aliasing fixed.
- Join interceptors are noops in PrebuiltDispatch chains (zero allocation).

#### `GroupBy` on `IEntityAccessor<T>` (#82)

`GroupBy<TKey>` added directly to `IEntityAccessor<T>` interface.

#### `SensitiveParameter` for RawSql (#103)

Prevents accidental logging of passwords/tokens in `RawSqlAsync` calls.

### Migration Framework

#### Runtime Hooks (#47)

```csharp
var options = new MigrationOptions {
    BeforeEach = async (version, name, conn) => { /* pre-migration logic */ },
    AfterEach = async (version, name, elapsed, conn) => { /* post-migration logic */ },
    OnError = async (version, name, ex, conn) => { /* error handling */ }
};
```

Hooks receive `DbConnection` but not `DbTransaction`, preventing interference with migration atomicity. Skipped during `DryRun` mode.

#### Safety & Recovery (#50)

- **Partial failure recovery** — `status` column tracks `running` → `applied` lifecycle; incomplete migrations detected on startup.
- **Checksum validation** — FNV-1a checksums detect modified migrations after application (`StrictChecksums` option).
- **Idempotent DDL** — `IF NOT EXISTS`/`IF EXISTS` guards across all 4 dialects.

#### Timeout Control (#49)

```csharp
var options = new MigrationOptions {
    CommandTimeout = TimeSpan.FromMinutes(10),
    LockTimeout = TimeSpan.FromSeconds(30) // dialect-specific SET command
};
```

#### `SuppressTransaction` for Concurrent Indexes (#48)

PostgreSQL `CREATE INDEX CONCURRENTLY` automatically suppressed from transactions. Operations partitioned into transactional (Phase 1) and non-transactional (Phase 2) phases.

#### Large Table Warnings (#51)

Opt-in `WarnOnLargeTable` queries database catalog statistics before DDL on existing tables.

#### Typed Seed Data (#52)

```csharp
builder.InsertData("Users", new { UserName = "admin", IsActive = true });
builder.UpdateData("Users", set: new { IsActive = false }, where: new { UserName = "admin" });
builder.DeleteData("Users", where: new { UserName = "admin" });
```

Dialect-aware SQL literal formatting across all 4 dialects.

#### Views & Stored Procedures (#54)

```csharp
builder.CreateView("ActiveUsers", "SELECT * FROM Users WHERE IsActive = 1");
builder.CreateProcedure("GetUser", "CREATE PROCEDURE ...", schema: "dbo");
```

Dialect-specific DDL rendering. Idempotent support included.

#### Snapshot & Differ Improvements (#55)

- **Hungarian algorithm** for globally optimal N:N rename detection (replaces greedy matching).
- **Schema transfer** detection with dialect-specific DDL (`ALTER SCHEMA ... TRANSFER`, `SET SCHEMA`, `RENAME TABLE`).
- **New column properties**: `ComputedExpression`, `Collation`, `DescendingColumns`, `CharacterSet`.

#### Migration Bundles (#56)

```bash
quarry migrate bundle -p src/MyApp -o bundle.exe --self-contained -r linux-x64
```

Self-contained deploy artifact. Connection string provided at runtime via CLI arg or `QUARRY_CONNECTION` environment variable.

#### CLI Commands (#53)

- **`migrate diff`** — Preview schema changes without generating files.
- **`migrate script`** — Generate offline SQL scripts with `--from`/`--to` range.
- **`migrate status`** — Cross-reference applied vs pending migrations against a database.
- **`migrate squash`** — Collapse all migrations into a single baseline.

### Validation & Security (#103)

- All 22 public `MigrationBuilder` methods reject null/empty/whitespace identifiers.
- SQL injection prevention in DDL idempotent guards for MySQL INFORMATION_SCHEMA and SQL Server sys catalog queries.
- `ColumnDefBuilder.Nullable()` now accepts `bool nullable = true`.
- `RelationshipBuilder<T>` stub methods (`OnDelete`, `OnUpdate`, `MapTo`).

---

## Performance

### Scalar Aggregate Overhead: ~2x → ~1.1x (#96, #97)

- `ScalarConverter` with JIT-eliminated `typeof` branches replaces `Convert.ChangeType` + reflection.
- Instrumentation gated behind single boolean check (zero overhead when no logger attached).
- `OpId.Next()` conditional on logging (skips `Interlocked.Increment`).

### Interface Dispatch Elimination (#101)

- `IQueryExecutionContext` interface deleted — `QuarryContext` properties are non-virtual, inlineable by JIT.
- Carrier `Ctx` field typed as user's concrete context class (e.g., `MyDb?` instead of `IQueryExecutionContext?`).
- `EnsureConnectionOpenAsync` sync fast-path: `Task.CompletedTask` when connection already open.

### Generated Code Optimizations (#108, #78)

- `__c.Ctx` and `LogsmithOutput.Logger` cached in locals — eliminates redundant field loads per terminal.
- SQL dispatch moved from terminal method bodies to static fields on carrier class — array index replaces switch expression for multi-variant chains.

### Source Generator Speed (#28, #122, #5)

- Per-file incremental generator output — single file change only regenerates relevant output files.
- Display class enrichment batched: `AnalyzeDataFlow` calls reduced from O(N·L) to O(L) per method.
- Single-pass collection partitioning, `BuilderKind` enum replacing `string.Contains()`, `ConditionalWeakTable`-backed display string caching.
- Interceptor and migration generation deferred to build-time via `RegisterImplementationSourceOutput` — invisible to IDE (#107).

### Zero-Alloc Variable Extraction (#120, #126)

- `Expression<Func<>>` → `Func<>`: eliminates 3–10+ expression tree heap objects per call site.
- `[UnsafeAccessor]` field access is JIT-intrinsic — equivalent to direct field access with no boxing.
- Non-capturing lambdas: zero allocation (compiler caches delegate in static field).

---

## Architecture

### Carrier-Only Architecture (#72)

~17,500 lines of runtime builder infrastructure deleted: `QueryBuilder<T>`, `JoinedQueryBuilder<T>`, `SqlBuilder`, `QueryState`, `ModificationExecutor`, `DiagnosticsHelper`, and all related types. `QueryExecutor` reduced from 923 to 310 lines.

### Layered IR Compiler Pipeline (#58)

6-stage pipeline replacing monolithic `UsageSiteInfo`:

1. **Discovery** — `RawCallSite` extraction from syntax.
2. **Binding** — `BoundCallSite` with semantic model resolution.
3. **Translation** — `TranslatedCallSite` with `SqlExpr` IR trees.
4. **Chain Analysis** — `QueryPlan` grouping and conditional dispatch.
5. **Assembly** — `AssembledPlan` with dialect-specific SQL.
6. **Carrier Optimization** — `CarrierPlan` with field layout and extraction plans.

Clean-room `SqlExprParser` → `SqlExprRenderer` → `SqlAssembler` chain replaces dual expression translators.

### Default Interface Methods (#89)

12 abstract `CarrierBase` classes (~1,500 lines) eliminated. Throwing stubs moved to default interface method implementations. Generated carrier classes implement interfaces directly.

### Design-Time / Build-Time Split (#107)

Interceptor pipeline deferred to `RegisterImplementationSourceOutput`. Design-time profile: one `CreateSyntaxProvider` + one `RegisterSourceOutput`, zero `Collect()` calls. Generator is invisible to IDE except when schemas change.

---

## Bug Fixes

### SQL Generation

- Redundant WHERE clause parentheses removed (#58)
- Boolean column comparisons made explicit with dialect-appropriate literals (#58)
- Navigation join ON clauses use table-qualified column names (#58)
- `SetAction` column names properly quoted (#58)
- Batch insert correctly excluded from carrier optimization (#58)
- Conditional chain grouping fixed for delete/update/insert builders (#58)
- MySQL `InsertExecuteScalar` uses combined `INSERT ... ; SELECT LAST_INSERT_ID()` (#58)
- Mixed literal/parameterized pagination correctly emits both clauses (#82, #83)

### Code Generation

- Cross-namespace entity type resolution in generated carrier interceptors (#15)
- Carrier insert bool/enum parameter binding for SQLite and MySQL (#71)
- CS8618 warnings on non-nullable reference-type carrier fields (#88)
- CS1522 empty switch block in generated RawSql interceptor (#93)
- CS8629/CS8604 nullable warnings in generated enum interceptors (#95)
- Named tuple element codegen in Select projections (#112)
- Joined query `ExecuteScalarAsync` codegen (#114)
- Pre-join WHERE alias (#114)
- `ComputeChainId` for top-level programs (#111)
- AOT-safe `UpdateSetAction` — reflection replaced with invoke-and-read (#111)
- Mutable static arrays no longer incorrectly inlined (#118)
- Instance field captures correctly emit `UnsafeAccessorKind.Field` vs `.StaticField` (#122)
- `SELECT *` replaced with explicit column lists for identity projections (#73)
- Mask-aware terminal parameter binding — inactive conditional parameters excluded (#82, #84)
- Collection parameter binding in carrier terminals (#82, #85)

### Runtime

- ScalarConverter null/DBNull guard (#103)
- MigrationRunner `IsDBNull` check for null checksums (#103)
- QuarryContext nullable type unwrap before `Convert.ChangeType` (#103)
- `DbCommand` disposal via `await using` in all QueryExecutor methods (#101)
- EntityReader delegation for identity projections (#75)

### Security

- SQL injection prevention in DDL INFORMATION_SCHEMA/sys queries (#103)

---

## Documentation & Tooling

- **DocFX documentation site** with API reference and 13 articles covering getting started, schema definition, context definition, switching dialects, querying, modifications, prepared queries, migrations, scaffolding, diagnostics, logging, analyzer rules, and benchmarks (#90)
- **Sample web application** — ASP.NET Core Razor Pages app with cookie auth, CRUD, joins, aggregates, and SQL diagnostics inspector (#82)
- **AOT verification sample** — `PublishAot` CLI app running 15 AOT scenarios against in-memory SQLite (#111)
- **Benchmark suite** — 5 libraries (Raw ADO.NET, Dapper, EF Core, SqlKata, Quarry) across 13 benchmark classes with publication-quality adaptive iterations (#90, #123)
- **NuGet package READMEs** for all packages (#1)
- **Zero-dependency logging** via Logsmith Abstraction mode (#76)
- **New analyzer: QRA305** — Detects `static readonly T[]` in `.Contains()` / IN clauses; suggests `ImmutableArray<T>` (#118)

---

## Migration Guide from v0.1.0

### Required Changes

1. **Lambda parameters** — Inline lambdas work without changes. Change any explicit `Expression<Func<...>>` variables to `Func<...>`.

2. **`ToSql()` → `ToDiagnostics().Sql`**:
   ```csharp
   // Before
   var sql = db.Users().Where(u => u.IsActive).ToSql();
   // After
   var sql = db.Users().Where(u => u.IsActive).ToDiagnostics().Sql;
   ```

3. **Batch insert API**:
   ```csharp
   // Before
   db.Users().InsertMany(users);
   // After
   db.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users).ExecuteNonQueryAsync();
   ```

4. **`Set<TValue>` overload** — Use `Set(Action<T>)` instead:
   ```csharp
   // Before
   db.Users().Update().Set(u => u.IsActive, true)...
   // After
   db.Users().Update().Set(u => u.IsActive = true)...
   ```

5. **Logging setup**:
   ```csharp
   // Before
   LogManager.Initialize(...);
   // After
   using Quarry.Logging;
   LogsmithOutput.Logger = new MyLogger(); // implements ILogsmithLogger
   ```

6. **`ColumnDefBuilder.Default()` → `DefaultValue()`** in migration code.

7. **Non-analyzable queries** — Any chain that previously fell back to runtime builders will now produce compile error `QRY032`. Restructure to be statically analyzable.

### Optional Improvements

- Use `.Prepare()` for multi-terminal chains (diagnostics + execution).
- Use `Set(Action<T>)` for multi-assignment updates.
- Use `InsertBatch(lambda).Values(collection)` for batch inserts.
- Use `Sql.Raw("{0} > {1}", ...)` for raw SQL fragments.
- Leverage `QueryDiagnostics` expanded metadata for debugging.

---

## Stats

- **70+ PRs merged** since v0.1.0
- **~17,500 lines deleted** (runtime builder infrastructure)
- **~15,000 lines deleted** (old compiler pipeline)
- **~12,000 lines added** (new compiler pipeline)
- **All 4 dialects supported**: SQLite, PostgreSQL, MySQL, SQL Server
- **2,178 tests passing**
- **Scalar overhead**: ~2x → ~1.1x vs raw ADO.NET

---

## Full Changelog

### Architecture & Compiler

- Rewrite generator compiler pipeline: layered IR replacing monolithic UsageSiteInfo (#58)
- Remove runtime builder infrastructure, establish carrier-only architecture (#72)
- Replace CarrierBase class hierarchy with default interface methods (#89)
- Defer interceptor & migration generation to build-time via RegisterImplementationSourceOutput (#107)
- Replace Expression<Func<>> with Func<> and UnsafeAccessorType for zero-alloc captured variable extraction (#120)
- Unified per-variable UnsafeAccessor extraction with computed expression support (#126)

### New Features

- Carrier class optimization for PrebuiltDispatch chains (#10)
- ToDiagnostics() as full chain terminal with parameters, clauses, and carrier optimization (#18)
- Inline constants and collection parameter carrier support (#24)
- Carrier chain support for Update Set() interceptors (#30)
- Add Action\<T> Set overload for assignment syntax (#33)
- Runtime pre/post hooks on MigrationRunner (#47)
- SuppressTransaction support for ConcurrentIndex correctness (#48)
- Add CommandTimeout and LockTimeout to MigrationOptions (#49)
- MigrationRunner safety — recovery, checksums, idempotent DDL (#50)
- Estimated row count warnings before DDL on large tables (#51)
- Typed seed data operations on MigrationBuilder (#52)
- New CLI commands — migrate diff, script, status, squash (#53)
- View and stored procedure migration support (#54)
- Snapshot & differ improvements — Hungarian rename, tracking gaps, schema move, collation (#55)
- Migration bundles — self-contained deploy artifact (#56)
- Batch Insert API Redesign + Variable-Walking Chain Unification (#62)
- Compiler-sourced QueryDiagnostics: unify emitters, remove ToSql, expand metadata (#67)
- .Prepare() multi-terminal support for all builder types (#70)
- Add sample webapp, fix generator bugs, fix terminal binding (#82)
- Add DocFX documentation site and enhance benchmark suite (#90)
- Add AOT verification sample, fix ComputeChainId for top-level programs (#111)
- Inline constant LIKE patterns (#114)
- Extend constant LIKE pattern inlining to subqueries and qualified member access (#119)

### Performance

- Make Join interceptors noops in PrebuiltDispatch chains (#8)
- Reduce allocations in source generator hot paths (#28)
- Move mask-based SQL dispatch from terminal emitters to carrier class (#78)
- Reduce ~2x scalar aggregate overhead (#96)
- Devirtualize execution hot path & EnsureConnectionOpenAsync fast-path (#101)
- Cache __c.Ctx and LogsmithOutput.Logger in generated interceptor bodies (#108)
- Batch display class enrichment to eliminate redundant AnalyzeDataFlow calls (#122)

### Bug Fixes

- Fix NU5128 pack warning for analyzer project (#7)
- Fix ToSql prebuilt chains emit literal Limit/Offset values (#14)
- Fix cross-namespace entity type resolution in generated carrier interceptors (#15)
- Fix VariableTracer type matching and propagate CancellationToken (#66)
- Fix carrier insert bool/enum parameter binding for SQLite and MySQL (#71)
- Fix EntityReader delegation test for identity projections (#75)
- Fix CS8618 warnings on non-nullable reference-type carrier fields (#88)
- Fix CS1522 empty switch block in generated RawSql interceptor (#93)
- Fix CS8629/CS8604 nullable warnings in generated enum interceptors (#95)
- Address code review findings across security, correctness, API, architecture, and tests (#103)
- Fix named tuple element codegen in Select projections (#112)
- Fix joined query ExecuteScalarAsync codegen, pre-join WHERE alias, and inline constant LIKE patterns (#114)
- Fix TryResolveConstantArray incorrectly inlining mutable static arrays (#118)
- Fix benchmark fairness: per-library POCOs, parameterized DML, modern EF Core APIs (#123)

### Refactoring & Cleanup

- Add NuGet package READMEs and CodeFixes listing (#1)
- Split InterceptorCodeGenerator into partial class files (#4)
- Per-file incremental generator output (#5)
- Enable ToSql-terminated chains for PrebuiltDispatch grouping (#16)
- Remove ToSql interceptor terminal, migrate test suite to QueryDiagnostics (#20)
- Remove unused internal Metadata.g.cs generation (#23)
- Remove unimplemented SetOperationBuilder and set operation API surface (#27)
- Eliminate redundant variable re-tracing in ExtractBatchInsertColumnNamesFromChain (#65)
- Unified test infrastructure: QueryTestHarness (#73)
- Switch to Logsmith Abstraction mode for zero-dependency logging (#76)
- Remove unused GetColumnTypeName() and dialect partials (#100)
- Remove vestigial multi-tier and carrier-qualifier terminology (#110)
- Standardize cross-dialect test structure for integration-ready pattern (#127)
