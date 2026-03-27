# Diagnostics

Quarry provides diagnostics at two levels. At **compile time**, the source generator and analyzers report errors and warnings (QRY and QRA codes) when a query chain cannot be statically analyzed or contains anti-patterns. At **runtime**, `ToDiagnostics()` returns a `QueryDiagnostics` object that exposes the generated SQL, bound parameters, clause breakdown, projection metadata, and conditional variant information for any query chain. Together these give you full visibility into what SQL Quarry will execute, before it executes.

## Query Diagnostics

`ToDiagnostics()` returns a `QueryDiagnostics` object with comprehensive compile-time analysis. It is available on all builder types (select, join, insert, update, delete, batch insert) and on `PreparedQuery<T>`.

```csharp
var diag = db.Users()
    .Where(u => u.IsActive)
    .OrderBy(u => u.UserName)
    .Select(u => u)
    .ToDiagnostics();

Console.WriteLine(diag.Sql);                // SELECT "UserId", "UserName", ... FROM "users" WHERE ...
Console.WriteLine(diag.Dialect);            // SQLite
Console.WriteLine(diag.Tier);              // PrebuiltDispatch
Console.WriteLine(diag.IsCarrierOptimized); // True
Console.WriteLine(diag.Kind);              // Select
Console.WriteLine(diag.TableName);         // users
```

### Available Properties

| Property | Description |
|---|---|
| `Sql` | The generated SQL string for the current variant |
| `Parameters` | Active bound parameters (filtered by mask for conditional chains) |
| `AllParameters` | All parameters including inactive conditional ones |
| `Kind` | Select, Delete, Update, or Insert |
| `Dialect` | The SQL dialect |
| `TableName` | Target table name |
| `Tier` | Optimization tier (PrebuiltDispatch) |
| `IsCarrierOptimized` | Whether the chain uses carrier optimization |
| `Clauses` | Per-clause SQL fragments with metadata |
| `SqlVariants` | All pre-built SQL variants keyed by conditional bitmask |
| `ActiveMask` | Current runtime bitmask for conditional clauses |
| `ConditionalBitCount` | Number of conditional bits in the chain |
| `ProjectionColumns` | SELECT column breakdown with types and ordinals |
| `ProjectionKind` | Entity, Dto, Tuple, or SingleColumn |
| `Joins` | JOIN metadata (table, kind, alias, ON condition) |
| `CarrierClassName` | Name of the generated carrier class |
| `IsDistinct` | Whether DISTINCT is applied |
| `Limit` | LIMIT value |
| `Offset` | OFFSET value |
| `IdentityColumnName` | Identity column for INSERT chains |
| `TierReason` | Human-readable tier classification explanation |
| `DisqualifyReason` | Why the chain is not PrebuiltDispatch (null when it is) |
| `CarrierIneligibleReason` | Why carrier optimization was not used |
| `UnmatchedMethodNames` | Method names that could not be matched to known operations |

### Parameter Inspection

Each `DiagnosticParameter` carries metadata beyond just name and value:

```csharp
foreach (var p in diag.Parameters)
{
    Console.WriteLine($"{p.Name} = {p.Value}");
    Console.WriteLine($"  Type: {p.TypeName}, Sensitive: {p.IsSensitive}");
    Console.WriteLine($"  IsEnum: {p.IsEnum}, IsCollection: {p.IsCollection}");
    Console.WriteLine($"  IsConditional: {p.IsConditional}, BitIndex: {p.ConditionalBitIndex}");
}
```

`Parameters` only includes parameters from active clauses. Use `AllParameters` to see every parameter in the chain regardless of which conditional branches were taken.

### Clause Inspection

```csharp
foreach (var clause in diag.Clauses)
{
    Console.WriteLine($"{clause.ClauseType}: {clause.SqlFragment}");
    Console.WriteLine($"  Active={clause.IsActive}, Conditional={clause.IsConditional}");

    if (clause.IsConditional)
        Console.WriteLine($"  BitIndex={clause.ConditionalBitIndex}, Branch={clause.BranchKind}");

    if (clause.SourceLocation is { } loc)
        Console.WriteLine($"  Defined at {loc.FilePath}:{loc.Line}:{loc.Column}");

    foreach (var p in clause.Parameters)
        Console.WriteLine($"  Param: {p.Name} = {p.Value}");
}
```

