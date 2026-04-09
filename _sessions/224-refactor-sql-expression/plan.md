# Plan: Refactor SqlExpression to dialect-agnostic representation

## Key Concepts

**Canonical format**: Column identifiers inside `SqlExpression` are stored as `{identifier}` placeholders (curly braces) instead of dialect-specific quoted forms. Example: `SUM({Total})` instead of `SUM("Total")`.

**Render-time quoting**: A new `SqlFormatting.QuoteSqlExpression(string?, SqlDialect)` method scans for `{...}` placeholders and replaces each with `QuoteIdentifier(dialect, identifier)`. This is called at every site that renders SqlExpression to final SQL.

**No-op safety**: `QuoteSqlExpression` is a no-op on strings that contain no `{...}` placeholders. This allows Phase 1 to add it to render sites before the production side switches to canonical format.

## Phases

### Phase 1: Add `QuoteSqlExpression` and wire up render sites

**Goal**: Add the new method and install it at all render sites. All tests still pass because `QuoteSqlExpression` is a no-op on existing expressions (no `{...}` tokens).

**Changes**:

1. **`src/Quarry.Shared/Sql/SqlFormatting.cs`** — Add new public method:
   ```csharp
   public static string? QuoteSqlExpression(string? sqlExpression, SqlDialect dialect)
   ```
   Algorithm: scan for `{`, extract identifier up to `}`, replace with `QuoteIdentifier(dialect, identifier)`. Pass through non-brace content verbatim. Return null if input is null.

2. **`src/Quarry.Generator/IR/SqlAssembler.cs`** — In `AppendSelectColumns`, where `col.SqlExpression` is appended verbatim (line ~626), wrap with `SqlFormatting.QuoteSqlExpression(col.SqlExpression, dialect)`.

3. **`src/Quarry.Generator/Projection/ReaderCodeGenerator.cs`** — Two sites:
   - `GenerateColumnList` (line ~38): wrap `column.SqlExpression` with `SqlFormatting.QuoteSqlExpression(column.SqlExpression, dialect)`. Add `dialect` parameter to `GenerateColumnList` (currently missing — it uses a local `QuoteIdentifier` for aliases). Actually, `GenerateColumnList` already takes `SqlDialect dialect` as a parameter (line 22). So just call `SqlFormatting.QuoteSqlExpression`.
   - `GenerateColumnNamesArray` (line ~328): wrap `column.SqlExpression` with `SqlFormatting.QuoteSqlExpression(column.SqlExpression!, dialect)` before escaping.

4. **`src/Quarry.Tests/DialectTests.cs`** — Add tests for `QuoteSqlExpression`:
   - Null input returns null
   - No placeholders → passthrough (e.g., `SUM("Total")` → `SUM("Total")`)
   - Single placeholder: `SUM({Total})` → `SUM("Total")` / `SUM([Total])` / `` SUM(`Total`) ``
   - Multiple placeholders: `{t0}.{Amount}` → `"t0"."Amount"` etc.
   - Mixed content: `COUNT(*)` → `COUNT(*)` (no placeholders)
   - OVER clause: `SUM({Total}) OVER (ORDER BY {Date})` resolved correctly

**Tests**: All 3062 existing tests pass (no-op). New unit tests for `QuoteSqlExpression` pass.

---

### Phase 2: Switch production to canonical format

**Goal**: Change `ProjectionAnalyzer` to produce `{identifier}` placeholders. Remove `ReQuoteSqlExpression` enrichment. Now the render-site `QuoteSqlExpression` calls become active.

**Changes**:

1. **`src/Quarry.Generator/Projection/ProjectionAnalyzer.cs`** — Change local `QuoteIdentifier` (line ~2435) to produce `{identifier}` instead of dialect-specific quoting:
   ```csharp
   private static string WrapIdentifier(string identifier) => $"{{{identifier}}}";
   ```
   Update all 5 call sites of the old `QuoteIdentifier`:
   - Line 1877: `GetColumnSql` → `WrapIdentifier(column.ColumnName)`
   - Line 1884: `GetColumnSql` fallback → `WrapIdentifier(propertyName)`
   - Line 1972: `GetJoinedColumnSql` → `$"{WrapIdentifier(info.Alias)}.{WrapIdentifier(column.ColumnName)}"`
   - Line 1975: `GetJoinedColumnSql` fallback → same pattern
   - Line 2568: `ResolveColumnSqlFromExpression` → `WrapIdentifier(column.ColumnName)`

