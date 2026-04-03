# Implementation Plan Part 2: Navigation Joins Hardening

**Scope:** Projection support for navigation access in Select, table-qualified columns, diagnostic wiring, HasManyThrough tests, 5-6 table join tests, parser precision improvement.
**Branch:** `feature/navigation-joins`
**Depends on:** All work from `impl-plan-joins.md` (Phases 1-12 complete per `handoff.md`)

---

## Phase 1: Extend ProjectionAnalyzer for Navigation Access in Select

### Problem

When a user writes `.Select(o => (o.OrderId, o.User.UserName))`, the `ProjectionAnalyzer` cannot resolve tuple element types through `One<T>` navigation chains. The generator produces unresolved tuple types like `(OrderId, UserName)` instead of `(int, string)`, causing CS0246 build errors in generated interceptors. The SQL column rendering is already correct — the `SqlExprBinder` produces `"j0"."UserName"` — but the projection metadata (CLR type, column name, table alias) is missing or wrong.

This affects both single-entity queries (`o => o.User.UserName`) and joined queries (`(o, u) => o.User.UserName`).

### Root Cause

The `ProjectionAnalyzer` has three member-access resolution paths, none of which handle navigation chains:

**Path 1 — Direct property access** (`AnalyzeProjectedExpression`, line 1397): Checks `memberAccess.Expression is IdentifierNameSyntax identifier && identifier.Identifier.Text == lambdaParameterName`. For `o.User.UserName`, the inner expression is `o.User` (a `MemberAccessExpressionSyntax`), not an `IdentifierNameSyntax`. Fails.

**Path 2 — Ref.Id access** (`AnalyzeProjectedExpression`, line 1447): Checks `memberAccess.Name.Identifier.Text == "Id"`. For `o.User.UserName`, the member name is "UserName". Fails.

**Path 3 — Semantic model fallback** (`AnalyzeProjectedExpression`, line 1532): Calls `semanticModel.GetTypeInfo(expression)`. In a source generator context, generated entity types may not be visible to the semantic model during analysis. Even when type resolution succeeds, the resulting `ProjectedColumn` has `columnName: ""` (because `sourceMemberName` is only set for direct property access) and no `tableAlias`, making enrichment and SQL rendering impossible.

The same gap exists in the joined path: `ResolveJoinedColumnWithPlaceholder` (line 266) and `ResolveJoinedColumn` (line 656) only handle direct `param.Property` and `param.Ref.Id` patterns.

### Solution Design

The fix spans four files and introduces one new concept: a `NavigationHops` field on `ProjectedColumn` that carries navigation chain metadata through the pipeline so that `BuildProjection` can resolve types and aliases during enrichment.

The approach uses a **hybrid strategy**: `BuildProjection` resolves column metadata (CLR type, column name) from the `EntityRegistry`, and for the table alias it first checks existing `ImplicitJoinInfo` entries from other clauses (Where, OrderBy), reusing their aliases. If no matching implicit join exists (navigation-only-in-Select), it creates one using a shared helper extracted from `SqlExprBinder.BindNavigationAccess`.

### 1.1 Add `NavigationHops` to `ProjectedColumn`

**File:** `src/Quarry.Generator/Models/ProjectionInfo.cs`

Add a new optional parameter and property to `ProjectedColumn`:

```csharp
// Constructor — add after isEnum parameter:
IReadOnlyList<string>? navigationHops = null

// Property:
/// <summary>
/// Navigation chain hops for One&lt;T&gt; navigation access in projections.
/// E.g., ["User"] for o.User.UserName, or ["User", "Department"] for o.User.Department.Name.
/// Null for non-navigation columns.
/// </summary>
public IReadOnlyList<string>? NavigationHops { get; }
```

Update `Equals` and `GetHashCode` to include `NavigationHops`. For equality, use `EqualityHelpers.SequenceEqual` on the lists, treating null as empty.

The field is propagated through the enrichment pipeline in `BuildProjection` and consumed to resolve the target entity and its columns. After enrichment, the column has its final `TableAlias`, `ColumnName`, and `ClrType` set, and `NavigationHops` is no longer needed — but it persists for incremental caching correctness.

### 1.2 Detect Navigation Chains in `ProjectionAnalyzer`

**File:** `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs`

Add a private helper method `TryParseNavigationChain` that detects the pattern `lambdaParam.Nav1.Nav2...NavN.FinalProp` by walking the `MemberAccessExpressionSyntax` chain from leaf to root:

```csharp
/// <summary>
/// Attempts to parse a member access chain as a navigation access.
/// Returns the navigation hops and final property name, or null if
/// the chain root is not the lambda parameter or has zero hops.
/// </summary>
private static (List<string> Hops, string FinalProp)? TryParseNavigationChain(
    MemberAccessExpressionSyntax memberAccess,
    string lambdaParameterName)
{
    var hops = new List<string>();
    var finalProp = memberAccess.Name.Identifier.Text;
    var current = memberAccess.Expression;

    while (current is MemberAccessExpressionSyntax inner)
    {
        hops.Insert(0, inner.Name.Identifier.Text);
        current = inner.Expression;
    }

    if (current is IdentifierNameSyntax id &&
        id.Identifier.Text == lambdaParameterName &&
        hops.Count > 0)
        return (hops, finalProp);

    return null;
}
```

