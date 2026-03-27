# Schema Definition

Define tables as C# classes inheriting `Schema`. The source generator reads the syntax tree directly — no attributes, no conventions, no runtime model building.

The Roslyn incremental generator (`Quarry.Generator`) analyzes your schema classes at compile time by walking the C# syntax tree. It extracts column types, modifiers, foreign key relationships, indexes, and naming conventions from the structure of your code. Nothing is evaluated at runtime: `Schema` subclasses have no instance state, and the modifier methods (like `Identity()` and `Length(100)`) return dummy values. The generator only needs to see the method calls in the syntax tree to understand what you declared. This means no reflection, no `[Column]` attributes, and no fluent model-builder callbacks at startup.

## Basic Schema

```csharp
public class UserSchema : Schema
{
    public static string Table => "users";

    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive => Default(true);
    public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);
    public Col<decimal> Total => Precision(18, 2);
}
```

Columns without a modifier use a plain `{ get; }` auto-property. The generator infers the column type from the CLR type (`string` maps to `TEXT`, `int` to `INTEGER`, etc.), and nullability from the `?` annotation.

## Column Types

| Type | Purpose |
|---|---|
| `Key<T>` | Primary key column |
| `Col<T>` | Standard column |
| `Ref<TSchema, TKey>` | Foreign key reference |
| `Many<T>` | One-to-many navigation (not a column) |
| `CompositeKey` | Multi-column primary key |

Generated entities use `EntityRef<TEntity, TKey>` for FK properties, providing `.Id` and `.Value` navigation access.

## Column Modifiers

| Modifier | Description |
|---|---|
| `Identity()` | Auto-increment identity column |
| `ClientGenerated()` | Client-side generated value (e.g., GUIDs) |
| `Computed()` | Database-computed column (excluded from inserts) |
| `Length(n)` | String length constraint |
| `Precision(p, s)` | Decimal precision and scale |
| `Default(v)` | Constant default value |
| `Default(() => v)` | Expression default value |
| `MapTo("name")` | Explicit column name mapping |
| `Mapped<TMapping>()` | Custom type mapping |
| `Unique()` | Single-column unique constraint (shorthand for a unique index) |
| `Sensitive()` | Redacts parameter values in log output |

Modifiers can be chained:

```csharp
public Col<string> PasswordHash => Length(256).Sensitive();
public Col<string> Sku => Length(50).Unique();
```

## Foreign Keys

```csharp
public class OrderSchema : Schema
{
    public static string Table => "orders";

    public Key<int> OrderId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Col<decimal> Total { get; }
}
```

In the schema, `Ref<TSchema, TKey>` declares the relationship. The first type parameter is the referenced schema class, and the second is the key type. The generator uses this to produce the correct `REFERENCES` constraint in DDL and to resolve join correlations.

### EntityRef in Generated Entities

When the generator emits the entity class for `OrderSchema`, the `Ref<UserSchema, int>` property becomes an `EntityRef<User, int>`:

```csharp
// Generated entity (simplified)
public class Order
{
    public int OrderId { get; set; }
    public EntityRef<User, int> UserId { get; set; }
    public decimal Total { get; set; }
}
```

`EntityRef<TEntity, TKey>` is a readonly struct with two members:

- `.Id` -- the raw foreign key value (the `int` stored in the column).
- `.Value` -- a navigation property to the referenced entity. This is `null` unless the related entity was fetched via a join.

Use `.Id` when you need the key value directly:

```csharp
// Reading the FK value
int userId = order.UserId.Id;

// Setting the FK value (implicit conversion from TKey)
var newOrder = new Order { UserId = 42, Total = 99.95m };
```

Use `.Value` when you fetched the related entity through a join:

```csharp
var results = await db.Orders()
    .Join<User>((o, u) => o.UserId.Id == u.UserId)
    .Select((o, u) => o)
    .ExecuteFetchAllAsync();

// After a join, Value is populated
string name = results[0].UserId.Value!.UserName;
```

In join conditions, always compare `.Id` against the referenced table's primary key column (`o.UserId.Id == u.UserId`), not the `EntityRef` directly.

## Navigation Properties

```csharp
public class UserSchema : Schema
{
    // ... columns ...

    public Many<OrderSchema> Orders => HasMany<OrderSchema>(o => o.UserId);
}
```

