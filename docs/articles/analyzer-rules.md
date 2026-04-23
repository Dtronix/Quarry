# Analyzer Rules

Quarry ships two independent diagnostic systems that report issues at compile time:

- **QRY (Generator Diagnostics)** -- Emitted by `Quarry.Generator`, the Roslyn source generator that builds SQL. These diagnostics cover schema errors, query translation failures, chain analyzability, migration integrity, and internal generator faults. They are always active when the generator package is referenced.
- **QRA (Analyzer Rules)** -- Emitted by `Quarry.Analyzers`, a separate Roslyn analyzer package. These are optional, advisory rules that inspect query patterns for simplification opportunities, wasteful constructs, performance pitfalls, anti-patterns, and dialect-specific issues. Three rules (QRA101, QRA102, QRA201) include automatic code fixes.

Both systems surface diagnostics in the Error List, `dotnet build` output, and IDE squiggles like any other Roslyn diagnostic.

---

## Installing Quarry.Analyzers

Add the analyzer and (optionally) its code fix companion to your `.csproj`:

```xml
<PackageReference Include="Quarry.Analyzers" Version="1.0.0"
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false" />

<!-- Optional: enables lightbulb code fixes in Visual Studio / Rider -->
<PackageReference Include="Quarry.Analyzers.CodeFixes" Version="1.0.0"
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false" />
```

