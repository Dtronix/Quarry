# Implementation Plan: Navigation Joins (`One<T>`), `HasManyThrough`, and Explicit Join Extension to 6 Tables

**Scope:** Runtime types + Generator pipeline + T4 codegen infrastructure
**Branch:** `feature/navigation-joins`

---

## Problem Summary

Quarry's join system is limited in two ways. First, all joins require explicit `.Join<T>()` / `.LeftJoin<T>()` calls with manual ON conditions, even when the FK relationship is already declared in the schema via `Ref<T,K>`. This forces users to repeat relationship knowledge the generator already has. Second, explicit join chains are capped at 4 tables due to hand-written `IJoinedQueryBuilder` / `IJoinedQueryBuilder3` / `IJoinedQueryBuilder4` interfaces (6 interfaces total, ~245 lines of near-identical stubs). Extending this limit currently means hand-writing more boilerplate.

This plan adds three features that compose together:

| Feature | Description |
|---------|-------------|
| **A. `One<T>` navigation joins** | Dot-access through FK relationships in Where/Select/OrderBy triggers implicit JOINs |
| **B. `HasManyThrough` skip-navigation** | Junction table traversal without exposing the junction entity in queries |
| **C. Explicit join extension to 6 tables** | T4-generated interfaces replace hand-written stubs; max arity becomes a single constant |

---

## Feature A: `One<T>` Navigation Joins

### Concept

`One<T>` is a new schema marker type that declares a singular (N:1) navigation from the current entity to a target entity. When a user accesses a property through a `One<T>` navigation in a query lambda, the generator detects this at the syntax level and emits an implicit JOIN in the generated SQL. The join type (INNER vs LEFT) is inferred from the nullability of the underlying FK column.

### Schema API

`One<T>` mirrors `Many<T>` as a readonly struct with a `where T : Schema` constraint. It carries no runtime state and exists solely as a compile-time marker for the source generator. Unlike `Many<T>`, which is a collection navigation (1:N) and produces subqueries via `.Any()` / `.Count()`, `One<T>` is a singular navigation (N:1) and produces JOINs via dot-access to the target entity's properties.

**FK association** is resolved by the generator, not declared in the `One<T>` type itself. When the generator encounters `One<TTarget>` on a schema, it scans the same schema's columns for `Ref<TTarget, K>` columns. If exactly one exists, the association is automatic. If zero or more than one exist, the generator requires explicit disambiguation via `HasOne<T>(nameof(FkColumn))` and reports a diagnostic error otherwise.

The `HasOne<T>(string foreignKeyPropertyName)` method is added to the `Schema` base class alongside `HasMany<T>()`. It takes a `string` parameter (intended for use with `nameof()`) identifying which `Ref` column on the current entity should be used for the join. An `OneBuilder<T>` type (paralleling `RelationshipBuilder<T>`) provides the implicit conversion target for the property expression body. This builder is a zero-allocation readonly struct following the same pattern as `ColumnBuilder<T>`, `RefBuilder<T,K>`, and `RelationshipBuilder<T>`: all configuration is extracted from the syntax tree at compile time, and no runtime state is needed.

```csharp
// Common case — single Ref<UserSchema, K> exists, auto-detected:
public class OrderSchema : Schema
{
    public static string Table => "orders";
    public Key<int> OrderId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public One<UserSchema> User { get; }      // FK inferred: UserId
    public Col<decimal> Total { get; }
}

// Disambiguation — multiple Refs to same target entity:
public class AuditSchema : Schema
{
    public static string Table => "audit_log";
    public Key<int> AuditId => Identity();
    public Ref<UserSchema, int> CreatedById => ForeignKey<UserSchema, int>();
    public Ref<UserSchema, int> UpdatedById => ForeignKey<UserSchema, int>();
    public One<UserSchema> CreatedBy => HasOne<UserSchema>(nameof(CreatedById));
    public One<UserSchema> UpdatedBy => HasOne<UserSchema>(nameof(UpdatedById));
}
```

### Join Type Inference

The join type is determined by the nullability of the associated `Ref<T,K>` column:

| FK Declaration | Inferred Join | Rationale |
|---|---|---|
| `Ref<UserSchema, int> UserId` | INNER JOIN | Non-nullable FK guarantees the referenced row exists |
| `Ref<UserSchema, int?> UserId` | LEFT JOIN | Nullable FK means the referenced row may not exist |

This mirrors SQL semantics: an INNER JOIN is safe when the FK has a NOT NULL constraint, while a LEFT JOIN is required when the FK allows NULLs to avoid dropping rows with no match. Users who want to override this inference (e.g., LEFT JOIN on a non-nullable FK for defensive queries) can always fall back to the explicit `.LeftJoin<T>()` API.

### Query API

Navigation joins are triggered by dot-access through a `One<T>` property in any expression lambda (Where, Select, OrderBy, GroupBy, Having). The user never writes `.Join<T>()`; the generator detects the navigation access in the syntax tree and injects the appropriate JOIN clause.

```csharp
// Implicit INNER JOIN — User.IsActive triggers join to users table
var activeOrders = await db.Orders()
    .Where(o => o.User.IsActive && o.Total > 100)
    .Select(o => (o.OrderId, o.User.UserName, o.Total))
    .OrderBy(o => o.User.UserName)
    .ExecuteFetchAllAsync();

// Generated SQL:
// SELECT t0."OrderId", j0."UserName", t0."Total"
// FROM orders AS t0
// INNER JOIN users AS j0 ON t0."UserId" = j0."UserId"
// WHERE j0."IsActive" = 1 AND t0."Total" > @p0
// ORDER BY j0."UserName" ASC
```

Navigation joins compose with explicit joins without affecting lambda arity. The implicit join is purely additive to the FROM clause:

