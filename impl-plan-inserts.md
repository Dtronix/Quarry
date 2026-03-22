# Quarry: Batch Insert API Redesign

## 1. Overview

Replaces the current `Values(T entity)` chaining and `InsertMany(IEnumerable<T>)` batch insert API with a column-selector + data-provider pattern. The new design separates column declaration (compile-time analyzable) from data provision (runtime), making all insert chains carrier-eligible.

**Current API (being replaced):**
```
db.Users().Insert(new User { Name = "a" }).Values(new User { Name = "b" }).ExecuteNonQueryAsync()
db.Users().InsertMany(new[] { user1, user2 }).ExecuteNonQueryAsync()
```

**New API:**
```
// Single insert (unchanged):
db.Users().Insert(new User { Name = "a" }).ExecuteNonQueryAsync()
db.Users().Insert(new User { Name = "a" }).ExecuteScalarAsync<int>()

// Batch insert (new):
db.Users().Insert(u => (u.Username, u.Password)).Values(users).ExecuteNonQueryAsync()
db.Users().Insert(u => (u.Username, u.Password)).Values(users).ExecuteScalarAsync<int>()

// Single-column batch:
db.Users().Insert(u => u.Username).Values(users).ExecuteNonQueryAsync()
```

**Key design principle:** The `Insert(lambda)` column selector is a pure expression fully analyzable at compile time. `Values(IEnumerable<T>)` provides runtime data. The terminal builds SQL from compile-time column info and runtime entity count.

---

## 2. Constraints

- netstandard2.0 target: No `record` types, no `init` properties.
- Roslyn 5.0 API surface for source generator.
- All pipeline types must implement `IEquatable<T>`.
- Breaking change: `Values(T entity)` removed from `IInsertBuilder<T>`. `InsertMany(IEnumerable<T>)` removed from `IEntityAccessor<T>`.

---

## 3. New Interface Hierarchy

### IEntityAccessor<T> Changes

Remove `InsertMany`. Add `Insert` overload with column selector expression.

```csharp
public interface IEntityAccessor<T> where T : class
{
    // Existing — single insert (unchanged):
    IInsertBuilder<T> Insert(T entity);

    // New — batch insert with column selector:
    IBatchInsertBuilder<T> Insert<TColumns>(Expression<Func<T, TColumns>> columnSelector);

    // Removed:
    // IInsertBuilder<T> InsertMany(IEnumerable<T> entities);
}
```

Both single-column (`u => u.Username`) and tuple (`u => (u.Username, u.Password)`) selectors are supported. The generator handles both `Expression<Func<T, TCol>>` and `Expression<Func<T, TupleN>>` forms.

### IInsertBuilder<T> Changes

Remove `Values`. Single insert only.

```csharp
public interface IInsertBuilder<T> where T : class
{
    // Removed:
    // IInsertBuilder<T> Values(T entity);

    IInsertBuilder<T> WithTimeout(TimeSpan timeout);
    Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default);
    Task<TKey> ExecuteScalarAsync<TKey>(CancellationToken cancellationToken = default);
    string ToSql();
    QueryDiagnostics ToDiagnostics();
}
```

### New: IBatchInsertBuilder<T>

Returned by the column-selector `Insert(lambda)`. Only allows `Values()` to provide data and `WithTimeout()`.

```csharp
public interface IBatchInsertBuilder<T> where T : class
{
    IExecutableBatchInsert<T> Values(IEnumerable<T> entities);
    IBatchInsertBuilder<T> WithTimeout(TimeSpan timeout);
}
```

### New: IExecutableBatchInsert<T>

Terminal interface after `Values()`. Supports execution and diagnostics.

```csharp
public interface IExecutableBatchInsert<T> where T : class
{
    Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default);
    Task<TKey> ExecuteScalarAsync<TKey>(CancellationToken cancellationToken = default);
    QueryDiagnostics ToDiagnostics();
}
```

`ExecuteScalarAsync<TKey>` returns the identity value. The user specifies `TKey` explicitly (matches current single-insert pattern).

**Identity return per dialect:**
- PostgreSQL, SQLite, SQL Server: `RETURNING`/`OUTPUT` returns one identity per inserted row. Result is the first identity value (consistent with single-insert behavior).
- MySQL: `SELECT LAST_INSERT_ID()` returns the first generated identity from the batch.

---

## 4. Carrier Base Classes

### BatchInsertCarrierBase<T>

New carrier base for batch insert chains. Implements both `IBatchInsertBuilder<T>` and `IExecutableBatchInsert<T>` with throw stubs (same pattern as other carriers).