For conditional chains, each clause reports `IsConditional` and `IsActive` so you can inspect which branches were taken. `BranchKind` is `Independent` for `if`-without-`else` and `MutuallyExclusive` for `if`/`else` pairs.

### SQL Variant Inspection

When a query chain contains conditional branches, the generator pre-builds one SQL variant per possible clause combination. `SqlVariants` maps each bitmask to its SQL string:

```csharp
var query = db.Users().Select(u => u);

if (activeOnly)
    query = query.Where(u => u.IsActive);

if (sortByName)
    query = query.OrderBy(u => u.UserName);

var diag = query.Limit(10).ToDiagnostics();

// 2 conditional bits = up to 4 variants
Console.WriteLine($"ConditionalBitCount: {diag.ConditionalBitCount}"); // 2
Console.WriteLine($"ActiveMask: {diag.ActiveMask}");                   // depends on runtime values

if (diag.SqlVariants is not null)
{
    foreach (var (mask, variant) in diag.SqlVariants)
    {
        Console.WriteLine($"Mask {mask}: {variant.Sql} ({variant.ParameterCount} params)");
    }
    // Mask 0: SELECT ... FROM "users" LIMIT 10                                (no Where, no OrderBy)
    // Mask 1: SELECT ... FROM "users" WHERE "IsActive" = 1 LIMIT 10           (Where only)
    // Mask 2: SELECT ... FROM "users" ORDER BY "UserName" LIMIT 10            (OrderBy only)
    // Mask 3: SELECT ... FROM "users" WHERE "IsActive" = 1 ORDER BY ... LIMIT 10 (both)
}
```

## Projection Columns and ProjectionKind

For SELECT queries, `ProjectionColumns` lists every column in the result set with type information and ordinal position. `ProjectionKind` tells you the shape of the projection.

### ProjectionKind Values

| Kind | Select Expression | Description |
|---|---|---|
| `Entity` | `Select(u => u)` | Full entity -- all columns emitted |
| `Dto` | `Select(u => new UserDto { Name = u.UserName })` | Named DTO with mapped properties |
| `Tuple` | `Select(u => (u.UserId, u.UserName))` | Value tuple of selected columns |
| `SingleColumn` | `Select(u => u.UserName)` | Single scalar column |

### Inspecting Projection Columns

```csharp
var diag = db.Users()
    .Select(u => new UserDto { Name = u.UserName, Active = u.IsActive })
    .ToDiagnostics();

Console.WriteLine($"ProjectionKind: {diag.ProjectionKind}"); // Dto

foreach (var col in diag.ProjectionColumns!)
{
    Console.WriteLine($"  [{col.Ordinal}] {col.PropertyName} -> {col.ColumnName} ({col.ClrType})");
    Console.WriteLine($"      Nullable={col.IsNullable}, FK={col.IsForeignKey}, Enum={col.IsEnum}");

    if (col.IsForeignKey)
        Console.WriteLine($"      References: {col.ForeignKeyEntityName}");

    if (col.TypeMappingClass is not null)
        Console.WriteLine($"      TypeMapping: {col.TypeMappingClass}");
}
```

This is useful for verifying that your projection maps to the columns you expect, and for confirming that custom type mappings and foreign key wrappers are applied correctly.

## Trace

Trace is a compile-time debugging feature that causes the generator to emit `// [Trace]` comment lines inside the generated interceptor source code. These comments show what the generator did at each pipeline stage for a given chain, helping you diagnose unexpected SQL output or generator behavior.

### Enabling Trace

Two things are required:

1. Add `QUARRY_TRACE` to your project's `DefineConstants`:

```xml
<PropertyGroup>
  <DefineConstants>$(DefineConstants);QUARRY_TRACE</DefineConstants>
</PropertyGroup>
```

2. Add `.Trace()` to the query chain you want to inspect:

```csharp
var results = await db.Users()
    .Where(u => u.IsActive)
    .Select(u => u)
    .Trace()
    .ExecuteFetchAllAsync();
```

`.Trace()` is a compile-time-only signal. At runtime it is a no-op that returns the same builder instance. It can be placed anywhere in the chain.

### Trace Categories

The generator emits trace information at each pipeline stage:

| Category | Scope | What It Shows |
|---|---|---|
| Discovery | Per call site | Detected method kind, entity type, chain ID, conditional flags |
| Binding | Per call site | Resolved entity, context, dialect, join relationships |
| Translation | Per call site | SQL expression binding, parameter extraction, rendered SQL fragment |
| ChainAnalysis | Per chain | Grouped sites, terminal detection, tier classification, bitmask allocation |
| Assembly | Per chain | Final SQL per mask variant, parameter counts |
| Carrier | Per chain | Carrier class name, fields, mask type, implemented interfaces |

