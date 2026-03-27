# Switching Database Targets

One of Quarry's key advantages is that switching database targets requires no changes to your query code, schema definitions, or business logic. The SQL dialect is configured in a single place -- the `[QuarryContext]` attribute -- and the source generator handles all dialect-specific differences at compile time.

## The One-Line Switch

Change the `Dialect` property on your context and rebuild:

```csharp
// Before: targeting SQLite for local development
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class AppDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}

// After: targeting PostgreSQL for production -- change one enum value
[QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = "public")]
public partial class AppDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}
```

On the next build, the generator re-emits every interceptor with PostgreSQL-correct SQL. Your queries, inserts, updates, and deletes all produce dialect-appropriate output without any code changes.

## What Changes Automatically

The generator handles these dialect differences for you:

| Concern | SQLite | PostgreSQL | MySQL | SQL Server |
|---|---|---|---|---|
| Identifier quoting | `"col"` | `"col"` | `` `col` `` | `[col]` |
| Parameter placeholders | `@p0, @p1` | `$1, $2` | `?, ?` | `@p0, @p1` |
| Boolean literals | `1` / `0` | `TRUE` / `FALSE` | `1` / `0` | `1` / `0` |
| Pagination | `LIMIT n OFFSET m` | `LIMIT n OFFSET m` | `LIMIT n OFFSET m` | `OFFSET m ROWS FETCH NEXT n ROWS ONLY` |
| Identity return | `RETURNING "Id"` | `RETURNING "Id"` | `SELECT LAST_INSERT_ID()` | `OUTPUT INSERTED.[Id]` |
| Schema qualification | (ignored) | `"public"."users"` | `` `mydb`.`users` `` | `[dbo].[users]` |
| String concatenation | `\|\|` | `\|\|` | `CONCAT()` | `+` |
| LIKE escape | `ESCAPE '\'` | `ESCAPE '\'` | `ESCAPE '\'` | `ESCAPE '\'` |

Every query in your project gets all of these adjustments automatically. There is no runtime dialect negotiation -- the SQL is baked into string literals at compile time.

## What Stays the Same

Everything else is dialect-independent:

- **Schema classes** -- `Key<T>`, `Col<T>`, `Ref<T, TKey>`, `Many<T>`, modifiers, indexes, naming styles
- **Query expressions** -- `.Where()`, `.Select()`, `.OrderBy()`, `.Join()`, `.GroupBy()`, aggregates
- **Modifications** -- `.Insert()`, `.Update()`, `.Delete()`, `.InsertBatch()`
- **Diagnostics** -- `ToDiagnostics()` returns the dialect-specific SQL
- **Migrations** -- `MigrationBuilder` operations are rendered with dialect-correct DDL at runtime

## Running Multiple Dialects Side by Side

You can define multiple context classes that share the same schema definitions but target different dialects. Each context generates its own interceptor file with fully independent SQL.

```csharp
// Local development / testing
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class LocalDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}

// Production
[QuarryContext(Dialect = SqlDialect.PostgreSQL, Schema = "public")]
public partial class ProdDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}
```

This is useful for:

- **Local development** -- Use SQLite with an in-memory or file database for fast iteration, then deploy against PostgreSQL or SQL Server in production.
- **Testing** -- Run integration tests against SQLite for speed, with a separate test suite against the production dialect for verification.
- **Migration** -- Gradually switch from one database to another by running both contexts in parallel, comparing results.

Both contexts can be used in the same application simultaneously:

```csharp
// Fast local queries against SQLite cache
await using var local = new LocalDb(sqliteConnection);
var cached = await local.Users().Where(u => u.IsActive).Select(u => u).ExecuteFetchAllAsync();

// Production queries against PostgreSQL
await using var prod = new ProdDb(npgsqlConnection);
var users = await prod.Users().Where(u => u.IsActive).Select(u => u).ExecuteFetchAllAsync();
```

The generated SQL is completely independent -- `local` emits SQLite SQL, `prod` emits PostgreSQL SQL, and neither affects the other.

## Verifying Dialect Output

Use `ToDiagnostics()` to inspect the generated SQL for any query:

```csharp
var diag = db.Users()
    .Where(u => u.IsActive && u.Email != null)
    .OrderBy(u => u.UserName)
    .Select(u => u)
    .Limit(10)
    .ToDiagnostics();

Console.WriteLine(diag.Dialect);  // PostgreSQL
Console.WriteLine(diag.Sql);
// SELECT "UserId", "UserName", "Email", "IsActive", "CreatedAt"
// FROM "public"."users"
// WHERE "IsActive" = TRUE AND "Email" IS NOT NULL
// ORDER BY "UserName"
// LIMIT 10
```

This is particularly useful when switching dialects to confirm the generated SQL matches your expectations before deploying.

## Dialect-Aware Type Mappings

If you have custom type mappings that need dialect-specific behavior (e.g., `jsonb` on PostgreSQL vs `TEXT` on SQLite), implement `IDialectAwareTypeMapping`:

```csharp
public class JsonDocMapping : TypeMapping<JsonDoc, string>, IDialectAwareTypeMapping
{
    public override string ToDb(JsonDoc value) => JsonSerializer.Serialize(value);
    public override JsonDoc FromDb(string value) => JsonSerializer.Deserialize<JsonDoc>(value)!;

    public string GetSqlTypeName(SqlDialect dialect) => dialect switch
    {
        SqlDialect.PostgreSQL => "jsonb",
        _ => "TEXT"
    };

    public void ConfigureParameter(SqlDialect dialect, DbParameter parameter)
    {
        if (dialect == SqlDialect.PostgreSQL && parameter is NpgsqlParameter npgsql)
            npgsql.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;
    }
}
```

The generator calls `GetSqlTypeName()` when emitting DDL and migration code, and the runtime calls `ConfigureParameter()` when binding query parameters. Both adapt automatically to whichever dialect the context is configured for.