```csharp
db.Orders()
    .Join<Warehouse>((o, w) => o.WarehouseId == w.WarehouseId.Id)
    .Where((o, w) => o.User.IsActive && w.Region == "US")
    .Select((o, w) => (o.User.UserName, w.Name, o.Total))
    .ExecuteFetchAllAsync();

// Generated SQL:
// SELECT j0."UserName", t1."Name", t0."Total"
// FROM orders AS t0
// INNER JOIN warehouses AS t1 ON t0."WarehouseId" = t1."WarehouseId"
// INNER JOIN users AS j0 ON t0."UserId" = j0."UserId"
// WHERE j0."IsActive" = 1 AND t1."Region" = @p0
```

Navigation joins also compose inside subquery predicates. When `One<T>` is traversed inside a `Many<T>.Any()` / `.Count()` predicate, the implicit join is added to the subquery's FROM clause, not the outer query:

```csharp
// Junction table traversal inside subquery
db.Users()
    .Where(u => u.UserAddresses.Any(ua => ua.Address.City == "Portland"))
    .Select(u => (u.UserId, u.UserName))
    .ExecuteFetchAllAsync();

// Generated SQL:
// SELECT t0."UserId", t0."UserName"
// FROM users AS t0
// WHERE EXISTS (
//     SELECT 1 FROM user_addresses AS sq0
//     INNER JOIN addresses AS j0 ON sq0."AddressId" = j0."AddressId"
//     WHERE sq0."UserId" = t0."UserId" AND j0."City" = @p0
// )
```

Deep navigation chains are supported when each level has a `One<T>` navigation: `o.User.Department.Company.Name` would produce three chained JOINs. Each level resolves independently via the same mechanism.

### Deduplication

When the same navigation is traversed in multiple clauses of the same chain (e.g., `o.User.IsActive` in Where and `o.User.UserName` in Select), the generator must reuse the same join alias rather than emitting duplicate JOINs. Deduplication is keyed on `(sourceEntityAlias, fkColumnName, targetEntityName)`: if a prior implicit join with the same key exists in the current context, the binder returns the existing alias instead of allocating a new one.

---

## Feature A: Implementation Details

### A1. New Runtime Types

#### File: `src/Quarry/Schema/One.cs` (new)

A readonly struct mirroring `Many<T>`. Where `Many<T>` has subquery marker methods (`.Any()`, `.Count()`), `One<T>` has no methods — navigation is expressed through property access on the target entity, which the generator handles at the syntax level. The struct's only purpose is to type the schema property so the generator can detect it.

The struct needs an implicit conversion from `OneBuilder<T>` so that expression-bodied properties like `public One<UserSchema> User => HasOne<UserSchema>(nameof(UserId))` compile. For the auto-detected case (`public One<UserSchema> User { get; }`), the struct's default value is used and no conversion is needed.

```csharp
public readonly struct One<T> where T : Schema
{
    public static implicit operator One<T>(OneBuilder<T> builder) => default;
}
```

#### File: `src/Quarry/Schema/OneBuilder.cs` (new)

A minimal readonly struct following the same zero-allocation pattern as `RelationshipBuilder<T>` and `RefBuilder<T,K>`. It exists solely as the return type of `HasOne<T>()` to enable the implicit conversion to `One<T>`. No methods are needed initially; the builder exists to make the expression `HasOne<UserSchema>(nameof(UserId))` return a type convertible to `One<UserSchema>`.

```csharp
public readonly struct OneBuilder<T> where T : Schema { }
```

#### File: `src/Quarry/Schema/Schema.cs` (modify)

Add `HasOne<T>` to the Relationship Modifiers region, after `HasMany<T>`:

```csharp
protected static OneBuilder<T> HasOne<T>(string foreignKeyPropertyName) where T : Schema
    => default;
```

The `string` parameter will be parsed from the syntax tree by the generator. Users are expected to pass `nameof(FkColumn)` for compile-time safety, but any string constant works. The generator extracts the argument value from the `InvocationExpressionSyntax.ArgumentList`.

### A2. New Model Type: `SingleNavigationInfo`

#### File: `src/Quarry.Generator/Models/SingleNavigationInfo.cs` (new)

This model parallels `NavigationInfo` (which represents `Many<T>` relationships) but represents `One<T>` (N:1) relationships. It stores the information the generator needs to emit implicit JOINs when the navigation is traversed.

Fields:

| Field | Type | Description |
|-------|------|-------------|
| `PropertyName` | `string` | The `One<T>` property name on the schema (e.g., "User") |
| `TargetEntityName` | `string` | The target entity type name, normalized from `TSchema` (e.g., "User" from "UserSchema") |
| `ForeignKeyPropertyName` | `string` | The `Ref<T,K>` property name on the current entity (e.g., "UserId") |
| `IsNullableFk` | `bool` | Whether the FK column is nullable, determining INNER vs LEFT join |

The class implements `IEquatable<SingleNavigationInfo>` for incremental caching, following the pattern established by `NavigationInfo`, `ColumnInfo`, and all other pipeline models.

#### File: `src/Quarry.Generator/Models/EntityInfo.cs` (modify)

Add a new property alongside the existing `Navigations` list:

```csharp
public IReadOnlyList<SingleNavigationInfo> SingleNavigations { get; }
```

Update the constructor to accept and store the list. Update the `Equals` and `GetHashCode` implementations to include the new field. The `SingleNavigations` list is populated by `SchemaParser` during schema analysis and consumed by `SqlExprBinder` during column resolution.

### A3. Schema Parsing

#### File: `src/Quarry.Generator/Parsing/SchemaParser.cs` (modify)

Add a `TryParseSingleNavigation` method following the pattern of `TryParseNavigation` (lines 865-892). The method detects `One<T>` properties in exactly the same way `TryParseNavigation` detects `Many<T>`: check that the property's type symbol name is `"One"`, is generic, and has exactly one type argument. Extract `TargetEntityName` via `NormalizeEntityName()` on the type argument.

**FK resolution algorithm:**

The FK association between a `One<T>` navigation and a `Ref<T,K>` column is resolved in `TryParseSingleNavigation` after all columns on the schema have been parsed. The algorithm has two paths:

