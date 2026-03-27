# Modifications

Quarry supports insert, update, and delete operations. Like queries, all SQL is generated at compile time -- the Roslyn source generator analyzes each modification call site and emits the SQL as a string literal in an interceptor method. No SQL is built or translated at runtime. What the generator produces is exactly what executes against the database.

## Insert

The generator inspects the object initializer syntax of the `new T { ... }` expression passed to `Insert()`. Only properties that are explicitly set in the initializer generate INSERT columns. Properties you leave out are omitted from the SQL entirely -- there is no runtime reflection over property values to decide what to include. Identity columns (marked with `Identity()` in the schema) and computed columns (marked with `Computed()`) are automatically excluded.

This means two calls with different initializer shapes produce different SQL:

```csharp
// Generates: INSERT INTO "users" ("UserName", "IsActive") VALUES (@p0, @p1)
await db.Users()
    .Insert(new User { UserName = "Alice", IsActive = true })
    .ExecuteNonQueryAsync();

// Generates: INSERT INTO "users" ("UserName") VALUES (@p0)
await db.Users()
    .Insert(new User { UserName = "Alice" })
    .ExecuteNonQueryAsync();
```

Use `ExecuteScalarAsync<T>()` to retrieve a generated identity value after insertion:

```csharp
var id = await db.Users()
    .Insert(new User { UserName = "Alice", IsActive = true })
    .ExecuteScalarAsync<int>();
```

The generator emits a dialect-correct `RETURNING`, `OUTPUT INSERTED`, or `LAST_INSERT_ID()` clause depending on the configured SQL dialect.

## Batch Insert

Batch inserts use a column-selector + data-provider pattern that separates the compile-time and runtime concerns:

1. **Column selector (compile time):** `InsertBatch(u => (u.UserName, u.IsActive))` declares which columns to include. The generator analyzes this lambda at build time and emits the `INSERT INTO ... (columns) VALUES` prefix as a string literal.
2. **Data provider (runtime):** `.Values(users)` supplies the actual collection of entities. At runtime, the generated interceptor reads only the selected properties from each entity and expands the parameter placeholders.

```csharp
await db.Users()
    .InsertBatch(u => (u.UserName, u.IsActive))
    .Values(users)
    .ExecuteNonQueryAsync();
```

### Parameter count guard

Databases impose limits on the number of parameters in a single statement (SQL Server: 2100, SQLite: 999 by default, PostgreSQL: 65535). Quarry enforces a conservative ceiling of **2100 parameters** across all dialects. If `entityCount * columnsPerRow` exceeds this limit, `Values()` throws an `ArgumentException` at runtime with a message indicating the overflow and advising you to split the batch into smaller chunks.

For large data sets, partition the collection before calling `Values()`:

```csharp
const int chunkSize = 500;
foreach (var chunk in users.Chunk(chunkSize))
{
    await db.Users()
        .InsertBatch(u => (u.UserName, u.IsActive))
        .Values(chunk)
        .ExecuteNonQueryAsync();
}
```

## Update

Update chains start with `.Update()` and require at least one `.Set()` call to declare which columns to modify.

### Set overloads

There are two `Set` styles:

**Assignment syntax** -- pass a lambda (or block lambda) that assigns values to entity properties. The generator reads the assignment targets at compile time to determine which columns appear in the `SET` clause. Captured variables are extracted from the delegate closure at runtime.

```csharp
// Single column
await db.Users().Update()
    .Set(u => u.UserName = "New")
    .Where(u => u.UserId == 1)
    .ExecuteNonQueryAsync();

// Multiple columns in one lambda body
await db.Users().Update()
    .Set(u => { u.UserName = "New"; u.IsActive = true; })
    .Where(u => u.UserId == 1)
    .ExecuteNonQueryAsync();

// Captured variables -- values resolved at runtime from the closure
var newName = GetNameFromInput();
await db.Users().Update()
    .Set(u => { u.UserName = newName; u.IsActive = true; })
    .Where(u => u.UserId == 1)
    .ExecuteNonQueryAsync();
```

**Entity form** -- pass an entity instance. Like single inserts, the generator inspects the object initializer to determine which properties are set and includes only those in the `SET` clause.

```csharp
// Only UserName appears in the SET clause
await db.Users().Update()
    .Set(new User { UserName = "New" })
    .Where(u => u.UserId == 1)
    .ExecuteNonQueryAsync();
```

### Where / All safety requirement

`ExecuteNonQueryAsync()` is not available on `IUpdateBuilder<T>`. You must call `Where()` or `All()` first, which transitions the chain to `IExecutableUpdateBuilder<T>` where the execution terminal is defined. This is enforced at compile time through the type system -- there is no way to accidentally execute an unfiltered update.

If you intentionally want to update every row, call `All()` explicitly:

```csharp
await db.Users().Update()
    .Set(u => u.IsActive = false)
    .All()
    .ExecuteNonQueryAsync();
```

## Delete

Delete chains follow the same safety pattern as updates. `ExecuteNonQueryAsync()` is only available after `Where()` or `All()`, enforced by the interface transition from `IDeleteBuilder<T>` to `IExecutableDeleteBuilder<T>`.

```csharp
await db.Users()
    .Delete()
    .Where(u => u.UserId == 1)
    .ExecuteNonQueryAsync();

// Delete all rows -- requires explicit All() call
await db.Users()
    .Delete()
    .All()
    .ExecuteNonQueryAsync();
```

Omitting both `Where()` and `All()` is a compile-time error -- the builder type does not expose `ExecuteNonQueryAsync()`, so the code will not compile.

## Raw SQL

When the built-in query builder does not cover your use case, `RawSqlAsync`, `RawSqlScalarAsync`, and `RawSqlNonQueryAsync` execute hand-written SQL directly.

```csharp
var users = await db.RawSqlAsync<User>(
    "SELECT * FROM users WHERE id = @p0", userId);

var count = await db.RawSqlScalarAsync<int>(
    "SELECT COUNT(*) FROM users");

await db.RawSqlNonQueryAsync(
    "DELETE FROM logs WHERE date < @p0", cutoff);
```

### Source-generated typed readers

Even for raw SQL, Quarry avoids runtime reflection. The generator inspects the type parameter (`<User>`, `<int>`, etc.) at each call site and emits a typed reader delegate in the interceptor. For entity types, the reader maps columns by name to properties using a `switch` over `DbDataReader.GetName(i)`, calling the appropriate typed getter (`GetInt32`, `GetString`, etc.) for each property. For scalar types, it emits a direct conversion. Custom type mappings, `EntityRef<T, TKey>` foreign keys, and enum casts are all handled in the generated reader -- no dictionary lookups, no `Activator.CreateInstance`, no reflection.