The method handles arbitrarily deep chains: `o.User.Department.Company.Name` produces `hops = ["User", "Department", "Company"]`, `finalProp = "Name"`. The Ref.Id check in `AnalyzeProjectedExpression` runs before this method is called, so `o.UserId.Id` is never misidentified as a navigation chain.

**Modification 1 — Single-entity path** (`AnalyzeProjectedExpression`, after the Ref.Id check at line ~1466):

After the existing `// Ref<T,K>.Id access` block and before the aggregate/string method checks, insert a navigation chain detection block:

```csharp
// Navigation access: o.User.UserName or o.User.Department.Name
var navChain = TryParseNavigationChain(memberAccess, lambdaParameterName);
if (navChain != null)
{
    return new ProjectedColumn(
        propertyName: propertyName,
        columnName: navChain.Value.FinalProp,
        clrType: "",
        fullClrType: "",
        isNullable: false,
        ordinal: ordinal,
        navigationHops: navChain.Value.Hops);
}
```

This creates a placeholder column with empty types. The `columnName` is set to the final property name (e.g., "UserName") so that `BuildProjection` can match it against the target entity's columns. Types are enriched in `BuildProjection`.

**Modification 2 — Joined placeholder path** (`ResolveJoinedColumnWithPlaceholder`, after the Ref.Id check at line ~329):

The joined placeholder path uses `perParamLookup` with per-parameter lookup dictionaries. The helper needs a variant that accepts a parameter name check against the lookup:

```csharp
// Navigation access: o.User.UserName
var navChain = TryParseNavigationChainJoined(memberAccess, perParamLookup);
if (navChain != null)
{
    return new ProjectedColumn(
        propertyName: propertyName,
        columnName: navChain.Value.FinalProp,
        clrType: "",
        fullClrType: "",
        isNullable: false,
        ordinal: ordinal,
        tableAlias: navChain.Value.SourceAlias,
        navigationHops: navChain.Value.Hops);
}
```

`TryParseNavigationChainJoined` is similar to `TryParseNavigationChain` but checks if the root identifier is in `perParamLookup` and returns the source alias from that lookup entry.

**Modification 3 — Joined enriched path** (`ResolveJoinedColumn`, after the Ref.Id check at line ~731):

Same pattern as Modification 2 but with the `SemanticModel`-aware joined path. The placeholder approach is the same — types are enriched in `BuildProjection`.

**Modification 4 — `IsJoinedMemberAccess` guard** (line 471):

The `AnalyzeJoinedExpression` switch at line 422 has a guard `IsJoinedMemberAccess(memberAccess, perParamLookup)` that checks if the root expression is directly a lambda parameter. For navigation chains like `o.User.UserName`, the root is `o` (a parameter) but the immediate expression is `o.User` (not a parameter identifier). This guard currently rejects navigation chains, routing them to the `_ =>` catch-all which returns `CreateFailed`.

Add a new branch before the catch-all:

```csharp
// Navigation member access: o.User.UserName (chained member access rooted on a parameter)
MemberAccessExpressionSyntax memberAccess2 when IsNavigationMemberAccess(memberAccess2, perParamLookup) =>
    AnalyzeJoinedSingleColumnWithPlaceholder(memberAccess2, perParamLookup, resultType),
```

Where `IsNavigationMemberAccess` walks the chain to check if the root is a known parameter. A similar branch is needed in `AnalyzeJoinedExpressionWithPlaceholders` (line 117).

**Modification 5 — `AnalyzeExpression` guard** (line 941):

The single-entity `AnalyzeExpression` switch at line 941 uses `IsMemberOfParameter(memberAccess, lambdaParameterName)` as a guard for the single-column case. For `o.User.UserName` as a standalone Select expression (not inside a tuple), this guard fails and the expression hits the `_ =>` catch-all.

Add a new branch:

```csharp
// Navigation single column: o => o.User.UserName
MemberAccessExpressionSyntax memberAccess when IsNavigationMemberAccess(memberAccess, lambdaParameterName) =>
    AnalyzeSingleColumn(memberAccess, semanticModel, columnLookup, lambdaParameterName, resultType, dialect),
```

`IsNavigationMemberAccess` (single-entity variant) walks the chain to the root and checks if it matches `lambdaParameterName`.

### 1.3 Extract Shared `ImplicitJoinHelper`

**File:** `src/Quarry.Generator/Models/ImplicitJoinHelper.cs` (new)

Extract the implicit join creation logic from `SqlExprBinder.BindNavigationAccess` (lines 643-688) into a shared static helper. This avoids duplicating the FK resolution, PK lookup, and alias allocation logic between the binder and `BuildProjection`.