**Explicit path** (property has expression body invoking `HasOne`): Parse the invocation argument to extract the FK property name string. The extraction follows the same syntax-tree pattern as `ExtractForeignKeyPropertyName` for `HasMany`, but instead of parsing a lambda body, it extracts the string literal from the first argument. When the user writes `HasOne<UserSchema>(nameof(CreatedById))`, the Roslyn syntax tree contains a `LiteralExpressionSyntax` with the string value `"CreatedById"` (since `nameof()` is folded to a string constant at compile time). Alternatively, the argument may be an `InvocationExpressionSyntax` with identifier `"nameof"` if the semantic model hasn't folded it yet; in that case, extract the argument identifier text directly.

**Auto-detect path** (property has no expression body — `{ get; }` syntax): Scan all `ColumnInfo` objects on the current schema where `Kind == ColumnKind.ForeignKey` and `ReferencedEntityName` matches the target entity name extracted from `One<T>`. If exactly one matching column is found, use its `PropertyName` as the FK. If zero matches are found, report a diagnostic (new QRY code: "No Ref<{Target}, K> column found for One<{Target}> navigation '{PropertyName}'"). If more than one match is found, report a diagnostic: "Ambiguous FK for One<{Target}> navigation '{PropertyName}': multiple Ref<{Target}, K> columns found ({list}). Use HasOne<{Target}>(nameof(column)) to disambiguate."

**FK nullability**: After resolving the FK property name, look up the `ColumnInfo` by that name and check `IsNullable`. Store the result in `SingleNavigationInfo.IsNullableFk`.

### A4. Entity Code Generation

#### File: `src/Quarry.Generator/Generation/EntityCodeGenerator.cs` (modify)

Add a new loop after the existing navigation property generation (which emits `NavigationList<T>` for `Many<T>` navigations). For each `SingleNavigationInfo` on the entity, emit a nullable reference property for the target entity:

```csharp
public User? User { get; internal set; }
```

The property is nullable because the navigation is not populated unless the query includes a join to that entity. The `internal set` accessor follows the same pattern as `NavigationList<T>` properties. The property type is the target entity's generated class name (e.g., `User` from `SingleNavigationInfo.TargetEntityName`).

These properties exist on the entity class for discoverability and IntelliSense, but the generator does not populate them at query time in the initial implementation. Population of navigation properties from joined result sets is a separate feature that can be added later. For this plan, the entity property exists to make the syntax `o.User.UserName` resolve at compile time. The generator intercepts the lambda expression before any runtime navigation occurs.

### A5. New SqlExpr Node: `NavigationAccessExpr`

#### File: `src/Quarry.Generator/IR/SqlExprNodes.cs` (modify)

Add a new `SqlExpr` node type that represents property access through a `One<T>` navigation. This node is emitted by `SqlExprParser` when it detects a member access chain that traverses a `One<T>` property, and is later resolved by `SqlExprBinder` into a `ResolvedColumnExpr` with the correct join alias.

Fields:

| Field | Type | Description |
|-------|------|-------------|
| `SourceParameterName` | `string` | The lambda parameter that owns the navigation (e.g., "o") |
| `NavigationPropertyName` | `string` | The `One<T>` property name (e.g., "User") |
| `TargetPropertyName` | `string` | The property accessed on the target entity (e.g., "UserName") |
| `TargetNestedProperty` | `string?` | For chained access like `o.User.DeptId.Id` (Ref.Id on the target) |
| `IsResolved` | `bool` | False when created by parser, true after binder resolution |
| `ResolvedColumnExpr` | `ResolvedColumnExpr?` | Set by binder — the final qualified column reference |

Equality includes all fields for incremental caching. The unresolved form (`IsResolved = false`) is emitted by the parser; the resolved form (`IsResolved = true`, with `ResolvedColumnExpr` set) is emitted by the binder.

For **deep navigation chains** like `o.User.Department.Company.Name`, the parser produces nested `NavigationAccessExpr` nodes. The outermost node has `SourceParameterName = "o"`, `NavigationPropertyName = "User"`, and its `TargetPropertyName` is itself represented as another `NavigationAccessExpr` (or a `ColumnRefExpr` at the leaf). Alternatively, the parser can flatten the chain into a list of navigation hops — the binder processes them left-to-right, each hop allocating a join alias and feeding it as the source for the next hop. The flat representation is simpler to bind and is recommended.

A flat representation uses a list of hops:

| Field | Type | Description |
|-------|------|-------------|
| `SourceParameterName` | `string` | The lambda parameter that starts the chain |
| `NavigationHops` | `IReadOnlyList<string>` | Sequence of navigation property names traversed (e.g., ["User", "Department", "Company"]) |
| `FinalPropertyName` | `string` | The leaf property on the final entity (e.g., "Name") |
| `FinalNestedProperty` | `string?` | For Ref.Id access on the leaf |

The binder resolves each hop left-to-right, looking up `SingleNavigationInfo` on the current entity, registering an implicit join, and advancing to the target entity for the next hop.

### A6. SqlExprParser Changes

#### File: `src/Quarry.Generator/IR/SqlExprParser.cs` (modify)

The `ParseMemberAccess` method (lines 122-191) needs a new code path for `One<T>` navigation traversal. Currently, chained member access like `o.UserId.Id` is handled by recursively parsing the inner expression and then checking for `.Id`, `.Value`, or `.HasValue` on the result. The new path detects when a member access chain goes through a `One<T>` property.

The challenge is that `SqlExprParser` works purely at the syntax level — it does not have access to the semantic model or `EntityInfo`. It cannot look up whether `o.User` is a `One<T>` navigation. However, the parser can detect multi-level member access chains and delegate classification to a later stage.

**Approach**: The parser treats any member access chain deeper than 2 levels (e.g., `o.User.UserName` where the inner expression resolves to a `ColumnRefExpr`) as a potential navigation access. It emits a `NavigationAccessExpr` with the full chain. The binder then validates whether the intermediate property is actually a `One<T>` navigation on the entity. If it is, the binder resolves it to an implicit join. If it isn't, the binder reports a diagnostic error.