After building, open the generated `.g.cs` interceptor file (found in the `obj/GeneratedFiles` directory when `EmitCompilerGeneratedFiles` is enabled) and search for `// [Trace]` to see the output.

### QRY034 Warning

If you add `.Trace()` to a chain but `QUARRY_TRACE` is not defined in `DefineConstants`, the generator emits a **QRY034** warning:

> .Trace() found on chain but QUARRY_TRACE is not defined. Add `<DefineConstants>QUARRY_TRACE</DefineConstants>` to enable trace output.

This prevents accidentally leaving `.Trace()` calls in code that will never produce output. Remove the `.Trace()` call or add the symbol to suppress the warning.

## Using ToDiagnostics() for Testing

`ToDiagnostics()` is the primary tool for verifying that Quarry generates the SQL you expect. Since all SQL is pre-built at compile time, you can assert against it in unit tests without executing any database queries.

### Basic SQL Assertion

```csharp
var diag = db.Users()
    .Where(u => u.IsActive)
    .Select(u => (u.UserId, u.UserName))
    .ToDiagnostics();

Assert.That(diag.Sql, Is.EqualTo(
    "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1"));
Assert.That(diag.Kind, Is.EqualTo(DiagnosticQueryKind.Select));
Assert.That(diag.Dialect, Is.EqualTo(SqlDialect.SQLite));
```

### Cross-Dialect Verification

When you have multiple contexts with different dialects, you can verify the SQL for each:

```csharp
// Build the same query against each dialect context
var lite = dbSqlite.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();
var pg   = dbPostgres.Users().Where(u => u.IsActive).Select(u => (u.UserId, u.UserName)).Prepare();

Assert.That(lite.ToDiagnostics().Sql, Is.EqualTo(
    "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = 1"));
Assert.That(pg.ToDiagnostics().Sql, Is.EqualTo(
    "SELECT \"UserId\", \"UserName\" FROM \"users\" WHERE \"IsActive\" = TRUE"));
```

### Verifying Conditional Queries

For conditional chains, verify that each variant produces the expected SQL:

```csharp
string GetSql(bool activeOnly, bool sortByName)
{
    var q = db.Users().Select(u => (u.UserId, u.UserName));

    if (activeOnly)
        q = q.Where(u => u.IsActive);
    if (sortByName)
        q = q.OrderBy(u => u.UserName);

    return q.ToDiagnostics().Sql;
}

Assert.That(GetSql(false, false), Does.Not.Contain("WHERE"));
Assert.That(GetSql(true,  false), Does.Contain("WHERE"));
Assert.That(GetSql(false, true),  Does.Contain("ORDER BY"));
Assert.That(GetSql(true,  true),  Does.Contain("WHERE").And.Contain("ORDER BY"));
```

### Verifying Modifications

`ToDiagnostics()` works on insert, update, and delete chains too:

```csharp
var diag = db.Users()
    .Update()
    .Set(u => u.UserName = "Updated")
    .Where(u => u.UserId == 1)
    .ToDiagnostics();

Assert.That(diag.Kind, Is.EqualTo(DiagnosticQueryKind.Update));
Assert.That(diag.Sql, Does.Contain("SET \"UserName\""));
Assert.That(diag.Sql, Does.Contain("WHERE \"UserId\""));
```

### Using Prepare for Multi-Terminal Testing

`.Prepare()` compiles the chain once and lets you call both `ToDiagnostics()` and an execution terminal on the same chain. This is the recommended pattern for tests that need to verify SQL and then execute:

```csharp
var prepared = db.Users()
    .Where(u => u.IsActive)
    .Select(u => (u.UserId, u.UserName))
    .Prepare();

// Verify SQL
var diag = prepared.ToDiagnostics();
Assert.That(diag.Sql, Does.Contain("WHERE"));
Assert.That(diag.ProjectionKind, Is.EqualTo("Tuple"));

// Then execute
var results = await prepared.ExecuteFetchAllAsync();
Assert.That(results, Has.Count.GreaterThan(0));
```

## Compile-Time Diagnostics

See [Analyzer Rules](analyzer-rules.md) for the full list of QRY and QRA diagnostic codes.