```csharp
internal static class ImplicitJoinHelper
{
    /// <summary>
    /// Creates an ImplicitJoinInfo for a One&lt;T&gt; navigation hop, or returns an
    /// existing one if a matching join already exists (deduplication by target alias).
    /// </summary>
    public static ImplicitJoinInfo? CreateOrReuse(
        SingleNavigationInfo nav,
        EntityRef sourceEntity,
        string sourceAlias,
        EntityRef targetEntity,
        SqlDialect dialect,
        List<ImplicitJoinInfo> existingJoins,
        ref int aliasCounter)
    {
        // Resolve FK column name on source entity
        string? fkColumnName = null;
        foreach (var col in sourceEntity.Columns)
        {
            if (col.PropertyName == nav.ForeignKeyPropertyName)
            {
                fkColumnName = col.ColumnName;
                break;
            }
        }
        if (fkColumnName == null) return null;

        // Resolve PK column name on target entity
        string? pkColumnName = null;
        foreach (var col in targetEntity.Columns)
        {
            if (col.Kind == ColumnKind.PrimaryKey)
            {
                pkColumnName = col.ColumnName;
                break;
            }
        }
        if (pkColumnName == null) return null;

        // Dedup: check if a join with matching (sourceAlias, fkColumn, targetEntity) exists
        foreach (var existing in existingJoins)
        {
            if (existing.SourceAlias == sourceAlias &&
                existing.FkColumnName == fkColumnName &&
                existing.TargetTableName == targetEntity.TableName)
                return existing;
        }

        // Allocate new alias
        var joinAlias = $"j{aliasCounter++}";
        var joinKind = nav.IsNullableFk ? JoinClauseKind.Left : JoinClauseKind.Inner;

        var info = new ImplicitJoinInfo(
            sourceAlias: sourceAlias,
            fkColumnName: fkColumnName,
            fkColumnQuoted: SqlFormatting.QuoteIdentifier(dialect, fkColumnName),
            targetTableName: targetEntity.TableName,
            targetTableQuoted: SqlFormatting.QuoteIdentifier(dialect, targetEntity.TableName),
            targetSchemaQuoted: null,
            targetAlias: joinAlias,
            targetPkColumnQuoted: SqlFormatting.QuoteIdentifier(dialect, pkColumnName),
            joinKind: joinKind,
            targetPkColumnName: pkColumnName);

        existingJoins.Add(info);
        return info;
    }
}
```

After extracting this helper, refactor `SqlExprBinder.BindNavigationAccess` to call `ImplicitJoinHelper.CreateOrReuse` instead of duplicating the logic. The binder's existing `BindContext` fields (`ImplicitJoins`, `ImplicitJoinAliasCounter`, `ImplicitJoinAliases`) continue to be used for the binder's own deduplication, but the core join creation logic is shared.

### 1.4 Enrich Navigation Columns in `BuildProjection`

**File:** `src/Quarry.Generator/Parsing/ChainAnalyzer.cs`

Modify the `BuildProjection` method (line 1102) to accept an additional parameter: `List<ImplicitJoinInfo> implicitJoins`. This list accumulates implicit joins from navigation columns in the projection. After `BuildProjection` returns, the caller in the main chain analysis loop merges these into the chain's `implicitJoinInfos` list.

**Change `BuildProjection` signature:**

```csharp
private static SelectProjection BuildProjection(
    ProjectionInfo projInfo,
    TranslatedCallSite executionSite,
    EntityRegistry registry,
    SqlDialect dialect,
    List<ImplicitJoinInfo> implicitJoins)
```

The `dialect` parameter is needed for `ImplicitJoinHelper.CreateOrReuse` to quote identifiers correctly.

**Add navigation column enrichment** inside the `foreach (var col in projInfo.Columns)` loop (after line 1141), before the existing `NeedsEnrichment` check:

```csharp
if (col.NavigationHops != null && col.NavigationHops.Count > 0 && entityRef != null)
{
    var resolved = ResolveNavigationColumn(
        col, entityRef, registry, dialect, implicitJoins);
    if (resolved != null)
    {
        columns.Add(resolved);
        continue;
    }
}
```

**New helper method `ResolveNavigationColumn`:**

This method walks the navigation chain through `SingleNavigations`, resolves the target entity from the registry, creates or reuses an implicit join for each hop, and looks up the final column metadata on the terminal entity.

