# Quarry Usage Reference

Compile-time SQL builder for .NET 10. Source generators + C# 12 interceptors. Zero reflection, AOT compatible.

## Schema

Inherit `Schema`. Expression-bodied properties define columns.

```csharp
[EntityReader(typeof(MyReader))] // optional custom materialization
public class UserSchema : Schema
{
    public static string Table => "users";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive => Default(true);
    public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);
    public Col<decimal> Total => Precision(18, 2);
    public Col<MyEnum> Priority { get; }
    public Ref<OrderSchema, int> OrderId => ForeignKey<OrderSchema, int>();
    public Many<OrderSchema> Orders => HasMany<OrderSchema>(o => o.UserId);
}
```

**Column types:** `Key<T>` PK, `Col<T>` standard, `Ref<TSchema,TKey>` FK (TSchema must derive from Schema), `Many<T>` 1:N navigation (T must derive from Schema). Generated entities use `EntityRef<TEntity,TKey>` for FK properties.
**Modifiers:** `Identity()`, `ClientGenerated()`, `Computed()`, `Length(n)`, `Precision(p,s)`, `Default(v)`, `Default(()=>v)`, `MapTo("name")`.
**NamingStyle:** `Exact` (default), `SnakeCase`, `CamelCase`, `LowerCase` — override via `protected override NamingStyle NamingStyle`.
**Enums:** auto-detected, stored as underlying integral type.

## Context

```csharp
[QuarryContext(Dialect = SqlDialect.SQLite, Schema = "public")]
public partial class AppDb : QuarryContext
{
    public partial QueryBuilder<User> Users { get; }
    public partial QueryBuilder<Order> Orders { get; }
}
```

Dialects: `SQLite`, `PostgreSQL`, `MySQL`, `SqlServer`. Multiple contexts with different dialects can coexist.

## Querying

```csharp
await using var db = new AppDb(connection);

// Select — tuple, DTO, single column, or entity
db.Users.Select(u => (u.UserId, u.UserName));
db.Users.Select(u => new UserDto { Name = u.UserName });
db.Users.Select(u => u.UserName);
db.Users.Select(u => u);

// Where — chainable, supports captured variables
db.Users.Where(u => u.IsActive && u.UserId > minId);
// Operators: ==, !=, <, >, <=, >=, &&, ||, !
// Null: u.Email == null, u.Email != null
// String: Contains, StartsWith, EndsWith, ToLower, ToUpper, Trim, Substring
// IN: new[] {1,2,3}.Contains(u.Id)

// OrderBy
db.Users.OrderBy(u => u.UserName);
db.Users.OrderBy(u => u.CreatedAt, Direction.Descending);

// GroupBy + Having + Aggregates
db.Orders.GroupBy(o => o.Status)
    .Having(o => Sql.Count() > 5)
    .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total), Sql.Avg(o.Total), Sql.Min(o.Total), Sql.Max(o.Total)));

// Pagination
db.Users.Select(u => u).Limit(10).Offset(20);

// Distinct
db.Users.Select(u => u.UserName).Distinct();
```

### Joins

```csharp
// 2-table (Join, LeftJoin, RightJoin)
db.Users.Join<Order>((u, o) => u.UserId == o.UserId.Id)
    .Where((u, o) => o.Total > 100)
    .Select((u, o) => (u.UserName, o.Total));

// Navigation-based
db.Users.Join(u => u.Orders).Select((u, o) => (u.UserName, o.Total));

// 3-table chain
db.Users.Join<Order>((u, o) => u.UserId == o.UserId.Id)
    .Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
    .Select((u, o, oi) => (u.UserName, o.Total, oi.ProductName));
// Max 4 tables
```

### Navigation Subqueries

On `Many<T>` properties inside `Where`:

```csharp
db.Users.Where(u => u.Orders.Any());                          // EXISTS
db.Users.Where(u => u.Orders.Any(o => o.Total > 100));        // filtered EXISTS
db.Users.Where(u => u.Orders.All(o => o.Status == "paid"));   // NOT EXISTS + negated
db.Users.Where(u => u.Orders.Count() > 5);                    // scalar COUNT
db.Users.Where(u => u.Orders.Count(o => o.Total > 50) > 2);  // filtered COUNT
```

### Raw SQL Expressions

```csharp
// In projections/where — Sql.Raw<T>("sql", params)
db.Users.Where(u => Sql.Raw<bool>("\"Age\" > @p0", 18));
db.Users.Select(u => Sql.Raw<int>("COALESCE(\"Score\", 0)"));
```

## Modifications

```csharp
// Insert — initializer-aware (only set properties generate columns)
await db.Insert(new User { UserName = "x", IsActive = true }).ExecuteNonQueryAsync();
var id = await db.Insert(user).ExecuteScalarAsync<int>(); // returns identity
await db.InsertMany(users).ExecuteNonQueryAsync();

// Update — requires Where() or All()
await db.Update<User>().Set(u => u.UserName, "New").Where(u => u.UserId == 1).ExecuteNonQueryAsync();
await db.Update<User>().Set(new User { UserName = "New" }).Where(u => u.UserId == 1).ExecuteNonQueryAsync();

// Delete — requires Where() or All()
await db.Delete<User>().Where(u => u.UserId == 1).ExecuteNonQueryAsync();
```

## Execution Methods

| Method | Returns |
|---|---|
| `ExecuteFetchAllAsync()` | `Task<List<T>>` |
| `ExecuteFetchFirstAsync()` | `Task<T>` (throws if empty) |
| `ExecuteFetchFirstOrDefaultAsync()` | `Task<T?>` |
| `ExecuteFetchSingleAsync()` | `Task<T>` (throws if not exactly one) |
| `ExecuteScalarAsync<T>()` | `Task<T>` |
| `ExecuteNonQueryAsync()` | `Task<int>` |
| `ToAsyncEnumerable()` | `IAsyncEnumerable<T>` |
| `ToSql()` | `string` (preview SQL) |

## Raw SQL

Source-generated typed readers — zero reflection.

```csharp
await db.RawSqlAsync<User>("SELECT * FROM users WHERE id = @p0", userId);
await db.RawSqlScalarAsync<int>("SELECT COUNT(*) FROM users");
await db.RawSqlNonQueryAsync("DELETE FROM logs WHERE date < @p0", cutoff);
```

## Set Operations

```csharp
db.Union(query1, query2);     // also UnionAll, Except, Intersect
```

## Custom Entity Reader

Override generated materialization for entity projections (`Select(u => u)`):

```csharp
public class MyReader : EntityReader<User>
{
    public override User Read(DbDataReader reader) => new User { /* custom */ };
}
```

Apply to schema: `[EntityReader(typeof(MyReader))]`. Does not affect tuple/DTO projections.

## Custom Type Mapping

Map custom CLR types to DB types:

```csharp
public class MoneyMapping : TypeMapping<Money, decimal>
{
    public override decimal ToDb(Money value) => value.Amount;
    public override Money FromDb(decimal value) => new Money(value);
}
```

Use in schema: `public Col<Money> Balance => TypeMap<MoneyMapping>();`

## Dialect Differences

| | SQLite | PostgreSQL | MySQL | SqlServer |
|---|---|---|---|---|
| Quote | `"` | `"` | `` ` `` | `[`/`]` |
| Params | `@p0` | `$1` | `?` | `@p0` |
| Bool | `1`/`0` | `TRUE`/`FALSE` | `1`/`0` | `1`/`0` |
| Pagination | `LIMIT/OFFSET` | `LIMIT/OFFSET` | `LIMIT/OFFSET` | `OFFSET/FETCH` |
| Returning | `RETURNING` | `RETURNING` | `LAST_INSERT_ID()` | `OUTPUT INSERTED` |
