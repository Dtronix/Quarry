# Plan: Sql.Raw<T> Select-projection support (#256)

## Key concepts

**Existing IR pipeline (Where/Having path — works correctly):** `SqlExprParser` builds a `RawCallExpr` from `Sql.Raw<T>(template, args...)`, with each arg parsed as a child `SqlExpr` (unresolved `ColumnRefExpr`, `LiteralExpr`, `CapturedValueExpr`, or composite ops). `SqlExprBinder` resolves columns to dialect-quoted form, and `SqlExprRenderer` substitutes `{0}/{1}/…` placeholders with rendered arg text. This path is already correct — no change needed.

**Existing projection pipeline (Select path — broken for Sql.Raw):** `ProjectionAnalyzer.AnalyzeProjectedExpression` walks the Select lambda body via direct AST pattern matching (does NOT use the IR). For `Sql.X()` aggregates it calls `GetAggregateInfo` which supports `Count/Sum/Avg/Min/Max` and returns a canonical `SqlExpression` with `{ColumnName}` placeholders (for columns), `@__proj{N}` placeholders (for captured runtime vars), and inline SQL literals (for compile-time consts). Window functions use the same conventions via `ResolveScalarArgSql`. `Sql.Raw` isn't handled → falls through to the generic fallback which sets `columnName: ""` and `isAggregateFunction: false`. `QuarryGenerator.StripNonAggregateSqlExpressions` then nullifies the `SqlExpression`, and `SqlAssembler.cs:1077` emits `QuoteIdentifier("")` → `""`.

**Canonical projection-SqlExpression format (what we produce for Sql.Raw):**
- `{ColumnName}` — identifier placeholder, dialect-resolved at render time via `SqlFormatting.QuoteSqlExpression`.
- `{@N}` — parameter placeholder with global index (written by `ChainAnalyzer.RemapProjectionParameters`, which rewrites `@__proj{N}` local placeholders into `{@globalIdx}`).
- Bare SQL text — inline literals, operators, function calls.

**Fix strategy:** Piggyback on the existing projection-aggregate infrastructure. Add a `Raw` case to `GetAggregateInfo` (single-entity) and `GetJoinedAggregateInfo` (joined). Each case extracts the `T` CLR type from the generic type argument, the template from the first string-literal argument, and builds a canonical `sqlExpression` by substituting arg-index `{0}/{1}/…` placeholders with canonical forms of the arguments. Validate placeholder/arg count via `RawCallExpr.Validate()` — on failure, return `(null, null)` so the fallback path produces a QRY diagnostic (see note on diagnostics below).

**Argument rendering — "full IR support":** We reuse `SqlExprParser.ParseExpression(argExpr, lambdaParams)` to parse each Sql.Raw argument into a SqlExpr tree. We then render each tree via a new, small projection-aware renderer `RenderRawArgToCanonical(expr, columnLookup, projectionParams, semanticModel)` that walks the tree and produces canonical placeholder form:
- `ColumnRefExpr { ParameterName, PropertyName }` → `{ColumnName}` if the property is a known column, else `{PropertyName}` fallback (matches `GetColumnSql` behavior). For joined contexts, emit `{Alias}.{ColumnName}`.
- `CapturedValueExpr` → call `ResolveScalarArgSql` to append a `ParameterInfo` and return `@__proj{N}`. Use the same captured-var extraction (`ILocalSymbol`/`IParameterSymbol`) as window-func args.
- `LiteralExpr` → `FormatConstantForSql` (inline SQL literal).
- `BinaryOpExpr`, `UnaryOpExpr`, `FunctionCallExpr`, `IsNullCheckExpr`, `InExpr`, `LikeExpr` → recurse with standard operator formatting (parens, spaces, commas). This gives us `u.Price * 2`, `u.Name + "_suffix"`, etc.
- Anything else (Subquery, NavigationAccess, NestedRawCall, ParamSlot) → bail out (return `null`) → signals unsupported arg.
The renderer does not perform entity binding (no `SqlExprBinder`), because the projection layer uses canonical `{ColumnName}` placeholders with dialect resolution deferred. The small renderer is local to `ProjectionAnalyzer` and needs no cross-cutting changes.

**Why `IsAggregateFunction: true` is correct for Sql.Raw:** Despite the name, the semantic in this codebase is "emit from SqlExpression verbatim instead of looking up ColumnName". Window functions already set it (they are not aggregates either). It is the gate that (a) survives `StripNonAggregateSqlExpressions`, (b) triggers the `SqlAssembler` expression branch at line 1077, (c) suppresses the auto `t0.` qualifier at `ChainAnalyzer.cs:1342`, (d) prevents `NeedsEnrichment` from trying to replace an empty `ColumnName`. All four behaviors are correct for user-written raw SQL. A rename to `RenderAsSqlExpression` would be cleaner but is out of scope.

## Phases

### Phase 1 — Single-entity Sql.Raw Select-projection support
**Files:**
- `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs`

