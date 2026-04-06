# Plan: CTE & Derived Table Support (#187)

## Key Concepts

**CTE (Common Table Expression)**: A `WITH name AS (subquery)` prefix that defines a named virtual table scoped to the statement. The inner subquery produces rows; the outer query references the CTE by name in FROM or JOIN.

**DTO-as-entity pattern**: The user defines a plain C# class whose public properties represent the CTE's columns. The source generator reads these properties to build column metadata, treating the DTO as a pseudo-entity for binding and projection. No attribute required.

**Nested chain analysis**: The inner query passed to `With<TDto>()` is a complete method chain (e.g., `db.Orders().GroupBy(...).Select(...)`). The generator discovers and analyzes it as a separate chain, captures its assembled SQL, then embeds that SQL into the outer chain's QueryPlan as a `CteDef`. The inner chain's standalone carrier is suppressed.

**Return-context pattern**: `With<TDto>()` returns the context type itself, so `.Users()`, `.With<AnotherDto>()`, and `.FromCte<TDto>()` are all valid continuations. The source generator intercepts the full chain regardless of the intermediate type.

## API Surface

```csharp
// CTE joined to a real table
db.With<OrderCountDto>(
      db.Orders().GroupBy(o => o.UserId)
        .Select(o => new OrderCountDto { UserId = o.UserId.Id, OrderCount = Sql.Count() })
  )
  .Users()
  .Join<OrderCountDto>((u, oc) => u.UserId == oc.UserId)
  .Where((u, oc) => oc.OrderCount > 2)
  .Select((u, oc) => (u.UserName, oc.OrderCount))
  .ExecuteFetchAllAsync();

// CTE as primary FROM (derived table equivalent)
db.With<UserTotalDto>(innerQuery)
  .FromCte<UserTotalDto>()
  .Where(ut => ut.TotalSpent > 500)
  .ExecuteFetchAllAsync();

// Multiple CTEs
db.With<A>(innerA).With<B>(innerB)
  .Users()
  .Join<A>((u, a) => u.UserId == a.UserId)
  .Join<B>((u, a, b) => a.SomeId == b.SomeId)
  .Select(...)
  .ExecuteFetchAllAsync();
```

Generated SQL (SQLite example):
```sql
WITH "OrderCountDto" AS (
  SELECT "UserId", COUNT(*) AS "OrderCount"
  FROM "orders" GROUP BY "UserId"
)
SELECT "t0"."UserName", "t1"."OrderCount"
FROM "users" AS "t0"
INNER JOIN "OrderCountDto" AS "t1" ON "t0"."UserId" = "t1"."UserId"
WHERE "t1"."OrderCount" > @p0
```

## Phases

### Phase 1: IR Foundation & Model Types
Add the data types needed for CTE support without changing any existing behavior.

**Files to modify:**
- `src/Quarry.Generator/Models/InterceptorKind.cs` — Add `CteDefinition` and `FromCte` enum values
- `src/Quarry.Generator/Models/OptimizationTier.cs` — Add `CteDefinition` and `FromCte` to `ClauseRole` enum
- `src/Quarry.Generator/IR/QueryPlan.cs` — Add `CteDef` class and `CteDefinitions` property to `QueryPlan`
- `src/Quarry.Generator/IR/SqlExpr.cs` — Add `Cte` to `SqlExprKind` if needed for CTE column references

**New type `CteDef`:**
```csharp
internal sealed class CteDef : IEquatable<CteDef>
{
    public string Name { get; }              // DTO class name (quoted for SQL)
    public string InnerSql { get; }          // Assembled SQL of the inner query
    public IReadOnlyList<QueryParameter> InnerParameters { get; }
    public IReadOnlyList<CteColumn> Columns { get; }  // Column metadata from DTO properties
}

internal sealed class CteColumn : IEquatable<CteColumn>
{
    public string PropertyName { get; }      // C# property name
    public string ColumnName { get; }        // SQL column name (same as property name for DTOs)
    public string ClrType { get; }           // C# type name
}
```

**QueryPlan extension:**
- New constructor parameter: `IReadOnlyList<CteDef>? cteDefinitions = null`
- New property: `IReadOnlyList<CteDef> CteDefinitions { get; }`
- Update `Equals()` and `GetHashCode()` to include CTE definitions

**Tests:** All existing tests pass — this is purely additive.

---

### Phase 2: Runtime API & Context Code Generation
Add `With<TDto>()` and `FromCte<TDto>()` methods to the generated context class.

**Files to modify:**
- `src/Quarry.Generator/Generation/ContextCodeGenerator.cs` — Emit `With<TDto>()` and `FromCte<TDto>()` methods on the generated context partial class