In concrete terms, the new code sits in the chained member access handling block (after line 145 in `ParseMemberAccess`). After recursively parsing the inner expression and getting a `ColumnRefExpr` back, the current code checks for `memberName == "Id"`, `"Value"`, or `"HasValue"`. The new code adds an `else` branch: if the inner result is a `ColumnRefExpr` and the member name is not one of the special Ref/Nullable cases, emit a `NavigationAccessExpr`:

```csharp
// Existing: o.UserId.Id → ColumnRefExpr with nestedProperty
if (memberName == "Id")
    return new ColumnRefExpr(propAccess.ParameterName, propAccess.PropertyName, nestedProperty: "Id");

// Existing: Nullable<T>.Value → unwrap
if (memberName == "Value")
    return propAccess;

// Existing: Nullable<T>.HasValue → IS NOT NULL
if (memberName == "HasValue")
    return new IsNullCheckExpr(propAccess, isNegated: true);

// NEW: potential One<T> navigation — o.User.UserName
// propAccess is ColumnRefExpr("o", "User"), memberName is "UserName"
return new NavigationAccessExpr(
    sourceParameterName: propAccess.ParameterName,
    navigationHops: new[] { propAccess.PropertyName },
    finalPropertyName: memberName,
    finalNestedProperty: null);
```

For deeper chains (`o.User.Department.Name`), the recursive parsing naturally builds up the chain: `o.User` → `ColumnRefExpr("o", "User")`, then `o.User.Department` → `NavigationAccessExpr("o", ["User"], "Department")`, then `o.User.Department.Name` → the parser detects the inner expression is already a `NavigationAccessExpr` and extends its hop list:

```csharp
if (innerExpr is NavigationAccessExpr navAccess)
{
    // Extend the chain: add the previous finalPropertyName as a hop,
    // and set the new memberName as the new finalPropertyName
    var extendedHops = navAccess.NavigationHops.Append(navAccess.FinalPropertyName).ToList();
    return new NavigationAccessExpr(
        sourceParameterName: navAccess.SourceParameterName,
        navigationHops: extendedHops,
        finalPropertyName: memberName,
        finalNestedProperty: null);
}
```

This ensures arbitrarily deep chains are represented as a flat list of hops in a single `NavigationAccessExpr`.

**Subquery context**: When `NavigationAccessExpr` appears inside a subquery predicate (e.g., `ua.Address.City` inside `u.UserAddresses.Any(ua => ua.Address.City == "Portland")`), no special handling is needed in the parser. The parser emits the same `NavigationAccessExpr` regardless of context. The binder handles the context difference by registering the implicit join in the subquery's join list rather than the outer query's join list.

### A7. SqlExprBinder Changes

#### File: `src/Quarry.Generator/IR/SqlExprBinder.cs` (modify)

This is the most significant change in the pipeline. The binder resolves `NavigationAccessExpr` nodes into `ResolvedColumnExpr` nodes by looking up the navigation metadata, registering implicit joins, and mapping the final property to a qualified column reference.

**New state on BindContext**: Add an `ImplicitJoins` list to `BindContext`. This list accumulates `ImplicitJoinInfo` records as the binder encounters `NavigationAccessExpr` nodes during tree traversal:

```csharp
internal class ImplicitJoinInfo
{
    public string SourceAlias;        // The alias of the source table (e.g., "t0" or "sq0")
    public string FkColumnQuoted;     // The FK column on the source table (e.g., "\"UserId\"")
    public string TargetTableQuoted;  // The target table name (e.g., "\"users\"")
    public string TargetSchemaQuoted; // The target schema name (e.g., "\"public\""), nullable
    public string TargetAlias;        // The alias assigned to this join (e.g., "j0")
    public string TargetPkColumnQuoted; // The PK column on the target table (e.g., "\"UserId\"")
    public JoinClauseKind JoinKind;   // INNER or LEFT, based on FK nullability
}
```

The `ImplicitJoins` list is carried through the bind context and passed upward to the caller (`CallSiteTranslator` or the subquery binder) so it can be incorporated into the `TranslatedClause` or the subquery rendering.

**Deduplication state**: Add a `Dictionary<(string sourceAlias, string fkColumn, string targetEntity), string>` to `BindContext` that maps join keys to their allocated aliases. Before creating a new implicit join, the binder checks this dictionary. If a matching entry exists, the binder returns the existing alias without creating a duplicate join.

**New method: `BindNavigationAccess`**:

This method processes a `NavigationAccessExpr` node. It iterates through the navigation hops left-to-right:

1. **Look up the current entity**: Start from the primary entity (if `SourceParameterName == ctx.LambdaParameterName`) or a joined entity (if the source parameter is in `ctx.JoinedEntities`). Determine the source table alias from `ctx.TableAliases`.

2. **For each hop in `NavigationHops`**: Look up the `SingleNavigationInfo` on the current entity where `PropertyName` matches the hop name. If not found, this is not a valid navigation — return an error. If found, check the deduplication dictionary. If a matching implicit join already exists, use its alias. Otherwise, allocate a new alias (`j0`, `j1`, ...) by incrementing a counter on the bind context, and record the join in `ImplicitJoins`. Then look up the target entity from `ctx.EntityLookup` by `TargetEntityName`. This becomes the "current entity" for the next hop.

3. **Resolve the final property**: After all hops, the current entity is the target of the last navigation. Look up `FinalPropertyName` on the target entity's columns. Resolve to a `ResolvedColumnExpr` with the column's quoted name and the last join's alias as the table qualifier.

4. **Handle `FinalNestedProperty`**: If set (e.g., for `o.User.DeptRef.Id`), apply the same `.Id` handling as `BindColumnRef` does for `Ref<T,K>.Id`.

5. **Handle boolean context**: If the resolved column is a boolean and `inBooleanContext` is true, apply the same `= 1` / `= TRUE` wrapping as `BindColumnRef`.

