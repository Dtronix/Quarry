# Plan: 185-migration-dapper

## Key Concepts

**SchemaMap** — A lookup structure built by `SchemaResolver` that maps SQL table names to Quarry entity types and SQL column names to entity property names. Built from the Roslyn semantic model by finding `Schema` subclasses and extracting their `Table` static property, `Col<T>`/`Key<T>`/`Ref<T,K>` properties, and naming conventions.

**DapperCallSite** — A model representing a detected Dapper invocation: the SQL string literal, the anonymous object parameter members (name→type), the result type `T`, the Dapper method name (QueryAsync, ExecuteAsync, etc.), and the source location.

**ChainEmitter** — The core translation engine that takes a parsed SQL AST (`SqlSelectStatement`) plus a `SchemaMap` plus a `DapperCallSite` and produces a C# source string representing the equivalent Quarry chain API call.

**ConversionResult** — The output model: the generated C# chain code, a list of diagnostics (warnings for Sql.Raw fallbacks, infos for unsupported patterns), and metadata (original SQL, confidence level).

## Algorithm Details

### SQL → Chain Translation

The translator walks the SQL AST and emits chain method calls in order:

1. **FROM** → Resolve table name via SchemaMap → `db.{EntityAccessor}()`. The accessor name is the pluralized schema class name without "Schema" suffix.
2. **JOIN** → For each join, resolve the joined table → `.Join<TEntity>((a, b) => {ON condition})` or `.LeftJoin<TEntity>(...)`. The ON condition is translated as a C# binary expression using resolved property names.
3. **WHERE** → `.Where({lambda} => {expression})`. SQL expressions are recursively translated:
   - Column refs → `{alias}.{PropertyName}` (resolved via SchemaMap)
   - Parameters → captured variable names (stripped of `@`/`$`/`:` prefix, camelCased)
   - `AND`/`OR` → `&&`/`||`
   - `IN (values)` → `new[] { values }.Contains(prop)`
   - `BETWEEN a AND b` → `prop >= a && prop <= b`
   - `IS NULL` / `IS NOT NULL` → `== null` / `!= null`
   - `LIKE` → `Sql.Like(prop, pattern)`
   - Unmappable expressions → `Sql.Raw<bool>("...")`
4. **GROUP BY** → `.GroupBy({lambda} => {column})` or `.GroupBy({lambda} => ({col1}, {col2}))` for multi-column.
5. **HAVING** → `.Having({lambda} => {expression})`. Aggregate functions in the expression translated as `Sql.Count()`, `Sql.Sum(prop)`, etc.
6. **SELECT** → `.Select({lambda} => {projection})`:
   - `SELECT *` → `.Select(u => u)`
   - `SELECT col1, col2` → `.Select(u => (u.Prop1, u.Prop2))`
   - `SELECT COUNT(*)` → `.Select(u => Sql.Count())`
   - Mixed columns+aggregates → tuple with both
7. **ORDER BY** → `.OrderBy({lambda} => {col})` then `.ThenBy(...)` for additional sorts. Descending uses `Direction.Descending`.
8. **LIMIT/OFFSET** → `.Limit(n)` / `.Offset(n)`
9. **Terminal** → Mapped from Dapper method:
   - `QueryAsync<T>` → `.ExecuteFetchAllAsync()`
   - `QueryFirstAsync<T>` → `.ExecuteFetchFirstAsync()`
   - `QueryFirstOrDefaultAsync<T>` → `.ExecuteFetchFirstOrDefaultAsync()`
   - `QuerySingleAsync<T>` → `.ExecuteFetchSingleAsync()`
   - `ExecuteAsync` → `.ExecuteNonQueryAsync()`
   - `ExecuteScalarAsync<T>` → `.ExecuteScalarAsync<T>()`

### Lambda Variable Naming

Single table: first letter lowercase of entity name (`Users` → `u`, `Orders` → `o`).
Multi-table joins: stack variables `(u, o) =>`, `(u, o, oi) =>`. If names collide, append a digit.

### Parameter Resolution

Dapper's `new { userId, name }` produces SQL parameters `@userId`, `@name`. The translator:
1. Strips the prefix (`@`, `:`, `$`) from SQL parameter references.
2. Matches against the Dapper anonymous object member names.
3. Uses the original C# variable name as a captured variable in the Quarry lambda.
4. If no match found, keeps the parameter name and emits a diagnostic.