**Generated methods on context:**
```csharp
public partial TContext With<TDto>(IQueryBuilder<TDto> innerQuery) where TDto : class
    => throw new NotSupportedException("CTE methods must be intercepted by the Quarry source generator.");

public partial TContext With<TEntity, TDto>(IQueryBuilder<TEntity, TDto> innerQuery)
    where TEntity : class where TDto : class
    => throw new NotSupportedException("CTE methods must be intercepted by the Quarry source generator.");

public partial IEntityAccessor<TDto> FromCte<TDto>() where TDto : class
    => throw new NotSupportedException("CTE methods must be intercepted by the Quarry source generator.");
```

Two overloads of `With()` are needed because the inner query may or may not have a Select projection:
- `IQueryBuilder<TDto>`: inner chain with identity projection (e.g., `db.Users().Where(...)` where User IS the DTO)
- `IQueryBuilder<TEntity, TDto>`: inner chain with Select projection (e.g., `db.Orders().Select(o => new Dto{...})`)

**Tests:** All existing tests pass. New methods exist on context but throw without interception.

---

### Phase 3: CTE DTO Type Discovery
Build infrastructure to resolve CTE DTO column metadata from public properties.

**Files to create/modify:**
- New: `src/Quarry.Generator/IR/CteDtoResolver.cs` — Resolves DTO type symbol to column metadata
- `src/Quarry.Generator/IR/EntityRegistry.cs` — Add CTE DTO lookup capability (separate index from entities)