**Subquery integration**: When binding a subquery predicate (inside `BindSubquery`), the child `BindContext` already has its own entity and alias state. The `ImplicitJoins` list on the child context is separate from the parent context. Any `NavigationAccessExpr` nodes inside the subquery predicate register their implicit joins on the child context. After the predicate is bound, the child context's `ImplicitJoins` list is attached to the `SubqueryExpr` so the renderer can include them.

**Alias namespace**: Implicit join aliases use the `j0`, `j1`, `j2` prefix to avoid collision with explicit join aliases (`t0`, `t1`, `t2`) and subquery aliases (`sq0`, `sq1`, `sq2`). The counter is scoped to the bind context, so outer query implicit joins and subquery implicit joins have independent counters. If a subquery has its own implicit joins, those aliases are `j0`, `j1` relative to that subquery — no conflict since they're rendered inside the subquery's FROM clause.

### A8. SqlExprClauseTranslator Changes

#### File: `src/Quarry.Generator/IR/SqlExprClauseTranslator.cs` (modify)

Add a case for `NavigationAccessExpr` in the parameter extraction visitor. Since `NavigationAccessExpr` nodes do not contain captured values or parameters themselves (they resolve to column references), the handler simply recurses into any child expressions and returns the node unchanged. However, if the node has been resolved (after binding), it contains a `ResolvedColumnExpr` which also has no parameters.

The main change is ensuring the visitor does not throw an `UnexpectedNodeType` exception when encountering `NavigationAccessExpr`. The handler is a pass-through:

```csharp
case NavigationAccessExpr nav:
    return nav; // No parameters to extract; binder handles resolution
```

### A9. SqlExprRenderer Changes

#### File: `src/Quarry.Generator/IR/SqlExprRenderer.cs` (modify)

Add rendering for `NavigationAccessExpr`. In the resolved case, the node's `ResolvedColumnExpr` is rendered directly (qualified column name with join alias). In the unresolved case, emit a diagnostic comment (`/* unresolved navigation: o.User.Name */`).

### A10. SubqueryExpr Extension for Implicit Joins

#### File: `src/Quarry.Generator/IR/SqlExprNodes.cs` (modify)

Extend the resolved `SubqueryExpr` constructor to accept an optional `IReadOnlyList<ImplicitJoinInfo>` field. When the subquery predicate contains `One<T>` traversals, the binder populates this list from the child bind context's `ImplicitJoins`.

#### File: `src/Quarry.Generator/IR/SqlExprRenderer.cs` (modify)

Extend `RenderSubquery` to emit implicit JOIN clauses between the `FROM table AS alias` and the `WHERE correlation` parts of the subquery:

The current rendering is:
```
EXISTS (SELECT 1 FROM [table] AS [alias] WHERE [correlation] AND [predicate])
```

With implicit joins:
```
EXISTS (SELECT 1 FROM [table] AS [alias]
    INNER JOIN [target] AS [j0] ON [alias].[fk] = [j0].[pk]
    WHERE [correlation] AND [predicate])
```

The renderer iterates the `ImplicitJoins` list and emits each join clause between FROM and WHERE, using the same format as `SqlAssembler.RenderSelectSql`'s explicit join rendering (lines 177-194 of SqlAssembler.cs).

### A11. TranslatedClause Extension

#### File: `src/Quarry.Generator/IR/TranslatedCallSite.cs` (modify)

Add an `ImplicitJoins` field to `TranslatedClause`:

```csharp
public IReadOnlyList<ImplicitJoinInfo>? ImplicitJoins { get; }
```

This field is populated by `CallSiteTranslator` after binding, carrying the bind context's implicit joins forward to the assembly stage. The field participates in equality comparison for incremental caching.

### A12. CallSiteTranslator Changes

#### File: `src/Quarry.Generator/IR/CallSiteTranslator.cs` (modify)

After calling `SqlExprBinder.Bind()`, extract the `ImplicitJoins` list from the bind context and pass it to the `TranslatedClause` constructor. The translator already creates the `TranslatedClause` with the bound expression and parameter list; this adds one more field.

The `EntityLookup` dictionary (needed by the binder to resolve target entities by name) is already available via the `EntityRegistry` that is combined into the pipeline via `.Combine(entityRegistry)`. The translator passes it as the `entityLookup` parameter to `SqlExprBinder.Bind()`, which is already an existing parameter (used for subquery entity resolution). The same lookup is now used for `One<T>` navigation resolution.

### A13. QueryPlan and SqlAssembler Changes

#### File: `src/Quarry.Generator/IR/QueryPlan.cs` (modify)

The `JoinPlan` class already has an `IsNavigationJoin` boolean field (line 158), which suggests this feature was anticipated. Implicit joins from `One<T>` navigations are added to the `QueryPlan.Joins` list alongside explicit joins. The `IsNavigationJoin` flag is set to `true` for these joins so that downstream stages can distinguish them from explicit joins if needed.

#### File: `src/Quarry.Generator/IR/SqlAssembler.cs` (modify)

The `ChainAnalyzer` (or `PipelineOrchestrator`) collects all `ImplicitJoins` from the chain's translated clause sites and merges them (with deduplication) into the `QueryPlan.Joins` list. The `SqlAssembler` renders them using the existing JOIN rendering code (lines 177-194 of `RenderSelectSql`). No changes to the rendering code itself — the existing loop over `plan.Joins` handles both explicit and implicit joins uniformly.

The alias assignment for implicit joins differs: explicit joins use `t1`, `t2`, etc. (auto-assigned from index), while implicit joins carry their alias (`j0`, `j1`) from the binder. The assembler uses `join.Table.Alias` (which is already preferred over auto-assignment when set) to render the correct alias.

### A14. ChainAnalyzer / PipelineOrchestrator Changes

#### File: `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` (modify)

When building the `QueryPlan` from analyzed chain sites, collect all `ImplicitJoins` from each site's `TranslatedClause` and merge them into the plan's `Joins` list. Deduplication is by `(sourceAlias, fkColumn, targetEntity)` — the same key used during binding. If multiple clause sites (e.g., Where and Select) reference the same navigation, the binder has already deduplicated within each clause. The chain analyzer performs a second deduplication pass across clauses, since the same navigation may appear in different clauses with independently-assigned aliases. In this cross-clause pass, the analyzer standardizes aliases so that the same navigation uses the same alias throughout the plan.