```csharp
public abstract class BatchInsertCarrierBase<T> : IEntityAccessor<T>, IBatchInsertBuilder<T>, IExecutableBatchInsert<T>
    where T : class
{
    public IQueryExecutionContext? Ctx;

    // All IEntityAccessor<T> methods → throw
    // All IBatchInsertBuilder<T> methods → throw
    // All IExecutableBatchInsert<T> methods → throw
}
```

Generated carrier inherits from this and adds a field for the entity collection:

```csharp
// Generated:
file sealed class Chain_5 : BatchInsertCarrierBase<User>
{
    internal IEnumerable<User>? BatchEntities;
}
```

### InsertCarrierBase<T> Changes

Remove `Values` stub from `IInsertBuilder<T>` implementation since `Values` no longer exists on the interface.

---

## 5. Generator Changes

### 5.1 New InterceptorKinds

Add to `InterceptorKind` enum:

```csharp
BatchInsertColumnSelector,    // Insert(u => (u.Col1, u.Col2)) — column declaration
BatchInsertValues,            // .Values(entities) — data provision
BatchInsertExecuteNonQuery,   // .ExecuteNonQueryAsync() on IExecutableBatchInsert
BatchInsertExecuteScalar,     // .ExecuteScalarAsync<TKey>() on IExecutableBatchInsert
BatchInsertToDiagnostics,     // .ToDiagnostics() on IExecutableBatchInsert
```

### 5.2 Discovery Changes (UsageSiteDiscovery)

**Detect batch insert column selector:**
When `Insert` is called on `IEntityAccessor<T>` with a lambda expression argument (not a concrete entity), classify as `BatchInsertColumnSelector`.

Detection logic: Check if the first argument to `Insert()` is a `LambdaExpressionSyntax`. If yes → `BatchInsertColumnSelector`. If it's an object creation or identifier → existing `InsertTransition`.

**Parse column selector:** Reuse `ProjectionAnalyzer` to extract column names from the lambda. For `u => (u.Username, u.Password)`, extract `["Username", "Password"]`. For `u => u.Username`, extract `["Username"]`. Store as `ImmutableArray<string>` on `RawCallSite.BatchInsertColumnNames`.

**Detect Values on IBatchInsertBuilder:** Add `"Values"` to `InterceptableMethods` mapping to `BatchInsertValues` when the receiver type is `IBatchInsertBuilder<T>`.

**Detect terminals on IExecutableBatchInsert:** Map `ExecuteNonQueryAsync` → `BatchInsertExecuteNonQuery`, `ExecuteScalarAsync` → `BatchInsertExecuteScalar`, `ToDiagnostics` → `BatchInsertToDiagnostics` when receiver is `IExecutableBatchInsert<T>`.

### 5.3 Chain Analysis (ChainAnalyzer)

Batch insert chains are identified by having a `BatchInsertColumnSelector` site. The chain structure is:

```
ChainRoot → BatchInsertColumnSelector → BatchInsertValues → BatchInsertExecuteNonQuery
```

ChainAnalyzer produces a `QueryPlan` with:
- `Kind = QueryKind.BatchInsert` (new enum value)
- `InsertColumns` populated from the column selector's parsed column names (resolved against entity metadata via `InsertInfo.FromEntityInfo`)
- No `UnmatchedMethodNames` (all sites are recognized)
- Always `PrebuiltDispatch` tier

### 5.4 SQL Assembly (SqlAssembler)

Batch insert SQL cannot be fully pre-built because entity count is unknown at compile time. SqlAssembler produces a **template** instead of final SQL:

```csharp
// AssembledSqlVariant for batch insert:
public string SqlPrefix { get; }      // "INSERT INTO \"users\" (\"Username\", \"Password\") VALUES "
public string RowTemplate { get; }    // "(@p{0}, @p{1})"  — {0},{1} are column-relative offsets
public int ColumnsPerRow { get; }     // 2
public string? ReturningSuffix { get; } // " RETURNING \"UserId\"" or null
```

Add these properties to `AssembledSqlVariant` (or a new `BatchInsertTemplate` type on `AssembledPlan`).

The `SqlPrefix` includes the table name, schema, and column list — all known at compile time. The `RowTemplate` uses dialect-appropriate parameter format with relative indices. The `ReturningSuffix` is dialect-specific identity return clause.

### 5.5 Carrier Eligibility (CarrierAnalyzer)

Batch insert chains are always carrier-eligible. The `AnalyzeNew` method recognizes `QueryKind.BatchInsert` and produces a `CarrierPlan` with:
- `BaseClassName = "BatchInsertCarrierBase<EntityType>"`
- No parameter fields (parameters are extracted from entities at runtime)
- A single `BatchEntities` field of type `IEnumerable<EntityType>`

### 5.6 Code Emission

**BatchInsertColumnSelector interceptor:** No-op. Column info is captured at compile time. Returns carrier cast to `IBatchInsertBuilder<T>`.

