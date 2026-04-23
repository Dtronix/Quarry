# Querying

All query builder methods return interfaces (`IQueryBuilder<T>`, `IJoinedQueryBuilder<T1, T2>`, etc.) to keep internal builder methods hidden from the public API.

## Select

Select controls the shape of the returned data. You can project full entities, single columns, tuples, or DTO objects.

```csharp
db.Users().Select(u => u);                                         // full entity
db.Users().Select(u => u.UserName);                                // single column
db.Users().Select(u => (u.UserId, u.UserName));                    // tuple
db.Users().Select(u => new UserDto { Name = u.UserName });         // DTO projection
```

## Where

Filter rows using standard C# comparison and logical operators. The generator translates them to SQL at compile time.

```csharp
db.Users().Where(u => u.IsActive && u.UserId > minId);
```

Supported operators: `==`, `!=`, `<`, `>`, `<=`, `>=`, `&&`, `||`, `!`

### Null Checks

Use standard C# null comparisons. They translate to `IS NULL` and `IS NOT NULL`.

```csharp
db.Users().Where(u => u.Email == null);       // WHERE "Email" IS NULL
db.Users().Where(u => u.Email != null);       // WHERE "Email" IS NOT NULL
```

### String Methods

Common `System.String` methods are translated to dialect-appropriate SQL. `Contains` and `StartsWith` use `LIKE` with proper escaping.

```csharp
db.Users().Where(u => u.UserName.Contains("admin"));              // LIKE '%admin%'
db.Users().Where(u => u.UserName.StartsWith("A"));                // LIKE 'A%'
db.Users().Where(u => u.UserName.EndsWith("son"));                // LIKE '%son'
db.Users().Where(u => u.Email.ToLower() == "test@example.com");   // LOWER("Email") = ...
db.Users().Where(u => u.UserName.ToUpper() == "ADMIN");           // UPPER("UserName") = ...
db.Users().Where(u => u.UserName.Trim() == "admin");              // TRIM("UserName") = ...
db.Users().Where(u => u.UserName.Substring(0, 3) == "adm");      // SUBSTR/SUBSTRING
```

### IN Clauses

Use `Contains` on any collection to generate an `IN` clause. Arrays, lists, and any `IEnumerable<T>` (including LINQ projections) are supported.

```csharp
var ids = new[] { 1, 2, 3 };
db.Users().Where(u => ids.Contains(u.UserId));                    // WHERE "UserId" IN (1, 2, 3)

// LINQ projections work too
IEnumerable<int> activeIds = users.Select(u => u.Id);
db.Orders().Where(o => activeIds.Contains(o.UserId));             // WHERE "UserId" IN (@p0, @p1, ...)
```

Arrays and lists with compile-time constant elements are inlined as literals. Other collections are parameterized and expanded at runtime. Empty collections produce a no-match clause (`WHERE 1=0` semantics) rather than a SQL error.

### Raw SQL in Where