#### File: `src/Quarry.Generator/IR/PipelineOrchestrator.cs` (modify)

Update `AnalyzeAndGroupTranslated` to pass implicit join information through to the assembly stage. The orchestrator already chains ChainAnalyzer → SqlAssembler → CarrierAnalyzer; the implicit joins flow through via the `QueryPlan.Joins` list.

---

## Feature B: `HasManyThrough` Skip-Navigation

### Concept

`HasManyThrough` allows declaring a Many-to-Many relationship that traverses a junction table without exposing the junction entity in query lambdas. It is syntactic sugar: the generator expands it into a `Many<T>` with an implicit join inside the subquery.

### Schema API

```csharp
public class UserSchema : Schema
{
    public static string Table => "users";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);

    // Direct navigation to junction table
    public Many<UserAddressSchema> UserAddresses => HasMany<UserAddressSchema>(ua => ua.UserId);

    // Skip-navigation through junction table
    public Many<AddressSchema> Addresses
        => HasManyThrough<AddressSchema, UserAddressSchema>(
            junction => junction.UserAddresses,    // Many<T> to junction
            through => through.Address);           // One<T> on junction to target
}
```

`HasManyThrough<TTarget, TJunction>` takes two lambdas. The first selects the `Many<TJunction>` navigation that reaches the junction table. The second selects the `One<TTarget>` navigation on the junction table that reaches the target entity. Both are expression-tree markers parsed syntactically by the generator.

### Runtime Type

Add a new `HasManyThrough` method to `Schema`:

```csharp
protected static RelationshipBuilder<TTarget> HasManyThrough<TTarget, TJunction>(
    Expression<Func<Schema, object?>> junctionNavigation,
    Expression<Func<TJunction, object?>> targetNavigation)
    where TTarget : Schema
    where TJunction : Schema
    => default;
```

The return type is `RelationshipBuilder<TTarget>` so it implicitly converts to `Many<TTarget>` via the existing implicit conversion. The `Many<TTarget>` property type means `.Any()`, `.Count()`, `.All()` all work identically to regular `Many<T>` navigations.

### New Model: `ThroughNavigationInfo`

#### File: `src/Quarry.Generator/Models/ThroughNavigationInfo.cs` (new)

Fields:

| Field | Type | Description |
|-------|------|-------------|
| `PropertyName` | `string` | The skip-navigation property name (e.g., "Addresses") |
| `TargetEntityName` | `string` | The target entity (e.g., "Address") |
| `JunctionEntityName` | `string` | The junction entity (e.g., "UserAddress") |
| `JunctionNavigationName` | `string` | The Many<T> property that reaches the junction (e.g., "UserAddresses") |
| `TargetNavigationName` | `string` | The One<T> property on the junction that reaches the target (e.g., "Address") |

Implements `IEquatable<ThroughNavigationInfo>`.

### EntityInfo Extension

Add `IReadOnlyList<ThroughNavigationInfo> ThroughNavigations` to `EntityInfo`.

### SchemaParser Extension

Add `TryParseThroughNavigation` that recognizes `HasManyThrough<TTarget, TJunction>(...)` invocations. Extract the two lambda arguments: the first is a member access on the current schema (parsed as an identifier text), the second is a member access on `TJunction` (parsed from `SimpleLambdaExpressionSyntax.Body`).

### SqlExprBinder Extension

When resolving a `SubqueryExpr` where the navigation property matches a `ThroughNavigationInfo` (checked by property name on the outer entity), the binder expands the skip-navigation into a two-step resolution:

1. Resolve the junction entity using the `JunctionNavigationName` (like a normal `Many<T>` subquery — correlation on the junction's FK to the outer entity's PK).
2. Add an implicit join inside the subquery from the junction entity to the target entity, using the `TargetNavigationName` (which is a `One<T>` on the junction entity).
3. Bind the predicate in the context of the target entity (not the junction entity), so `a => a.City == "Portland"` resolves columns on Address, not UserAddress.

The resulting SQL is identical to what a user would write manually with `u.UserAddresses.Any(ua => ua.Address.City == "Portland")`, but the user writes `u.Addresses.Any(a => a.City == "Portland")`.

---

## Feature C: Explicit Join Extension to 6 Tables via T4

### Concept

Replace the hand-written `IJoinedQueryBuilder.cs` (245 lines, 6 interfaces) with a T4 template that generates interfaces for any arity from 2 to N. The max arity is a single constant in the template. Setting it to 6 generates 10 interfaces (5 arity levels × 2 variants each: base + projected). A second T4 template generates a helper class for the generator project with arity-dependent lookup methods, eliminating switch statements scattered across emitter files.

### T4 Template: Runtime Interfaces

#### File: `src/Quarry/Query/IJoinedQueryBuilder.tt` (new)

This template replaces `src/Quarry/Query/IJoinedQueryBuilder.cs`. The generated output is `IJoinedQueryBuilder.g.cs`, checked into source. The template uses a single `MaxArity` constant (set to 6) and loops from arity 2 to MaxArity, emitting two interfaces per arity level.

**Template structure:**

The template defines `MaxArity = 6` at the top. For each arity `n` from 2 to MaxArity, it emits:

1. **Base interface** `IJoinedQueryBuilder{suffix}<T1, ..., Tn>` — no projection. Contains: `Select<TResult>()`, `Where()`, `OrderBy()`, `ThenBy()`, `Offset()`, `Limit()`, `Distinct()`, `Join<T{n+1}>()` (if `n < MaxArity`), `LeftJoin<T{n+1}>()` (if `n < MaxArity`), `RightJoin<T{n+1}>()` (if `n < MaxArity`), `ToDiagnostics()`, `Prepare()`.

