# Plan: #257 — Many<T>.Sum/Min/Max/Avg/Average/Count in Select projection

## Concept summary

The Quarry generator has two disjoint pipelines for translating LINQ expressions to SQL:

- **Where/Having:** `SqlExprParser` → `SqlExprBinder` → `SqlExprRenderer`. The parser already
  recognizes navigation-aggregate calls (`u.Orders.Sum(...)`) via `IsSubqueryMethod` and
  produces `SubqueryExpr` IR nodes. Bind resolves correlation; render produces SQL.
- **Select projection:** `ProjectionAnalyzer` builds `ProjectedColumn` records directly,
  with the SQL fragment as a plain string in `ProjectedColumn.SqlExpression`. It only
  recognizes static `Sql.Sum/Count/...` aggregates (via `IsAggregateCall` checking for
  receiver `"Sql"`). Navigation-aggregate calls fall through silently — producing empty
  SQL columns, `(object, object, ...)` tuple type, and `(?)r.GetValue(N)` reader code
  that doesn't compile.

The fix bridges the two pipelines: have `ProjectionAnalyzer` parse navigation-aggregate
calls into `SubqueryExpr` (using the existing `SqlExprParser`), defer binding/rendering
to `ChainAnalyzer.BuildProjection` (which already has full entity registry, dialect, and
implicit-join context), and emit final SQL into the existing `ProjectedColumn.SqlExpression`
text field. `SqlAssembler.AppendSelectColumns` stays untouched — its existing aggregate
rendering branch handles the populated `SqlExpression` correctly.

## Key concepts

### Detection in ProjectionAnalyzer
A navigation-aggregate is an `InvocationExpressionSyntax` of the shape
`receiver.{Sum|Min|Max|Avg|Average|Count}(...)` where `receiver` is itself a
`MemberAccessExpressionSyntax` whose innermost root is a lambda parameter:

```
u.Orders.Sum(o => o.Total)        // receiver = u.Orders; root = u
u.Addresses.Count()               // receiver = u.Addresses; root = u
```

This is distinct from `Sql.Sum(u.Total)` (root = identifier "Sql"). We keep the existing
`IsAggregateCall` check (which matches `Sql.*`) and add a new `IsNavigationAggregateCall`
check before it falls through.

The aggregate method names are exactly the set `SqlExprParser.IsSubqueryMethod` already
recognizes (Sum/Min/Max/Avg/Average/Count + Any/All — but Any/All return bool and are
predicates, so we restrict the projection path to the scalar-aggregate subset).

### Storage on ProjectedColumn
Add two new init-only fields:

```csharp
public SqlExpr? SubqueryExpression { get; init; }  // unbound parsed expression
public string? OuterParameterName { get; init; }   // lambda param name (for binding context)
```

ProjectionAnalyzer writes these on a placeholder `ProjectedColumn` whose `SqlExpression`,
`ColumnName`, and `ClrType` are intentionally empty — to be filled in by BuildProjection.

### Binding + rendering in BuildProjection
`ChainAnalyzer.BuildProjection` (`src/Quarry.Generator/Parsing/ChainAnalyzer.cs:1764`)
already iterates each `rawCol` in `projInfo.Columns`. We add a new branch (before the
existing aggregate-type-resolution branch at line 1843) that handles columns with
`SubqueryExpression != null`:

