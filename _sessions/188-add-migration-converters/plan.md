# Implementation Plan: #188 — EF Core, ADO.NET, and SqlKata Converters

## Key Concepts

**ISqlCallSite**: A small internal interface extracting the three properties ChainEmitter needs from a call site — `Sql`, `ParameterNames`, and `MethodName`. Allows ChainEmitter to serve both Dapper and ADO.NET without knowing the source framework.

**Converter pipeline**: Each converter follows the same pipeline: Detector (finds call sites in Roslyn syntax trees) → Emitter (translates call site to Quarry chain code) → Facade (public API orchestrating the pipeline) → Analyzer (Roslyn diagnostic reporter) → CodeFix (IDE auto-fix provider).

**EfCoreEmitter**: Translates EF Core LINQ chains (C# expression trees) directly to Quarry chain code. No SQL parsing — it walks the method chain nodes syntactically: `.Where(u => u.X == v)` becomes `.Where(u => u.X == v)`, `.ToListAsync()` becomes `.ExecuteFetchAllAsync()`, etc. The lambda bodies are preserved as-is since Quarry uses the same expression pattern.

**AdoNetDetector**: Scans a method body for DbCommand usage patterns. Finds `ExecuteReader`/`ExecuteNonQuery`/`ExecuteScalar` calls, then traces backward through the method to find `CommandText` assignments and `Parameters.Add`/`AddWithValue` calls on the same variable. Produces an `AdoNetCallSite` implementing `ISqlCallSite`.

**SqlKataEmitter**: Translates SqlKata's string-based fluent API to Quarry's lambda-based chain API. The key transformation is converting string column references (e.g., `"name"`) to lambda property access (e.g., `u => u.Name`) via schema resolution with PascalCase fallback.

**Diagnostic IDs**: Each converter gets three IDs at 100-offset intervals: `QRMx01` (convertible), `QRMx02` (with warnings), `QRMx03` (not convertible). Dapper=001-003, EF Core=101-103, ADO.NET=201-203, SqlKata=301-303.

## Phase 1: ISqlCallSite Interface Extraction

Extract the interface that ChainEmitter needs from call sites, and make `DapperCallSite` implement it. This is a refactoring-only phase — no new functionality.

**Files modified:**
- `ISqlCallSite.cs` (new) — Interface with `Sql`, `ParameterNames`, `MethodName` properties
- `DapperCallSite.cs` — Add `: ISqlCallSite` to class declaration
- `ChainEmitter.cs` — Change `Translate(SqlParseResult, DapperCallSite)` to `Translate(SqlParseResult, ISqlCallSite)`. Change `DapperCallSite?` parameter type in `EmitExpression` and `ResolveParameter` to `ISqlCallSite?`.

**Tests:** Run existing tests — all must still pass. No new tests needed (pure refactor).

## Phase 2: EF Core Detector

Detect EF Core LINQ chains in user code. The detector finds terminal LINQ calls (`.ToListAsync()`, `.FirstOrDefaultAsync()`, etc.) on expressions rooted in `DbSet<T>` property access.

**Files created:**
- `EfCoreCallSite.cs` — DTO capturing: entity type name, the full LINQ chain syntax, terminal method name, location, invocation syntax. Also stores intermediate LINQ method calls parsed from the chain (list of method name + arguments pairs).
- `EfCoreDetector.cs` — Walks invocation expressions. For each, checks if it's a terminal LINQ method (ToListAsync, FirstOrDefaultAsync, FirstAsync, SingleAsync, SingleOrDefaultAsync, CountAsync, SumAsync, AnyAsync, ToList, First, FirstOrDefault, Single, SingleOrDefault, Count, Sum, Any). Then walks the chain backward to find the root: a `DbSet<T>` property access on a `DbContext`-derived type (verified via semantic model). Collects all intermediate LINQ calls in order. Extracts the entity type `T` from `DbSet<T>`.

**Detection strategy:** Start from terminal methods, walk the expression chain upward through `MemberAccessExpressionSyntax` → `InvocationExpressionSyntax` pairs until reaching a property access that returns `DbSet<T>`. Verify the property's containing type derives from `DbContext` via the semantic model.

**Intermediate chain parsing:** For each LINQ method in the chain, record the method name and its arguments as syntax nodes (to be translated in Phase 3). The supported methods: `Where`, `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`, `Select`, `GroupBy`, `Join`, `Take`, `Skip`, `Count`, `Sum`, `Average`, `Min`, `Max`, `Any`, `Distinct`.

**Flagged methods** (detected but marked for manual conversion): `Include`, `ThenInclude`, `AsNoTracking`, `AsTracking`, `FromSqlRaw`, `FromSqlInterpolated`, `ExecuteUpdate`, `ExecuteDelete`, `AsSplitQuery`, `AsNoTrackingWithIdentityResolution`.

**Tests:**
- `EfCoreDetectorTests.cs` — Tests with stub DbContext/DbSet types. Verify detection of simple chains (`.Where().ToListAsync()`), multi-method chains, various terminals, flagged methods, and non-EF-Core LINQ chains (should not detect).

## Phase 3: EF Core Emitter + Facade + Analyzer + CodeFix

Translate detected EF Core chains to Quarry chain code.

**Files created:**
- `EfCoreEmitter.cs` — Takes an `EfCoreCallSite` and `SchemaMap`. Walks the parsed LINQ chain and builds the Quarry chain string:
  - Root: `DbSet<T>` property → `db.{AccessorName}()` (resolve entity type `T` to schema via class name matching to EntityMapping)
  - `Where(lambda)` → `.Where(lambda)` — preserve lambda body text as-is (Quarry uses same expression pattern)
  - `OrderBy(lambda)` → `.OrderBy(lambda)`
  - `OrderByDescending(lambda)` → `.OrderBy(lambda, Direction.Descending)`
  - `ThenBy(lambda)` → `.ThenBy(lambda)`
  - `ThenByDescending(lambda)` → `.ThenBy(lambda, Direction.Descending)`
  - `Select(lambda)` → `.Select(lambda)`
  - `GroupBy(lambda)` → `.GroupBy(lambda)`
  - `Join(...)` → Complex: extract outer key, inner key, result selectors. Emit `.Join<TInner>(lambda => condition)`. May need Sql.Raw fallback for complex join result selectors.
  - `Take(n)` → `.Limit(n)`
  - `Skip(n)` → `.Offset(n)`
  - `Distinct()` → `.Distinct()`
  - Terminal mapping: `ToListAsync()` → `ExecuteFetchAllAsync()`, `FirstAsync()` → `ExecuteFetchFirstAsync()`, `FirstOrDefaultAsync()` → `ExecuteFetchFirstOrDefaultAsync()`, `SingleAsync()` → `ExecuteFetchSingleAsync()`, `SingleOrDefaultAsync()` → `ExecuteFetchSingleOrDefaultAsync()`, `CountAsync()` → `ExecuteScalarAsync()` (with `Select(Sql.Count())`), `SumAsync(lambda)` → similar scalar pattern, `AnyAsync()` → `ExecuteScalarAsync()` with `Select(Sql.Count())` + comparison. Sync variants map the same way.
  - Flagged methods produce a warning diagnostic and return `IsSuggestionOnly = true` with a comment.

- `EfCoreConverter.cs` — Public facade. `ConvertAll(Compilation)` → orchestrates detector + emitter + schema resolver. Returns `IReadOnlyList<EfCoreConversionEntry>`.
- `EfCoreConversionEntry.cs` — Public DTO (same shape as `DapperConversionEntry`: FilePath, Line, SourceMethod, ResultType, OriginalCode, ChainCode, Diagnostics, IsSuggestionOnly, IsConvertible, HasWarnings).
- `EfCoreMigrationAnalyzer.cs` — Roslyn analyzer. Checks for `DbContext` type in compilation, registers syntax node action for `InvocationExpression`. Uses `EfCoreDetector.TryDetectSingle()`. Reports QRM101/102/103.
- `EfCoreMigrationCodeFix.cs` — Fixes QRM101 and QRM102. Replaces the entire LINQ chain (from DbSet property access through terminal) with the Quarry chain expression. Adds `using Quarry;` and `using Quarry.Query;`.
- Add QRM101-103 to `MigrationDiagnosticDescriptors.cs`.

**Tests:**
- `EfCoreConverterTests.cs` — End-to-end tests with stub DbContext. Test: simple Where+ToList, OrderBy+ThenBy, Select projection, GroupBy, Take/Skip, multiple chained methods, Include flagging, FromSqlRaw flagging, Count/Sum aggregates.
- `EfCoreEmitterTests.cs` — Unit tests for individual LINQ method translations.

## Phase 4: ADO.NET Detector

Detect raw ADO.NET patterns (DbCommand + Execute*) within method bodies.

**Files created:**
- `AdoNetCallSite.cs` — Implements `ISqlCallSite`. Stores: SQL string (from CommandText), parameter names (from Parameters.Add/AddWithValue), execute method name, location, invocation syntax, source variable name.
- `AdoNetDetector.cs` — Two-pass detection within a method body:
  1. **Find Execute calls:** Walk invocations looking for `ExecuteReader`/`ExecuteReaderAsync`/`ExecuteNonQuery`/`ExecuteNonQueryAsync`/`ExecuteScalar`/`ExecuteScalarAsync` on types deriving from `System.Data.Common.DbCommand` or implementing `System.Data.IDbCommand` (verified via semantic model).
  2. **Trace backward:** For each Execute call, identify the DbCommand variable (from `memberAccess.Expression`). Scan the containing method body for:
     - `cmd.CommandText = "..."` assignment — extract the SQL string (literal, constant, or concatenation, same strategy as DapperDetector.ExtractSqlString)
     - `cmd.Parameters.Add(new SqlParameter("@name", value))` / `cmd.Parameters.AddWithValue("@name", value)` — extract parameter names (strip @/$/: prefix)
  3. Construct `AdoNetCallSite` with collected data. Map Execute method: `ExecuteReader` → "Query" (for ChainEmitter terminal mapping), `ExecuteNonQuery` → "Execute", `ExecuteScalar` → "ExecuteScalar".

**Tests:**
- `AdoNetDetectorTests.cs` — Tests with stub DbCommand/DbConnection types. Verify: simple inline pattern, parameter collection, constant CommandText, multiple commands in same method (should detect each independently), Execute call without CommandText (should not detect).

## Phase 5: ADO.NET Emitter (reuse ChainEmitter) + Facade + Analyzer + CodeFix

Since ADO.NET uses raw SQL, it reuses ChainEmitter via the ISqlCallSite interface.

**Files created:**
- `AdoNetConverter.cs` — Public facade. `ConvertAll(Compilation, string? dialect)` → for each syntax tree, runs `AdoNetDetector`, then for each call site: `SqlParser.Parse(site.Sql, dialect)` → `ChainEmitter(schemaMap).Translate(parseResult, site)`. Returns `IReadOnlyList<AdoNetConversionEntry>`.
- `AdoNetConversionEntry.cs` — Public DTO (same shape as Dapper's).
- `AdoNetMigrationAnalyzer.cs` — Checks for `DbCommand`/`IDbCommand` type in compilation. Reports QRM201/202/203.
- `AdoNetMigrationCodeFix.cs` — Fixes QRM201 and QRM202. The code fix replaces the entire command block (CommandText assignment through Execute call) with the Quarry chain expression. This is more complex than Dapper's single-invocation replacement — needs to identify the statement range to replace. For the initial version, the code fix replaces only the Execute call and adds a comment indicating the CommandText/Parameters lines can be removed.
- Add QRM201-203 to `MigrationDiagnosticDescriptors.cs`.

**Tests:**
- `AdoNetConverterTests.cs` — End-to-end tests. Test: SELECT with parameters, INSERT (suggestion only), UPDATE/DELETE, ExecuteScalar, multiple commands in one method.

## Phase 6: SqlKata Detector

Detect SqlKata fluent query chains.

**Files created:**
- `SqlKataCallSite.cs` — DTO capturing: table name (from `new Query("table")`), the full fluent chain syntax, terminal method name, location, invocation syntax. Also stores parsed method chain (list of method name + string arguments).
- `SqlKataDetector.cs` — Walks invocation expressions looking for terminal methods (`.Get()`, `.GetAsync()`, `.First()`, `.FirstAsync()`, `.FirstOrDefault()`, `.FirstOrDefaultAsync()`, `.Paginate()`, `.Count()`, `.Sum()`, `.Avg()`, `.Min()`, `.Max()`). Walks backward through the chain to find the root: `new Query("tableName")` — an `ObjectCreationExpressionSyntax` whose type is `SqlKata.Query` (verified via semantic model). Collects intermediate fluent calls.

**Supported fluent methods:** `Where`, `WhereNot`, `OrWhere`, `WhereNull`, `WhereNotNull`, `WhereIn`, `WhereNotIn`, `WhereBetween`, `WhereLike`, `OrderBy`, `OrderByDesc`, `Select`, `Join`, `LeftJoin`, `RightJoin`, `CrossJoin`, `Limit`, `Offset`, `GroupBy`, `Having`, `HavingRaw`, `Distinct`, `SelectRaw`, `WhereRaw`, `OrderByRaw`.

**Flagged methods** (detected but manual): `SelectRaw`, `WhereRaw`, `OrderByRaw`, `HavingRaw`, `When`, `WhenNot`, subquery patterns.

**Tests:**
- `SqlKataDetectorTests.cs` — Tests with stub SqlKata.Query type. Verify: simple chain, multi-method chain, terminal detection, non-SqlKata chains (should not detect).

## Phase 7: SqlKata Emitter + Facade + Analyzer + CodeFix

Translate SqlKata fluent chains to Quarry chain code.

**Files created:**
- `SqlKataEmitter.cs` — Takes `SqlKataCallSite` and `SchemaMap`. The key challenge is converting string column references to lambda expressions via schema resolution.
  - Root: `new Query("tableName")` → `db.{AccessorName}()` via schema table name lookup
  - Lambda variable: derived same way as ChainEmitter (first letter of AccessorName, lowercase)
  - `Where("col", value)` → `.Where(u => u.{Property} == value)` — resolve "col" via schema, emit value from argument syntax
  - `Where("col", ">", value)` → `.Where(u => u.{Property} > value)` — three-arg Where with operator
  - `WhereNull("col")` → `.Where(u => u.{Property} == null)`
  - `WhereNotNull("col")` → `.Where(u => u.{Property} != null)`
  - `WhereIn("col", values)` → `.Where(u => values.Contains(u.{Property}))`
  - `WhereBetween("col", low, high)` → `.Where(u => u.{Property} >= low && u.{Property} <= high)`
  - `WhereLike("col", pattern)` → `.Where(u => Sql.Like(u.{Property}, pattern))`
  - `OrWhere(...)` → warning diagnostic + Sql.Raw fallback (Quarry doesn't have OrWhere — needs restructuring)
  - `OrderBy("col")` → `.OrderBy(u => u.{Property})`
  - `OrderByDesc("col")` → `.OrderBy(u => u.{Property}, Direction.Descending)`
  - `Select("col1", "col2")` → `.Select(u => (u.{Prop1}, u.{Prop2}))`
  - `Join("table", "t.col1", "u.col2")` → `.Join<TSchema>((u, t) => t.{Prop1} == u.{Prop2})`
  - `LeftJoin(...)` / `RightJoin(...)` / `CrossJoin(...)` → corresponding Quarry join methods
  - `Limit(n)` → `.Limit(n)`
  - `Offset(n)` → `.Offset(n)`
  - `GroupBy("col")` → `.GroupBy(u => u.{Property})`
  - `Having(...)` → `.Having(u => ...)` with expression translation
  - `Distinct()` → `.Distinct()`
  - Terminals: `Get/GetAsync` → `ExecuteFetchAllAsync()`, `First/FirstAsync` → `ExecuteFetchFirstAsync()`, `FirstOrDefault/FirstOrDefaultAsync` → `ExecuteFetchFirstOrDefaultAsync()`, `Count` → scalar with Count, `Sum("col")` → scalar with Sum, etc.
  - Raw methods → warning diagnostic, IsSuggestionOnly=true with comment

- `SqlKataConverter.cs` — Public facade.
- `SqlKataConversionEntry.cs` — Public DTO.
- `SqlKataMigrationAnalyzer.cs` — Checks for `SqlKata.Query` type. Reports QRM301/302/303.
- `SqlKataMigrationCodeFix.cs` — Fixes QRM301 and QRM302. Replaces the full SqlKata chain with Quarry chain. Adds using directives.
- Add QRM301-303 to `MigrationDiagnosticDescriptors.cs`.

**Tests:**
- `SqlKataConverterTests.cs` — End-to-end tests. Test: simple Where+Get, OrderBy, Select multi-column, Join, Limit/Offset, GroupBy, WhereNull, WhereIn, WhereLike, Raw method flagging, OrWhere fallback.
- `SqlKataEmitterTests.cs` — Unit tests for individual method translations.

## Phase Dependencies

```
Phase 1 (ISqlCallSite) ← Phase 4 (ADO.NET Detector) ← Phase 5 (ADO.NET full)
Phase 2 (EF Core Detector) ← Phase 3 (EF Core full)
Phase 6 (SqlKata Detector) ← Phase 7 (SqlKata full)
Phases 2, 4, 6 can proceed in any order after Phase 1.
```

## Summary

| Phase | Description | New Files | Modified Files |
|-------|-------------|-----------|---------------|
| 1 | ISqlCallSite extraction | 1 | 2 |
| 2 | EF Core Detector | 2 + 1 test | 0 |
| 3 | EF Core Emitter + Facade + Analyzer + CodeFix | 5 + 2 tests | 1 (descriptors) |
| 4 | ADO.NET Detector | 2 + 1 test | 0 |
| 5 | ADO.NET full pipeline | 4 + 1 test | 1 (descriptors) |
| 6 | SqlKata Detector | 2 + 1 test | 0 |
| 7 | SqlKata Emitter + Facade + Analyzer + CodeFix | 5 + 2 tests | 1 (descriptors) |