**Changes:**
1. Add a `Raw` case to `GetAggregateInfo(methodName, invocation, semanticModel, columnLookup, lambdaParameterName, projectionParams)` — handles single-entity Select (tuple, DTO, object-initializer, and single-column via `AnalyzeInvocation` which already routes through `GetAggregateInfo`).
2. In that case:
   - Extract `T` from the generic name syntax (`invocation.Expression.Name` as `GenericNameSyntax` → `TypeArgumentList.Arguments[0]` → `semanticModel.GetTypeInfo(typeArg).Type` → `GetSimpleTypeName(type)`). Fallback to `"object"` if unresolvable.
   - Require `arguments.Count >= 1` and `arguments[0].Expression is LiteralExpressionSyntax` of string kind (or at least a compile-time string constant via `semanticModel.GetConstantValue`). If not, return `(null, null)` — unsupported template form.
   - Build a `RawCallExpr` shell with `template = templateText` and `arguments = <parsed SqlExpr list>`, call `Validate()`. On failure return `(null, null)` so the fallback path (generic ProjectedColumn creation) still emits something (the enrichment/emitter already has loud-fail behavior for unresolved types — but see diagnostic note below).
   - For each non-template arg: call the new `RenderRawArgToCanonical(arg, columnLookup, projectionParams, semanticModel, lambdaParameterName, joinedAlias: null)`. On null, return `(null, null)` — one unsupported arg aborts the whole Sql.Raw.
   - Substitute `{0}, {1}, …` indices in the template with the rendered canonical texts. Return `(finalSql, clrType)`.
3. Add private helper `RenderRawArgToCanonical(ExpressionSyntax arg, Dictionary<string, ColumnInfo> columnLookup, List<ParameterInfo>? projectionParams, SemanticModel semanticModel, string lambdaParameterName, string? joinedAlias)`:
   - Call `SqlExprParser.ParseExpression(arg, new HashSet { lambdaParameterName })` to get a SqlExpr tree.
   - Walk the tree via a local recursive function `RenderNode(SqlExpr node) → string?` that emits canonical form:
     - `ColumnRefExpr col`: look up `col.PropertyName` in `columnLookup`. Found → `$"{joinedAlias ?? ""}{(joinedAlias != null ? "." : "")}{{{column.ColumnName}}}"` (i.e., `{ColumnName}` for single-entity, `{alias}.{ColumnName}` for joined). Not found → `{PropertyName}` fallback (mirrors `GetColumnSql` behavior; matches what existing Where-path Sql.Raw would emit for unknown columns).
     - `LiteralExpr lit`: render directly via SQL-literal formatting. For strings and chars, preserve existing Renderer logic (single-quote + double `''` escape). For numerics, pass through `SqlText`. For nulls, `"NULL"`. For booleans, use dialect-agnostic `TRUE`/`FALSE` (matching the `LiteralExpr` SqlText for booleans).
     - `CapturedValueExpr cap`: delegate to a small inline version of `ResolveScalarArgSql` that uses `cap.SyntaxText` / `cap.VariableName`. Because `CapturedValueExpr` comes out of `SqlExprParser` with known info (name, expression path) we can build a `ParameterInfo` directly: `new ParameterInfo(index: projectionParams.Count, name: $"@__proj{index}", clrType: cap.ClrType, valueExpression: cap.SyntaxText, isCaptured: true) { CapturedFieldName = cap.VariableName, CapturedFieldType = cap.ClrType, IsStaticField = cap.IsStaticField }`. Append to `projectionParams`. Return `@__proj{index}`. If `projectionParams == null`, return null (unsupported).
     - `BinaryOpExpr bin`: recurse left/right, emit `$"({left} {op} {right})"` using `GetSqlOperator`-equivalent operator text. Bail on recursion failure.
     - `UnaryOpExpr un`: `NOT (…)` or `-…` for supported operators.
     - `FunctionCallExpr fn`: `$"{fn.FunctionName}({comma-joined-args})"`.
     - `IsNullCheckExpr`, `InExpr`, `LikeExpr`: matching canonical forms (mirroring `SqlExprRenderer`).
     - Fallback: return null (unsupported arg type — abort).
   - Return the top-level rendered string, or null if any recursion bailed.
4. Helper to extract the template string:
   ```csharp
   private static string? TryExtractTemplate(ExpressionSyntax expr, SemanticModel sm)
   {
       if (expr is LiteralExpressionSyntax l && l.Kind() == SyntaxKind.StringLiteralExpression)
           return l.Token.ValueText;
       var constValue = sm.GetConstantValue(expr);
       return constValue.HasValue && constValue.Value is string s ? s : null;
   }
   ```

**Test additions (Phase 3 holds all tests; no new tests in this phase).**

**Validation after this phase:** Existing Where-path Sql.Raw tests (`CrossDialectMiscTests.Where_SqlRaw_*`) must still pass unchanged. Existing aggregate/window-func projection tests must still pass.

### Phase 2 — Joined Select-projection + single-column support
**Files:**
- `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs`