`Many<T>` properties enable [navigation subqueries](querying.md#navigation-subqueries) in `Where` clauses — `Any()`, `All()`, and `Count()`.

They also enable navigation-based joins, which infer the join condition from the FK relationship:

```csharp
// These two are equivalent:
db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
db.Users().Join(u => u.Orders)
```

## Indexes

```csharp
public class UserSchema : Schema
{
    // ... columns ...

    public Index IX_UserName => Index(UserName).Unique();
    public Index IX_CreatedAt => Index(CreatedAt.Desc());
    public Index IX_Active => Index(IsActive).Where(IsActive);  // filtered index
}
```

**Index modifiers:** `Unique()`, `Where(col)`, `Where("raw SQL")`, `Include(columns...)`, `Using(IndexType)`.

**Sort direction:** `.Asc()` / `.Desc()` on columns.

**Index types:** `BTree`, `Hash`, `Gin`, `Gist`, `SpGist`, `Brin` (PostgreSQL), `Clustered`, `Nonclustered` (SQL Server).

**Multi-column indexes:**

```csharp
public Index IX_Name_Email => Index(UserName, Email);
public Index IX_Covering => Index(UserName).Include(Email, CreatedAt);
```

## Composite Keys

```csharp
public class EnrollmentSchema : Schema
{
    public static string Table => "enrollments";

    public Col<int> StudentId { get; }
    public Col<int> CourseId { get; }
    public CompositeKey PK => PrimaryKey(StudentId, CourseId);
}
```

Composite key tables do not use `Key<T>`. The `CompositeKey` property tells the generator which columns form the primary key.

## Naming Styles

Override `NamingStyle` to control how property names map to column names:

```csharp
public class UserSchema : Schema
{
    protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;

    public static string Table => "users";
    public Key<int> UserId => Identity();  // maps to "user_id"
}
```

Options: `Exact` (default), `SnakeCase`, `CamelCase`, `LowerCase`.

Use `MapTo("name")` to override the naming style for individual columns:

```csharp
public Col<string> FullName => MapTo<string>("full_name");  // explicit override
```

## Enums

Enum-typed columns are automatically detected. Values are stored and read as the underlying integral type:

```csharp
public Col<Priority> Priority { get; }  // stored as int
```

The generator handles enum-to-integer conversion in both parameter binding and row materialization. No mapping class is needed.

## Sensitive Columns

The `Sensitive()` modifier marks a column so that its parameter values are redacted in all log output. When Quarry logs parameters at the `Trace` level, sensitive columns display as `[SENSITIVE]` instead of the actual value.

```csharp
public class UserSchema : Schema
{
    public static string Table => "users";

    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string> PasswordHash => Length(256).Sensitive();
    public Col<string> SocialSecurityNumber => Length(11).Sensitive();
    public Col<string?> Email { get; }
}
```

When a query binds parameters for these columns, the generated interceptor emits `ParameterLog.BoundSensitive()` instead of `ParameterLog.Bound()`. The log output looks like:

```
[Trace] Quarry.Parameters: [42] @p0 = john_doe
[Trace] Quarry.Parameters: [42] @p1 = [SENSITIVE]
[Trace] Quarry.Parameters: [42] @p2 = [SENSITIVE]
```

This applies to all query types -- `Where`, `Insert`, `Update`, and `Set` clauses. The redaction is determined at compile time from the schema definition, so there is no runtime flag to toggle.

## Custom Type Mappings

Map custom CLR types to database types by extending `TypeMapping<TCustom, TDb>`:

```csharp
public class MoneyMapping : TypeMapping<Money, decimal>
{
    public override decimal ToDb(Money value) => value.Amount;
    public override Money FromDb(decimal value) => new(value);
}

// In schema:
public Col<Money> Balance => Mapped<Money, MoneyMapping>();
```

The generator calls `ToDb()` inline when binding parameters and `FromDb()` in the materialization reader. Both calls are emitted directly in the interceptor -- no dictionary lookup or virtual dispatch at runtime.

### Dialect-Aware Type Mappings

When a custom type needs different SQL types or ADO.NET parameter configuration depending on the target database, implement `IDialectAwareTypeMapping` on the mapping class:

```csharp
public class JsonDocMapping : TypeMapping<JsonDoc, string>, IDialectAwareTypeMapping
{
    public override string ToDb(JsonDoc value) => JsonSerializer.Serialize(value);
    public override JsonDoc FromDb(string value) => JsonSerializer.Deserialize<JsonDoc>(value)!;

    public string? GetSqlTypeName(SqlDialect dialect) => dialect switch
    {
        SqlDialect.PostgreSQL => "jsonb",
        SqlDialect.SqlServer => "NVARCHAR(MAX)",
        _ => "TEXT"
    };

    public void ConfigureParameter(SqlDialect dialect, DbParameter parameter)
    {
        if (dialect == SqlDialect.PostgreSQL && parameter is NpgsqlParameter npgsql)
            npgsql.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;
    }
}
```

The interface has two members:

- `GetSqlTypeName(SqlDialect dialect)` -- returns the SQL type name used in DDL generation (`CREATE TABLE`) and `CAST` expressions. Return `null` to fall back to the default CLR-to-SQL mapping for that dialect.
- `ConfigureParameter(SqlDialect dialect, DbParameter parameter)` -- called after the parameter value is set, allowing you to configure provider-specific properties (e.g., `NpgsqlDbType` for PostgreSQL, `SqlDbType` for SQL Server).

Use it in the schema like any other mapping:

```csharp
public Col<JsonDoc> Metadata => Mapped<JsonDoc, JsonDocMapping>();
```

Both `GetSqlTypeName` and `ConfigureParameter` are called by the runtime `TypeMappingRegistry` on the fallback path. On the compile-time interceptor path, the generator inlines the `ToDb`/`FromDb` calls directly, but parameter configuration is still applied when the mapping implements `IDialectAwareTypeMapping`.
