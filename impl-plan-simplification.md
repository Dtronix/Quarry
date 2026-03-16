# IEntityAccessor Unification & Generator Simplification Plan

## Overview

Replace the fragmented entry point API (`db.Users()` for queries, `db.Delete<User>()` for deletes, `db.Update<User>()` for updates, `db.Insert(entity)` for inserts) with a single unified entry point per entity: `db.Users()` returns `IEntityAccessor<T>` which branches into any operation kind.

**Key insight**: For fully analyzed chains, the generator knows the operation kind at compile time. The ChainRoot interceptor creates the correct carrier type directly — `CarrierBase<T,R>` for queries, `DeleteCarrierBase<T>` for deletes, `UpdateCarrierBase<T>` for updates. The `IEntityAccessor<T>` is never instantiated on the carrier path.

**Generator simplification**: The chain walker no longer needs special handling for `Delete<T>()`/`Update<T>()` methods on QuarryContext. All chains start with `db.Entity()` → ChainRoot. The `.Delete()`/`.Update()` transition is a recognized noop node that confirms the operation kind. The execution terminal determines the final carrier type.

---

## 1. IEntityAccessor<T> Interface

`IEntityAccessor<T>` is a **slim** interface — it does NOT extend `IQueryBuilder<T>`. It contains only chain-starting methods. The full `IQueryBuilder<T>` surface (OrderBy, Limit, GroupBy, Having, Offset, ThenBy) only appears after the first clause transitions the chain.

```csharp
public interface IEntityAccessor<T> where T : class
{
    // Query chain starters (return IQueryBuilder<T> to enter full query surface):
    IQueryBuilder<T> Where(Expression<Func<T, bool>> predicate);
    IQueryBuilder<T, TResult> Select<TResult>(Func<T, TResult> selector);
    IJoinedQueryBuilder<T, TJoined> Join<TJoined>(Expression<Func<T, TJoined, bool>> condition) where TJoined : class;
    IJoinedQueryBuilder<T, TJoined> LeftJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition) where TJoined : class;
    IJoinedQueryBuilder<T, TJoined> RightJoin<TJoined>(Expression<Func<T, TJoined, bool>> condition) where TJoined : class;
    IJoinedQueryBuilder<T, TJoined> Join<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation) where TJoined : class;
    IJoinedQueryBuilder<T, TJoined> LeftJoin<TJoined>(Expression<Func<T, NavigationList<TJoined>>> navigation) where TJoined : class;
    IQueryBuilder<T> Distinct();
    IQueryBuilder<T> WithTimeout(TimeSpan timeout);
    string ToSql();

    // Modification entry points:
    IDeleteBuilder<T> Delete();
    IUpdateBuilder<T> Update();
    IInsertBuilder<T> Insert(T entity);
    IInsertBuilder<T> InsertMany(IEnumerable<T> entities);

    // Diagnostics:
    QueryPlan ToQueryPlan();
}
```

**Design rationale**: The accessor is the entity "handle" — it knows the entity, table, dialect, and context. Calling a chain-starting method transitions to the appropriate builder interface where the full query API is available. Methods like `OrderBy`, `Limit`, `GroupBy` are not meaningful as chain starters and don't appear on the accessor.

**IntelliSense benefit**: Users see only meaningful starting operations. After `.Where(...)`, the full `IQueryBuilder<T>` surface appears with `OrderBy`, `Limit`, `GroupBy`, etc.

**Carrier path impact**: The carrier base class implements both `IEntityAccessor<T>` and `IQueryBuilder<T>`. `Unsafe.As` works for all interface crossings since the carrier satisfies all interfaces. The slim accessor only affects what IntelliSense shows at the root — runtime behavior is unchanged.

### Breaking Changes

- `db.Users().OrderBy(...)` → `db.Users().Where(u => true).OrderBy(...)` or `db.Users().Select(u => u).OrderBy(...)` (OrderBy no longer on accessor)
- `db.Users().Limit(10)` → needs a Where/Select first
- `db.Users().GroupBy(...)` → needs a Where/Select first
- `db.Delete<User>()` → `db.Users().Delete()`
- `db.Update<User>()` → `db.Users().Update()`
- `db.Insert(entity)` → `db.Users().Insert(entity)`
- `db.InsertMany(entities)` → `db.Users().InsertMany(entities)`