```csharp
private static ProjectedColumn? ResolveNavigationColumn(
    ProjectedColumn col,
    EntityRef sourceEntity,
    EntityRegistry registry,
    SqlDialect dialect,
    List<ImplicitJoinInfo> implicitJoins)
{
    var currentEntity = sourceEntity;
    string currentAlias = "t0"; // primary entity alias when implicit joins exist
    int aliasCounter = implicitJoins.Count; // continue from existing aliases

    foreach (var hop in col.NavigationHops)
    {
        // Find the SingleNavigationInfo for this hop
        SingleNavigationInfo? nav = null;
        foreach (var sn in currentEntity.SingleNavigations)
        {
            if (sn.PropertyName == hop) { nav = sn; break; }
        }
        if (nav == null) return null;

        // Resolve target entity from registry
        var targetEntry = registry.Resolve(nav.TargetEntityName);
        if (targetEntry == null) return null;
        var targetRef = EntityRef.FromEntityInfo(targetEntry.Entity);

        // Create or reuse implicit join
        var joinInfo = ImplicitJoinHelper.CreateOrReuse(
            nav, currentEntity, currentAlias, targetRef,
            dialect, implicitJoins, ref aliasCounter);
        if (joinInfo == null) return null;

        currentEntity = targetRef;
        currentAlias = joinInfo.TargetAlias;
    }

    // Resolve the final property on the terminal entity
    ColumnInfo? targetCol = null;
    foreach (var c in currentEntity.Columns)
    {
        if (c.PropertyName == col.ColumnName) { targetCol = c; break; }
    }
    if (targetCol == null) return null;

    return new ProjectedColumn(
        propertyName: col.PropertyName,
        columnName: targetCol.ColumnName,
        clrType: targetCol.ClrType,
        fullClrType: targetCol.FullClrType,
        isNullable: targetCol.IsNullable,
        ordinal: col.Ordinal,
        customTypeMapping: targetCol.CustomTypeMappingClass,
        isValueType: targetCol.IsValueType,
        readerMethodName: targetCol.DbReaderMethodName ?? targetCol.ReaderMethodName,
        tableAlias: currentAlias,
        isForeignKey: targetCol.Kind == ColumnKind.ForeignKey,
        foreignKeyEntityName: targetCol.ReferencedEntityName,
        isEnum: targetCol.IsEnum);
}
```

The alias counter starts from `implicitJoins.Count` to avoid collisions with aliases already allocated by the binder for other clauses. The deduplication inside `ImplicitJoinHelper.CreateOrReuse` ensures that if the same navigation was already resolved by a Where clause, the existing alias is reused.

### 1.5 Merge Projection Implicit Joins into Chain

**File:** `src/Quarry.Generator/Parsing/ChainAnalyzer.cs`

In the main chain analysis loop (around line 630, inside the `InterceptorKind.Select` branch), after calling `BuildProjection`, merge any new implicit joins:

```csharp
else if (kind == InterceptorKind.Select && raw.ProjectionInfo != null)
{
    // ... existing code ...
    var projectionImplicitJoins = new List<ImplicitJoinInfo>();
    projection = BuildProjection(raw.ProjectionInfo, executionSite, registry, dialect, projectionImplicitJoins);

    // Merge projection-sourced implicit joins
    foreach (var pij in projectionImplicitJoins)
    {
        var isDuplicate = implicitJoinInfos.Any(existing => existing.TargetAlias == pij.TargetAlias);
        if (!isDuplicate)
            implicitJoinInfos.Add(pij);
    }
}
```

This ensures that implicit joins created by navigation-only-in-Select queries are included in the `QueryPlan.ImplicitJoins` and rendered in the SQL output.

### 1.6 Handle `GetImplicitPropertyName` for Navigation Chains

**File:** `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs`

The existing `GetImplicitPropertyName` method (line 1560) returns the last member access name for unnamed tuple elements. For `o.User.UserName`, it returns "UserName", which is correct. No change needed.

### 1.7 Tests

**File:** `src/Quarry.Tests/SqlOutput/CrossDialectNavigationJoinTests.cs`

Add new test methods in a `#region One<T> navigation in Select` section:

**Test 1 — Navigation in Select tuple with Where:**
```csharp
[Test]
public async Task NavigationJoin_Select_TupleWithNavigation()
{
    await using var t = await QueryTestHarness.CreateAsync();
    var (Lite, Pg, My, Ss) = t;

    var lt = Lite.Orders().Where(o => o.User!.IsActive)
        .Select(o => (o.OrderId, o.User!.UserName)).ToDiagnostics();
    // ... other dialects ...

    QueryTestHarness.AssertDialects(
        lt, pg, my, ss,
        sqlite: "SELECT \"t0\".\"OrderId\", \"j0\".\"UserName\" FROM \"orders\" AS \"t0\" INNER JOIN \"users\" AS \"j0\" ON \"t0\".\"UserId\" = \"j0\".\"UserId\" WHERE \"j0\".\"IsActive\" = 1",
        // ... other dialects ...
    );
}
```

**Test 2 — Navigation only in Select (no Where navigation):**
```csharp
[Test]
public async Task NavigationJoin_Select_NavigationOnlyInSelect()
{
    // Navigation appears only in Select — the implicit join must still be created
    var lt = Lite.Orders()
        .Select(o => (o.OrderId, o.User!.UserName)).ToDiagnostics();
    // Expected: INNER JOIN still emitted from projection pipeline
}
```

