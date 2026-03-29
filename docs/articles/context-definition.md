# Context Definition

A `QuarryContext` subclass is the entry point for all queries and modifications.

## Basic Context

```csharp
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class AppDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}
```

The class must be `partial`. The source generator emits a companion partial class containing:

- **Constructors** -- one accepting `IDbConnection`, one accepting `IDbConnection` and `bool ownsConnection`, and a full constructor accepting `IDbConnection`, `bool ownsConnection`, `TimeSpan? defaultTimeout`, and `IsolationLevel? defaultIsolation`.
- **Entity accessor methods** -- each `partial IEntityAccessor<T>` declaration gets a generated body that returns a carrier instance wired to the context's connection and dialect.
- **Insert / Update / Delete** -- accessed through the entity accessor (`db.Users().Insert(entity)`, `db.Users().Update()`, `db.Users().Delete()`). The generator intercepts each call site and emits pre-built SQL.
- **MigrateAsync** -- generated when migration classes are present in the project. Accepts a `DbConnection` and optional `MigrationOptions`, then delegates to the migration runner with the correct dialect and an ordered list of discovered migrations.

All of these generated method bodies throw `NotSupportedException` at the carrier base class level. The interceptor emitter replaces every call site with a concrete implementation at compile time, so the throw path is never reached at runtime.

## Dialect Selection

Available dialects: `SQLite`, `PostgreSQL`, `MySQL`, `SqlServer`.

```csharp
[QuarryContext(Dialect = SqlDialect.PostgreSQL)]
public partial class PgDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
```

The `Dialect` property is required. It determines quoting style, parameter formatting, boolean literals, pagination syntax, and RETURNING/OUTPUT clauses for the entire context.

## Schema Property

The optional `Schema` property on `QuarryContextAttribute` qualifies all table references with a database schema name:

```csharp
[QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = "public")]
public partial class PgDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}
```

When set, generated SQL uses qualified table names:

| Dialect | Schema value | Generated SQL |
|---|---|---|
| PostgreSQL | `"public"` | `"public"."users"` |
| SqlServer | `"dbo"` | `[dbo].[users]` |
| MySQL | `"mydb"` | `` `mydb`.`users` `` |
| SQLite | (ignored) | `"users"` |

If `Schema` is omitted or null, tables are referenced without schema qualification. This property allows the same schema classes to be reused across multiple contexts targeting different database schemas -- for example, a multi-tenant application where each tenant maps to a separate PostgreSQL schema.

## Multiple Contexts

Multiple contexts with different dialects can coexist in the same project. Each generates its own interceptor file with dialect-correct SQL. The generator resolves the correct context by walking the receiver chain at each call site.

```csharp
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class CacheDb : QuarryContext
{
    public partial IEntityAccessor<CachedItem> Items();
}

[QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = "public")]
public partial class MainDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}
```

A practical example: an application that uses SQLite for a local offline cache and PostgreSQL for the primary database. Each context operates independently with its own connection:

```csharp
// Local cache -- SQLite file on disk
await using var cache = new CacheDb(new SqliteConnection("Data Source=cache.db"));
await cache.MigrateAsync(cacheConnection);

var cached = await cache.Items()
    .Where(i => i.ExpiresAt > DateTime.UtcNow)
    .Select(i => i)
    .ExecuteFetchAllAsync();

// Main database -- PostgreSQL
await using var main = new MainDb(new NpgsqlConnection(connectionString));

var users = await main.Users()
    .Where(u => u.IsActive)
    .Select(u => (u.UserId, u.UserName))
    .ExecuteFetchAllAsync();
```

Both contexts can be registered in a DI container and injected where needed. The generated interceptors are fully independent -- each file contains only the SQL variants for its own dialect.

## Connection Management and the Disposable Pattern

`QuarryContext` implements both `IAsyncDisposable` and `IDisposable`. Use `await using` to ensure the connection is cleaned up:

```csharp
await using var db = new AppDb(connection);

var users = await db.Users()
    .Select(u => u)
    .ExecuteFetchAllAsync();
// connection is restored to its original state when db goes out of scope
```

Key behaviors:

- The context accepts any `IDbConnection`, but it must be a `DbConnection` underneath (required for async support). Passing a non-`DbConnection` throws `ArgumentException`.
- If the connection was **already open** when the context was created, the context leaves it open on dispose.
- If the connection was **closed**, the context opens it on first query and closes it on dispose.
- The default query timeout is 30 seconds. Override it via the constructor:

```csharp
await using var db = new AppDb(
    connection,
    ownsConnection: false,
    defaultTimeout: TimeSpan.FromSeconds(10),
    defaultIsolation: IsolationLevel.ReadCommitted);
```

Avoid sharing a single context across concurrent operations. Create a new context per unit of work (per request, per background job, etc.).

### Connection Ownership

By default, the context **borrows** the connection -- it may close it on dispose but never disposes it. When `ownsConnection` is `true`, the context takes full ownership and disposes the connection when the context is disposed:

```csharp
// Context owns the connection -- disposes it when done
await using var db = new AppDb(
    new SqliteConnection("Data Source=app.db"),
    ownsConnection: true);
```

| `ownsConnection` | Connection was closed | Connection was open |
|---|---|---|
| `false` (default) | Closes on dispose | Left open |
| `true` | Disposes on dispose | Disposes on dispose |

This is primarily useful for dependency injection scenarios where the context should manage the entire connection lifecycle.

## Dependency Injection

Register the context as a scoped service so each request gets its own context and connection. The DI container handles disposal at the end of the scope, which disposes the owned connection and returns it to the pool:

```csharp
// Program.cs
services.AddScoped<AppDb>(_ =>
    new AppDb(new SqliteConnection(connectionString), ownsConnection: true));
```

Consumers inject the context directly -- no connection knowledge required:

```csharp
public class UserService(AppDb db)
{
    public async Task<List<User>> GetActiveUsers()
    {
        return await db.Users()
            .Where(u => u.IsActive)
            .Select(u => u)
            .ExecuteFetchAllAsync();
    }
}
```

## Usage

```csharp
await using var db = new AppDb(connection);

// Query
var users = await db.Users()
    .Select(u => u)
    .ExecuteFetchAllAsync();

// Insert
await db.Users()
    .Insert(new User { UserName = "Alice" })
    .ExecuteNonQueryAsync();

// Update
await db.Users()
    .Update()
    .Set(u => u.UserName = "Bob")
    .Where(u => u.UserId == 1)
    .ExecuteNonQueryAsync();

// Delete
await db.Users()
    .Delete()
    .Where(u => u.UserId == 1)
    .ExecuteNonQueryAsync();

// Migrations
await db.MigrateAsync(connection);
```

## InterceptorsNamespaces

Your consumer `.csproj` must register the context's namespace for interceptors:

```xml
<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);MyApp.Data</InterceptorsNamespaces>
</PropertyGroup>
```

If your context has no namespace, use `Quarry.Generated`.

For multiple contexts in different namespaces, add each one:

```xml
<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);MyApp.Data;MyApp.Cache</InterceptorsNamespaces>
</PropertyGroup>
```

**If you forget this property or use the wrong namespace**, the interceptors will not activate. The code will still compile, but every query and modification call will hit the carrier base class methods, which throw `NotSupportedException` at runtime with a message like "Carrier method ... is not intercepted in this optimized chain." If you see this exception, check that `InterceptorsNamespaces` includes the namespace of your `QuarryContext` subclass.