Query chains starting with Where/Select/Join/Distinct are unchanged.

---

## 2. Runtime Implementation: EntityAccessor<T> Struct

For the non-intercepted fallback path, a zero-allocation readonly struct. Since `IEntityAccessor<T>` is slim (no `IQueryBuilder<T>` inheritance), the struct only implements the chain-starting methods:

```csharp
public readonly struct EntityAccessor<T> : IEntityAccessor<T> where T : class
{
    private readonly SqlDialect _dialect;
    private readonly string _tableName;
    private readonly string? _schemaName;
    private readonly IQueryExecutionContext? _ctx;

    public EntityAccessor(SqlDialect dialect, string tableName, string? schemaName, IQueryExecutionContext? ctx);

    // Query chain starters — each creates a QueryBuilder<T> on demand:
    IQueryBuilder<T> IEntityAccessor<T>.Where(Expression<Func<T, bool>> predicate)
        => QueryBuilder<T>.Create(_dialect, _tableName, _schemaName, _ctx).Where(predicate);
    IQueryBuilder<T, TResult> IEntityAccessor<T>.Select<TResult>(Func<T, TResult> selector)
        => QueryBuilder<T>.Create(_dialect, _tableName, _schemaName, _ctx).Select(selector);
    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.Join<TJoined>(Expression<Func<T, TJoined, bool>> condition)
        => QueryBuilder<T>.Create(_dialect, _tableName, _schemaName, _ctx).Join(condition);
    // ... LeftJoin, RightJoin, navigation Join/LeftJoin overloads ...
    IQueryBuilder<T> IEntityAccessor<T>.Distinct()
        => QueryBuilder<T>.Create(_dialect, _tableName, _schemaName, _ctx).Distinct();
    IQueryBuilder<T> IEntityAccessor<T>.WithTimeout(TimeSpan timeout)
        => QueryBuilder<T>.Create(_dialect, _tableName, _schemaName, _ctx).WithTimeout(timeout);
    string IEntityAccessor<T>.ToSql()
        => throw new InvalidOperationException("ToSql requires a Select clause.");

    // Modification methods — each creates the appropriate builder:
    IDeleteBuilder<T> IEntityAccessor<T>.Delete()
        => DeleteBuilder<T>.Create(_dialect, _tableName, _schemaName, _ctx);
    IUpdateBuilder<T> IEntityAccessor<T>.Update()
        => UpdateBuilder<T>.Create(_dialect, _tableName, _schemaName, _ctx);
    IInsertBuilder<T> IEntityAccessor<T>.Insert(T entity)
        => InsertBuilder<T>.Create(_dialect, _tableName, _schemaName, _ctx).Values(entity);
    IInsertBuilder<T> IEntityAccessor<T>.InsertMany(IEnumerable<T> entities)
        => InsertBuilder<T>.Create(_dialect, _tableName, _schemaName, _ctx).Values(entities);

    // Diagnostics:
    QueryPlan IEntityAccessor<T>.ToQueryPlan()
        => new QueryPlan(sql: null, QueryPlanTier.RuntimeBuild, _dialect);
}
```

The struct holds only 4 fields. No heap allocation until a chain-starting method is called. Each method creates the appropriate concrete builder on demand and delegates to it.

### Boxing Avoidance

The context method returns `EntityAccessor<T>` (struct type) not `IEntityAccessor<T>`:

```csharp
// Generated context method:
public partial EntityAccessor<User> Users()
    => new EntityAccessor<User>(_dialect, "users", _schemaName, (IQueryExecutionContext)this);
```

- Fluent chains: `db.Users().Where(...)` calls Where on the struct — no boxing
- Carrier path: ChainRoot interceptor returns a carrier class cast to `IEntityAccessor<T>` — no boxing (carrier is a class)
- Struct never escapes to the heap on the optimized path

---

## 3. Generator Changes: Unified ChainRoot