No additional configuration is required. All QRA rules are enabled by default and can be tuned via EditorConfig or `#pragma` directives (see [Suppressing Diagnostics](#suppressing-diagnostics) below).

---

## Generator Diagnostics (QRY)

### Query (QRY001--QRY019)

| Code | Severity | Description |
|---|---|---|
| QRY001 | Warning | Query chain not fully analyzable |
| QRY002 | Error | Missing `Table` property on schema |
| QRY003 | Error | Invalid column type |
| QRY004 | Error | Unknown navigation entity |
| QRY005 | Error | Unmapped projection property |
| QRY006 | Error | Unsupported Where operator |
| QRY007 | Error | Undefined join relationship |
| QRY008 | Warning | `Sql.Raw` usage risk |
| QRY009 | Error | Aggregate without GroupBy |
| QRY010 | Error | Composite key unsupported in this context |
| QRY011 | Error | Select clause required |
| QRY012 | Error | Where or All required for modifications |
| QRY013 | Error | GUID column needs ClientGenerated modifier |
| QRY014 | Error | Anonymous type unsupported in this context |
| QRY015 | Warning | Ambiguous context resolution |
| QRY016 | Error | Unbound parameter |
| QRY019 | Warning | Clause not translatable |

### Subquery (QRY020--QRY025)

| Code | Severity | Description |
|---|---|---|
| QRY020 | Error | `All()` requires a predicate |
| QRY021 | Error | Subquery entity not found |
| QRY022 | Error | Foreign key not found for correlation |
| QRY023 | Error | Correlation ambiguous |
| QRY024 | Error | Subquery on non-Many property |
| QRY025 | Error | Composite PK not supported for subqueries |

### Entity Reader (QRY026--QRY027)

| Code | Severity | Description |
|---|---|---|
| QRY026 | Info | Custom entity reader active |
| QRY027 | Error | Invalid entity reader type |

### Chain (QRY028--QRY036)

| Code | Severity | Description |
|---|---|---|
| QRY028 | Warning | Redundant unique constraint on index |
| QRY029 | Warning | `Sql.Raw` placeholder mismatch |
| QRY030 | Info | Prebuilt dispatch optimization applied |
| QRY031 | Error | `RawSqlAsync<T>` with unresolvable generic type parameter |
| QRY032 | Error | Chain not analyzable at compile time |
| QRY033 | Error | Forked chain (multiple terminals) |
| QRY034 | Warning | `.Trace()` without `QUARRY_TRACE` symbol |
| QRY035 | Error | PreparedQuery escapes method scope |
| QRY036 | Error | `.Prepare()` with no terminal calls |

### SQL Manifest (QRY040)

| Code | Severity | Description |
|---|---|---|
| QRY040 | Warning | SQL manifest write failure (see [SQL Manifest](sql-manifest.md)) |

### Raw SQL Resolution (QRY041--QRY043)

| Code | Severity | Description |
|---|---|---|
| QRY041 | Warning | `RawSqlAsync` literal SQL has an unresolvable column or un-aliased expression |
| QRY042 | Info | `RawSqlAsync` call is convertible to a chain query (code fix available) |
| QRY043 | Error | `RawSqlAsync<T>` / `RawSqlScalarAsync<T>` row entity `T` is not materializable — positional record, init-only property, abstract class, or interface. Project on a chain query with `Select(x => new Dto { ... })` for immutable shapes |

### Project Setup (QRY044)

| Code | Severity | Description |
|---|---|---|
| QRY044 | Warning | `[QuarryContext]` class's namespace is not listed in the MSBuild `<InterceptorsNamespaces>` property. Without it, C# 12 interceptors for that namespace are ignored and every terminal call fails with `CS9137`. The diagnostic message includes the exact csproj line to paste |

Analyzer-only diagnostic (ships in `Quarry.Analyzers`); no code fix, because the target is the `.csproj`, not a source document. `Quarry.Generated` is auto-registered by the shipped `build/Quarry.targets` so consumers only need to list their own context namespaces.

### Migration (QRY050--QRY055)

| Code | Severity | Description |
|---|---|---|
| QRY050 | Warning | Schema drift detected |
| QRY051 | Error | Unknown table or column reference |
| QRY052 | Error | Version gap or duplicate |
| QRY053 | Warning | Pending migrations not applied |
| QRY054 | Warning | Destructive operation without backup |
| QRY055 | Warning | Nullable-to-non-null column change |

### Navigation (QRY060--QRY065)

| Code | Severity | Description |
|---|---|---|
| QRY060 | Error | No FK column for `One<T>` navigation |
| QRY061 | Error | Ambiguous FK for `One<T>` navigation |
| QRY062 | Error | `HasOne` references invalid column |
| QRY063 | Error | Navigation target entity not found |
| QRY064 | Error | `HasManyThrough` invalid junction navigation |
| QRY065 | Error | `HasManyThrough` invalid target navigation |

### Set Operations (QRY070--QRY072)

| Code | Severity | Description |
|---|---|---|
| QRY070 | Warning | `IntersectAll` not supported on this dialect (e.g., SQLite) |
| QRY071 | Warning | `ExceptAll` not supported on this dialect (e.g., SQLite) |
| QRY072 | Error | Set operation projection mismatch (column count or type) |

QRY073 was introduced in v0.3.0 for cross-entity set-ops and retired in the same release when cross-entity support landed. Remove any `#pragma warning disable QRY073` directives. The ID is intentionally skipped going forward so those pragmas keep their warning-free meaning.

### Projection Subqueries (QRY074)

| Code | Severity | Description |
|---|---|---|
| QRY074 | Error | Navigation aggregate (`Sum`/`Min`/`Max`/`Avg`/`Average`/`Count`) in a `Select` projection could not be resolved — the navigation property does not exist on the outer entity or its target entity is not registered on the context |

### Common Table Expressions (QRY080--QRY082)

| Code | Severity | Description |
|---|---|---|
| QRY080 | Error | CTE inner query not analyzable |
| QRY081 | Error | `FromCte` without matching `With` |
| QRY082 | Error | Duplicate CTE name in chain |

### Internal

| Code | Severity | Description |
|---|---|---|
| QRY900 | Error | Internal generator error |

### Migration Converters (QRM series)

Emitted by the [`Quarry.Migration`](https://www.nuget.org/packages/Quarry.Migration) package. Each diagnostic includes an IDE code fix that replaces the source call site with equivalent Quarry chain code.

| Code | Severity | Source tool | Description |
|---|---|---|---|
| QRM001 | Info | Dapper | Dapper call convertible to Quarry |
| QRM002 | Warning | Dapper | Converted with warnings |
| QRM003 | Info | Dapper | Not convertible (falls back to `Sql.Raw` or manual migration) |
| QRM011 | Info | EF Core | EF Core query convertible to Quarry |
| QRM012 | Warning | EF Core | Converted with warnings |
| QRM013 | Info | EF Core | Not convertible |
| QRM021 | Info | ADO.NET | ADO.NET call convertible to Quarry |
| QRM022 | Warning | ADO.NET | Converted with warnings |
| QRM023 | Info | ADO.NET | Not convertible |
| QRM031 | Info | SqlKata | SqlKata query convertible to Quarry |
| QRM032 | Warning | SqlKata | Converted with warnings |
| QRM033 | Info | SqlKata | Not convertible |

Analyzers only activate when the source tool's framework type is present in the compilation. See [Migrating to Quarry](migrating-to-quarry.md) for the `quarry convert --from <tool>` CLI workflow.

### Common QRY Diagnostics

These are the generator diagnostics you are most likely to encounter during normal development.

#### QRY032 -- Chain not analyzable at compile time

Quarry requires every query chain to be fully analyzable by the source generator. If the generator cannot trace the chain from entry point to terminal, it emits QRY032. Common causes:

- Storing a builder in a field, property, or collection instead of a local variable.
- Passing a builder across method boundaries (as a parameter or return value).
- Building a chain inside a loop where the iteration variable influences the chain structure.

```csharp
// QRY032 -- builder escapes method scope
IQueryBuilder<User> BuildQuery() =>
    db.Users().Where(u => u.IsActive);

// Fix: keep the entire chain in one method
var users = await db.Users()
    .Where(u => u.IsActive)
    .Select(u => u)
    .ExecuteFetchAllAsync();
```

Use conditional branches (`if`/`else`) instead of dynamic chain construction -- the generator handles those natively via bitmask dispatch.

#### QRY011 -- Select clause required

Every query chain must include a `.Select()` call before a terminal. The generator needs it to know which columns to emit.

```csharp
// QRY011 -- missing Select
var users = await db.Users()
    .Where(u => u.IsActive)
    .ExecuteFetchAllAsync();

// Fix: add Select
var users = await db.Users()
    .Where(u => u.IsActive)
    .Select(u => u)
    .ExecuteFetchAllAsync();
```

#### QRY012 -- Where or All required for modifications

Update and Delete operations require an explicit `Where()` or `All()` call to prevent accidental full-table modifications.

```csharp
// QRY012 -- no scope for delete
await db.Users().Delete().ExecuteNonQueryAsync();

// Fix: add Where or acknowledge All
await db.Users().Delete().Where(u => u.UserId == 1).ExecuteNonQueryAsync();
await db.Users().Delete().All().ExecuteNonQueryAsync(); // explicit full-table
```

---

## Analyzer Rules (QRA)

These rules are provided by the `Quarry.Analyzers` package and are independent of the source generator. They perform pattern-based analysis of Quarry query call sites.

### Simplification (QRA101--QRA106)

Rules that detect query patterns which can be written more simply.

| Code | Severity | Description | Code Fix |
|---|---|---|---|
| QRA101 | Info | Count compared to zero -- `Count() > 0` or `Count() == 0` can be replaced with `Any()` | Yes |
| QRA102 | Info | Single-value IN clause -- `new[] { x }.Contains(col)` simplifies to `col == x` | Yes |
| QRA103 | Info | Tautological condition -- always-true conditions like `1 == 1` or `col == col` | |
| QRA104 | Info | Contradictory condition -- always-false conditions like `x > 5 && x < 3` | |
| QRA105 | Info | Redundant condition -- a condition subsumed by a stronger one (e.g., `x > 5 && x > 3`) | |
| QRA106 | Info | Nullable column without null check -- nullable column compared with `==` without null handling | |

**QRA101 code fix** rewrites `Count() > 0` to `Any()` and `Count() == 0` to `!Any()`, handling async variants.

**QRA102 code fix** converts `new[] { x }.Contains(col)` to `col == x`.

### Wasteful Patterns (QRA201--QRA205)

Rules that detect unnecessary work in query construction.

| Code | Severity | Description | Code Fix |
|---|---|---|---|
| QRA201 | Warning | Unused join -- joined table not referenced in SELECT, WHERE, or ORDER BY | Yes |
| QRA202 | Info | Wide table SELECT -- `Select(u => u)` on a table exceeding the column threshold (default: 10) | |
| QRA203 | Info | ORDER BY without LIMIT -- sorting without pagination on an unbounded result set | |
| QRA204 | Info | Duplicate projection column -- same column projected multiple times in SELECT | |
| QRA205 | Warning | Cartesian product -- JOIN with missing or trivial ON condition (`1 == 1`) | |

**QRA201 code fix** removes the unused `.Join(...)` call from the query chain, preserving the receiver.

### Performance (QRA301--QRA304)

Rules that flag potential performance concerns, primarily around index usage.

| Code | Severity | Description |
|---|---|---|
| QRA301 | Info | Leading wildcard LIKE -- `Contains()` translates to `LIKE '%...%'`, preventing index usage |
| QRA302 | Info | Function on column in WHERE -- `ToLower()`, `ToUpper()`, `Substring()`, etc. on a column prevents index usage |
| QRA303 | Info | OR across different columns -- `col1 == x \|\| col2 == y` prevents single-index scan |
| QRA304 | Info | WHERE on non-indexed column -- filtering on a column not covered by any declared schema index |

### Pattern Issues (QRA401--QRA402)

Rules that detect common anti-patterns in how queries are used.

| Code | Severity | Description |
|---|---|---|
| QRA401 | Warning | Query inside loop -- execution method called inside `for`/`foreach`/`while` or LINQ `.Select()`, indicating an N+1 risk |
| QRA402 | Info | Multiple queries on same table -- multiple independent queries on the same entity within one method; consider combining |

### Dialect (QRA501--QRA502)

Rules that detect dialect-specific issues or missed optimizations.

| Code | Severity | Description |
|---|---|---|
| QRA501 | Info | Dialect-specific optimization available -- e.g., PostgreSQL `ILIKE` instead of `LOWER() + LIKE`, SQLite `COLLATE NOCASE` |
| QRA502 | Warning | Suboptimal for dialect -- feature unsupported or problematic for the target dialect (e.g., SQLite `RIGHT JOIN` / `FULL OUTER JOIN`, SQL Server `OFFSET` without `ORDER BY`) |

---

## Suppressing Diagnostics

When a diagnostic is intentional or not applicable, suppress it with standard C# mechanisms.

### EditorConfig (project-wide)

Use `.editorconfig` to change severity or disable rules across the project:

```ini
# Disable leading-wildcard LIKE warnings entirely
dotnet_diagnostic.QRA301.severity = none

# Promote tautological condition from Info to Warning
dotnet_diagnostic.QRA103.severity = warning
```

The `Quarry.Analyzers` package also supports an EditorConfig option for the wide-table column threshold:

```ini
# QRA202: column threshold for wide-table detection (default: 10)
quarry_wide_table_column_threshold = 15
```

### Pragma (per-site)

Use `#pragma warning` to suppress a specific diagnostic at a single call site:

```csharp
#pragma warning disable QRA301
var results = await db.Users()
    .Where(u => u.UserName.Contains(searchTerm))
    .Select(u => u)
    .ExecuteFetchAllAsync();
#pragma warning restore QRA301
```

### SuppressMessage attribute

For method-level or class-level suppression:

```csharp
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "QuarryAnalyzer", "QRA401",
    Justification = "Batch size is bounded and intentional")]
public async Task ProcessItems(List<int> ids) { ... }
```