**CteDtoResolver:**
Accepts an `INamedTypeSymbol` for the DTO class. Iterates public instance properties with getters and setters. For each property, creates a `CteColumn` with:
- `PropertyName` = property name
- `ColumnName` = property name (DTOs don't have column name mappings)
- `ClrType` = property type's display string

Also builds a pseudo-`EntityInfo` (or a lightweight `CteEntityInfo`) that the binder can use when resolving `Join<CteDto>()` column references. This pseudo-entity has:
- No table name / schema (it references a CTE name instead)
- Columns derived from DTO properties
- No navigation properties
- No primary key (not needed for CTE joins)

**Tests:** All existing tests pass — purely additive utility.

---

### Phase 4: Discovery — Recognizing CTE Chains
Teach UsageSiteDiscovery to recognize `With()` and `FromCte()` calls and identify inner chains.

**Files to modify:**
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs`

**Changes:**

1. **InterceptableMethods dictionary**: Add entries:
   ```csharp
   ["With"] = InterceptorKind.CteDefinition,
   ["FromCte"] = InterceptorKind.FromCte,
   ```

2. **Chain root detection**: Currently, chain roots are identified by: empty args, capitalized method name, on context type. `With<TDto>(inner)` has args (the inner query), so it won't match the current chain root heuristic. Add special handling: if the method name is "With" and the receiver is a context type, treat it as a CTE chain entry.

3. **CTE-specific discovery in `DiscoverRawCallSite()`**:
   - For `InterceptorKind.CteDefinition`: extract `TDto` type from type arguments, extract the inner argument expression's syntax. Store the inner chain's terminal invocation syntax node so we can match it to an analyzed chain later. Store the DTO type name on the RawCallSite.
   - For `InterceptorKind.FromCte`: extract `TDto` type from type arguments. Store on RawCallSite.

4. **Inner chain tagging**: When we discover call sites that are part of an inner chain (the argument to `With()`), we need to tag them so the ChainAnalyzer knows they're consumed by a CTE. Add a property to RawCallSite: `bool IsCteInnerChain`. During discovery, when the invocation's parent is an argument to a `With()` call, walk the inner chain and tag all sites.

5. **BuilderTypeNames**: The context class itself becomes a valid "builder" receiver for `With()` chains. Ensure `IsQuarryBuilderType()` recognizes context types, or handle CTE methods before the builder type check.

6. **ChainId computation**: Ensure `With()` and `FromCte()` get the same ChainId as the rest of the outer chain (they're part of one method chain in the syntax tree).

**RawCallSite extensions:**
- `string? CteEntityTypeName` — the TDto type name for CteDefinition/FromCte sites
- `InvocationExpressionSyntax? CteInnerTerminal` — the terminal node of the inner chain argument (for matching to analyzed chains)
- `bool IsCteInnerChain` — marks sites that belong to a CTE's inner query

**Tests:** All existing tests pass. CTE chains are discovered but not yet analyzed.

---

### Phase 5: Binding & Translation for CTE DTOs
Wire CTE DTO types into the binding and translation pipeline.

**Files to modify:**
- `src/Quarry.Generator/IR/CallSiteBinder.cs` — Resolve CTE DTO as join target entity
- `src/Quarry.Generator/IR/CallSiteTranslator.cs` — Handle CteDefinition and FromCte translation
- `src/Quarry.Generator/IR/EntityRegistry.cs` — Store/retrieve CTE DTO metadata

**CallSiteBinder changes:**
When binding a `Join<CteDto>()` site, the binder currently looks up `CteDto` in the EntityRegistry and fails (it's not a schema entity). Add fallback: if entity lookup fails and the join target matches a known CTE DTO type, use the CTE pseudo-EntityInfo from CteDtoResolver.

For CTE DTO awareness, the binder needs access to the CTE DTO types declared in the chain. This requires enrichment: during the analysis post-pass (Phase 6), after CTE definitions are identified, retranslate join sites with CTE DTO context.

Alternatively, register CTE DTOs in the EntityRegistry during the discovery phase (when we see `With<CteDto>(...)`, register `CteDto` as a pseudo-entity). This is simpler — the binder automatically resolves it.

**Approach: Register CTE DTOs in EntityRegistry.**
During binding, when we encounter a `CteDefinition` site, resolve the DTO type symbol via CteDtoResolver and register a pseudo-EntityInfo in the registry. Subsequent `Join<CteDto>()` lookups find it automatically.

**CallSiteTranslator changes:**
- `CteDefinition` sites: no SQL expression to translate (the inner chain is analyzed separately). Create a TranslatedCallSite with CTE metadata only.
- `FromCte` sites: no SQL expression to translate. Create a TranslatedCallSite that signals "primary table is a CTE."

**Tests:** All existing tests pass.

---

### Phase 6: Chain Analysis — CTE Composition
Handle CTE chains in ChainAnalyzer, compose inner chain SQL into outer chain's QueryPlan.

**Files to modify:**
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs`

**Changes to `AnalyzeChainGroup()`:**

1. **Identify CTE definition sites** in the chain's clause sites. Each `CteDefinition` site provides:
   - The CTE DTO type name (becomes CTE name)
   - A reference to the inner chain's terminal (for matching)

2. **Match inner chains**: The inner chain's sites were discovered with `IsCteInnerChain = true`. These form separate chain groups. After analyzing inner chain groups, match them to CTE definition sites via the terminal syntax reference.

3. **Assemble inner chains**: For each matched inner chain, call `SqlAssembler.Assemble()` to get the inner SQL. Build a `CteDef` object with:
   - Name = DTO class name
   - InnerSql = assembled SQL for mask 0 (inner chains should not have conditional clauses)
   - InnerParameters = inner chain's parameters (must be merged into outer carrier)
   - Columns = from CteDtoResolver

4. **Suppress inner chains**: Mark analyzed inner chains so the emitter skips carrier generation for them.

5. **Handle `FromCte` site**: When the chain has a `FromCte` site instead of a regular entity chain root:
   - `PrimaryTable` becomes a `TableRef` with the CTE name (no schema)
   - The CTE is the primary FROM source

6. **Handle `Join<CteDto>()`**: The join plan's `TableRef` uses the CTE name instead of a real table name (since the pseudo-entity has no table mapping, use the CTE name directly).

7. **Parameter merging**: Inner chain parameters must be prepended to the outer chain's parameter list (CTE SQL renders first, so CTE parameters come first). Global indices must be remapped.

**Build QueryPlan with CTE definitions:**
Add `cteDefinitions` to the QueryPlan constructor call.

**Two-pass analysis**:
The ChainAnalyzer's `Analyze()` method processes all chain groups. CTE inner chains must be analyzed before their outer chains. Implementation:
1. First pass: identify which chains are CTE inner chains (tagged during discovery)
2. Analyze CTE inner chains first
3. Analyze outer chains second, with inner chain results available

**Tests:** All existing tests pass. CTE chains produce correct QueryPlans.

---

### Phase 7: SQL Assembly — WITH Clause Rendering
Render CTE definitions as SQL `WITH` prefix.

**Files to modify:**
- `src/Quarry.Generator/IR/SqlAssembler.cs`

**Changes to `RenderSelectSql()`:**
Before the `SELECT` keyword, check `plan.CteDefinitions`. If non-empty:
```
WITH "CteName1" AS (
  inner_sql_1
), "CteName2" AS (
  inner_sql_2
)
SELECT ...
```

The inner SQL is already fully rendered (from inner chain assembly). Parameter indices in the inner SQL are already correct relative to the global parameter list (thanks to parameter merging in Phase 6).

**Changes to table reference handling:**
When the primary table or a join target references a CTE name (no schema, matching a CTE definition name), render it as a plain quoted identifier without schema prefix. The CTE name acts as a virtual table name.

**Parameter counting:**
The `paramIndex` counter must start after CTE parameters. Since CTE definitions are prepended, CTE parameters come first in the global order. The assembler's `paramIndex` initialization accounts for inner chain parameter count.

**Tests:** All existing tests pass. CTE SQL renders correctly (verified in Phase 8).

---

### Phase 8: Code Generation — Carrier & Interceptor Emission
Generate carrier classes and interceptor methods for CTE chains.

**Files to modify:**
- `src/Quarry.Generator/CodeGen/CarrierAnalyzer.cs` — Analyze CTE carrier structure
- `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` — Emit CTE carrier code
- `src/Quarry.Generator/CodeGen/FileEmitter.cs` — Route CTE interceptor methods
- `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs` — If CTE terminal emission differs

**CarrierAnalyzer changes:**
- Include CTE inner parameters in carrier field list (they're part of the global parameter list)
- CTE definition sites don't add clause extraction plans (no user-facing lambda to extract from — the inner chain's parameters are extracted from its own carrier logic)

Wait — the inner chain doesn't have its own carrier (it's suppressed). The inner chain's captured variables need to be extracted by the OUTER carrier. So:
- CTE inner parameters are added to the outer carrier's parameter list
- Extraction plans for CTE inner parameters come from the inner chain's clause sites

