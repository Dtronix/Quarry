# Carrier Class Optimization for PrebuiltDispatch Chains

## Overview

Replace the current multi-allocation interceptor chain (QueryBuilder/JoinedQueryBuilder objects + QueryState) with a single `file sealed class` per analyzed chain. The carrier implements all interfaces needed by the chain, carries only the fields actually used (execution context, typed params, optional pagination/mask), and flows through interceptors via `Unsafe.As` downcasts and implicit interface upcasts.

**Goal**: Reduce per-chain heap allocations from 3-5+ objects to exactly 2 (carrier + terminal param array), eliminate dead QueryState fields, and remove intermediate builder construction entirely.

**Scope**: PrebuiltDispatch tier only. PrequotedFragments planned as future expansion.

---

## Design Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Carrier shape | `file sealed class` | file-scoped = invisible outside generated file; sealed = JIT devirtualization |
| Param fields | Strongly typed (`decimal P0`, `int P1`) | Avoids boxing value-type params during chain traversal; boxing deferred to terminal |
| Param passing to DbCommand | `new object?[N]` at terminal | stackalloc invalid for managed types; small array alloc is negligible next to carrier win |
| Interface crossing (Select) | `Unsafe.As` | Consistent with other interceptors, skips runtime type check |
| Limit/Offset | Bake constant values into SQL; typed carrier fields for captured variables | Constants: zero overhead; variables: treated as additional typed param fields |
| Carrier uniqueness | One carrier per chain | No deduplication logic; file-scoped prevents pollution; negligible duplication cost |
| Carrier naming | Per-file counter (`Chain_0`, `Chain_1`, ...) | Collision-free within file; `file` modifier prevents cross-file collisions |
| Dead interface methods | Explicit interface impl, body = `throw new InvalidOperationException(...)` | Hidden from public surface; descriptive error aids debugging (watch window ToSql, reflection) |
| Applicable tiers | PrebuiltDispatch only (for now) | Fully analyzed chains with const SQL; PrequotedFragments deferred |
| Forked chains | Analyzer error diagnostic (all tiers) | Builder variable consumed by >1 execution path is a compile error |
| New InterceptorKinds | WithTimeout, Distinct | Required so carrier path can intercept these calls instead of hitting dead stubs |

---

## Phase 0: Forked Chain Diagnostic

### 0.1 Fork Detection

Add a general-purpose analyzer diagnostic that applies to **all** optimization tiers (PrebuiltDispatch, PrequotedFragments, RuntimeBuild). A builder variable that forks into multiple execution-terminating paths is a compile error.

**Diagnostic**: `QRY0XX: Query builder variable '{name}' is consumed by multiple execution paths. Each execution path must use its own builder chain expression.`

**Severity**: Error.

**Algorithm**: During `AnalyzeVariableChain`, after building the flow graph, detect if the builder variable has >1 consumer that leads to an execution terminal (`ExecuteFetchAllAsync`, `ExecuteFetchFirstAsync`, etc.). A "consumer" is any method call on the variable that is not a reassignment back to the same variable.

**Examples**:
- **Error**: `var q = db.Users.Where(...); await q.OrderBy(x).ExecuteAsync(); await q.OrderBy(y).ExecuteAsync();`
- **OK**: `var q = db.Users; q = q.Where(...); q = q.OrderBy(...); await q.ExecuteAsync();` (linear reassignment, single execution path)
- **OK**: `await db.Users.Where(...).OrderBy(...).ExecuteAsync();` (single-expression chain, no variable)

**Scope**: This diagnostic is independent of carrier optimization. It catches a class of bugs where the immutable builder contract creates confusing aliasing behavior regardless of optimization tier.

### 0.2 Carrier Eligibility Gate

Beyond fork detection, the carrier path requires the chain to be a **single-expression fluent chain** or a **linear variable reassignment chain** (no intermediate reference reuse). If the chain analyzer cannot prove single-owner linear flow, the carrier path is disabled and the existing PrebuiltDispatch builder path is used.

---

## Phase 1: Runtime Infrastructure

New executor methods that bypass QueryState entirely, accepting IQueryExecutionContext + SQL + params directly.