**Changes:**
1. Add a `Raw` case to `GetJoinedAggregateInfo(methodName, invocation, perParamLookup, semanticModel, projectionParams)` — mirrors the Phase 1 Raw case but:
   - For each arg, call `RenderRawArgToCanonicalJoined(arg, perParamLookup, projectionParams, semanticModel)`.
   - `RenderRawArgToCanonicalJoined` walks the SqlExprParser output and resolves `ColumnRefExpr.ParameterName` to `perParamLookup[paramName].Alias` / `.Lookup`, producing `{alias}.{ColumnName}` canonical form.
   - Captured-var, literal, binary, unary, function, etc. handling matches Phase 1 exactly (refactor the `RenderNode` inner walker to accept a `column-resolver` delegate to avoid duplicating the big switch — the only piece that differs is how `ColumnRefExpr` is resolved).
2. Single-column projection `.Select(u => Sql.Raw<string>(...))` — verify it already works via `AnalyzeInvocation` (line 1526), which routes through `GetAggregateInfo` / `GetJoinedAggregateInfo`. If the generic-name-syntax detection is correct (see Phase 1 step 2), both single-column and tuple/DTO/object-init forms work automatically. If not, add explicit handling.
3. Joined single-column `.Select((a, b) => Sql.Raw<…>(…))` — verify via `AnalyzeJoinedInvocation` (line 604) which routes through `GetJoinedAggregateInfo`. No additional code expected.

**Validation after this phase:** All existing tests still pass; manual sanity check that the doc-sample query (`src/Samples/5_DocsValidation`-style) now emits the raw template SQL.

### Phase 3 — Cross-dialect tests
**Files:**
- `src/Quarry.Tests/SqlOutput/CrossDialectMiscTests.cs`

**Changes:**
Add a new region `#region Sql.Raw in Select projection` with these tests (each asserts SQL across all four dialects via `QueryTestHarness.AssertDialects`; mirrors existing `Where_SqlRaw_*` structure):

1. `Select_SqlRaw_ColumnReference` — `.Select(u => (u.UserId, Bucket: Sql.Raw<string>("UPPER({0})", u.UserName)))` → `SELECT "UserId", UPPER("UserName") FROM "users"` (SQLite; analogous for Pg/My/Ss with their quoting).
2. `Select_SqlRaw_MultipleColumnReferences` — `.Select(u => (u.UserId, Tag: Sql.Raw<string>("concat({0}, '-', {1})", u.UserName, u.Email)))`.
3. `Select_SqlRaw_CapturedVariable` — `.Select(u => (u.UserId, Bucket: Sql.Raw<string>("CASE WHEN {0} > {1} THEN 'a' ELSE 'b' END", u.UserId, threshold)))` where `threshold` is a local int. Verifies `@p0` / `$1` / `?` parameterization.
4. `Select_SqlRaw_LiteralParameter` — `.Select(u => (u.UserId, Flag: Sql.Raw<int>("coalesce({0}, {1})", u.UserId, 42)))`.
5. `Select_SqlRaw_NoPlaceholders` — `.Select(u => (u.UserId, Literal: Sql.Raw<string>("'fixed'")))`.
6. `Select_SqlRaw_InDtoInitializer` — `.Select(u => new UserDto { UserId = u.UserId, Display = Sql.Raw<string>("UPPER({0})", u.UserName) })`. Requires `Display` property on a test DTO (use an existing one if available, otherwise add to test DTOs).
7. `Select_SqlRaw_SingleColumn` — `.Select(u => Sql.Raw<string>("UPPER({0})", u.UserName))` → single scalar result.
8. `Select_SqlRaw_JoinedTuple` — `.Select((u, o) => (u.UserId, o.OrderId, Label: Sql.Raw<string>("concat({0}, '-', {1})", u.UserName, o.OrderId)))`. Uses the `QueryTestHarness`'s existing joined-entity setup.
9. `Select_SqlRaw_BinaryOpArg` — `.Select(u => (u.UserId, Bucket: Sql.Raw<int>("bucket({0})", u.UserId * 10)))` to exercise the IR-based arg-expression rendering.

All 9 tests must produce correct SQL for all 4 dialects and prepare without QRY diagnostics.

**Phase-3 commits** the test additions atomically.

## Dependencies
- Phase 2 depends on Phase 1 (the `RenderRawArgToCanonical` inner walker is refactored/reused).
- Phase 3 depends on Phases 1+2 (tests assume the feature works across single and joined projection forms).

## Diagnostics note
Issue calls out "emit a QRY diagnostic when RawCallExpr in projection" as a fallback. With the full fix, the diagnostic is only relevant for unsupported arg types (bailout path). For scope reasons we do NOT add a new QRY code in this fix — unsupported cases fall through to existing generic fallback which already emits a QRY (generator error when it can't resolve). If testing reveals a silent-failure path not caught by existing diagnostics, adding an explicit QRY is a follow-up.

## Non-goals
- IR integration for the entire projection layer (too large; hybrid approach above is sufficient).
- Rename of `IsAggregateFunction` → `IsComputedExpression` or similar (cross-cutting; not required for fix).
- Support for nested `Sql.Raw` inside aggregate args (`Sql.Sum(Sql.Raw<int>(...))`) — not requested, no existing tests.