**Test 3 — Single column navigation Select:**
```csharp
[Test]
public async Task NavigationJoin_Select_SingleColumn()
{
    var lt = Lite.Orders().Select(o => o.User!.UserName).ToDiagnostics();
    // Expected: SELECT "j0"."UserName" FROM "orders" AS "t0" INNER JOIN ...
}
```

**Test 4 — Deep chain in Select:**
```csharp
[Test]
public async Task NavigationJoin_Select_DeepChain()
{
    var lt = Lite.OrderItems()
        .Select(i => (i.ProductName, i.Order!.User!.UserName)).ToDiagnostics();
    // Expected: two INNER JOINs, Select uses j1 alias for User column
}
```

---

## Phase 2: Table-Qualify Select Columns When Implicit Joins Present

### Problem

When implicit joins exist, SELECT columns from the primary entity render as `SELECT "Total"` instead of `SELECT "t0"."Total"`. The assembler correctly aliases the primary table as `"t0"` when `plan.ImplicitJoins.Count > 0` (line 170 of `SqlAssembler.cs`), but the `ProjectedColumn.TableAlias` for primary entity columns is null because the `ProjectionAnalyzer` creates them without a table alias.

This is valid SQL when column names are unambiguous, but will break if the primary entity and a joined entity share a column name (e.g., both have an "Id" column).

### Root Cause

In `ProjectionAnalyzer.AnalyzeProjectedExpression` (line 1404), single-entity columns are created without a `tableAlias` parameter:

```csharp
return new ProjectedColumn(
    propertyName: propertyName,
    columnName: columnInfo.ColumnName,
    // ... no tableAlias parameter — defaults to null
);
```

In `SqlAssembler.AppendSelectColumns` (line 502), the column is only qualified when `col.TableAlias != null`. Since it's null for primary entity columns, no qualification is added.

### Solution

The fix is applied in `BuildProjection` (ChainAnalyzer), not in the ProjectionAnalyzer. The ProjectionAnalyzer runs at discovery time when implicit joins are not yet known. `BuildProjection` runs during chain analysis when implicit joins from other clauses are available.

**File:** `src/Quarry.Generator/Parsing/ChainAnalyzer.cs`

After the `BuildProjection` method completes and returns a `SelectProjection`, check if `implicitJoinInfos.Count > 0`. If so, post-process the projection columns: any column with `TableAlias == null` (primary entity column) gets `TableAlias = "t0"`.

This post-processing happens in the main chain analysis loop, after projection building and implicit join merging:

```csharp
// Post-process: table-qualify primary entity columns when implicit joins exist
if (projection != null && implicitJoinInfos.Count > 0)
{
    var qualified = new List<ProjectedColumn>();
    foreach (var col in projection.Columns)
    {
        if (col.TableAlias == null && !col.IsAggregateFunction)
        {
            qualified.Add(new ProjectedColumn(
                propertyName: col.PropertyName,
                columnName: col.ColumnName,
                clrType: col.ClrType,
                fullClrType: col.FullClrType,
                isNullable: col.IsNullable,
                ordinal: col.Ordinal,
                alias: col.Alias,
                sqlExpression: col.SqlExpression,
                isAggregateFunction: col.IsAggregateFunction,
                customTypeMapping: col.CustomTypeMapping,
                isValueType: col.IsValueType,
                readerMethodName: col.ReaderMethodName,
                tableAlias: "t0",
                isForeignKey: col.IsForeignKey,
                foreignKeyEntityName: col.ForeignKeyEntityName,
                isEnum: col.IsEnum));
        }
        else
        {
            qualified.Add(col);
        }
    }
    projection = new SelectProjection(
        projection.Kind, projection.ResultTypeName, qualified,
        projection.CustomEntityReaderClass, projection.IsIdentity);
}
```

Aggregate columns are excluded because they render via `col.SqlExpression`, not `col.TableAlias + col.ColumnName`.

### Tests

Update existing `CrossDialectNavigationJoinTests` to expect table-qualified SELECT columns:

```
// Before: SELECT "Total" FROM "orders" AS "t0" INNER JOIN ...
// After:  SELECT "t0"."Total" FROM "orders" AS "t0" INNER JOIN ...
```

All 4 existing navigation join tests need their expected SQL updated.

---

## Phase 3: Wire QRY040-045 Diagnostics into Parser and Binder

### Problem

Six diagnostic descriptors (QRY040-045) are defined in `DiagnosticDescriptors.cs` (lines 514-580) but no code ever calls `context.ReportDiagnostic()` with them. The `SchemaParser.TryParseSingleNavigation` silently returns false on errors. The `SqlExprBinder.BindNavigationAccess` returns SQL comment strings instead of diagnostics.

### Architecture Gap

The `SchemaParser` is a static utility class with no access to `SourceProductionContext`. Diagnostics must be collected during parsing and reported by the caller.

### Solution: Diagnostic Collection Pattern

**Step 1 — Add diagnostic list to parse output**

**File:** `src/Quarry.Generator/Models/EntityInfo.cs`