### 1.1 QueryExecutor Static Entry Points

Add to `Quarry/Internal/QueryExecutor.cs`. All methods accept `object?[]` (not `ReadOnlySpan`) because async methods cannot accept ref struct parameters.

```csharp
// Array-based (all param counts)
public static Task<List<TResult>> ExecuteCarrierAsync<TResult>(
    IQueryExecutionContext ctx, string sql, SqlDialect dialect, object?[] parameters,
    Func<DbDataReader, TResult> reader, TimeSpan timeout, CancellationToken ct);

public static Task<TResult> ExecuteCarrierFirstAsync<TResult>(...);
public static Task<TResult?> ExecuteCarrierFirstOrDefaultAsync<TResult>(...);
public static Task<TResult> ExecuteCarrierSingleAsync<TResult>(...);
public static Task<TScalar> ExecuteCarrierScalarAsync<TScalar>(...);
public static IAsyncEnumerable<TResult> ToCarrierAsyncEnumerable<TResult>(...);

// Non-query (DELETE/UPDATE)
public static Task<int> ExecuteCarrierNonQueryAsync(
    IQueryExecutionContext ctx, string sql, SqlDialect dialect, object?[] parameters,
    TimeSpan timeout, CancellationToken ct);

// Zero-param overloads (no array needed)
public static Task<List<TResult>> ExecuteCarrierAsync<TResult>(
    IQueryExecutionContext ctx, string sql,
    Func<DbDataReader, TResult> reader, TimeSpan timeout, CancellationToken ct);
```

**Key differences from existing ExecuteWithPrebuiltSqlAsync**:
- Takes `IQueryExecutionContext` directly instead of `QueryState`
- Takes `SqlDialect` as explicit parameter (needed for `TypeMappingRegistry.TryConfigureParameter`)
- Takes resolved `TimeSpan timeout` (caller resolves carrier field vs default)
- Takes `object?[]` parameters directly instead of hydrating through QueryState
- No `PromotePaginationParameters` step (pagination params are already in the array)

**Core implementation**: Extract shared logic from existing `ExecuteFetchAllCoreAsync`, `ExecuteFirstCoreAsync`, `ExecuteSingleCoreAsync`, `ExecuteNonQueryAsync` into private helpers that accept the new parameter shape. The `IAsyncEnumerable` path must take ownership of the `DbCommand` via `await using` since the command lifetime spans multiple `MoveNextAsync` calls.

### 1.2 Parameter Binding from Array

Add to `QueryExecutor.cs`:

```csharp
private static DbCommand CreateCarrierCommand(
    DbConnection connection, string sql, SqlDialect dialect,
    object?[] parameters, TimeSpan timeout);
```

**Algorithm**: Iterate array by index, create `DbParameter` with name `@p{i}`, apply `NormalizeParameterValue` and `TypeMappingRegistry.TryConfigureParameter` per existing `CreateCommand` logic. No QueryState dependency.

**Enum boxing rule**: Generated terminal code must box carrier parameter fields without casting to underlying type. `__args[0] = __c.P0` where `P0` is `Status` (enum) preserves the enum type for `NormalizeParameterValue` detection of `type.IsEnum` and for `TryConfigureParameter` type mapping lookups.

### 1.3 Timeout Resolution

The generated terminal interceptor resolves timeout before calling the executor:

```csharp
var timeout = __c.Timeout ?? __c.Ctx.DefaultTimeout;
```

This keeps the executor simple. The carrier's `Timeout` field only exists when the chain uses `.WithTimeout()`. When absent, the terminal emits `__c.Ctx.DefaultTimeout` directly.

### 1.4 Parameter Logging

Add logging overload to maintain trace-level parameter logging for carrier-optimized queries:

```csharp
private static void LogParameters(long opId, object?[] parameters);
```

Called from the carrier executor methods before `EnsureConnectionOpenAsync`, matching existing logging behavior. Without this, parameter logging silently disappears for carrier-optimized queries (debugging regression).

---

## Phase 2: Generator — Carrier Class Emission

### 2.1 New Model: CarrierClassInfo

Add to `Quarry.Generator/Models/`:

```csharp
internal sealed class CarrierClassInfo
{
    string ClassName;                                    // "Chain_0", "Chain_1", ...
    IReadOnlyList<string> ImplementedInterfaces;        // Fully qualified closed interface names
    IReadOnlyList<CarrierField> Fields;                 // Typed fields
    IReadOnlyList<CarrierInterfaceStub> DeadMethods;    // Explicit interface impls
}

internal sealed class CarrierField
{
    string Name;        // "P0", "Ctx", "Limit", "Mask", "Timeout"
    string TypeName;    // "decimal", "IQueryExecutionContext?", "int?", "byte"
    FieldRole Role;     // Parameter, ExecutionContext, Pagination, ClauseMask, Timeout
}

internal enum FieldRole
{
    ExecutionContext, Parameter, ClauseMask, Limit, Offset, Timeout
}

internal sealed class CarrierInterfaceStub
{
    string InterfaceName;           // "IQueryBuilder<User>"
    string MethodName;              // "Where"
    string FullSignature;           // Complete explicit impl signature including params
    string ReturnTypeName;
    IReadOnlyList<string> GenericTypeParamNames;     // ["TResult"], ["TKey"], etc.
    // NOTE: Constraints must NOT be emitted on explicit interface impls (C# rule)
}
```

### 2.2 CarrierClassBuilder

Add to `Quarry.Generator/Generation/`:

```csharp
internal sealed class CarrierClassBuilder
{
    public CarrierClassInfo Build(PrebuiltChainInfo chain);
}
```

**Algorithm**:

1. **Determine interfaces**: Walk `chain.Analysis.Clauses` in order. Track the interface type at each interceptor boundary:
   - `IQueryBuilder<T>` at chain start (single-entity chains)
   - `IJoinedQueryBuilder<T1, T2>` after first Join (arity 2)
   - `IJoinedQueryBuilder3<T1, T2, T3>` after second Join (arity 3)
   - `IJoinedQueryBuilder4<T1, T2, T3, T4>` after third Join (arity 4)
   - `IQueryBuilder<T, TResult>` / `IJoinedQueryBuilder<T1, T2, TResult>` / `IJoinedQueryBuilder3<..., TResult>` / `IJoinedQueryBuilder4<..., TResult>` after Select
   - Collect unique interface types across all interceptor boundaries. Deduplicate using `ITypeSymbol` equality (not string comparison) to avoid namespace normalization issues.
   - All type arguments are closed/concrete. The carrier class itself is never generic.

2. **Determine fields**:
   - `IQueryExecutionContext? Ctx` — nullable; null when builder created without context (ToSql-only chains). Lazy null-check at execution time preserves existing behavior.
   - For each parameter across all clause sites: typed field `P0`, `P1`, ... using the parameter's resolved `ITypeSymbol` from the semantic model
   - If `chain.Analysis.ConditionalClauses.Count > 0`: `byte Mask` (sufficient for up to 8 conditional bits). Use `ushort` if >8, `uint` if >16. Document upper bound; if exceeded, fall back to non-carrier path.
   - If chain contains Limit/Offset with runtime value expression: `int? Limit` / `int? Offset`
   - If chain contains WithTimeout: `TimeSpan? Timeout`
   - If chain terminal is `ToSql()` only: omit `Ctx` field entirely

