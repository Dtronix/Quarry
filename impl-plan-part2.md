# Carrier Optimization — Gap Resolution Plan

Addresses deviations from the original `impl-plan.md` identified after the initial implementation PR (#10).

Decisions locked via review:
- **Gap 1**: Carrier base classes with hardcoded interface registry
- **Gap 2**: Keep current threading approach (carrier info through existing generators)
- **Gap 3**: Keep FieldInfo extraction for captured params
- **Gap 4a**: Factory method refactor for chain root interception
- **Gap 4b**: Add Delete/Update carrier branches
- **Gap 5**: All four interceptor kinds (Limit/Offset/Distinct/WithTimeout)
- **Gap 6+10**: Nullable heuristic (`?` suffix detection)
- **Gap 8**: Snapshot tests only (no benchmarks)
- **Gap 9**: Ignored (deviation is fine)

---

## 1. Carrier Base Classes

### Problem
Generated carrier classes are plain `file sealed class` with no interface implementation. Debugging tools (watch window, reflection) can't inspect carrier instances as query builders.

### Solution
Define 8 abstract base classes in `Quarry/Internal/` that implement all builder interface methods as explicit impls throwing `InvalidOperationException`. Generated carriers inherit from the appropriate base and declare only fields.

### Base Class Variants

Each variant implements the complete interface progression for its chain shape. All preceding interfaces are included (a 2-join chain needs `IQueryBuilder<T1>` + `IJoinedQueryBuilder<T1,T2>`).

| Class | Implements | Use Case |
|-------|-----------|----------|
| `CarrierBase<T>` | `IQueryBuilder<T>` | ToSql-only, no Select |
| `CarrierBase<T, R>` | `IQueryBuilder<T>`, `IQueryBuilder<T, R>` | Single-entity with Select |
| `JoinedCarrierBase<T1, T2>` | `IQueryBuilder<T1>`, `IJoinedQueryBuilder<T1, T2>` | 2-join no Select |
| `JoinedCarrierBase<T1, T2, R>` | above + `IJoinedQueryBuilder<T1, T2, R>` | 2-join with Select |
| `JoinedCarrierBase3<T1, T2, T3>` | above (no R) + `IJoinedQueryBuilder3<T1, T2, T3>` | 3-join no Select |
| `JoinedCarrierBase3<T1, T2, T3, R>` | above + `IJoinedQueryBuilder3<T1, T2, T3, R>` | 3-join with Select |
| `JoinedCarrierBase4<T1, T2, T3, T4>` | above (no R) + `IJoinedQueryBuilder4<T1, T2, T3, T4>` | 4-join no Select |
| `JoinedCarrierBase4<T1, T2, T3, T4, R>` | above + `IJoinedQueryBuilder4<T1, T2, T3, T4, R>` | 4-join with Select |

### Base Class Structure

```csharp
// Quarry/Internal/CarrierBase.cs
public abstract class CarrierBase<T, TResult> : IQueryBuilder<T>, IQueryBuilder<T, TResult> where T : class
{
    // Shared carrier state
    internal IQueryExecutionContext? Ctx;

    // Explicit IQueryBuilder<T> impls — all throw
    IQueryBuilder<T> IQueryBuilder<T>.Where(Expression<Func<T, bool>> predicate) => throw ...;
    IQueryBuilder<T, TR> IQueryBuilder<T>.Select<TR>(Func<T, TR> selector) => throw ...;
    IQueryBuilder<T> IQueryBuilder<T>.OrderBy<TKey>(...) => throw ...;
    // ... all remaining IQueryBuilder<T> methods

    // Explicit IQueryBuilder<T, TResult> impls — all throw
    IQueryBuilder<T, TResult> IQueryBuilder<T, TResult>.Where(...) => throw ...;
    Task<List<TResult>> IQueryBuilder<T, TResult>.ExecuteFetchAllAsync(...) => throw ...;
    // ... all remaining IQueryBuilder<T, TResult> methods
}
```

**Exception message format**: `"Carrier method {InterfaceName}.{MethodName} is not intercepted in this optimized chain. This indicates a code generation bug."`

### Base Class Selection Algorithm

In `CarrierClassBuilder.Build()`:

1. Walk `chain.Analysis.Clauses` to detect join count (0, 1, 2, 3)
2. Check if chain has a Select clause (`ClauseRole.Select` present)
3. Map to base class variant:
   - No joins + no Select → `CarrierBase<T>`
   - No joins + Select → `CarrierBase<T, R>`
   - 1 join + Select → `JoinedCarrierBase<T1, T2, R>`
   - etc.
4. Emit: `file sealed class Chain_0 : CarrierBase<User, UserDto>`

### Generated Carrier Output

```csharp
// Before (current):
file sealed class Chain_0
{
    internal IQueryExecutionContext? Ctx;
    internal string P0;
    internal int P1;
    internal byte Mask;
}

// After:
file sealed class Chain_0 : CarrierBase<User, UserDto>
{
    internal string P0;
    internal int P1;
    internal byte Mask;
}
```

`Ctx` moves to the base class. Fields specific to the chain remain on the carrier.

### Impact on Unsafe.As

With interface implementation, `builder is Chain_0` works at runtime. This enables the check-and-create pattern for conditional chains (used as fallback, see §2).

### File Changes

**New files:**
- `Quarry/Internal/CarrierBase.cs` — `CarrierBase<T>`, `CarrierBase<T, R>`
- `Quarry/Internal/JoinedCarrierBase.cs` — 2-entity variants
- `Quarry/Internal/JoinedCarrierBase3.cs` — 3-entity variants
- `Quarry/Internal/JoinedCarrierBase4.cs` — 4-entity variants

**Modified files:**
- `CarrierClassBuilder.cs` — base class selection logic, remove `Ctx` field from generated fields
- `InterceptorCodeGenerator.Carrier.cs` — emit base class in class declaration, update `EmitCarrierChainEntry` to use inherited `Ctx`

---

## 2. Chain Root Interception (Factory Method Refactor)

### Problem
Entity set accessors on `QuarryContext` are `partial` properties (`public partial IQueryBuilder<User> Users { get; }`). `[InterceptsLocation]` cannot intercept property accesses. For variable-based chains where the first clause is conditional, there's no unconditional interception point to create the carrier.

### Solution
Change entity set accessors from properties to methods. The generated getter becomes a method call that `[InterceptsLocation]` can intercept. The root interceptor creates the carrier unconditionally.

### API Change

```csharp
// Before (property):
public partial IQueryBuilder<User> Users { get; }

// After (method):
public partial IQueryBuilder<User> Users();
```

**Breaking change**. User code changes from `db.Users` to `db.Users()`. All existing test/sample code must be updated.

### Generator Changes

**ContextCodeGenerator.cs**: Change property emission to method emission.

```csharp
// Before:
sb.AppendLine($"    public partial IQueryBuilder<{entity}> {name}");
sb.AppendLine($"    {{");
sb.AppendLine($"        get => QueryBuilder<{entity}>.Create(_dialect, \"{table}\", {schema}, (IQueryExecutionContext)this);");
sb.AppendLine($"    }}");

// After:
sb.AppendLine($"    public partial IQueryBuilder<{entity}> {name}()");
sb.AppendLine($"        => QueryBuilder<{entity}>.Create(_dialect, \"{table}\", {schema}, (IQueryExecutionContext)this);");
```

**UsageSiteDiscovery.cs**: Add chain root detection.

New `InterceptorKind.ChainRoot` enum value. Discovery logic: when a method call's return type is `IQueryBuilder<T>` and the containing type derives from `QuarryContext`, classify as `ChainRoot`.

```csharp
// New entry in InterceptableMethods (not needed — detected by return type + containing type)
// Instead, add to ResolveInterceptorKind:
if (method.ContainingType != null && IsQuarryContextType(method.ContainingType)
    && method.ReturnType is INamedTypeSymbol { Name: "IQueryBuilder" })
    return InterceptorKind.ChainRoot;
```

**ChainAnalyzer.cs**: Recognize `ChainRoot` as the chain's first node. When walking fluent chains backward, the root method call becomes the terminal backward-walk node.

**InterceptorCodeGenerator.Query.cs**: New `GenerateChainRootInterceptor` method.

```csharp
private static void GenerateChainRootInterceptor(
    StringBuilder sb, UsageSiteInfo site, string methodName,
    PrebuiltChainInfo? prebuiltChain, bool isFirstInChain, CarrierClassInfo? carrier);
```

Carrier path body:
```csharp
// Intercepts db.Users() — creates carrier unconditionally
var __b = Unsafe.As<QueryBuilder<{Entity}>>(original_result);
return new Chain_X { Ctx = __b.State.ExecutionContext };
```

Non-carrier path body: passthrough (`return original_result;`).

### Chain Analysis Impact

The root call becomes the first entry in `chain.Analysis.Clauses` with `ClauseRole.ChainRoot` (new enum value). It is always unconditional (the `db.Users()` call itself is never inside a conditional block for the tracked variable). This guarantees every carrier chain has an unconditional first clause.

Add to `ClauseRole`:
```csharp
ChainRoot  // Entity set accessor — carrier creation point
```

Add to `InterceptorKind`:
```csharp
ChainRoot  // Entity set method on QuarryContext
```

### Migration

The API change from property to method is a compile error (not a silent behavior change). Users get immediate feedback. The fix is mechanical: add `()` to every entity accessor call.

---

## 3. Limit/Offset/Distinct/WithTimeout Interceptors

### Problem
These four clause types are not intercepted on the carrier path. Chains containing them fall back to non-carrier because the original builder methods run unchanged, breaking the carrier's `Unsafe.As` assumption.

### Solution
Generate carrier-aware interceptors for all four. Remove them from the carrier eligibility exclusion list.

### Approach

Stop early-returning for these kinds in `GenerateInterceptorMethod` when on the carrier path. Instead, generate interceptors:

**InterceptorCodeGenerator.Query.cs** — update early skip:
```csharp
if (site.Kind is InterceptorKind.Limit or InterceptorKind.Offset
    or InterceptorKind.Distinct or InterceptorKind.WithTimeout)
{
    if (!isCarrierSite)
        return; // Non-carrier: skip (original builder method runs)
    // Carrier: fall through to generate interceptor
}
```

Add cases to the dispatch switch:
```csharp
case InterceptorKind.Limit:
case InterceptorKind.Offset:
    GeneratePaginationInterceptor(sb, site, methodName, carrier, carrierChain);
    break;
case InterceptorKind.Distinct:
    GenerateDistinctInterceptor(sb, site, methodName, carrier, carrierChain);
    break;
case InterceptorKind.WithTimeout:
    GenerateWithTimeoutInterceptor(sb, site, methodName, carrier, carrierChain);
    break;
```

### Method Signatures

All methods are simple — they match the builder interface methods exactly.

```csharp
private static void GeneratePaginationInterceptor(
    StringBuilder sb, UsageSiteInfo site, string methodName,
    CarrierClassInfo carrier, PrebuiltChainInfo chain);
// Emits: Unsafe.As<Chain_X>(builder).{Limit|Offset} = count; return builder;

private static void GenerateDistinctInterceptor(
    StringBuilder sb, UsageSiteInfo site, string methodName,
    CarrierClassInfo carrier, PrebuiltChainInfo chain);
// Emits: return builder;  (noop — Distinct is baked into SQL)

private static void GenerateWithTimeoutInterceptor(
    StringBuilder sb, UsageSiteInfo site, string methodName,
    CarrierClassInfo carrier, PrebuiltChainInfo chain);
// Emits: Unsafe.As<Chain_X>(builder).Timeout = timeout; return builder;
```

### Method Signature Emission

The interceptor method signature must match the original builder method:

- `Limit`: `public static {ReceiverType} {methodName}(this {ReceiverType} builder, int count)`
- `Offset`: same pattern
- `Distinct`: `public static {ReceiverType} {methodName}(this {ReceiverType} builder)`
- `WithTimeout`: `public static {ReceiverType} {methodName}(this {ReceiverType} builder, TimeSpan timeout)`

`ReceiverType` is resolved from `site.BuilderTypeName` + entity/result type args using the same pattern as existing generators.

### Carrier Eligibility Update

In `BuildChainParameters`, remove the exclusion:
```csharp
// REMOVE:
if (clause.Role is ClauseRole.Limit or ClauseRole.Offset
    or ClauseRole.Distinct or ClauseRole.WithTimeout)
    return null;
```

### Carrier Field Handling

- **Limit/Offset with constant value**: SQL already contains the constant. Interceptor is noop (`return builder`). No carrier field needed. `CarrierClassBuilder` only adds Limit/Offset fields when the clause analysis indicates a runtime value.
- **Limit/Offset with runtime value**: Carrier gets `int Limit` / `int Offset` field. Interceptor sets the field. Terminal appends to param array.
- **Distinct**: Always noop. No carrier field.
- **WithTimeout**: Carrier gets `TimeSpan? Timeout` field. Terminal resolves `__c.Timeout ?? __c.Ctx.DefaultTimeout`.

### Pagination Classification

Extend `ChainedClauseSite` or `ChainAnalysisResult` with:
```csharp
internal enum PaginationKind { None, Constant, RuntimeValue }
```

Determine at analysis time by checking if the Limit/Offset argument is a `LiteralExpressionSyntax` (constant) or anything else (runtime value). Record in a new `PaginationInfo` on `ChainAnalysisResult` or as metadata on the `ChainedClauseSite`.

`CarrierClassBuilder` reads this to decide whether to add Limit/Offset fields.

---

## 4. Delete/Update Carrier Branches

### Problem
`DeleteWhere`, `UpdateWhere`, `UpdateSet`, `UpdateSetPoco`, `Set` clause interceptors lack carrier-aware branches. Delete/Update chains fall back to non-carrier.

### Solution
Add `CarrierClassInfo? carrier = null` parameter to modification generators and emit carrier body using `EmitCarrierClauseBody`.

### Modified Methods

In `InterceptorCodeGenerator.Modifications.cs`:

```csharp
private static void GenerateDeleteWhereInterceptor(
    ..., CarrierClassInfo? carrier = null);

private static void GenerateUpdateSetInterceptor(
    ..., CarrierClassInfo? carrier = null);

private static void GenerateUpdateWhereInterceptor(
    ..., CarrierClassInfo? carrier = null);

private static void GenerateUpdateSetPocoInterceptor(
    ..., CarrierClassInfo? carrier = null);

private static void GenerateSetInterceptor(
    ..., CarrierClassInfo? carrier = null);
```

Each follows the same pattern as `GenerateWhereInterceptor`: add `if (carrier != null && prebuiltChain != null)` block before the existing prebuilt body, call `EmitCarrierClauseBody`.

### Carrier Eligibility Update

In `BuildChainParameters`, remove:
```csharp
// REMOVE:
if (clause.Role is ClauseRole.DeleteWhere or ClauseRole.UpdateWhere
    or ClauseRole.UpdateSet or ClauseRole.Set)
    return null;
```

In `BuildPrebuiltChainInfo` (single-entity), remove:
```csharp
// REMOVE:
&& queryKind.Value == QueryKind.Select
```

### Dispatch Update

In `GenerateInterceptorMethod` switch, pass carrier to modification cases:
```csharp
case InterceptorKind.DeleteWhere:
    GenerateDeleteWhereInterceptor(..., carrier: carrierInfo);
    break;
case InterceptorKind.UpdateSet:
    GenerateUpdateSetInterceptor(..., carrier: carrierInfo);
    break;
// etc.
```

### Carrier Base Classes for Delete/Update

Delete/Update chains use `IExecutableDeleteBuilder<T>` / `IExecutableUpdateBuilder<T>` interfaces, not `IQueryBuilder<T>`. Additional base classes needed:

| Class | Implements |
|-------|-----------|
| `DeleteCarrierBase<T>` | `IDeleteBuilder<T>`, `IExecutableDeleteBuilder<T>` |
| `UpdateCarrierBase<T>` | `IUpdateBuilder<T>`, `IExecutableUpdateBuilder<T>` |

These are simpler than query carriers — Delete/Update interfaces have fewer methods.

---

## 5. Nullable Heuristic

### Problem
`ChainParameterInfo.TypeName` is a raw string. Nullable types (e.g., `int?`, `string?`) should emit nullable carrier fields, but there's no explicit handling.

### Solution
In `CarrierClassBuilder.Build()`, when emitting parameter fields, detect nullable types via `?` suffix and ensure the field type preserves nullability.

### Algorithm

```csharp
// In CarrierClassBuilder, when creating CarrierField for a parameter:
var typeName = param.TypeName;
// Normalize: ensure Nullable<T> is represented as T?
if (typeName.StartsWith("System.Nullable<") || typeName.StartsWith("Nullable<"))
{
    var inner = typeName.Substring(typeName.IndexOf('<') + 1).TrimEnd('>');
    typeName = inner + "?";
}
// The TypeName already ends with '?' for nullable value types from ParameterInfo.ClrType
// Just pass through — the string representation is already correct for code emission
```

For reference types, `string?` vs `string` — `ParameterInfo.ClrType` may or may not include the `?`. Since we emit in a `#nullable enable` context, non-annotated reference types get a warning. The heuristic: if the type is a known reference type (`string`, or any type not in the value-type set), emit with `?` suffix to suppress nullable warnings in the carrier class.

### Value Type Set

Quick check: if `TypeName` is one of `int`, `long`, `short`, `byte`, `float`, `double`, `decimal`, `bool`, `char`, `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`, `DateOnly`, `TimeOnly` (or their `System.` prefixed forms), it's a value type. Otherwise assume reference type and append `?` if not already present.

Alternatively: if `TypeName` does NOT end with `?` and is NOT in the value type set → append `?`.

### File Changes
- `CarrierClassBuilder.cs` — add nullable normalization when creating `CarrierField` instances for parameters

---

## 6. Snapshot Tests

### Problem
No generator-level tests verify that carrier classes and carrier-aware interceptor bodies are emitted correctly.

### Solution
Add snapshot tests that compile representative source → run generator → assert generated output contains expected carrier patterns.

### Test Scenarios

| Scenario | Chain Shape | Validates |
|----------|-------------|-----------|
| SimpleNoParams | `db.Users().Select(u => u).ExecuteFetchAllAsync()` | Carrier with just Ctx, zero-param executor call |
| TwoParamsOneBit | `var q = db.Users(); if (c) q = q.Where(...); q.Select().Execute()` | Typed P0/P1 fields, Mask field, conditional bit setting |
| JoinTwoTables | `db.Users().Join<Order>(...).Select(...).Execute()` | JoinedCarrierBase, Unsafe.As interface crossing |
| JoinThreeTables | Same with 3 joins | JoinedCarrierBase3 |
| JoinFourTables | Same with 4 joins | JoinedCarrierBase4 |
| ConstantLimit | `...Select().Limit(10).Execute()` | Limit baked into SQL, noop interceptor |
| RuntimeLimit | `...Select().Limit(pageSize).Execute()` | Carrier Limit field, param array append |
| WithTimeout | `...WithTimeout(ts).Select().Execute()` | Carrier Timeout field, timeout resolution |
| Distinct | `...Distinct().Select().Execute()` | Noop interceptor, SQL includes DISTINCT |
| ToSqlOnly | `db.Users().Where(...).ToSql()` | CarrierBase<T> (no Ctx), ToSql terminal |
| ForkedChain | `var q = db.Users(); q.Execute(); q.Execute()` | QRY033 diagnostic emitted |
| DeleteWithWhere | `db.Delete<User>().Where(...).ExecuteNonQueryAsync()` | DeleteCarrierBase, non-query terminal |
| ChainRootConditional | `var q = db.Users(); if (c) q = q.Where(); q.Select().Execute()` | Root creates carrier, conditional Where sets mask |

### Test Pattern

```csharp
[Test]
public void CarrierGeneration_SimpleNoParams()
{
    var source = @"...user source code...";
    var (compilation, diagnostics) = CompileWithGenerator(source);

    var generated = GetGeneratedSource(compilation, "...Interceptors...");

    Assert.That(generated, Does.Contain("file sealed class Chain_0 : CarrierBase<"));
    Assert.That(generated, Does.Contain("QueryExecutor.ExecuteCarrierAsync<"));
    Assert.That(generated, Does.Not.Contain("AllocatePrebuiltParams"));
}
```

### File Changes
- New file: `Quarry.Tests/Generation/CarrierGenerationTests.cs`

---

## Implementation Order

Dependencies determine order:

1. **§2 Factory method refactor** — breaking change, must go first so all subsequent work builds on methods not properties. Update all test/sample code.
2. **§1 Carrier base classes** — enables interface implementation and `is` checks. Update `CarrierClassBuilder` and emission.
3. **§3 Limit/Offset/Distinct/WithTimeout interceptors** — expands eligibility. Depends on §2 (chain root ensures carrier exists).
4. **§4 Delete/Update carrier branches** — independent of §3 but benefits from §1 (base classes for Delete/Update builders).
5. **§5 Nullable heuristic** — small, independent change.
6. **§6 Snapshot tests** — last, validates everything above.

---

## File Change Map

### New Files
- `Quarry/Internal/CarrierBase.cs`
- `Quarry/Internal/JoinedCarrierBase.cs`
- `Quarry/Internal/JoinedCarrierBase3.cs`
- `Quarry/Internal/JoinedCarrierBase4.cs`
- `Quarry/Internal/DeleteCarrierBase.cs`
- `Quarry/Internal/UpdateCarrierBase.cs`
- `Quarry.Tests/Generation/CarrierGenerationTests.cs`

### Modified Files
- `Quarry/Context/QuarryContext.cs` — entity accessor API change (property → method)
- `Quarry.Generator/Generation/ContextCodeGenerator.cs` — emit methods instead of properties
- `Quarry.Generator/Models/UsageSiteInfo.cs` — `InterceptorKind.ChainRoot`
- `Quarry.Generator/Models/ChainAnalysisResult.cs` — `ClauseRole.ChainRoot`, `PaginationKind`
- `Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — chain root detection
- `Quarry.Generator/Parsing/ChainAnalyzer.cs` — chain root as first clause
- `Quarry.Generator/Generation/CarrierClassBuilder.cs` — base class selection, nullable heuristic
- `Quarry.Generator/Generation/InterceptorCodeGenerator.Carrier.cs` — base class emission, chain root interceptor
- `Quarry.Generator/Generation/InterceptorCodeGenerator.Query.cs` — Limit/Offset/Distinct/WithTimeout dispatch, chain root case
- `Quarry.Generator/Generation/InterceptorCodeGenerator.Modifications.cs` — carrier branches for Delete/Update
- `Quarry.Generator/QuarryGenerator.cs` — remove eligibility exclusions
- All test/sample files — `db.Users` → `db.Users()`