Add an optional `IReadOnlyList<Diagnostic> Diagnostics` property to `EntityInfo`. The constructor accepts a `diagnostics` parameter defaulting to `Array.Empty<Diagnostic>()`. The `Equals` method does NOT include diagnostics (they are side-channel data, not entity identity).

**Step 2 — Collect diagnostics in TryParseSingleNavigation**

**File:** `src/Quarry.Generator/Parsing/SchemaParser.cs`

Modify `TryParseSingleNavigation` (line 925) to accept a `List<Diagnostic> diagnostics` parameter and the `Location` of the property declaration for positioning.

At each error path, instead of returning false silently, add a diagnostic:

**QRY040 — No FK found** (line ~989, inside `matchingRefs.Count == 0`):
```csharp
diagnostics.Add(Diagnostic.Create(
    DiagnosticDescriptors.NoFkForOneNavigation,
    propertyLocation,
    targetEntityName, propertyName, schemaName));
return false;
```

**QRY041 — Ambiguous FK** (line ~989, inside `matchingRefs.Count > 1`):
```csharp
diagnostics.Add(Diagnostic.Create(
    DiagnosticDescriptors.AmbiguousFkForOneNavigation,
    propertyLocation,
    targetEntityName, propertyName,
    string.Join(", ", matchingRefs.Select(r => r.PropertyName))));
return false;
```