3. **Enumerate dead methods**: For each implemented interface, use `INamedTypeSymbol.GetMembers().OfType<IMethodSymbol>()` to enumerate all methods. Exclude methods that have matching interceptors in this chain — **match on specific interface + method combination**, not just method name (e.g., `Where` on `IQueryBuilder<User>` is distinct from `Where` on `IQueryBuilder<User, TResult>`).

   **Generic method handling**: Methods like `Select<TResult>`, `OrderBy<TKey>`, `Join<TJoined>` have method-level type parameters. The explicit interface impl preserves these type parameters but must **not** restate constraints (C# compiler error). Use `IMethodSymbol.TypeParameters` for names, suppress `ITypeParameterSymbol.ConstraintTypes` in emission.

   **Semantic model fallback**: If the semantic model returns incomplete members for a known interface (e.g., during error recovery), skip carrier emission for that chain and fall back to existing path.

4. **Generate class name**: Per-file monotonic counter: `Chain_0`, `Chain_1`, etc. The `file` modifier ensures uniqueness is only needed within the file.

### 2.3 Carrier Class Code Emission

Add new partial file `InterceptorCodeGenerator.Carrier.cs`:

```csharp
private void EmitCarrierClass(StringBuilder sb, CarrierClassInfo info);
private void EmitCarrierField(StringBuilder sb, CarrierField field);
private void EmitDeadInterfaceMethod(StringBuilder sb, CarrierInterfaceStub stub);
```

**EmitCarrierClass algorithm**:
1. Emit `file sealed class {ClassName} : {comma-separated interfaces}`
2. Emit each field as `internal {TypeName} {Name};`
3. Emit each dead method as explicit interface implementation with `InvalidOperationException`

**Structural placement**: Carrier classes must be emitted at namespace scope, **outside** the `file static class XxxInterceptors` that contains the interceptor methods. This requires a two-pass approach: first collect all `CarrierClassInfo` objects, emit them between the namespace opening and the static interceptor class, then emit the static class with interceptor methods.

### 2.4 Dead Method Enumeration

Use **Approach A** (compile-time reflection via semantic model). The semantic model for Quarry's own interfaces is always available since they are referenced types.

**Key rules**:
- Enumerate `INamedTypeSymbol.GetMembers()` on each interface symbol, filter to `IMethodSymbol`
- For explicit interface impl of generic methods: preserve method-level type parameters, suppress constraints
- Return types may reference the method's own type parameter (e.g., `IQueryBuilder<T, TResult>` from `Select<TResult>`)
- Navigation join overloads (`Join<TJoined>(Expression<Func<T, NavigationList<TJoined>>>)`) produce distinct stubs from expression-based join overloads — the semantic model naturally distinguishes these
- `IMethodSymbol.ToDisplayString()` with appropriate `SymbolDisplayFormat` handles parameter types (`Expression<Func<...>>` vs `Func<...>`)
- `WithTimeout` exists only on `IQueryBuilder<T>` and `IQueryBuilder<TEntity, TResult>`, not on any joined interface — do not generate stubs for it on joined interfaces
- `ExecuteScalarAsync<TScalar>` exists only on `IQueryBuilder<TEntity, TResult>`, not on joined projected interfaces — do not generate stubs for it on joined interfaces

---

## Phase 3: Generator — Interceptor Emission Changes

Modify existing interceptor emission in `InterceptorCodeGenerator.Query.cs`, `.Joins.cs`, and `.Execution.cs` to emit carrier-based code when a `CarrierClassInfo` is available.

### 3.1 Gate: When to Use Carrier Path

```csharp
// In the main generation loop (two-pass)
// Pass 1: Collect carrier infos
var carriers = new List<(CarrierClassInfo, PrebuiltChainInfo)>();
foreach (chain in prebuiltChains)
    if (chain.IsCarrierEligible)
        carriers.Add((carrierBuilder.Build(chain), chain));

// Emit carrier classes at namespace scope
foreach (var (carrier, _) in carriers)
    EmitCarrierClass(sb, carrier);

// Pass 2: Emit interceptor static class with methods
// Route carrier-eligible chains through carrier emission path
```

### 3.2 First Interceptor (Chain Entry)

The first interceptor in the chain creates the carrier. Identified by `isFirstInChain` flag (already computed).

```csharp
private void EmitCarrierChainEntry(StringBuilder sb, CarrierClassInfo carrier,
    string originalBuilderType, string returnInterface);
```

**Emitted pattern**:
- `Unsafe.As` the incoming builder to the original concrete type (e.g., `QueryBuilder<User>`)
- Extract `ExecutionContext` from it
- `return new {CarrierClassName} { Ctx = __b.ExecutionContext };`
- If the first interceptor also binds params or sets bits, do so on a local variable before returning

### 3.3 Parameter-Binding Interceptors (Where, Having with params)

```csharp
private void EmitCarrierParamBind(StringBuilder sb, CarrierClassInfo carrier,
    string fieldName, string paramExpression);
```

**Emitted pattern**:
- `var __c = Unsafe.As<{CarrierClassName}>(builder);`
- `__c.{P0} = {paramExpression};`
- If multiple params: `__c.{P1} = {paramExpression2};` etc.
- If conditional: `__c.Mask |= {1 << bitIndex};`
- `return builder;`

**paramExpression sourcing**: The value comes from `ParameterInfo.ValueExpression`, which can be a simple variable name, a method call (`GetMinAge()`), a binary expression (`x + 1`), or a complex accessor. The generated code replays the full expression — it is evaluated once per query execution, matching existing behavior.

### 3.4 Noop Interceptors (Join, unconditional Where without params, OrderBy, ThenBy, GroupBy, Distinct)

**Emitted pattern**: `return builder;`

For conditional noops (OrderBy with a bit but no params):
- `Unsafe.As<{CarrierClassName}>(builder).Mask |= {bit};`
- `return builder;`

**Distinct**: Always a noop on the carrier path — the SQL is pre-built with or without DISTINCT. The interceptor exists solely to prevent the dead stub from executing.

### 3.5 Select Interceptor (Interface Crossing)

**Emitted pattern**: `return Unsafe.As<{TargetInterface}>(builder);`

No field mutation. The carrier implements both the input and output interfaces.

### 3.6 Limit/Offset Interceptors

When Limit/Offset uses a runtime value expression:

```csharp
private void EmitCarrierPaginationBind(StringBuilder sb, CarrierClassInfo carrier,
    string fieldName, string valueExpression);
```

**Emitted pattern**:
- `Unsafe.As<{CarrierClassName}>(builder).Limit = {value};`
- `return builder;`

When Limit/Offset is a constant: emit a noop interceptor (`return builder;`) to prevent the dead interface method from executing. The constant value is baked into the pre-built SQL.

### 3.7 WithTimeout Interceptor

New `InterceptorKind.WithTimeout`. The carrier interceptor stores the value:

**Emitted pattern**:
- `Unsafe.As<{CarrierClassName}>(builder).Timeout = {timeoutExpression};`
- `return builder;`

Only generated when the chain uses `.WithTimeout()`. The `Timeout` field only exists on the carrier when needed.

### 3.8 Terminal Interceptor (Execution)

```csharp
private void EmitCarrierExecutionInterceptor(StringBuilder sb, CarrierClassInfo carrier,
    PrebuiltChainInfo chain, string executionMethodName);
```

**Emitted pattern**:
- `var __c = Unsafe.As<{CarrierClassName}>(builder);`
- Timeout resolution: `var __timeout = __c.Timeout ?? __c.Ctx.DefaultTimeout;` (or just `__c.Ctx.DefaultTimeout` if no Timeout field)
- Dispatch table (if masks > 1 variant): `var sql = __c.Mask switch { ... };`
- Single variant: `const string sql = @"...";`
- Parameter array fill:
  - If paramCount == 0: call zero-param executor overload
  - If paramCount > 0: `var __args = new object?[{N}]; __args[0] = __c.P0; ...`
  - Pagination param append: if Limit/Offset are runtime value fields, append to array after regular params
- Dialect: emitted as compile-time constant (e.g., `SqlDialect.PostgreSql`) — known at generation time
- Call: `return QueryExecutor.ExecuteCarrierAsync(__c.Ctx, sql, SqlDialect.{X}, __args, {readerLambda}, __timeout, cancellationToken);`

**Pagination in SQL**: When Limit/Offset are runtime values, the SQL contains `LIMIT @pN OFFSET @pM` with parameter indices after the regular params. The terminal fills these slots in the array from the carrier's Limit/Offset fields.

### 3.9 Terminal Interceptor (ToSql)

Separate from execution terminals. `ToSql()` returns `string`, does not need `Ctx`, params, or timeout.

```csharp
private void EmitCarrierToSqlInterceptor(StringBuilder sb, CarrierClassInfo carrier,
    PrebuiltChainInfo chain);
```

**Emitted pattern**:
- If single variant: `return @"...";` (no carrier access needed)
- If multiple variants: `var __c = Unsafe.As<{CarrierClassName}>(builder); return __c.Mask switch { ... };`

When the chain's only terminal is `ToSql()`, the carrier omits the `Ctx` field entirely.

---

## Phase 4: Chain Analysis Adjustments

### 4.1 Parameter Type Resolution

Currently `PrebuiltChainInfo` tracks `MaxParameterCount` but not per-parameter types. The carrier needs typed fields.

Extend `ChainedClauseSite` or create a parallel structure:

```csharp
internal sealed class ChainParameterInfo
{
    int Index;              // P0, P1, ...
    string TypeName;        // Fully qualified type name ("decimal", "int", "string")
    ITypeSymbol TypeSymbol; // For code emission
    UsageSiteInfo Source;   // Which clause site provides this param
}
```

**Algorithm**: During chain analysis, for each clause site that binds parameters, resolve the captured value types from the semantic model. The expression visitor already extracts captured variable references (`ParameterInfo.ValueExpression`) — extend it to also record `ITypeSymbol` for each captured value.

**LIKE wrapping for Contains/StartsWith/EndsWith**: The pre-built SQL already contains the correct LIKE pattern. The carrier parameter field carries the raw value (e.g., `searchTerm`). `NormalizeParameterValue` in `CreateCarrierCommand` handles the `%` wrapping at command-creation time, matching existing behavior.

### 4.2 Pagination Classification

Extend chain analysis to classify Limit/Offset calls:

```csharp
internal enum PaginationKind { None, Constant, RuntimeValue }
```

**Algorithm**: When the chain contains `.Limit(expr)` or `.Offset(expr)`:
- If `expr` is a `LiteralExpressionSyntax`: `PaginationKind.Constant`, bake value into SQL
- Everything else (`RuntimeValue`): variable, method call, binary expression, coalesce, etc. Add `int?` field to carrier. Field type is always `int` (matching the `Limit(int count)` / `Offset(int count)` parameter type).
- Record the constant value or the full `ValueExpression` string for runtime value cases

### 4.3 Interface Tracking

Add to `ChainAnalysisResult`:

```csharp
IReadOnlyList<INamedTypeSymbol> InterfaceProgression;  // Unique interfaces at each chain boundary
```

**Algorithm**: Walk clause sites in order, compute the interface type after each step based on clause role and entity types. Deduplicate using `ITypeSymbol` equality (via `SymbolEqualityComparer.Default`). Convert to display strings only at emission time.

### 4.4 New InterceptorKinds

Add to the `InterceptorKind` enum:

```csharp
WithTimeout,
Distinct
```

Update `UsageSiteDiscovery` method name map to recognize `"WithTimeout"` and `"Distinct"` as interceptable call sites. These generate noop or field-setting interceptors on the carrier path and are ignored on the non-carrier path (existing behavior unchanged).

---

## Phase 5: Integration and Fallback

### 5.1 Generation Pipeline Integration

In the main interceptor generation loop (`InterceptorCodeGenerator.cs`), restructure to two-pass:

**Pass 1**: Collect carrier infos for all carrier-eligible PrebuiltDispatch chains. Emit carrier classes at namespace scope (outside the static interceptor class).

**Pass 2**: Emit the static interceptor class. For carrier-eligible chains, route interceptor emission through carrier-aware methods. For non-carrier chains, use existing emission path.

### 5.2 Carrier Eligibility Criteria

A PrebuiltDispatch chain qualifies for carrier optimization when:
- Tier is `PrebuiltDispatch`
- Chain is single-expression fluent OR linear variable reassignment (no fork)
- Semantic model successfully resolves all implemented interface members (dead method enumeration succeeds)
- Conditional bit count does not exceed mask type capacity (≤8 for byte, ≤16 for ushort, etc.)

When any criterion fails, fall back to existing PrebuiltDispatch builder path (no regression).

### 5.3 Fallback Behavior

Chains that don't qualify for carrier optimization continue using the existing `AsJoined`/`SetClauseBit`/`BindParam` path unchanged. No regression risk — the carrier path is additive.

### 5.4 Diagnostic Annotations

Emit XML doc comments on carrier class and interceptors indicating optimization tier and carrier usage for debuggability:

```csharp
/// <remarks>Chain: Carrier-Optimized PrebuiltDispatch (2 allocations: carrier + param array)</remarks>
```

---

## Phase 6: Testing

### 6.1 Generator Snapshot Tests

Verify generated carrier class output for representative chain shapes:
- Single-entity, no params, no conditionals (simplest carrier: just Ctx)
- Single-entity, 2 params, 1 conditional bit
- Join chain (2 tables), params, Select projection
- Join chain (3 tables), mixed conditional/unconditional
- Join chain (4 tables)
- Chain with constant Limit/Offset (baked into SQL, noop interceptor)
- Chain with variable Limit/Offset (carrier fields)
- Chain with WithTimeout (carrier field + timeout resolution)
- Chain with Distinct (noop interceptor)
- ToSql-only terminal (no Ctx field)
- Forked chain producing analyzer error diagnostic

### 6.2 Integration Tests

Run existing integration test suite — all queries must produce identical SQL and results. The carrier is a runtime-invisible optimization; behavior must be unchanged.

### 6.3 Allocation Benchmarks

BenchmarkDotNet comparison: current PrebuiltDispatch vs carrier for representative chains. Measure:
- Heap allocations (count and bytes)
- Execution time (carrier setup + executor call)

---

## File Change Map

### New Files
- `Quarry.Generator/Models/CarrierClassInfo.cs` — carrier data model
- `Quarry.Generator/Models/ChainParameterInfo.cs` — per-param type info
- `Quarry.Generator/Generation/CarrierClassBuilder.cs` — builds CarrierClassInfo from PrebuiltChainInfo
- `Quarry.Generator/Generation/InterceptorCodeGenerator.Carrier.cs` — carrier class + interceptor emission
- `Quarry.Generator/Diagnostics/QRY0XX_ForkedChain.cs` — forked chain analyzer diagnostic (error)

### Modified Files
- `Quarry/Internal/QueryExecutor.cs` — add `ExecuteCarrierAsync` overloads + `CreateCarrierCommand` + `LogParameters(object?[])` overload
- `Quarry.Generator/Parsing/ChainAnalyzer.cs` — fork detection, parameter type resolution, pagination classification, interface tracking
- `Quarry.Generator/Models/ChainAnalysisResult.cs` — new fields (ChainParameterInfo list, PaginationKind, InterfaceProgression, IsCarrierEligible)
- `Quarry.Generator/Models/PrebuiltChainInfo.cs` — expose ChainParameterInfo list
- `Quarry.Generator/Models/UsageSiteInfo.cs` — add `InterceptorKind.WithTimeout`, `InterceptorKind.Distinct`
- `Quarry.Generator/Generation/InterceptorCodeGenerator.cs` — two-pass emission, carrier path gate
- `Quarry.Generator/Generation/InterceptorCodeGenerator.Query.cs` — carrier-aware clause emission methods
- `Quarry.Generator/Generation/InterceptorCodeGenerator.Joins.cs` — carrier-aware join emission
- `Quarry.Generator/Generation/InterceptorCodeGenerator.Execution.cs` — carrier-aware terminal emission (execution + ToSql)

### Unchanged
- `Quarry/Query/IQueryBuilder.cs` — no public API changes
- `Quarry/Query/IJoinedQueryBuilder.cs` — no public API changes
- `Quarry/Query/QueryBuilder.cs` — existing `AsJoined`/`BindParam`/`SetClauseBit` remain for non-carrier paths
- `Quarry/Query/JoinedQueryBuilder.cs` — same
- `Quarry/Query/QueryState.cs` — same (used by non-carrier tiers)

---

## Future: PrequotedFragments Carrier Expansion

The carrier class pattern extends to Tier 2 (PrequotedFragments) with modifications:
- Carrier gains `string?` fields for pre-quoted fragment storage instead of (or in addition to) mask-based dispatch
- Terminal concatenates fragments from carrier fields instead of from QueryState
- Same allocation-reduction benefit; SQL assembly is lightweight string concat rather than const dispatch

This is deferred until Tier 1 carrier is validated.