```csharp
// Generated:
public static IBatchInsertBuilder<User> Insert_abc(
    this IEntityAccessor<User> builder,
    Expression<Func<User, (string, string)>> _)
{
    return new Chain_5 { Ctx = (IQueryExecutionContext)@this };
}
```

**Values interceptor:** Stores the entity collection on the carrier.

```csharp
public static IExecutableBatchInsert<User> Values_def(
    this IBatchInsertBuilder<User> builder,
    IEnumerable<User> entities)
{
    var __c = Unsafe.As<Chain_5>(builder);
    __c.BatchEntities = entities;
    return Unsafe.As<IExecutableBatchInsert<User>>(builder);
}
```

**ExecuteNonQueryAsync terminal:** Expands the SQL template at runtime, binds parameters from entities, executes.

Algorithm:
1. Materialize `BatchEntities` to `IReadOnlyList<T>` if not already.
2. Build SQL string: `SqlPrefix` + join N copies of `RowTemplate` with `, ` separator, substituting parameter indices (`rowIndex * columnsPerRow + columnOffset`).
3. Create `DbCommand`, set `CommandText`.
4. For each entity, for each column, add parameter with value extracted via generated property accessor.
5. Call `QueryExecutor.ExecuteCarrierNonQueryWithCommandAsync()`.

**ExecuteScalarAsync terminal:** Same SQL expansion but appends `ReturningSuffix`. Uses `ExecuteScalarAsync` for MySQL (`LAST_INSERT_ID()`), `ExecuteReaderAsync` for PG/SQLite/SS to read the first RETURNING row.

**ToDiagnostics terminal:** Builds a preview SQL with placeholder entity count (1 row) and returns `QueryDiagnostics`.

### 5.7 BatchInsertSqlBuilder Runtime Helper

New internal static class in `src/Quarry/Internal/BatchInsertSqlBuilder.cs`. Called by generated terminal interceptors.

```csharp
internal static class BatchInsertSqlBuilder
{
    public static string Build(
        string sqlPrefix,
        string rowTemplate,
        int entityCount,
        int columnsPerRow,
        SqlDialect dialect,
        string? returningSuffix)
}
```

Expands the row template N times with correctly offset parameter indices. Returns the complete SQL string. This is the only runtime SQL generation for batch inserts — everything else is compile-time.

---

## 6. Remove Old Batch Insert Support

### Remove from IInsertBuilder<T>:
- `Values(T entity)` method

### Remove from IEntityAccessor<T>:
- `InsertMany(IEnumerable<T> entities)` method

### Remove from InsertCarrierBase<T>:
- `IInsertBuilder<T>.Values(T entity)` stub

### Remove from InsertBuilder<T> (runtime class):
- `Values(T entity)` method
- Multi-entity support in `_entities` list (only single entity needed)

### Remove from UsageSiteDiscovery:
- `DetectBatchInsertInChain()` — no longer needed since batch inserts have their own chain type
- `IsBatchInsert` flag on `RawCallSite`
- `TryExtractPropertyNamesFromInsertManyArgument()`

### Remove from ChainAnalyzer:
- `unmatchedMethodNames = new[] { "Values" }` batch insert handling

---

## 7. Test Changes

### Remove:
- Tests using `Values(T entity)` chaining pattern
- Tests using `InsertMany(IEnumerable<T>)` on entity accessor

### Add:
- Cross-dialect batch insert tests using new `Insert(lambda).Values(collection)` pattern
- Single-column batch: `Insert(u => u.Username).Values(users)`
- Multi-column batch: `Insert(u => (u.Username, u.Password)).Values(users)`
- Batch ExecuteNonQueryAsync: verify row count and SQL shape per dialect
- Batch ExecuteScalarAsync: verify identity return (PG/SQLite/SS return first ID, MySQL returns LAST_INSERT_ID)
- Batch ToDiagnostics: verify preview SQL
- Batch with WithTimeout

### Update:
- `CrossDialectInsertTests` — add batch insert SQL verification for all 4 dialects
- Integration tests — update any tests that used old `Values`/`InsertMany` patterns

---

## 8. Migration Path

This is a breaking API change. The migration for consumers:

**Before:**
```
db.Users().Insert(user1).Values(user2).Values(user3).ExecuteNonQueryAsync()
db.Users().InsertMany(users).ExecuteNonQueryAsync()
```

**After:**
```
db.Users().Insert(u => (u.Username, u.Password)).Values(new[] { user1, user2, user3 }).ExecuteNonQueryAsync()
```

The column selector makes explicit which properties are inserted. The `Values()` call takes any `IEnumerable<T>`. Single-entity insert (`Insert(T entity)`) is unchanged.