2. **Projected interface** `IJoinedQueryBuilder{suffix}<T1, ..., Tn, TResult>` — after Select. Contains: `Where()`, `OrderBy()`, `ThenBy()`, `Offset()`, `Limit()`, `Distinct()`, all execution terminals (`ExecuteFetchAllAsync`, `ExecuteFetchFirstAsync`, `ExecuteFetchFirstOrDefaultAsync`, `ExecuteFetchSingleAsync`, `ToAsyncEnumerable`, `ExecuteScalarAsync`), `ToDiagnostics()`, `Prepare()`.

The `suffix` is `""` for arity 2 (preserving backward compatibility: `IJoinedQueryBuilder<T1, T2>`), and the numeric arity for 3+: `IJoinedQueryBuilder3`, `IJoinedQueryBuilder4`, `IJoinedQueryBuilder5`, `IJoinedQueryBuilder6`.

**Lambda arity**: The `Where()` and `OrderBy()` lambdas take all `n` entity parameters: `Func<T1, T2, ..., Tn, bool>`. The `Select<TResult>()` lambda takes all `n` entity parameters plus the result: `Func<T1, T2, ..., Tn, TResult>`. The `Join<T{n+1}>()` condition lambda takes `n+1` parameters: `Func<T1, T2, ..., Tn, T{n+1}, bool>`.

**Join progression**: The base interface at arity `n` has `Join<T{n+1}>()` returning `IJoinedQueryBuilder{n+1}<T1, ..., Tn, T{n+1}>`. At arity `MaxArity`, no `Join` methods are emitted (this is the ceiling).

**Generic constraints**: All entity type parameters have `where T : class` constraints. This matches the current interfaces.

**Default implementation bodies**: All methods have default implementation bodies that throw `InvalidOperationException` with the carrier-not-intercepted message, matching the current pattern. These bodies are never executed at runtime — the source generator replaces every call with an interceptor.

**MSBuild integration**: Add a pre-build target to `src/Quarry/Quarry.csproj` that runs `dotnet-t4` on the template:

```xml
<Target Name="TransformT4" BeforeTargets="BeforeBuild">
  <Exec Command="t4 %(T4Template.Identity)" />
</Target>
<ItemGroup>
  <T4Template Include="Query\IJoinedQueryBuilder.tt" />
</ItemGroup>
```

The generated `.g.cs` file is checked into source control so that consumers of the NuGet package don't need `dotnet-t4` installed. The MSBuild target validates the generated file is up-to-date on build; if the `.tt` file has been modified, it regenerates.

#### File: `src/Quarry/Query/IJoinedQueryBuilder.cs` (delete)

The hand-written file is replaced by the T4-generated `IJoinedQueryBuilder.g.cs`.

### T4 Template: Generator Arity Helpers

#### File: `src/Quarry.Generator/CodeGen/JoinArityHelpers.tt` (new)

This template generates `JoinArityHelpers.g.cs`, a static helper class that centralizes all arity-dependent logic currently scattered across switch statements in `InterceptorCodeGenerator.GetJoinedBuilderTypeName`, `CarrierEmitter.GetJoinedConcreteBuilderTypeName`, `CarrierEmitter.ResolveCarrierInterfaceList`, `CarrierEmitter.ResolveCarrierReceiverType`, and `JoinBodyEmitter`.

**Generated class structure:**

```csharp
internal static class JoinArityHelpers
{
    public const int MaxJoinArity = 6;

    /// <summary>
    /// Returns the interface name for the given entity count.
    /// 2 → "IJoinedQueryBuilder", 3 → "IJoinedQueryBuilder3", etc.
    /// </summary>
    public static string GetInterfaceName(int entityCount) => entityCount switch
    {
        2 => "IJoinedQueryBuilder",
        3 => "IJoinedQueryBuilder3",
        4 => "IJoinedQueryBuilder4",
        5 => "IJoinedQueryBuilder5",
        6 => "IJoinedQueryBuilder6",
        _ => throw new ArgumentOutOfRangeException(nameof(entityCount))
    };

    /// <summary>
    /// Returns the full generic interface string for the given entity type names.
    /// e.g., ["User", "Order"] → "IJoinedQueryBuilder<User, Order>"
    /// </summary>
    public static string GetGenericInterface(string[] entityTypes)
        => $"{GetInterfaceName(entityTypes.Length)}<{string.Join(", ", entityTypes)}>";

    /// <summary>
    /// Returns the full generic interface string with a result type appended.
    /// e.g., (["User", "Order"], "MyResult") → "IJoinedQueryBuilder<User, Order, MyResult>"
    /// </summary>
    public static string GetGenericInterfaceWithResult(string[] entityTypes, string resultType)
        => $"{GetInterfaceName(entityTypes.Length)}<{string.Join(", ", entityTypes)}, {resultType}>";
}
```

**MSBuild integration**: Same pattern as the runtime template, added to `src/Quarry.Generator/Quarry.Generator.csproj`.

### Generator File Updates

With the arity helpers in place, the following files are updated to use `JoinArityHelpers` instead of local switch statements:

#### File: `src/Quarry.Generator/Generation/InterceptorCodeGenerator.cs` (modify)

Replace `GetJoinedBuilderTypeName` (lines 307-316) with a call to `JoinArityHelpers.GetInterfaceName(entityCount)`. The existing method is a switch on 2/3/4 that throws on other values; the helper extends this to 6.

#### File: `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` (modify)

Replace `GetJoinedConcreteBuilderTypeName` (lines 221-230) with `JoinArityHelpers.GetGenericInterface(entityTypes)`. Replace the switch statements in `ResolveCarrierInterfaceList` (lines 156-219) with a loop that builds the interface list using the helpers. Replace the switches in `ResolveCarrierReceiverType` (lines 253-273) with calls to `JoinArityHelpers.GetGenericInterface` / `GetGenericInterfaceWithResult`.

The `ResolveCarrierInterfaceList` method currently builds the list incrementally (base interfaces first, then adding each join level). This logic remains, but the interface name at each level is looked up via `JoinArityHelpers.GetInterfaceName(level)` instead of a hardcoded string.