For expressions that cannot be represented with the built-in operators, use `Sql.Raw<T>()` (see [Raw SQL Expressions](#raw-sql-expressions) below).

```csharp
db.Users().Where(u => Sql.Raw<bool>("\"Age\" > @p0", 18));
```

## OrderBy, GroupBy, Aggregates

Order results by one or more columns. Chain `ThenBy` for secondary sort keys.

```csharp
db.Users().OrderBy(u => u.UserName);
db.Users().OrderBy(u => u.CreatedAt, Direction.Descending);
```

Group rows and filter groups with `Having`. Aggregate markers are available in both `Having` and `Select`.

```csharp
db.Orders().GroupBy(o => o.Status)
    .Having(o => Sql.Count() > 5)
    .Select(o => (o.Status, Sql.Count(), Sql.Sum(o.Total)));
```

Aggregate markers: `Sql.Count()`, `Sql.Count(column)`, `Sql.Sum()`, `Sql.Avg()`, `Sql.Min()`, `Sql.Max()`. Aggregates work in both single-table and joined projections.

## Pagination and Distinct

Limit and offset control pagination. `Distinct()` eliminates duplicate rows from the result set.

```csharp
db.Users().Select(u => u).Limit(10).Offset(20);
```

`Distinct()` can be applied to any query builder and works with all projection types (entity, tuple, DTO, single column). It adds `SELECT DISTINCT` to the generated SQL.

```csharp
db.Users().Select(u => u.UserName).Distinct();                    // SELECT DISTINCT "UserName" ...
db.Users().Distinct().Select(u => (u.UserName, u.Email));         // SELECT DISTINCT "UserName", "Email" ...
```

## Joins

Explicit joins support `Join`, `LeftJoin`, `RightJoin`, `CrossJoin`, and `FullOuterJoin`. Up to 6 tables can be chained.

```csharp
// 2-table inner join
db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
    .Where((u, o) => o.Total > 100)
    .Select((u, o) => (u.UserName, o.Total));

// Navigation-based join (infers the join condition from the schema FK)
db.Users().Join(u => u.Orders)
    .Select((u, o) => (u.UserName, o.Total));

// 3-table chained join (up to 6 tables total)
db.Users().Join<Order>((u, o) => u.UserId == o.UserId.Id)
    .Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
    .Select((u, o, oi) => (u.UserName, o.Total, oi.ProductName));

// LEFT / RIGHT / CROSS / FULL OUTER
db.Users().LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id);
db.Users().RightJoin<Order>((u, o) => u.UserId == o.UserId.Id);
db.Users().CrossJoin<Region>();
db.Users().FullOuterJoin<Order>((u, o) => u.UserId == o.UserId.Id);
```

Columns projected from the nullable side of a `LeftJoin`, `RightJoin`, or `FullOuterJoin` are automatically `IsDBNull`-guarded in the generated reader, so unmatched rows no longer throw `InvalidCastException`. `FullOuterJoin` emits `QRA502` (warning) on SQLite and MySQL, which don't natively support the construct.

## Navigation Joins

Two navigation kinds add implicit joins to your queries without requiring an explicit `.Join()` call.

### One\<T\> — reverse 1:1 navigation

Declare `HasOne<T>()` on the non-FK side of a relationship:

```csharp
public class OrderSchema : Schema
{
    public Key<int> OrderId => Identity();
    public Ref<UserSchema, int> UserId { get; }
    public One<User> User => HasOne<User>();
}
```

The generated `Order` entity gains a nullable `User?` property. Navigation lambdas require `!.` to assert non-null:

```csharp
db.Orders().Where(o => o.User!.IsActive)
    .Select(o => (o.User!.UserName, o.Total));
```

### HasManyThrough — many-to-many skip navigation

Declare a many-to-many relationship through a junction table with `HasManyThrough<TTarget, TJunction>()`:

```csharp
public class OrderSchema : Schema
{
    public Many<Tag> Tags => HasManyThrough<Tag, OrderTag>();
}
```

`Count()`, `Any()`, and aggregates automatically include the junction→target JOIN:

```csharp
db.Orders().Where(o => o.Tags.Any(t => t.Name == "urgent"));
db.Orders().Select(o => (o.OrderId, TagCount: o.Tags.Count()));
```

## Navigation Subqueries

Use `Many<T>` properties in `Where` or `Select` clauses to generate correlated subqueries. The generator infers FK-to-PK correlation from the schema.

```csharp
db.Users().Where(u => u.Orders.Any());                          // EXISTS
db.Users().Where(u => u.Orders.Any(o => o.Total > 100));        // filtered EXISTS
db.Users().Where(u => u.Orders.All(o => o.Status == "paid"));   // NOT EXISTS + negated
db.Users().Where(u => u.Orders.Count() > 5);                    // scalar COUNT
db.Users().Where(u => u.Orders.Count(o => o.Total > 50) > 2);   // filtered COUNT

// Aggregates (Sum, Min, Max, Avg/Average) on Many<T> — in Where / Having / HasManyThrough Count only
db.Users().Where(u => u.Orders.Sum(o => o.Total) > 100);
db.Users().Where(u => u.Orders.Max(o => o.Total) >= 300);
db.Users().Where(u => u.Orders.Average(o => o.Total) > 100);
```

Both `Avg` and `Average` are accepted as selector names. `Many<T>` scalar aggregates are currently supported in predicate positions (`Where`, `Having`, comparisons); projecting one directly into a Select tuple isn't supported yet — the `Count()` form on `HasManyThrough` is fine because its result type is already known.

## Set Operations

Combine two queries with `Union`, `UnionAll`, `Intersect`, `IntersectAll`, `Except`, or `ExceptAll`. Both queries must project the same column shape; operands can reference different entities.

```csharp
db.Users().Select(u => u.UserName)
    .Union(db.Admins().Select(a => a.DisplayName));

db.Orders().Select(o => new IdAmount(o.Id, o.Amount))
    .UnionAll(db.Quotes().Select(q => new IdAmount(q.Id, q.Estimate)))
    .Where(x => x.Amount > 100)
    .OrderBy(x => x.Amount)
    .ExecuteFetchAllAsync();
```

When `Where`, `GroupBy`, or `Having` follows a set operation, the generator wraps the set expression in a subquery automatically. `IntersectAll` and `ExceptAll` are not supported on all dialects — the generator emits `QRY070` or `QRY071` when you use them on SQLite. A column-count or projection-type mismatch between operands produces `QRY072`.

Like all other query paths, set operations are fully compiled at build time.

## Window Functions

`Sql.RowNumber`, `Rank`, `DenseRank`, `Ntile`, `Lag`, `Lead`, `FirstValue`, `LastValue`, plus `Sum`/`Count`/`Avg`/`Min`/`Max(column, over => …)` aggregates use a fluent `IOverClause`:

```csharp
var ranked = await db.Sales()
    .Select(s => (
        s.Region,
        s.Amount,
        Rank: Sql.Rank(over => over.PartitionBy(s.Region).OrderByDescending(s.Amount)),
        RunningTotal: Sql.Sum(s.Amount, over => over.PartitionBy(s.Region).OrderBy(s.SaleDate)),
        Previous: Sql.Lag(s.Amount, 1, 0m, over => over.PartitionBy(s.Region).OrderBy(s.SaleDate))
    ))
    .ExecuteFetchAllAsync();
```

Non-column arguments (offsets, default values, NTILE buckets) are parameterized at compile time — `Sql.Lag(col, 1, 0m, …)` binds `1` and `0` as parameters and strips the `m` suffix. Frame specifications (`ROWS`/`RANGE`) are not yet supported.

## Common Table Expressions

`With<TDto>(dto => dto…)` and `FromCte<TDto>()` compile to standard SQL `WITH` clauses across all four dialects. To chain entity accessors after `With`, your context must derive from `QuarryContext<TSelf>` (see [Context Definition](context-definition.md)).

```csharp
public record ActiveUser(int UserId, string UserName);

var results = await db
    .With<User, ActiveUser>(users => users
        .Where(u => u.IsActive)
        .Select(u => new ActiveUser(u.UserId, u.UserName)))
    .FromCte<ActiveUser>()
    .Where(a => a.UserName.StartsWith("a"))
    .ExecuteFetchAllAsync();
```

Multi-CTE chains are supported:

```csharp
db.With<TopSellers>(…)
  .With<RecentBuyers>(…)
  .FromCte<TopSellers>()
  .Join<RecentBuyers>((t, r) => t.UserId == r.UserId)
  .Select((t, r) => (t.UserName, r.PurchaseCount));
```

Diagnostics: `QRY080` (CTE inner query not analyzable), `QRY081` (`FromCte` without matching `With`), `QRY082` (duplicate CTE name in a single chain).

## Raw SQL Expressions

When the built-in operators and string methods are not enough, `Sql.Raw<T>()` lets you inject a raw SQL fragment into any expression position. Use `@p0`, `@p1`, etc. as parameter placeholders -- the generator rewrites them to the correct dialect format and binds them as parameterized values.

```csharp
// Raw boolean expression in a Where clause
db.Users().Where(u => Sql.Raw<bool>("\"Age\" > @p0", 18));

// Multi-argument raw fragment
db.Users().Where(u => Sql.Raw<bool>("\"Age\" BETWEEN @p0 AND @p1", 18, 65));

// Custom function call
db.Users().Where(u => Sql.Raw<bool>("custom_predicate({0})", u.UserId));
```

`Sql.Raw<T>` is supported in predicate positions (`Where`, `Having`, boolean subquery fragments) and in `Select` projections — single-entity tuples/DTOs, joined projections, and single-column projections. Supported argument kinds inside a projection include column references, compile-time constants, captured runtime values, and IR expressions (e.g., `u.Price * 2`):

```csharp
// Single-column Sql.Raw projection
db.Users().Select(u => Sql.Raw<string>("UPPER({0})", u.UserName));

// Sql.Raw inside a tuple projection with a column ref and a captured value
var cutoff = 100m;
db.Orders().Select(o => (
    o.OrderId,
    Discounted: Sql.Raw<decimal>("{0} * 0.9", o.Total),
    OverCutoff: Sql.Raw<bool>("{0} > @p0", o.Total, cutoff)));
```

The generator emits QRY008 (warning) when `Sql.Raw` is used, as a reminder to verify that user input is not concatenated into the SQL string. Always pass dynamic values through the parameter placeholders. Placeholder mismatch (e.g., referencing `@p2` when only two arguments are provided) produces compile error QRY029 — this now fires from Where/Having and Select projection positions alike.

## Conditional Branches

Queries built with `if`/`else` are fully supported at compile time. The generator tracks each conditional clause assignment and emits all possible SQL variants ahead of time.

```csharp
var query = db.Users().Select(u => u);

if (activeOnly)
    query = query.Where(u => u.IsActive);

if (sortByName)
    query = query.OrderBy(u => u.UserName);

// Generator emits up to 4 SQL variants (2 bits x 2 states)
// and dispatches to the correct one at runtime via bitmask
var results = await query.Limit(10).ExecuteFetchAllAsync();
```

### How Bitmask Dispatch Works

Each conditional clause in the chain is assigned a bit index. At runtime, the carrier sets the corresponding bit in a `ClauseMask` field when a conditional clause is executed. The terminal method uses this mask as a key to select the correct pre-built SQL string from a switch expression.

For example, with 2 conditional clauses (Where and OrderBy above), the generator emits 4 SQL variants:

| Mask | Where active? | OrderBy active? | SQL |
|------|--------------|-----------------|-----|
| `0b00` | No | No | `SELECT ... FROM "users" LIMIT 10` |
| `0b01` | Yes | No | `SELECT ... FROM "users" WHERE "IsActive" = 1 LIMIT 10` |
| `0b10` | No | Yes | `SELECT ... FROM "users" ORDER BY "UserName" LIMIT 10` |
| `0b11` | Yes | Yes | `SELECT ... FROM "users" WHERE "IsActive" = 1 ORDER BY "UserName" LIMIT 10` |

The generator supports up to 8 conditional bits, producing a maximum of 256 SQL variants per chain. Chains that exceed this limit or that cannot be statically analyzed produce compile error QRY032. All dispatch is a constant-time switch -- no SQL is built or concatenated at runtime.

## Execution Methods

| Method | Returns |
|---|---|
| `ExecuteFetchAllAsync()` | `Task<List<T>>` |
| `ExecuteFetchFirstAsync()` | `Task<T>` (throws if empty) |
| `ExecuteFetchFirstOrDefaultAsync()` | `Task<T?>` |
| `ExecuteFetchSingleAsync()` | `Task<T>` (throws if not exactly one) |
| `ExecuteFetchSingleOrDefaultAsync()` | `Task<T?>` (throws if more than one) |
| `ExecuteScalarAsync<T>()` | `Task<T>` |
| `ExecuteNonQueryAsync()` | `Task<int>` |
| `ToAsyncEnumerable()` | `IAsyncEnumerable<T>` |
| `ToDiagnostics()` | `QueryDiagnostics` |
| `Prepare()` | `PreparedQuery<T>` |

These terminals are also available directly on `IQueryBuilder<T>` (no need to insert a `.Select(u => u)` before the terminal when you're fetching the full entity).