**QRY042 — HasOne references invalid column** (inside the explicit path, when the referenced column isn't a `Ref<T,K>` to the target):
```csharp
diagnostics.Add(Diagnostic.Create(
    DiagnosticDescriptors.HasOneInvalidColumn,
    propertyLocation,
    targetEntityName, referencedColumnName));
return false;
```

Similarly, modify `TryParseThroughNavigation` for QRY044 and QRY045.

**Step 3 — Report collected diagnostics in the generator**

**File:** `src/Quarry.Generator/QuarryGenerator.cs`

In `GenerateEntityAndContextCode` (around line 250), after `SchemaParser.FindAndParseSchema()` returns an `EntityInfo`, iterate `entity.Diagnostics` and report each:

```csharp
foreach (var diag in entity.Diagnostics)
    context.ReportDiagnostic(diag);
```

**Step 4 — QRY043 in the binder**

QRY043 (`NavigationTargetNotFound`) fires during expression binding, not schema parsing. The binder is a static utility and cannot report diagnostics directly. The current fallback of returning `SqlRawExpr("/* unresolved navigation ... */")` is acceptable as a runtime degradation. However, the error can be detected during chain analysis when `BuildProjection` tries to resolve a navigation column and the target entity is not found in the registry.

In `ResolveNavigationColumn` (the new method from Phase 1), when `registry.Resolve(nav.TargetEntityName)` returns null, collect a diagnostic in a list passed to `BuildProjection`. The chain analyzer reports these diagnostics through the pipeline error mechanism (setting `PipelineError` on the translated call site, which triggers QRY900 with a descriptive message).

### Tests

Add unit tests in a new `DiagnosticTests` class that verify:
- Schema with `One<UserSchema>` but no `Ref<UserSchema, K>` → QRY040
- Schema with two `Ref<UserSchema, K>` columns and unqualified `One<UserSchema>` → QRY041
- Schema with `HasOne<UserSchema>(nameof(WrongColumn))` where WrongColumn isn't a Ref → QRY042

---

## Phase 4: Add HasManyThrough Cross-Dialect Tests

### Problem

The `HasManyThrough` binder expansion is implemented (lines 361-562 of `SqlExprBinder.cs`) but has no cross-dialect tests. The expansion detects `ThroughNavigationInfo` on the outer entity, resolves the junction entity, adds an implicit join from junction to target inside the subquery, and rebinds the predicate in the target entity's context.

### Test Schema Setup

Create new test schemas for a many-to-many relationship:

**File:** `src/Quarry.Tests/Samples/AddressSchema.cs` (new)
```csharp
public class AddressSchema : Schema
{
    public static string Table => "addresses";
    public Key<int> AddressId => Identity();
    public Col<string> City => Length(100);
    public Col<string> Street => Length(200);
    public Col<string?> ZipCode { get; }
}
```

**File:** `src/Quarry.Tests/Samples/UserAddressSchema.cs` (new)
```csharp
public class UserAddressSchema : Schema
{
    public static string Table => "user_addresses";
    public Key<int> UserAddressId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Ref<AddressSchema, int> AddressId => ForeignKey<AddressSchema, int>();
    public One<AddressSchema> Address { get; }
}
```

**File:** `src/Quarry.Tests/Samples/UserSchema.cs` (modify)

Add the HasManyThrough navigation:
```csharp
public Many<UserAddressSchema> UserAddresses => HasMany<UserAddressSchema>(ua => ua.UserId);
public Many<AddressSchema> Addresses
    => HasManyThrough<AddressSchema, UserAddressSchema>(
        junction => junction.UserAddresses,
        through => through.Address);
```

These schemas also need dialect-specific variants in the `Pg`, `My`, and `Ss` namespaces, following the existing pattern for `OrderSchema` and `UserSchema`.

The `QueryTestHarness` must register these new schemas and expose accessor methods (`UserAddresses()`, `Addresses()`). The `DbContext` classes for each dialect namespace need entity accessor registrations.

### Tests

**File:** `src/Quarry.Tests/SqlOutput/CrossDialectHasManyThroughTests.cs` (new)

**Test 1 — Basic HasManyThrough with Any predicate:**
```csharp
[Test]
public async Task HasManyThrough_Any_WithPredicate()
{
    await using var t = await QueryTestHarness.CreateAsync();
    var (Lite, Pg, My, Ss) = t;

    var lt = Lite.Users().Where(u => u.Addresses.Any(a => a.City == "Portland"))
        .Select(u => u.UserName).ToDiagnostics();
    // Expected SQL (SQLite):
    // SELECT "UserName" FROM "users" AS "t0"
    // WHERE EXISTS (SELECT 1 FROM "user_addresses" AS "sq0"
    //   INNER JOIN "addresses" AS "j0" ON "sq0"."AddressId" = "j0"."AddressId"
    //   WHERE "sq0"."UserId" = "t0"."UserId" AND "j0"."City" = @p0)
}
```

**Test 2 — HasManyThrough with Count:**
```csharp
[Test]
public async Task HasManyThrough_Count()
{
    var lt = Lite.Users()
        .Where(u => u.Addresses.Count() > 2)
        .Select(u => u.UserName).ToDiagnostics();
    // Expected: EXISTS replaced by COUNT(*) subquery with > 2 condition
}
```

**Test 3 — HasManyThrough combined with direct navigation:**
```csharp
[Test]
public async Task HasManyThrough_CombinedWithOneNavigation()
{
    // Combine skip-navigation in Where with One<T> in OrderBy
    var lt = Lite.Orders()
        .Where(o => o.User!.Addresses.Any(a => a.City == "Portland"))
        .Select(o => o.Total).ToDiagnostics();
    // Two levels of join: outer implicit join to users, inner subquery with junction join
}
```

---

## Phase 5: Add 5-Table and 6-Table Join Integration Tests

### Problem

The T4-generated interfaces (`IJoinedQueryBuilder5`, `IJoinedQueryBuilder6`) compile but no tests exercise them. The existing `JoinedCarrierIntegrationTests.cs` covers up to 4-table joins.

### Test Entity Setup

Two additional test entities are needed to reach 6-table chains. Options:

**File:** `src/Quarry.Tests/Samples/WarehouseSchema.cs` (new)
```csharp
public class WarehouseSchema : Schema
{
    public static string Table => "warehouses";
    public Key<int> WarehouseId => Identity();
    public Col<string> WarehouseName => Length(100);
    public Col<string> Region => Length(50);
}
```

**File:** `src/Quarry.Tests/Samples/ShipmentSchema.cs` (new)
```csharp
public class ShipmentSchema : Schema
{
    public static string Table => "shipments";
    public Key<int> ShipmentId => Identity();
    public Ref<OrderSchema, int> OrderId => ForeignKey<OrderSchema, int>();
    public Ref<WarehouseSchema, int> WarehouseId => ForeignKey<WarehouseSchema, int>();
    public Col<DateTime> ShipDate { get; }
}
```

The `QueryTestHarness` needs these entities registered and exposed. The harness's SQLite schema creation must include `CREATE TABLE` statements for `warehouses` and `shipments`.

### Tests

**File:** `src/Quarry.Tests/Integration/JoinedCarrierIntegrationTests.cs` (modify)

**Test 1 — 5-table join:**
```csharp
[Test]
public async Task JoinedCarrier_FiveTable_CrossDialect()
{
    await using var t = await QueryTestHarness.CreateAsync();
    var (Lite, Pg, My, Ss) = t;

    // Users → Orders → OrderItems → Shipments → Warehouses
    var lt = Lite.Users()
        .Join<Order>((u, o) => u.UserId.Id == o.UserId.Id)
        .Join<OrderItem>((u, o, i) => o.OrderId.Id == i.OrderId.Id)
        .Join<Shipment>((u, o, i, s) => o.OrderId.Id == s.OrderId.Id)
        .Join<Warehouse>((u, o, i, s, w) => s.WarehouseId.Id == w.WarehouseId.Id)
        .Where((u, o, i, s, w) => w.Region == "US")
        .Select((u, o, i, s, w) => (u.UserName, o.Total, i.ProductName, s.ShipDate, w.WarehouseName))
        .ToDiagnostics();
    // Verify 4 JOIN clauses, 5 table aliases, correct column qualification
}
```

**Test 2 — 6-table join:**
Similar to 5-table but adds one more join. May need one more test entity (e.g., `CategorySchema`).

These tests exercise the T4-generated `IJoinedQueryBuilder5<T1,T2,T3,T4,T5>` and `IJoinedQueryBuilder6<T1,T2,T3,T4,T5,T6>` interfaces and verify that the carrier emitter and interceptor code generator correctly handle the extended arity.

---

## Phase 6: Improve Parser Precision for NavigationAccessExpr

### Problem

`SqlExprParser.ParseMemberAccess` (lines 170-176) emits `NavigationAccessExpr` for ALL chained member access that isn't `.Id`, `.Value`, or `.HasValue`. This means `o.UserName.Length` (a `.Length` call on a string column) produces `NavigationAccessExpr("o", ["UserName"], "Length")` instead of the expected `SqlRawExpr`. The binder gracefully falls back to a SQL comment (`/* unresolved navigation: 'UserName' is not a One<T> navigation */`), but the error message is misleading and the parser does unnecessary work.

The false positive set includes any .NET member accessed on a column: `.Length`, `.Count`, `.Year`, `.Month`, `.Day`, `.ToString()`, `.GetType()`, etc.

### Constraint

The parser operates purely at the syntax level — it has no access to the semantic model or entity metadata. It cannot determine whether a property is actually a `One<T>` navigation. Any filtering must be heuristic.

### Solution: Known .NET Member Exclusion List

**File:** `src/Quarry.Generator/IR/SqlExprParser.cs`

Add a static `HashSet<string>` of known .NET member names that should NOT be treated as navigation hops:

```csharp
private static readonly HashSet<string> KnownDotNetMembers = new(StringComparer.Ordinal)
{
    // String members
    "Length", "Chars",
    // DateTime/DateOnly/TimeOnly members
    "Year", "Month", "Day", "Hour", "Minute", "Second", "Millisecond",
    "Date", "TimeOfDay", "DayOfWeek", "DayOfYear", "Ticks",
    // Nullable members (already handled but for safety)
    "Value", "HasValue",
    // Common .NET members
    "MaxValue", "MinValue", "Empty",
};
```

At line 170 (the fallthrough in `ParseMemberAccess`), before emitting `NavigationAccessExpr`, check if the `memberName` is in the exclusion list:

```csharp
// Check if this is a known .NET member (not a navigation)
if (KnownDotNetMembers.Contains(propAccess.PropertyName) ||
    KnownDotNetMembers.Contains(memberName))
{
    // Not a navigation — return raw SQL for the full expression
    return new SqlRawExpr(node.ToString());
}

// Potential One<T> navigation — o.User.UserName
return new NavigationAccessExpr(
    sourceParameterName: propAccess.ParameterName,
    navigationHops: new[] { propAccess.PropertyName },
    finalPropertyName: memberName,
    finalNestedProperty: null);
```

The check examines both the hop name (`propAccess.PropertyName`, e.g., "UserName" in `o.UserName.Length`) and the final member name (`memberName`, e.g., "Length"). If either is a known .NET member, the expression is treated as a raw SQL expression.

This is conservative — it may still produce `NavigationAccessExpr` for user-defined properties with uncommon names. The binder's fallback remains the safety net. The list can be expanded as false positives are discovered.

Also check navigations being extended (line 179-204): when the inner expression is already a `NavigationAccessExpr`, the same exclusion should apply to the new member name being appended. If `memberName` is in `KnownDotNetMembers`, the chain should be treated as raw SQL rather than extending the navigation.

### Tests

Add tests in a parser unit test class that verify:
- `o.UserName.Length` → NOT a `NavigationAccessExpr` (returns `SqlRawExpr`)
- `o.OrderDate.Year` → NOT a `NavigationAccessExpr`
- `o.User.UserName` → IS a `NavigationAccessExpr` (User is not in the exclusion list)
- `o.User.Department.Name` → IS a `NavigationAccessExpr` (deep chain preserved)

---

## Implementation Order

| Phase | Depends On | Files Modified |
|-------|-----------|----------------|
| **1** | — | `ProjectionInfo.cs`, `ProjectionAnalyzer.cs`, `ImplicitJoinHelper.cs` (new), `SqlExprBinder.cs` (refactor), `ChainAnalyzer.cs`, `CrossDialectNavigationJoinTests.cs` |
| **2** | Phase 1 | `ChainAnalyzer.cs`, `CrossDialectNavigationJoinTests.cs` (update expected SQL) |
| **3** | — | `EntityInfo.cs`, `SchemaParser.cs`, `QuarryGenerator.cs`, new diagnostic test class |
| **4** | — | `AddressSchema.cs` (new), `UserAddressSchema.cs` (new), `UserSchema.cs`, `QueryTestHarness`, `CrossDialectHasManyThroughTests.cs` (new) |
| **5** | — | `WarehouseSchema.cs` (new), `ShipmentSchema.cs` (new), `QueryTestHarness`, `JoinedCarrierIntegrationTests.cs` |
| **6** | — | `SqlExprParser.cs`, parser unit tests |

Phases 3-6 are independent of each other and of Phases 1-2. However, Phase 2 should be done immediately after Phase 1 since the existing navigation join tests will need updated expected SQL once columns are table-qualified.