### Chain Root Detection

`UsageSiteDiscovery` already detects context entity methods as `InterceptorKind.ChainRoot`. No change needed — `Users()` returns `EntityAccessor<T>` which implements `IEntityAccessor<T>` which extends `IQueryBuilder<T>`. The detection pattern (`IQueryBuilder` return type) still matches.

Actually, since the return type changes from `IQueryBuilder<T>` to `EntityAccessor<T>`, the detection must match the new type:

```csharp
// Updated ChainRoot detection:
if (returnType.Name is "EntityAccessor" or "IEntityAccessor"
    || (returnType.AllInterfaces.Any(i => i.Name == "IQueryBuilder")))
```

### Operation Kind Transition: Delete()/Update()

New `InterceptorKind` values:
```csharp
InterceptorKind.DeleteTransition  // .Delete() on IEntityAccessor<T>
InterceptorKind.UpdateTransition  // .Update() on IEntityAccessor<T>
```

New `ClauseRole` values:
```csharp
ClauseRole.DeleteTransition
ClauseRole.UpdateTransition
```

These are noop transition nodes in the chain. The chain walker recognizes them and skips them. The execution terminal already determines the operation kind — the transition node confirms it.

On the carrier path:
- Query chains: ChainRoot creates `CarrierBase<T>` or `CarrierBase<T,R>` (determined by chain analysis at compile time)
- Delete chains: ChainRoot creates `DeleteCarrierBase<T>` (the `.Delete()` transition is a noop)
- Update chains: ChainRoot creates `UpdateCarrierBase<T>` (the `.Update()` transition is a noop)

On the non-carrier path:
- `.Delete()` creates a real `DeleteBuilder<T>` from the `EntityAccessor<T>` struct
- `.Update()` creates a real `UpdateBuilder<T>` from the `EntityAccessor<T>` struct

### How the Generator Knows the Operation Kind

The generator walks backward from the execution terminal. It already knows `QueryKind` from the terminal (`ExecuteFetchAllAsync` → Select, `ExecuteNonQueryAsync` on DeleteBuilder → Delete, etc.). The transition node (`.Delete()`/`.Update()`) is discovered during the walk and added to the chain's clause list as a noop.

For carrier building, `ResolveCarrierBaseClass` already uses `chain.QueryKind` to select the base class. No change needed.

### Chain Walker Simplification

Currently the walker handles:
1. `db.Users()` → ChainRoot (IQueryBuilder return)
2. `db.Delete<User>()` → not recognized (IDeleteBuilder return, different factory)
3. `db.Update<User>()` → not recognized (IUpdateBuilder return, different factory)

After unification:
1. `db.Users()` → ChainRoot (always)
2. `db.Users().Delete()` → ChainRoot + DeleteTransition
3. `db.Users().Update()` → ChainRoot + UpdateTransition

All chains start the same way. The walker only needs to recognize the transition nodes as noops. No special factory method handling per operation kind.

---

## 4. ContextCodeGenerator Changes

### Property → Method (Already Done)

Entity set accessors are already methods from the §2 refactor.

### Return Type Change

```csharp
// Before:
public partial IQueryBuilder<User> Users()
    => QueryBuilder<User>.Create(_dialect, "users", null, (IQueryExecutionContext)this);

// After:
public partial EntityAccessor<User> Users()
    => new EntityAccessor<User>(_dialect, "users", null, (IQueryExecutionContext)this);
```

### Remove Separate Delete/Update/Insert Methods

Remove from generated context:
- `Delete<T>()` generic method
- `Update<T>()` / `UpdateUser()` specific methods
- `Insert(entity)` / `InsertMany(entities)` methods

These are replaced by `db.Users().Delete()`, `db.Users().Update()`, `db.Users().Insert(entity)`.

### Insert Method on Context

`db.Insert(entity)` currently takes any entity. With the accessor, `db.Users().Insert(entity)` is entity-specific. The context-level `Insert` can be removed since each accessor provides it.

---

## 5. Carrier Base Class Changes

### CarrierBase<T> Implements IEntityAccessor<T> + IQueryBuilder<T>