2. **`src/Quarry.Generator/Parsing/ChainAnalyzer.cs`** — Remove the `ReQuoteSqlExpression` call and associated `ProjectedColumn` rebuild in `BuildProjection` (lines ~1814-1826). Just use `rawCol` directly (or a simple passthrough assignment).

**Tests**: All 3062 existing tests pass. The final SQL output is identical because `QuoteSqlExpression` at render sites now resolves `{identifier}` to the correct dialect.

---

### Phase 3: Simplify `ExtractColumnNameFromAggregateSql`

**Goal**: Now that SqlExpression uses `{identifier}`, simplify the column name extraction in `ChainAnalyzer.ExtractColumnNameFromAggregateSql`.

**Changes**:

1. **`src/Quarry.Generator/Parsing/ChainAnalyzer.cs`** — Rewrite `ExtractColumnNameFromAggregateSql` (lines ~2182-2220). New algorithm:
   - Skip `COUNT(*)` (unchanged).
   - For window functions, truncate at ` OVER (` (unchanged).
   - Find the last `{...}` in the search region (instead of scanning for three quote styles).
   - Extract the identifier between braces.

**Tests**: All existing tests pass. `TryResolveAggregateTypeFromSql` works correctly with canonical format.

---

### Phase 4: Remove unused `dialect` parameter from method chain

**Goal**: Clean up the `dialect` parameter that's no longer used for identifier quoting in the analysis methods.

**Changes in `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs`**:

Remove `SqlDialect dialect` parameter from these methods (and update all their call sites):
- `GetColumnSql` — 6 callers
- `GetJoinedColumnSql` — 6 callers
- `GetAggregateInfo` — callers pass dialect only for forwarding
- `GetJoinedAggregateInfo` — same
- `GetWindowFunctionInfo` — same
- `GetJoinedWindowFunctionInfo` — same
- `ParseOverClause` — same
- `ParseJoinedOverClause` — same
- `BuildLagLeadSql` — same
- `BuildJoinedLagLeadSql` — same
- `BuildValueFunctionSql` — same
- `BuildJoinedValueFunctionSql` — same
- `BuildAggregateOverSql` — same
- `BuildJoinedAggregateOverSql` — same
- `ResolveColumnSqlFromExpression` — 1 caller

**Keep `dialect` in**: `TranslateStringMethodToSql` and `TranslateSubstringToSql` (still needed for SQL syntax differences like SUBSTRING FROM vs SUBSTRING with 3 args). The caller `TryParseStringProjection` still has `dialect` for this purpose.

Delete the now-unused local `WrapIdentifier` method's... actually `WrapIdentifier` is still used. It just doesn't need `dialect`. This is already done in Phase 2.

**Tests**: All existing tests pass. Pure parameter removal, no behavior change.

---

### Phase 5: Delete `ReQuoteSqlExpression` and dead code

**Goal**: Remove dead code left behind by the refactoring.

**Changes**:

1. **`src/Quarry.Shared/Sql/SqlFormatting.cs`** — Delete the `ReQuoteSqlExpression` method (lines ~285-335).

2. **`src/Quarry.Generator/Projection/ProjectionAnalyzer.cs`** — Delete the unused `quotedName` variable at line ~221 (pre-existing dead code).

**Tests**: All existing tests pass. Pure deletion of unused code.

## Dependencies

- Phase 2 depends on Phase 1 (render sites must be ready before switching format)
- Phase 3 depends on Phase 2 (canonical format must be in place)
- Phase 4 depends on Phase 2 (parameters become unused after format switch)
- Phase 5 depends on Phase 2 (ReQuoteSqlExpression call removed)
- Phases 3, 4, 5 are independent of each other