**CarrierEmitter changes:**

1. **`EmitCarrierSqlField`**: The CTE prefix is part of the assembled SQL string. Since SqlAssembler now includes `WITH ... AS (...)` in the rendered SQL, the static SQL field already contains it. No change needed here.

2. **`With<TDto>()` interceptor body**: Similar to a no-op clause. The carrier captures the inner query's parameters (if any). The method:
   - Receives the context (from `@this`)
   - Creates the carrier: `new Chain_N { Ctx = @this }`
   - Extracts captured variables from the inner chain's closure (if the inner query has captured vars)
   - Returns the context (`@this`) so `.Users()` or `.With<Another>()` can follow

   Actually, for the return-context pattern, `With<TDto>(inner)` needs to return the context. But the carrier is created later at `Users()` (or `FromCte()`). So `With()` can't create the carrier.

   **Revised approach**: The chain root is actually `Users()` or `FromCte()`, not `With()`. The `With()` calls are pre-root modifiers. The carrier is created at `Users()`/`FromCte()`. The `With()` interceptor extracts CTE parameters and stores them on the carrier.

   But wait — the carrier doesn't exist yet when `With()` is called. We need a different approach:
   
   **Two-step carrier creation**: 
   - `With()` returns the context. The interceptor is a no-op at runtime (CTE SQL is compile-time only).
   - `Users()` creates the carrier AND extracts CTE parameters from the inner chain's closure.
   - The carrier stores all parameters (inner + outer) in its fields.

   But `Users()` doesn't have access to the inner chain's closure. The inner chain's argument is syntactically available at `With()`, not at `Users()`.

   **Better approach**: `With()` IS the chain root for CTE chains. It creates the carrier, extracts CTE parameters, and returns the carrier (typed as context for the compiler). Then `Users()` is a clause method (sets primary table, no carrier creation).

   Let me reconsider the interceptor model:
   
   For `db.With<A>(inner).Users().Join<A>(...).Select(...).ExecuteFetchAllAsync()`:
   - `With<A>(inner)` → InterceptorKind.CteDefinition → creates carrier, extracts inner params, returns carrier-as-context
   - `.Users()` → InterceptorKind.ChainRoot → sets primary table on carrier (but carrier already exists!)
   
   This conflicts with the current model where ChainRoot creates the carrier. We need a new kind: `InterceptorKind.CteChainTable` for the `.Users()` call that follows `With()`. It sets the primary table but doesn't create the carrier.

   Similarly, for `db.With<A>(inner).With<B>(inner2).Users()`:
   - First `With<A>(inner)` → creates carrier, extracts A's params
   - Second `With<B>(inner2)` → adds to carrier, extracts B's params  
   - `.Users()` → sets primary table

   And for `db.With<A>(inner).FromCte<A>()`:
   - `With<A>(inner)` → creates carrier, extracts A's params
   - `.FromCte<A>()` → sets primary table to CTE A

   **Implementation**:
   - `With<TDto>(inner)` interceptor: if first in chain → `new Chain_N { Ctx = @this, P0 = ..., P1 = ... }; return Unsafe.As<ContextType>(carrier);`
   - `With<TDto>(inner)` interceptor: if not first → `var __c = ...; __c.P2 = ...; return builder;` (the "builder" here is the carrier-as-context from the previous With)
   - `.Users()` interceptor: no-op (primary table is compile-time known). Just returns `Unsafe.As<IEntityAccessor<User>>(builder);`
   - `.FromCte<TDto>()` interceptor: no-op. Returns `Unsafe.As<IEntityAccessor<TDto>>(builder);`

   The key insight: the primary table is compile-time known (from ChainAnalyzer), so `.Users()` and `.FromCte()` don't need to do anything at runtime. They're just type-transition points.