1. Resolve the outer entity for `OuterParameterName`:
   - Single-entity: `entityRef` (already in scope).
   - Joined: look up via `joinedEntityTypeNames` indexed by lambda parameter order
     (which we'll need to capture on `ProjectionInfo` or per-column).
2. Construct a `BindContext` (single-entity = `lambdaParameterName` matches outer param;
   joined = build `joinedEntities` and `tableAliases` maps from `joinedEntityTypeNames`
   and lambda param order).
3. Call `SqlExprBinder.Bind(subqueryExpr, primaryEntity, dialect, lambdaParameterName,
   joinedEntities, tableAliases, inBooleanContext: false, entityLookup: registry.ByEntityName,
   out var subqueryImplicitJoins)`.
4. Call `SqlExprRenderer.Render(boundExpr, dialect, paramBase: 0, useGenericParamFormat: false)`
   to produce final SQL text. (Use literal-param format since we're not extracting projection
   parameters in this pass — simple selectors don't have parameters; the issue's repro doesn't.)
5. Resolve the CLR result type:
   - `SubqueryKind.Count` → `int`
   - `SubqueryKind.Sum` → selector column's CLR type (look up via target entity column lookup,
     using the parsed selector's `ColumnRefExpr.PropertyName`)
   - `SubqueryKind.Min`/`Max` → selector column's CLR type as-is
   - `SubqueryKind.Avg` → per `Sql.cs`: `int`/`long`/`decimal` selector → `double`/`double`/`decimal`,
     `double` selector → `double`
6. Build a fully-populated `ProjectedColumn` (via `with`):
   - `SqlExpression = renderedSql`
   - `ClrType = FullClrType = resolvedType`
   - `IsAggregateFunction = true`
   - `IsValueType = true`
   - `ReaderMethodName = TypeClassification.GetReaderMethod(resolvedType)`
   - `Alias = PropertyName` (so SqlAssembler emits `AS "OrderTotal"`)
   - Clear `SubqueryExpression` and `OuterParameterName` (no longer needed downstream)
7. On binding failure (e.g., navigation property doesn't exist on outer entity):
   - Emit a new diagnostic `QRY076` (or next free QRY code — to be confirmed by reading
     `DiagnosticDescriptors.cs`) at the call site location.
   - Still produce a placeholder column with `ClrType = "object"` so downstream emit
     doesn't crash on null fields. The user gets a clear error instead of a confusing
     CS0246 cascade.

### Subquery implicit joins
HasManyThrough subqueries produce implicit junction→target joins INSIDE the subquery
itself (per `SqlExprBinder.BindSubquery` lines 567-586). Those joins live on the
`SubqueryExpr.ImplicitJoins` and are rendered by `SqlExprRenderer.RenderSubquery`'s
`AppendImplicitJoins` call. They do NOT propagate to the outer `QueryPlan.ImplicitJoins`
(which would add joins to the outer FROM clause). So we don't need to merge implicit
joins into `BuildProjection`'s `implicitJoins` list — they stay encapsulated.

### Lambda parameter mapping for joined Select
For joined-context Select like `Lite.Users().Join(Lite.Orders()).On(...).Select((u, o) => ...)`,
the binding context needs to know that `u` → UserSchema (alias t0), `o` → OrderSchema (alias t1).

`AnalyzeJoinedSyntaxOnly` already extracts lambda param names in order. We propagate them
to `ProjectionInfo` via a new `LambdaParameterNames: IReadOnlyList<string>?` field.
`BuildProjection` zips `projInfo.LambdaParameterNames` with `joinedEntityTypeNames` to
build the `joinedEntities` map for binding.

For single-entity Select, `LambdaParameterNames = [paramName]` and `joinedEntities = null`
(use `lambdaParameterName = paramName`).

### Test coverage
Mirror existing `CrossDialectSubqueryTests.Where_Count_GreaterThan` / `Where_Sum_*` patterns
in a new `#region Select projection` section at the bottom of the same file. Each new test:
- Cross-dialect SQL string assertion (sqlite/pg/mysql/ss) with `QueryTestHarness.AssertDialects`.
- Execute on SQLite where seed data permits (Alice has orders, Charlie does not — empty-set
  cases produce NULL → `decimal default(0m)` per current reader convention; or assert filtered
  results to skip the null edge).

Tests to add (single-entity Select):
- `Select_Many_Count_InTuple` — `(u.UserName, OrderCount: u.Orders.Count())`
- `Select_Many_Sum_InTuple`
- `Select_Many_Min_InTuple`
- `Select_Many_Max_InTuple`
- `Select_Many_Avg_InTuple` (uses `Average`)
- `Select_Many_Multiple_InTuple` — exact repro from issue (Sum + Max + Average together)
- `Select_Many_Sum_InDto` — `new UserDto { OrderTotal = u.Orders.Sum(...) }`

Joined-context tests:
- `Select_Many_Sum_Joined_InTuple` — `Join(...).On(...).Select((u, o) => (u.UserName, OrderCount: u.Orders.Count()))`

Negative test:
- `Select_Many_Sum_UnknownNavigation_RaisesDiagnostic` — verify the QRY diagnostic fires
  for a non-existent nav property. (May require fixture changes — defer if it requires
  a separate invalid-input test class.)

## Phases

### Phase 1 — Add SubqueryExpression field to ProjectedColumn (foundation)
- Edit `src/Quarry.Generator/Models/ProjectionInfo.cs`:
  - Add `SqlExpr? SubqueryExpression` and `string? OuterParameterName` init-only fields
    on `ProjectedColumn`.
  - Update constructor to accept (optional) and assign these.
  - Update `Equals` to compare them (use reference equality for SqlExpr — they're built fresh).
  - Add `LambdaParameterNames: IReadOnlyList<string>?` field on `ProjectionInfo` constructor +
    auto-property. Default: null.
- Build only — no behavior change yet.
- **Tests:** none added in this phase (foundation only). Existing tests must remain green.

### Phase 2 — ProjectionAnalyzer: detect navigation aggregates
- Edit `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs`:
  - Add `IsNavigationAggregateCall(invocation, knownParams)` helper: returns true when
    invocation matches `<param>.<NavProp>.{Sum|Min|Max|Avg|Average|Count}(...)`. Walk
    the receiver chain to confirm root is a known parameter.
  - Add `TryParseNavigationAggregate(invocation, propertyName, ordinal, knownParams)`
    helper: calls `SqlExprParser.ParseWithPathTracking` on the invocation, returns a
    placeholder `ProjectedColumn` with `SubqueryExpression`, `OuterParameterName`,
    `IsAggregateFunction = true`, and empty `ColumnName`/`ClrType`/`SqlExpression`.
  - Wire into the four projection-element resolvers:
    1. `ResolveJoinedProjectedExpressionWithPlaceholder` (joined+single-entity placeholder path)
    2. `ResolveJoinedProjectedExpression` (joined enriched path; `EntityInfo`-based)
    3. `AnalyzeProjectedExpression` (single-entity enriched path; tuple + DTO)
    4. `ResolveJoinedAggregate` (the existing `AnalyzeInvocation`-style branch, for
       single-element Select)
  - Capture lambda parameter names on `ProjectionInfo` in the entry points
    (`AnalyzeSingleEntitySyntaxOnly`, `AnalyzeJoinedSyntaxOnly`, `Analyze`,
    `AnalyzeJoined`, `AnalyzeFromTypeSymbol`).
- **Tests:** add `Select_Many_Sum_InTuple` (sqlite-only first to confirm wiring;
  full cross-dialect assertions added once Phase 3 completes binding).
  Test will FAIL (placeholder column doesn't render to valid SQL) — expected for this
  phase. Mark task as in_progress, do NOT commit Phase 2 alone.

### Phase 3 — BuildProjection: bind + render SubqueryExpression
- Edit `src/Quarry.Generator/Parsing/ChainAnalyzer.cs`:
  - In `BuildProjection`, iterate columns. For columns where `SubqueryExpression != null`:
    1. Build `joinedEntities` map (paramName → EntityInfo) using
       `projInfo.LambdaParameterNames` zipped with `joinedEntityTypeNames` (joined),
       or `[primaryParamName] → entityInfo` (single-entity).
    2. Build `tableAliases` map similarly (paramName → "t0"/"t1"/...) for joined; null for single-entity.
    3. Resolve the outer entity for `col.OuterParameterName`. If not found → emit
       diagnostic, fall through to placeholder `object` column.
    4. Call `SqlExprBinder.Bind(subqueryExpr, primaryEntity, dialect, lambdaParamName,
       joinedEntities, tableAliases, inBooleanContext: false, entityLookup: registry.ByEntityName)`.
    5. Render via `SqlExprRenderer.Render(boundExpr, dialect, useGenericParamFormat: false)`.
       Strip outer parens? No — `RenderSubquery` already wraps `(SELECT ... )` inside the
       expression; SqlAssembler will inline the result as-is, producing
       `(SELECT SUM(...) FROM ...) AS "OrderTotal"` which is correct.
    6. Resolve CLR type from `boundExpr.SubqueryKind` and bound selector:
       - `Count` → `"int"`
       - `Sum`/`Min`/`Max` → look up selector column type via target entity's column lookup
       - `Avg` → look up selector's column type, then map per `Sql.cs` rules
         (int/long → double; decimal → decimal; double → double)
       - Fallback: `"object"` if selector resolution fails (with diagnostic)
    7. Construct the final `ProjectedColumn` (via `with`):
       ```
       ColumnName = "",
       SqlExpression = renderedSql,
       ClrType = FullClrType = resolvedType,
       IsAggregateFunction = true,
       IsValueType = true,
       ReaderMethodName = TypeClassification.GetReaderMethod(resolvedType),
       Alias = PropertyName,
       SubqueryExpression = null,
       OuterParameterName = null,
       ```
  - Add a small helper to map selector → CLR type: walk the bound `Selector` for the
    leaf `ResolvedColumnExpr` and read its `ClrType`. (For `o.Total` where Total is
    decimal, we get "decimal".)
  - Add a new diagnostic `QRY076` (or whatever the next free code is — read
    `DiagnosticDescriptors.cs` first) titled
    "Navigation aggregate in projection could not be bound" with message
    "Navigation property '{0}' on entity '{1}' could not be resolved for aggregate '{2}' in Select projection."
- **Tests:** All 7 new tests in the issue plan should pass after this phase. Run full
  suite to confirm no regressions in the 3242 baseline.
- **Commit:** Phases 1+2+3 commit together (Phase 2 alone breaks tests; Phase 1 is just
  data plumbing). Squashing them into one commit keeps each commit green.

### Phase 4 — Tests: cross-dialect coverage + DTO + joined
- Add to `src/Quarry.Tests/SqlOutput/CrossDialectSubqueryTests.cs`:
  - `Select_Many_Count_InTuple` — cross-dialect, with execute assertion
  - `Select_Many_Sum_InTuple` — cross-dialect, with execute assertion
  - `Select_Many_Min_InTuple` — cross-dialect, with execute assertion
  - `Select_Many_Max_InTuple` — cross-dialect, with execute assertion
  - `Select_Many_Avg_InTuple` (uses `Average`) — cross-dialect, with execute assertion
  - `Select_Many_Multiple_InTuple` — exact repro from issue
  - `Select_Many_Sum_InDto` — DTO initializer variant
- Add to `src/Quarry.Tests/SqlOutput/CrossDialectJoinTests.cs` (or similar joined test file):
  - `Select_Many_Sum_Joined_InTuple` — joined-context aggregate in Select
- **Commit:** Phase 4 standalone; just adds tests. Run full suite — must be green.

### Phase 5 — Negative case + diagnostic
- Add diagnostic test for QRY076 firing on unresolvable navigation. Likely lives in
  `Quarry.Generator` test project or a new `CrossDialectDiagnosticsTests` entry.
  Defer to scope-permitting; if the diagnostic test infrastructure requires significant
  setup, document as follow-up issue and move to REVIEW.

## Dependencies
- Phase 1 (data fields) → Phase 2 (analyzer writes them) → Phase 3 (BuildProjection reads them).
- Phase 4 depends on Phase 3 being green.
- Phase 5 is independent of Phase 4 once diagnostic emit is added in Phase 3.

## Open risks / things to verify during implementation
- The `useGenericParamFormat` flag on `SqlExprRenderer.Render` — check whether projection
  SQL needs canonical placeholders or final dialect format. Existing aggregate column SQL
  in `SqlAssembler.AppendSelectColumns` calls `SqlFormatting.QuoteSqlExpression` which
  re-formats — verify whether the rendered subquery passes through that cleanly without
  double-quoting identifiers. May need to render with already-formatted identifiers and
  pass-through, or emit canonical placeholders.
- Whether `BindSubquery` needs `OuterParameterName` to match `lambdaParameterName` or work
  with `JoinedEntities` — re-check at line 379 of SqlExprBinder.cs. Single-entity Select
  uses the param as the only lambda param; joined uses all params.
- The reader emits `(?)r.GetValue(N)` in the broken case because the column ClrType is
  empty — once we resolve the type properly, the reader should auto-pick the right
  `r.GetDecimal(N)` etc. via `TypeClassification.GetReaderMethod`. No reader-side changes
  expected, but verify with the generated `.g.cs` output.
- Multiple aggregates in a single tuple may receive duplicate `sq0` aliases from
  separate `BindSubquery` calls (each call has its own `SubqueryAliasCounter`). The
  Where path counts globally; for projection we'd want each aggregate column to use
  its own alias counter or share one across all bound aggregates. Verify when running
  `Select_Many_Multiple_InTuple` — the expected SQL probably has distinct `sq0`/`sq1`/`sq2`
  aliases. We may need a single shared counter across all binding calls in BuildProjection.