Since `IEntityAccessor<T>` is slim and does NOT extend `IQueryBuilder<T>`, the carrier must implement both interfaces independently. This gives the carrier the full surface needed for `Unsafe.As` casts at any point in the chain.

```csharp
public abstract class CarrierBase<T> : IEntityAccessor<T>, IQueryBuilder<T> where T : class
{
    public IQueryExecutionContext? Ctx;

    // IEntityAccessor<T> chain starters — all throw (intercepted on carrier path):
    IQueryBuilder<T> IEntityAccessor<T>.Where(...) => throw ...;
    IQueryBuilder<T, TResult> IEntityAccessor<T>.Select<TResult>(...) => throw ...;
    IJoinedQueryBuilder<T, TJoined> IEntityAccessor<T>.Join<TJoined>(...) => throw ...;
    // ... all IEntityAccessor methods ...
    IDeleteBuilder<T> IEntityAccessor<T>.Delete() => throw ...;
    IUpdateBuilder<T> IEntityAccessor<T>.Update() => throw ...;
    IInsertBuilder<T> IEntityAccessor<T>.Insert(T entity) => throw ...;
    IInsertBuilder<T> IEntityAccessor<T>.InsertMany(IEnumerable<T> entities) => throw ...;

    // IQueryBuilder<T> methods — all throw (intercepted on carrier path):
    IQueryBuilder<T> IQueryBuilder<T>.Where(...) => throw ...;
    IQueryBuilder<T> IQueryBuilder<T>.OrderBy<TKey>(...) => throw ...;
    IQueryBuilder<T> IQueryBuilder<T>.Limit(int count) => throw ...;
    // ... all IQueryBuilder methods (same as current) ...
}
```

**Note**: `IEntityAccessor<T>.Where()` and `IQueryBuilder<T>.Where()` have the same signature but are on different interfaces. The carrier implements both as separate explicit impls that both throw. On the carrier path, `Unsafe.As` casts the carrier to whichever interface the interceptor expects.

### CarrierBase<T, R>

Now inherits from `CarrierBase<T>` conceptually (or implements `IEntityAccessor<T>` + `IQueryBuilder<T>` + `IQueryBuilder<T, R>` directly). The `IEntityAccessor<T>` stubs are inherited.

### DeleteCarrierBase<T> and UpdateCarrierBase<T>

Created directly by ChainRoot when the chain is Delete/Update. These implement `IEntityAccessor<T>` (for the root crossing) plus their own builder interfaces. The `IEntityAccessor<T>` stubs are dead methods — the ChainRoot interceptor replaces `db.Users()` with the carrier, and the `.Delete()` transition is a noop.

---

## 6. Generator Simplification Summary

### What Gets Simpler

1. **ChainRoot is universal**: Every chain starts with `db.Entity()` → ChainRoot. No special detection for `Delete<T>()`/`Update<T>()` factory methods.

2. **Delete/Update ChainRoot eliminated**: No need to extend ChainRoot detection for `IDeleteBuilder`/`IUpdateBuilder` return types. All entity methods return `EntityAccessor<T>` (or `IEntityAccessor<T>`).

3. **Transition nodes are noops**: `.Delete()`/`.Update()` on the accessor are simple noop transitions in the chain. The generator already handles noops for carrier chains.

4. **Carrier type selection is cleaner**: `ResolveCarrierBaseClass` uses `chain.QueryKind` which is determined from the execution terminal. No change needed.

5. **No context-level generic methods**: `Delete<T>()`/`Update<T>()` on QuarryContext are removed. The generator no longer needs to emit them. Simpler context code generation.

### What Stays the Same

1. Chain analysis (walking backward from terminal)
2. Clause interceptor emission (Where, OrderBy, etc.)
3. Execution terminal emission (ExecuteFetchAllAsync, ExecuteNonQueryAsync, etc.)
4. Carrier field emission (parameters, mask, timeout)
5. Parameter extraction via FieldInfo

### New Complexity