3. **Inner chain suppression in FileEmitter**: When emitting interceptor methods, skip chains that are tagged as CTE inner chains (they have no carrier).

**FileEmitter changes:**
- Add routing for `InterceptorKind.CteDefinition` and `InterceptorKind.FromCte` in `EmitInterceptorMethod()`
- Skip inner chain carrier emission

**Tests:** All existing tests pass. CTE chains generate correct interceptor code.

---

### Phase 9: Cross-Dialect Tests
Comprehensive test coverage for CTE and FromCte functionality.

**Files to create:**
- `src/Quarry.Tests/SqlOutput/CrossDialectCteTests.cs`

**Test cases:**

1. **CTE_Join_WithAggregation** — CTE with GROUP BY + COUNT, joined to real table
   ```csharp
   db.With<OrderCountDto>(db.Orders().GroupBy(o => o.UserId).Select(o => new OrderCountDto { ... }))
     .Users().Join<OrderCountDto>((u, oc) => u.UserId == oc.UserId)
     .Select((u, oc) => (u.UserName, oc.OrderCount))
   ```

2. **CTE_FromCte_WithFilter** — CTE as primary FROM with WHERE
   ```csharp
   db.With<UserTotalDto>(db.Orders().GroupBy(o => o.UserId).Select(o => new UserTotalDto { ... }))
     .FromCte<UserTotalDto>()
     .Where(ut => ut.TotalSpent > 500)
   ```

3. **CTE_WithCapturedParameter** — CTE inner query with captured variable
   ```csharp
   var cutoff = DateTime.UtcNow.AddDays(-30);
   db.With<RecentDto>(db.Orders().Where(o => o.OrderDate > cutoff).GroupBy(...).Select(...))
     .Users().Join<RecentDto>(...).Select(...)
   ```

4. **CTE_MultipleCtes** — Two CTEs joined to a real table
   ```csharp
   db.With<A>(innerA).With<B>(innerB)
     .Users().Join<A>(...).Join<B>(...).Select(...)
   ```

5. **CTE_FromCte_Simple** — Minimal CTE-only query (no join)
   ```csharp
   db.With<Dto>(db.Orders().Select(o => new Dto { ... }))
     .FromCte<Dto>().ExecuteFetchAllAsync()
   ```

6. **CTE_Join_WithWhere_OnBothSides** — Filter in both inner CTE and outer query

7. **CTE_Join_LeftJoin** — LEFT JOIN to a CTE

8. **CTE_SelectAllColumns** — Select all CTE columns (identity projection on CTE entity)

**DTO test classes** (defined in test project):
```csharp
public class OrderCountDto { public int UserId { get; set; } public int OrderCount { get; set; } }
public class UserTotalDto { public int UserId { get; set; } public decimal TotalSpent { get; set; } }
```

Each test verifies:
- Generated SQL string for all 4 dialects (SQLite, PostgreSQL, MySQL, SQL Server)
- Execution against SQLite in-memory database with seed data (where possible)

**Tests:** All new and existing tests pass.

## Dependencies Between Phases

- Phase 1 has no dependencies
- Phase 2 depends on Phase 1 (uses CteDef, new InterceptorKinds)
- Phase 3 depends on Phase 1 (uses CteDtoResolver infrastructure)
- Phase 4 depends on Phase 1 (uses new InterceptorKind values)
- Phase 5 depends on Phases 3, 4 (uses CTE DTO metadata in binding)
- Phase 6 depends on Phases 4, 5 (needs discovered and bound CTE sites)
- Phase 7 depends on Phase 6 (needs QueryPlan with CTE definitions)
- Phase 8 depends on Phases 6, 7 (needs assembled CTE SQL)
- Phase 9 depends on all prior phases

Phases 1-3 can be done in parallel (no mutual dependencies). Phases 4-5 can overlap partially. Phases 6-8 are sequential.
