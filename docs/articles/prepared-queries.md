# Prepared Queries

## The Problem

A typical Quarry query chain ends with a single terminal method -- `ExecuteFetchAllAsync`, `ToDiagnostics`, etc. Each terminal triggers the source generator to emit an interceptor for that specific call site. This works well when you need one result, but sometimes you need to do more than one thing with the same query: inspect the generated SQL, then execute it; or fetch all rows and also stream them. Without `.Prepare()`, you would have to duplicate the entire chain for each terminal, causing the generator to emit redundant interceptors with identical SQL.

`.Prepare()` solves this by freezing a query chain into a `PreparedQuery<TResult>` that you can call multiple terminal methods on -- build once, execute multiple ways.

## Basic Usage

```csharp
var prepared = db.Users()
    .Where(u => u.IsActive)
    .Select(u => (u.UserId, u.UserName))
    .Prepare();

var diag = prepared.ToDiagnostics();             // inspect SQL
var all = await prepared.ExecuteFetchAllAsync();  // fetch all rows
var first = await prepared.ExecuteFetchFirstAsync(); // fetch first row
```

## With Modifications

`.Prepare()` works with insert, update, and delete chains too:

```csharp
var prepared = db.Users()
    .Insert(new User { UserName = "x", IsActive = true })
    .Prepare();

var diag = prepared.ToDiagnostics();
var affected = await prepared.ExecuteNonQueryAsync();
```

## Available Terminal Methods

`PreparedQuery<TResult>` exposes the same terminal methods available on regular query chains:

| Method | Returns | Description |
|---|---|---|
| `ExecuteFetchAllAsync()` | `Task<List<TResult>>` | Execute and return all rows |
| `ExecuteFetchFirstAsync()` | `Task<TResult>` | Execute and return the first row (throws if empty) |
| `ExecuteFetchFirstOrDefaultAsync()` | `Task<TResult?>` | Execute and return the first row, or default if empty |
| `ExecuteFetchSingleAsync()` | `Task<TResult>` | Execute and return exactly one row (throws if not exactly one) |
| `ExecuteScalarAsync<TScalar>()` | `Task<TScalar>` | Execute and return a scalar value |
| `ExecuteNonQueryAsync()` | `Task<int>` | Execute a modification and return rows affected |
| `ToAsyncEnumerable()` | `IAsyncEnumerable<TResult>` | Stream results row by row |
| `ToDiagnostics()` | `QueryDiagnostics` | Inspect SQL, parameters, and optimization metadata without executing |

Not all methods make sense on every query type. For example, `ExecuteNonQueryAsync` applies to insert/update/delete chains, while `ExecuteFetchAllAsync` applies to select chains. The generator validates this at compile time.

## Inspecting SQL with ToDiagnostics

`ToDiagnostics()` is particularly useful on a prepared query for verifying the generated SQL before executing it -- for example, in tests or during development:

```csharp
var prepared = db.Users()
    .Where(u => u.IsActive)
    .OrderBy(u => u.UserName)
    .Select(u => (u.UserId, u.UserName))
    .Prepare();

var diag = prepared.ToDiagnostics();

// Inspect before executing
Console.WriteLine(diag.Sql);       // SELECT "UserId", "UserName" FROM "users" WHERE "IsActive" = 1 ORDER BY "UserName"
Console.WriteLine(diag.Dialect);   // SQLite
Console.WriteLine(diag.Tier);     // PrebuiltDispatch

foreach (var p in diag.Parameters)
    Console.WriteLine($"{p.Name} = {p.Value}");

// Now execute
var results = await prepared.ExecuteFetchAllAsync();
```

Since `ToDiagnostics()` does not hit the database, calling it before execution is a zero-cost way to assert SQL correctness in unit tests:

```csharp
var q = db.Users().Where(u => u.IsActive).Select(u => u).Prepare();
Assert.That(q.ToDiagnostics().Sql, Does.Contain("WHERE"));
var users = await q.ExecuteFetchAllAsync();
```

## Scope Constraint

`PreparedQuery` variables must not escape their declaring method scope. The generator needs to see the `.Prepare()` call and all terminals within the same method body to emit correct interceptors. If the variable escapes, the generator cannot track which terminals will be called.

The following patterns produce compile error **QRY035** (PreparedQuery escapes scope):

**Returning from a method:**

```csharp
// QRY035 - prepared query returned from method
PreparedQuery<User> GetQuery()
{
    return db.Users().Select(u => u).Prepare();
}
```

**Passing as an argument:**

```csharp
// QRY035 - prepared query passed to another method
var prepared = db.Users().Select(u => u).Prepare();
SomeMethod(prepared);
```

**Lambda capture:**

```csharp
// QRY035 - prepared query captured by lambda
var prepared = db.Users().Select(u => u).Prepare();
var func = () => prepared.ExecuteFetchAllAsync();
```

The fix is always the same: keep the `Prepare()` call and all terminal calls in the same method, with no indirection.

## Prepare with No Terminals (QRY036)

If you call `.Prepare()` but never call any terminal method on the resulting variable, the generator reports compile error **QRY036**. A prepared query with no terminals serves no purpose -- there is no SQL to emit.

```csharp
// QRY036 - no terminal called on prepared query
var prepared = db.Users().Select(u => u).Prepare();
// ... prepared is never used
```

## Performance

**Single terminal:** When only one terminal is called after `.Prepare()`, the generator produces identical code to calling that terminal directly on the chain. The `PreparedQuery` wrapper is elided entirely via `Unsafe.As` -- there is zero overhead compared to a non-prepared chain.

```csharp
// These two produce identical generated code:
var a = await db.Users().Select(u => u).ExecuteFetchAllAsync();

var b = db.Users().Select(u => u).Prepare();
var result = await b.ExecuteFetchAllAsync();
```

**Multiple terminals:** When two or more terminals are called, the generator emits a carrier class that covers all observed terminal methods. Each terminal gets its own interceptor pointing at the same carrier, with pre-built SQL shared across them. The carrier handles parameter storage and clause-mask dispatch once, and each terminal reuses that state.