1. `IEntityAccessor<T>` slim interface definition (chain starters + modification entry points)
2. `EntityAccessor<T>` struct implementation (runtime fallback, only implements slim interface)
3. `DeleteTransition`/`UpdateTransition` InterceptorKind and ClauseRole values
4. Transition node interceptor generation (noop)
5. Carrier base classes implement both `IEntityAccessor<T>` (slim) and `IQueryBuilder<T>` (full) — duplicate Where/Select stubs on separate interfaces
6. All test/user code migration: `db.Delete<User>()` → `db.Users().Delete()`, and `db.Users().OrderBy(...)` → needs Where/Select first

### IntelliSense Improvement

The slim `IEntityAccessor<T>` means users typing `db.Users().` see only:
- `Where`, `Select`, `Join`, `LeftJoin`, `RightJoin`, `Distinct`, `WithTimeout`, `ToSql`
- `Delete`, `Update`, `Insert`, `InsertMany`
- `ToQueryPlan`

After `.Where(...)`, the full `IQueryBuilder<T>` surface appears:
- All of the above PLUS `OrderBy`, `ThenBy`, `GroupBy`, `Having`, `Offset`, `Limit`

---

## 7. Migration

### User Code Changes

| Before | After |
|--------|-------|
| `db.Delete<User>().Where(...)` | `db.Users().Delete().Where(...)` |
| `db.Update<User>().Set(...)` | `db.Users().Update().Set(...)` |
| `db.UpdateUser().Set(...)` | `db.Users().Update().Set(...)` |
| `db.Insert(entity)` | `db.Users().Insert(entity)` |
| `db.InsertMany(entities)` | `db.Users().InsertMany(entities)` |
| `db.Users().Where(...)` | `db.Users().Where(...)` (unchanged) |

All changes produce compile errors (method not found) — no silent behavior changes.

---

## 8. Implementation Order

1. **IEntityAccessor<T> interface** — define in `Quarry/Query/`
2. **EntityAccessor<T> struct** — implement in `Quarry/Query/` with all runtime fallback methods
3. **ContextCodeGenerator** — change return type to `EntityAccessor<T>`, remove Delete/Update/Insert methods
4. **InterceptorKind/ClauseRole** — add DeleteTransition, UpdateTransition
5. **UsageSiteDiscovery** — recognize `.Delete()`/`.Update()` on `IEntityAccessor<T>` as transitions
6. **ChainAnalyzer** — treat transitions as noop clause nodes
7. **CarrierBase<T>** — implement `IEntityAccessor<T>` with dead stubs for modification methods
8. **GenerateCarrierChainRootInterceptor** — update to handle `EntityAccessor<T>` return type
9. **Transition interceptor generation** — noop methods for `.Delete()`/`.Update()` on carrier path
10. **Test migration** — update all `db.Delete<User>()` → `db.Users().Delete()` patterns
11. **Remove deprecated context methods** — `Delete<T>()`, `Update<T>()`, `Insert()`, `InsertMany()`

---

## 9. File Change Map

### New Files
- `Quarry/Query/IEntityAccessor.cs` — interface definition
- `Quarry/Query/EntityAccessor.cs` — readonly struct runtime implementation

### Modified Files
- `Quarry/Internal/CarrierBase.cs` — implement `IEntityAccessor<T>`
- `Quarry/Context/QuarryContext.cs` — remove Delete/Update/Insert base methods
- `Quarry.Generator/Generation/ContextCodeGenerator.cs` — return `EntityAccessor<T>`, remove modification methods
- `Quarry.Generator/Models/UsageSiteInfo.cs` — add `DeleteTransition`, `UpdateTransition` kinds
- `Quarry.Generator/Models/ChainAnalysisResult.cs` — add transition clause roles
- `Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — detect transition methods
- `Quarry.Generator/Parsing/ChainAnalyzer.cs` — treat transitions as noops
- `Quarry.Generator/Generation/InterceptorCodeGenerator.Carrier.cs` — update ChainRoot for EntityAccessor return type, add transition noop interceptors
- `Quarry.Generator/Generation/InterceptorCodeGenerator.Query.cs` — add transition dispatch
- All test/sample/benchmark files — migration of Delete/Update/Insert calls
