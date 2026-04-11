# Plan: 188-migration-converters

## Key Concepts

**Converter Architecture** — Each source framework follows the same three-stage pipeline established by Dapper: Detector (finds call sites via Roslyn semantic model) → Converter (orchestrates translation, acts as public facade) → Emission (generates Quarry chain API code). EF Core and SqlKata are fluent-to-fluent translations (C# to C#, no SQL parsing). ADO.NET reuses the existing `SqlParser` + `ChainEmitter` path since it starts from SQL strings.

**Call Site** — A value object capturing everything detected about a single convertible invocation: the syntax node, extracted metadata (table name, method name, parameters, result type), and source location. Each framework has its own CallSite type since the captured metadata differs.

**Diagnostic ID Ranges** — QRM01x for EF Core, QRM02x for ADO.NET, QRM03x for SqlKata. Each range has three IDs: x1 (can convert), x2 (converted with warnings), x3 (cannot convert).

**EF Core Translation** — LINQ method chains on `DbSet<T>` map nearly 1:1 to Quarry chain API. Key differences: `OrderByDescending` → `OrderBy(..., Direction.Descending)`, `Take/Skip` → `Limit/Offset`, `ToListAsync` → `ExecuteFetchAllAsync`, etc. Unsupported constructs (Include, AsNoTracking, FromSqlRaw, ExecuteUpdate/Delete) emit QRM012 warnings but still convert the supported portion.

**ADO.NET Translation** — Detects `DbCommand.Execute*` patterns, tracks the `DbCommand` variable across the method body to collect `CommandText` assignments and `Parameters.Add/AddWithValue` calls. Once SQL and parameters are collected, feeds them through the same `SqlParser` → `ChainEmitter` pipeline as Dapper.

**SqlKata Translation** — Detects `new Query("table")` fluent chains validated as `SqlKata.Query` via semantic model. Maps fluent calls mechanically: `Where("col", op, val)` → `.Where(x => x.Col op val)`, `Select(cols)` → `.Select(x => new { ... })`, etc. Raw expressions (WhereRaw, SelectRaw, HavingRaw) emit warnings.

## Phases

### Phase 1: Diagnostic Descriptors

Add the nine new diagnostic descriptors to `MigrationDiagnosticDescriptors.cs` covering all three converters. This is a standalone change with no behavioral impact — just registering the descriptor metadata.

New descriptors:
- QRM011 "EF Core query can be converted to Quarry" (Info)
- QRM012 "EF Core query converted with warnings" (Warning)
- QRM013 "EF Core query cannot be converted" (Info)
- QRM021 "ADO.NET query can be converted to Quarry" (Info)
- QRM022 "ADO.NET query converted with warnings" (Warning)
- QRM023 "ADO.NET query cannot be converted" (Info)
- QRM031 "SqlKata query can be converted to Quarry" (Info)
- QRM032 "SqlKata query converted with warnings" (Warning)
- QRM033 "SqlKata query cannot be converted" (Info)

**Tests:** Verify each descriptor has correct ID, severity, and category.

---

### Phase 2: EF Core Detector and Call Site

Create `EfCoreCallSite.cs` — a value object capturing:
- `InvocationExpressionSyntax` (or `MemberAccessExpressionSyntax` for the full chain)
- `ExpressionSyntax ChainRoot` — the `DbSet<T>` access expression (e.g., `context.Users`)
- `string EntityTypeName` — the `T` from `DbSet<T>`
- `IReadOnlyList<EfCoreChainStep> Steps` — ordered list of chained method calls with their arguments
- `string TerminalMethod` — the terminal call (ToListAsync, FirstAsync, etc.)
- `Location Location`
- `IReadOnlyList<string> UnsupportedMethods` — flagged methods (Include, AsNoTracking, etc.)

Create `EfCoreChainStep` — represents one method call in the chain:
- `string MethodName` (Where, OrderBy, Select, Join, Take, Skip, GroupBy, etc.)
- `IReadOnlyList<ArgumentSyntax> Arguments` — raw syntax arguments
- `Location Location`

Create `EfCoreDetector.cs`:
- `IReadOnlyList<EfCoreCallSite> Detect(SemanticModel model, SyntaxNode root)` — scans for chains starting from `DbSet<T>` or `DbContext.Set<T>()`.
- `EfCoreCallSite? TryDetectSingle(SemanticModel model, InvocationExpressionSyntax invocation)` — for analyzer per-node use.
- Semantic validation: confirms the type is `Microsoft.EntityFrameworkCore.DbSet<T>` by checking the containing type's base class chain for `DbContext`.
- Walks the invocation chain backwards to find the root `DbSet<T>` access and collects each fluent call as a `ChainStep`.
- Classifies terminal methods: `ToListAsync/ToList` → FetchAll, `FirstAsync/First` → FetchFirst, `FirstOrDefaultAsync` → FetchFirstOrDefault, `SingleAsync/Single` → FetchSingle, `SingleOrDefaultAsync` → FetchSingleOrDefault, `CountAsync/Count/SumAsync/Sum/etc.` → aggregate.
- Flags unsupported methods: `Include`, `ThenInclude`, `AsNoTracking`, `AsTracking`, `FromSqlRaw`, `FromSqlInterpolated`, `ExecuteUpdate`, `ExecuteDelete`, `AsSplitQuery`, `AsNoTrackingWithIdentityResolution`.

**Tests:** Detection of DbSet property access, DbContext.Set<T>(), various chain lengths, parameter extraction from lambdas, unsupported method flagging, rejection of non-EF types.

---

### Phase 3: EF Core Converter, Analyzer, and Code Fix

Create `EfCoreConverter.cs` — public facade:
- `IReadOnlyList<EfCoreConversionEntry> ConvertAll(Compilation compilation)` — detects all EF Core chains, translates each to Quarry chain code.
- For each detected call site, walks the `Steps` list and builds a Quarry chain string:
  - Resolves entity type name → schema map entity (via `SchemaResolver`)
  - Emits `db.{AccessorName}()`
  - Maps each step:
    - `Where(lambda)` → `.Where(lambda)` (rewrite lambda parameter to use Quarry variable name)
    - `OrderBy(lambda)` → `.OrderBy(lambda)`
    - `OrderByDescending(lambda)` → `.OrderBy(lambda, Direction.Descending)`
    - `ThenBy(lambda)` → `.ThenBy(lambda)`
    - `ThenByDescending(lambda)` → `.ThenBy(lambda, Direction.Descending)`
    - `Select(lambda)` → `.Select(lambda)`
    - `GroupBy(lambda)` → `.GroupBy(lambda)`
    - `Take(n)` → `.Limit(n)`
    - `Skip(n)` → `.Offset(n)`
    - `Distinct()` → `.Distinct()`
    - `Join(...)` → `.Join<T>((a, b) => condition)` (rewrite LINQ Join syntax to Quarry join)
    - `Count()` → wrap in scalar: emit `.Select(x => Sql.Count()).ExecuteScalarAsync<int>()`
    - `Sum(lambda)` → similar scalar wrapping
  - Maps terminal: ToListAsync → ExecuteFetchAllAsync, FirstAsync → ExecuteFetchFirstAsync, etc.
  - For unsupported methods: skip in output, add warning diagnostic with method name.
- Returns `EfCoreConversionEntry` with: FilePath, Line, OriginalCode (chain text), ChainCode, Diagnostics, IsConvertible, HasWarnings.

Create `EfCoreMigrationAnalyzer.cs`:
- Follows same pattern as `DapperMigrationAnalyzer`.
- `RegisterCompilationStartAction`: build schema map, check for DbContext type in compilation.
- `RegisterSyntaxNodeAction` for `InvocationExpression`: detect, translate, classify → QRM011/012/013.

Create `EfCoreMigrationCodeFix.cs`:
- Fixes QRM011 and QRM012 (not QRM013).
- Replaces the full chain expression (from DbSet access through terminal) with the generated Quarry chain code.
- Adds `using Quarry;` and `using Quarry.Query;` directives.
- Preserves `await` if the original chain was awaited.

**Tests:** 
- Converter: simple query, where + orderby, select projection, take/skip, join, groupby, aggregate terminals, unsupported method warnings, schema miss.
- Analyzer: reports correct diagnostic IDs for convertible/warning/unconvertible chains.
- Code fix: replaces chain correctly, handles await, adds usings.

---

### Phase 4: ADO.NET Detector and Call Site

Create `AdoNetCallSite.cs`:
- `InvocationExpressionSyntax InvocationSyntax` — the Execute* call
- `string CommandVariableName` — the DbCommand variable name
- `string Sql` — extracted SQL from CommandText assignment
- `IReadOnlyList<string> ParameterNames` — collected parameter names
- `string MethodName` — ExecuteReader, ExecuteNonQuery, ExecuteScalar
- `string? ResultTypeName` — generic type arg if available (e.g., from a typed reader wrapper)
- `Location Location`

Create `AdoNetDetector.cs`:
- `IReadOnlyList<AdoNetCallSite> Detect(SemanticModel model, SyntaxNode root)` — scans for `Execute*` calls on `DbCommand`.
- `AdoNetCallSite? TryDetectSingle(SemanticModel model, InvocationExpressionSyntax invocation)` — per-node for analyzer.
- Semantic validation: confirms the receiver type derives from `System.Data.Common.DbCommand` or is `System.Data.SqlClient.SqlCommand`, `Microsoft.Data.SqlClient.SqlCommand`, `Npgsql.NpgsqlCommand`, `MySqlConnector.MySqlCommand`, etc.
- For each detected Execute* call:
  1. Identify the DbCommand variable from the member access receiver.
  2. Walk the enclosing method/block backwards to find `CommandText` assignments (literal string, constant, or string concatenation).
  3. Walk the enclosing method/block to collect `Parameters.Add(new SqlParameter("@name", value))` and `Parameters.AddWithValue("@name", value)` calls on the same variable.
  4. Extract parameter names (strip @/$/: prefix).
  5. If SQL cannot be extracted (dynamic construction), return null (not convertible).

**Tests:** Detection of ExecuteReader/ExecuteNonQuery/ExecuteScalar, parameter collection from Add/AddWithValue, CommandText from literal/constant/concatenation, multi-statement parameter collection, rejection of dynamic SQL.

---

### Phase 5: ADO.NET Converter, Analyzer, and Code Fix

Create `AdoNetConverter.cs` — public facade:
- `IReadOnlyList<AdoNetConversionEntry> ConvertAll(Compilation compilation, string? dialect = null)` — detects all ADO.NET call sites, translates each using `SqlParser` + `ChainEmitter` (same pipeline as Dapper).
- For each detected site: parse SQL with `SqlParser.Parse(sql, dialect)`, create `ChainEmitter`, translate, package result.
- The result types mirror Dapper: `AdoNetConversionEntry` with FilePath, Line, AdoNetMethod, OriginalSql, ChainCode, Diagnostics, IsSuggestionOnly, IsConvertible, HasWarnings.

Create `AdoNetMigrationAnalyzer.cs`:
- Same pattern as Dapper analyzer.
- Checks for DbCommand type in compilation.
- Reports QRM021/022/023.

Create `AdoNetMigrationCodeFix.cs`:
- Fixes QRM021 and QRM022 (not QRM023).
- Per decision D5: replaces only the Execute* call with the Quarry chain code.
- Adds a `// TODO: Remove DbCommand setup above — now using Quarry chain API` comment before the replacement.
- Adds using directives.
- Preserves await.

**Tests:**
- Converter: SELECT with parameters, INSERT (suggestion only), UPDATE, DELETE, dialect override.
- Analyzer: correct diagnostic IDs.
- Code fix: replaces Execute call, adds TODO comment, preserves await, adds usings.

---

### Phase 6: SqlKata Detector and Call Site

Create `SqlKataCallSite.cs`:
- `ExpressionSyntax ChainExpression` — the full fluent chain from `new Query(...)` through terminal
- `string TableName` — from `new Query("table_name")`
- `IReadOnlyList<SqlKataChainStep> Steps` — ordered fluent method calls
- `string? TerminalMethod` — optional terminal (Get, First, Paginate, etc.)
- `IReadOnlyList<string> UnsupportedMethods` — raw expressions, subqueries, CTEs
- `Location Location`

Create `SqlKataChainStep`:
- `string MethodName` (Where, OrWhere, OrderBy, OrderByDesc, Select, Join, LeftJoin, Limit, Offset, GroupBy, Having, Distinct, etc.)
- `IReadOnlyList<ArgumentSyntax> Arguments`
- `Location Location`

Create `SqlKataDetector.cs`:
- `IReadOnlyList<SqlKataCallSite> Detect(SemanticModel model, SyntaxNode root)`
- `SqlKataCallSite? TryDetectSingle(SemanticModel model, InvocationExpressionSyntax invocation)`
- Semantic validation: confirms the type is `SqlKata.Query`.
- Finds `new Query("table")` object creation expressions, then follows the fluent chain.
- Flags unsupported: `WhereRaw`, `SelectRaw`, `HavingRaw`, `OrderByRaw`, `WhereSubQuery`, `With` (CTE).

**Tests:** Detection of Query construction, chain walking, parameter extraction, unsupported flagging, rejection of non-SqlKata Query types.

---

### Phase 7: SqlKata Converter, Analyzer, and Code Fix

Create `SqlKataConverter.cs` — public facade:
- `IReadOnlyList<SqlKataConversionEntry> ConvertAll(Compilation compilation)`
- For each detected call site, resolves table name → schema entity, then maps each step:
  - `Where("col", "=", val)` → `.Where(x => x.Col == val)`
  - `Where("col", ">", val)` → `.Where(x => x.Col > val)`
  - `Where("col", val)` → `.Where(x => x.Col == val)` (default equals)
  - `OrWhere(...)` → combine with `||` in existing Where
  - `WhereNull("col")` → `.Where(x => x.Col == null)`
  - `WhereNotNull("col")` → `.Where(x => x.Col != null)`
  - `WhereIn("col", values)` → `.Where(x => values.Contains(x.Col))`
  - `WhereBetween("col", low, high)` → `.Where(x => x.Col >= low && x.Col <= high)`
  - `OrderBy("col")` → `.OrderBy(x => x.Col)`
  - `OrderByDesc("col")` → `.OrderBy(x => x.Col, Direction.Descending)`
  - `Select("col1", "col2")` → `.Select(x => new { x.Col1, x.Col2 })`
  - `Join("table", "t.col1", "o.col2")` → `.Join<T>((x, t) => x.Col1 == t.Col2)`
  - `LeftJoin(...)` → `.LeftJoin<T>(...)`
  - `RightJoin(...)` → `.RightJoin<T>(...)`
  - `CrossJoin(...)` → `.CrossJoin<T>()`
  - `Limit(n)` → `.Limit(n)`
  - `Offset(n)` → `.Offset(n)`
  - `GroupBy("col")` → `.GroupBy(x => x.Col)`
  - `Having(...)` → `.Having(x => ...)`
  - `Distinct()` → `.Distinct()`
  - `SelectRaw(...)` / `WhereRaw(...)` / `HavingRaw(...)` → warning diagnostic, skip in output
  - `AsCount()` → `.Select(x => Sql.Count()).ExecuteScalarAsync<int>()`
  - `AsSum("col")` → `.Select(x => Sql.Sum(x.Col)).ExecuteScalarAsync<int>()`
- Maps terminals: `Get()` → `ExecuteFetchAllAsync()`, `First()` → `ExecuteFetchFirstAsync()`, `Paginate(page, perPage)` → `.Offset((page-1)*perPage).Limit(perPage).ExecuteFetchAllAsync()`.

Create `SqlKataMigrationAnalyzer.cs` and `SqlKataMigrationCodeFix.cs`:
- Same pattern as EF Core. Analyzer reports QRM031/032/033.
- Code fix replaces full chain expression. Adds usings. Preserves await.

**Tests:**
- Converter: simple query, where variants, orderby, select, join, groupby, aggregate, unsupported raw expressions, schema miss.
- Analyzer: correct diagnostic IDs.
- Code fix: replaces chain, handles await, adds usings.