---

## Phases

### Phase 1: Project Scaffolding
Create all three new projects and wire them into the solution.

- Create `src/Quarry.Migration/Quarry.Migration.csproj` targeting `netstandard2.0` with `Microsoft.CodeAnalysis.CSharp` 5.0.0, importing `Quarry.Shared.projitems` (include only `Sql/` folder — exclude `Migration/` and `Scaffold/`). Mark as `IsRoslynComponent`, `EnforceExtendedAnalyzerRules`.
- Create `src/Quarry.Migration.Analyzers/Quarry.Migration.Analyzers.csproj` targeting `netstandard2.0` with Roslyn packages, `ProjectReference` to `Quarry.Migration`. Mark as `IsRoslynComponent`.
- Create `src/Quarry.Migration.Tests/Quarry.Migration.Tests.csproj` targeting `net10.0` with NUnit, Microsoft.NET.Test.Sdk, `ProjectReference` to both `Quarry.Migration` and `Quarry.Migration.Analyzers`, and to `Quarry` (for entity type references in tests).
- Add all three to `Quarry.sln` under the "src" folder.
- Add `InternalsVisibleTo` from `Quarry.Migration` to `Quarry.Migration.Tests` and `Quarry.Migration.Analyzers`.
- Create minimal placeholder classes so each project compiles.
- Verify: `dotnet build` succeeds, `dotnet test` baseline unchanged.

**Tests:** None yet — just verify build.

### Phase 2: SchemaResolver
Build the component that reads a Roslyn `Compilation` and produces a `SchemaMap`.