#### File: `src/Quarry.Generator/CodeGen/JoinBodyEmitter.cs` (modify)

Replace `InterceptorCodeGenerator.GetJoinedBuilderTypeName(priorTypes.Length)` calls with `JoinArityHelpers.GetInterfaceName(priorTypes.Length)`. The emitter constructs receiver and return type strings by combining the interface name with type argument lists; this logic stays, but the name lookup is centralized.

### IEntityAccessor and IQueryBuilder Join Methods

#### File: `src/Quarry/Query/IEntityAccessor.cs` (verify)

The `IEntityAccessor<T>` interface has `Join<TJoined>()` / `LeftJoin<TJoined>()` / `RightJoin<TJoined>()` returning `IJoinedQueryBuilder<T, TJoined>` (arity 2). This does not change — the first join always goes from arity 1 to arity 2, regardless of max arity.

#### File: `src/Quarry/Query/IQueryBuilder.cs` (verify)

Same situation as `IEntityAccessor`. The join methods return arity-2 interfaces. No changes needed.

---

## Diagnostics

### New Diagnostic Codes

| Code | Severity | Message |
|------|----------|---------|
| QRY040 | Error | No Ref<{Target}, K> column found for One<{Target}> navigation '{Property}' on schema '{Schema}' |
| QRY041 | Error | Ambiguous FK for One<{Target}> navigation '{Property}': multiple Ref<{Target}, K> columns found ({List}). Use HasOne<{Target}>(nameof(column)) to disambiguate |
| QRY042 | Error | HasOne<{Target}>(nameof({Column})) references '{Column}' which is not a Ref<{Target}, K> column |
| QRY043 | Warning | Navigation '{Property}' on '{Entity}' could not be resolved — target entity '{Target}' not found in any registered context |
| QRY044 | Error | HasManyThrough junction navigation '{Nav}' does not reference a Many<T> property |
| QRY045 | Error | HasManyThrough target navigation '{Nav}' does not reference a One<T> property on junction entity '{Junction}' |

These are added to `DiagnosticDescriptors.cs` following the existing pattern.

---

## Testing Strategy

### Unit Tests

**SchemaParser tests**: Verify `One<T>` properties are parsed into `SingleNavigationInfo` with correct FK resolution (auto-detect and explicit `HasOne`). Test the ambiguity diagnostic when multiple Refs exist. Test `HasManyThrough` parsing into `ThroughNavigationInfo`.

**SqlExprParser tests**: Verify `o.User.UserName` produces a `NavigationAccessExpr` with correct hops. Verify deep chains `o.User.Dept.Name` produce multi-hop nodes. Verify chains inside subquery predicates produce correctly-scoped nodes.

**SqlExprBinder tests**: Verify `NavigationAccessExpr` resolves to `ResolvedColumnExpr` with correct join alias. Verify deduplication (same navigation in Where and Select → same alias). Verify implicit join info is populated. Verify subquery-scoped implicit joins don't leak to outer query.

### Cross-Dialect SQL Output Tests

Add new test classes to `Quarry.Tests/SqlOutput/`:

**`CrossDialectNavigationJoinTests.cs`**: Tests for `One<T>` navigation joins across all 4 dialects. Cover: single navigation in Where, single navigation in Select, navigation in both Where and Select (deduplication), navigation in OrderBy, nullable FK producing LEFT JOIN, non-nullable FK producing INNER JOIN, deep navigation chains, navigation combined with explicit join, navigation inside subquery predicate.

**`CrossDialectHasManyThroughTests.cs`**: Tests for `HasManyThrough` skip-navigation. Cover: `.Any()` with predicate, `.Count()`, combined with other clauses.

### Integration Tests

Extend `JoinedCarrierIntegrationTests.cs` with 5-table and 6-table explicit join tests. Add navigation join integration tests that verify actual SQLite execution with seeded data.

---

## Implementation Order

The features have dependencies that determine the implementation order:

| Phase | Work | Depends On |
|-------|------|------------|
| **1** | T4 infrastructure: create both .tt files, MSBuild targets, delete hand-written IJoinedQueryBuilder.cs, verify build | Nothing |
| **2** | Extend explicit joins to 6: update T4 MaxArity, update generator arity helpers, update emitter switch statements via JoinArityHelpers, add 5-table and 6-table integration tests | Phase 1 |
| **3** | Runtime types for One<T>: create One.cs, OneBuilder.cs, add HasOne to Schema.cs | Nothing (can parallel Phase 1-2) |
| **4** | SingleNavigationInfo model + SchemaParser changes: parse One<T>, resolve FK, add to EntityInfo | Phase 3 |
| **5** | EntityCodeGenerator: emit entity properties for One<T> navigations | Phase 4 |
| **6** | NavigationAccessExpr node type + SqlExprParser changes | Phase 4 |
| **7** | SqlExprBinder changes: implicit join resolution, deduplication, BindContext extension | Phase 6 |
| **8** | SqlExprClauseTranslator + SqlExprRenderer changes | Phase 7 |
| **9** | TranslatedClause extension + CallSiteTranslator plumbing | Phase 7 |
| **10** | QueryPlan/SqlAssembler: merge implicit joins into plan, render | Phase 9 |
| **11** | ChainAnalyzer/PipelineOrchestrator: cross-clause deduplication | Phase 10 |
| **12** | Cross-dialect SQL output tests for navigation joins | Phase 11 |
| **13** | HasManyThrough: runtime type, ThroughNavigationInfo model, SchemaParser, SqlExprBinder expansion | Phase 11 |
| **14** | HasManyThrough cross-dialect tests | Phase 13 |
| **15** | Integration tests (SQLite execution with seeded data) | Phase 12, 14 |
| **16** | Diagnostics: add QRY040-045 to DiagnosticDescriptors.cs, wire into parser/binder error paths | Phase 4, 13 |

Phases 1-2 (T4/explicit joins) and Phases 3-5 (runtime types/schema parsing) can proceed in parallel since they touch different files.