- Create `SchemaMap` record: dictionary of SQL table name (case-insensitive) → `EntityMapping` (entity type name, accessor method name, column map: SQL column name → C# property name).
- Create `SchemaResolver` class with method `Resolve(Compilation compilation) → SchemaMap`:
  - Find all types inheriting from `Quarry.Schema` in the compilation.
  - For each, read the static `Table` property value (string literal from syntax).
  - Read all public instance properties that return `Col<T>`, `Key<T>`, `Ref<T,K>`.
  - Map property name → column name (applying NamingStyle if present, otherwise property name = column name).
  - Derive accessor method name: strip "Schema" suffix if present, ensure plural form.
- Handle edge cases: missing Table property, custom column names via `MapTo`, schema-qualified table names.

**Tests:** Create test compilation with sample Schema classes. Verify SchemaMap produces correct table→entity and column→property mappings. Test naming convention handling.

### Phase 3: DapperDetector
Build the component that finds Dapper call sites in a Roslyn compilation.

- Create `DapperCallSite` model: SQL string, parameter names (from anonymous object), result type name, Dapper method name (QueryAsync/ExecuteAsync/etc.), source location (file, span), connection expression syntax.
- Create `DapperDetector` class with method `Detect(SemanticModel model, SyntaxNode root) → IReadOnlyList<DapperCallSite>`:
  - Walk `InvocationExpressionSyntax` nodes.
  - Check if method is from Dapper namespace (`Dapper.SqlMapper` or extension methods on `IDbConnection`).
  - Extract the SQL string argument (string literal, interpolated string, or const field reference).
  - Extract the anonymous object argument: each member name and type.
  - Extract the generic type argument `T` (if present).
  - Record the Dapper method name for terminal mapping.
- Handle: string concatenation in SQL (emit diagnostic, try to resolve if all parts are literals), non-literal SQL (emit diagnostic, skip), missing parameter object.

**Tests:** Create test compilations with Dapper calls. Verify detection of all 6 Dapper patterns. Test parameter extraction from anonymous objects. Test non-literal SQL diagnostics.

### Phase 4: SqlTranslator — Core (FROM, SELECT, WHERE)
Build the chain emitter for single-table queries.

- Create `TranslationContext` holding: SchemaMap, DapperCallSite, list of diagnostics, current lambda variable names.
- Create `ChainEmitter` class with `Translate(SqlParseResult parseResult, SchemaMap schema, DapperCallSite callSite) → ConversionResult`:
  - If parse result is unsupported → return diagnostic, no conversion.
  - Resolve FROM table → entity accessor. If table not in SchemaMap → Sql.Raw fallback for entire query.
  - Emit `db.{Accessor}()`.
- Implement `EmitWhere(SqlExpr where)`: recursively translate SQL expression tree to C# lambda body string.
  - `SqlBinaryExpr` → `{left} {op} {right}` with operator mapping (=→==, <>→!=, AND→&&, OR→||).
  - `SqlColumnRef` → `{alias}.{ResolvedPropertyName}`.
  - `SqlParameter` → matched captured variable name.
  - `SqlLiteral` → C# literal (strings quoted, numbers as-is, booleans, null).
  - `SqlParenExpr` → `({inner})`.
  - `SqlUnaryExpr` → NOT → `!{operand}`.
- Implement `EmitSelect(IReadOnlyList<SqlNode> columns)`:
  - Star → `{v} => {v}` (entity projection).
  - Single column → `{v} => {v}.{Prop}`.
  - Multiple columns → `{v} => ({v}.{Prop1}, {v}.{Prop2})` (tuple projection).
- Append terminal method based on Dapper method name.
- Combine into full chain string.

**Tests:** Parameterized `(inputSql, dialect, expectedChainCode)` tests for:
- `SELECT * FROM users` → `db.Users().Select(u => u).ExecuteFetchAllAsync()`
- `SELECT user_id, user_name FROM users` → tuple projection
- `SELECT * FROM users WHERE is_active = 1` → `.Where(u => u.IsActive == 1)`
- `SELECT * FROM users WHERE user_id = @id AND name = @name` → parameterized WHERE
- `SELECT * FROM users WHERE user_id > @minId ORDER BY user_name` → (just WHERE portion here, ORDER BY in phase 5)

### Phase 5: SqlTranslator — JOINs, Aggregates, ORDER BY, LIMIT/OFFSET
Extend the translator for multi-table and grouped queries.

- **JOINs:** For each `SqlJoin` in the AST:
  - Resolve joined table via SchemaMap.
  - Map `SqlJoinKind.Inner` → `.Join<T>()`, `SqlJoinKind.Left` → `.LeftJoin<T>()`.
  - Translate ON condition as a multi-parameter lambda: `(u, o) => {condition}`.
  - Update lambda variable list for subsequent clauses (WHERE, SELECT, etc. now use multi-parameter lambdas).
- **GROUP BY:** Emit `.GroupBy({lambda} => {expr})`. Multi-column → tuple. After GROUP BY, SELECT/HAVING expressions may reference aggregate functions.
- **HAVING:** Emit `.Having({lambda} => {expr})`. Recognize aggregate function calls in expressions.
- **Aggregate functions:** Map SQL function names:
  - `COUNT(*)` → `Sql.Count()`
  - `COUNT(col)` → `Sql.Count({lambda}.{Prop})`
  - `SUM(col)` → `Sql.Sum({lambda}.{Prop})`
  - `AVG(col)` → `Sql.Avg({lambda}.{Prop})`
  - `MIN(col)` → `Sql.Min({lambda}.{Prop})`
  - `MAX(col)` → `Sql.Max({lambda}.{Prop})`
- **ORDER BY:** First term → `.OrderBy({lambda} => {col})` with optional `Direction.Descending`. Subsequent terms → `.ThenBy(...)`.
- **LIMIT/OFFSET:** `.Limit({n})` / `.Offset({n})`. If the value is a parameter, use the captured variable.

**Tests:** Parameterized tests for:
- `SELECT u.name, o.total FROM users u INNER JOIN orders o ON u.user_id = o.user_id`
- `SELECT u.name, o.total FROM users u LEFT JOIN orders o ON u.user_id = o.user_id`
- `SELECT dept, COUNT(*), SUM(salary) FROM employees GROUP BY dept`
- `SELECT dept, AVG(salary) FROM employees GROUP BY dept HAVING AVG(salary) > 50000`
- `SELECT * FROM users ORDER BY name ASC, created_at DESC`
- `SELECT * FROM users LIMIT 10 OFFSET 20`
- Joined query with WHERE, ORDER BY, and LIMIT combined.

### Phase 6: SqlTranslator — Parameters and Edge Cases
Handle special SQL constructs and the Sql.Raw fallback.

- **IN expression:** `col IN (1, 2, 3)` → `new[] { 1, 2, 3 }.Contains({v}.{Prop})`. `col IN (@a, @b)` → `new[] { a, b }.Contains({v}.{Prop})`.
- **BETWEEN:** `col BETWEEN @low AND @high` → `{v}.{Prop} >= low && {v}.{Prop} <= high`. `NOT BETWEEN` → `<` and `>`.
- **IS NULL / IS NOT NULL:** `col IS NULL` → `{v}.{Prop} == null`. `col IS NOT NULL` → `{v}.{Prop} != null`.
- **LIKE:** `col LIKE @pattern` → `Sql.Like({v}.{Prop}, pattern)`. `NOT LIKE` → `!Sql.Like(...)`.
- **Sql.Raw fallback:** Any expression the translator cannot map (e.g., unknown functions, CAST, CASE, complex subqueries) → `Sql.Raw<T>("{original SQL fragment}")`. Emit a QRM diagnostic warning for each Raw usage.
- **Unmappable table:** If FROM table not in SchemaMap → emit entire query as `db.RawSqlAsync<T>("{original SQL}")` with diagnostic.
- **Parameter type inference:** Where possible, infer the C# type from the Dapper anonymous object member type for Sql.Raw type parameters.
- **Negated expressions:** Handle `NOT (expr)` → `!(expr)`.

**Tests:** Parameterized tests for:
- `SELECT * FROM users WHERE id IN (1, 2, 3)`
- `SELECT * FROM users WHERE id IN (@a, @b, @c)`
- `SELECT * FROM users WHERE salary BETWEEN @min AND @max`
- `SELECT * FROM users WHERE email IS NULL`
- `SELECT * FROM users WHERE name LIKE @pattern`
- `SELECT * FROM users WHERE name NOT LIKE '%test%'`
- SQL with CASE expression (should produce Sql.Raw fallback)
- SQL with unknown function (should produce Sql.Raw fallback)
- SQL referencing table not in SchemaMap (should produce RawSqlAsync fallback)

### Phase 7: Roslyn Analyzer and Code Fix
Build the QRM001 diagnostic and code fix provider.

- Create `MigrationDiagnosticDescriptors` in `Quarry.Migration` with:
  - `QRM001`: "Dapper call can be converted to Quarry" (Info severity, category "Migration")
  - `QRM002`: "Dapper call uses Sql.Raw fallback for some expressions" (Warning severity)
  - `QRM003`: "Dapper call cannot be converted — SQL is not a string literal" (Info severity)
- Create `DapperMigrationAnalyzer` (`DiagnosticAnalyzer`) in `Quarry.Migration.Analyzers`:
  - `RegisterCompilationStartAction` → build SchemaMap from compilation.
  - `RegisterSyntaxNodeAction(InvocationExpression)` → run DapperDetector on each invocation.
  - Report QRM001 for convertible calls, QRM003 for non-convertible.
- Create `DapperMigrationCodeFix` (`CodeFixProvider`) in `Quarry.Migration.Analyzers`:
  - Fixes QRM001.
  - On trigger: parse SQL, translate via ChainEmitter, replace the Dapper invocation syntax with the Quarry chain code.
  - Add necessary `using` directives if missing.
  - `GetFixAllProvider()` → `WellKnownFixAllProviders.BatchFixer`.

**Tests:** 
- Analyzer test: verify QRM001 reported on Dapper QueryAsync call.
- Analyzer test: verify QRM003 on non-literal SQL.
- Code fix test: verify Dapper call replaced with correct Quarry chain.
- Code fix test: verify using directives added.

### Phase 8: CLI Convert Command
Add `quarry convert` command to Quarry.Tool.

- Add `ConvertCommand` class in `Quarry.Tool`:
  - `RunAsync(projectPath, dialect, fromLibrary)` — loads the project via MSBuild workspace, gets compilation, runs SchemaResolver + DapperDetector + ChainEmitter for each detected call site.
  - Outputs a report: file, line, original Dapper code, converted Quarry code, diagnostics.
  - Options: `--from dapper` (required, only Dapper for now), `--dry-run` (default: show report without modifying files), `--apply` (modify source files in place).
  - `--dialect` defaults to auto-detect from the connection type in code.
- Wire into `Program.cs` dispatch: `case "convert":` → `ConvertCommand.RunAsync(...)`.
- Update the help/usage text.

**Tests:** Add a test in `Quarry.Migration.Tests` that exercises ConvertCommand programmatically (create a temp project with a Dapper call, run conversion, verify output). Or: test the orchestration logic separately from the MSBuild workspace loading (unit test the report generation given mock inputs).
