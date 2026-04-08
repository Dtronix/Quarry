# Architectural Findings: Typed API Declarations in Base Library

## The Principle

**Typed API declarations belong in the hand-written base library so the source generator's own discovery SemanticModel can see them. Implementations belong in generator output.**

## Why It Exists

The source generator's discovery pass runs against a `SemanticModel` that contains only the **user's source code** plus referenced assemblies. The generator's own output for the current compilation is invisible to itself. Whenever a typed member exists only in generator-emitted source, every chain that walks through it drops into "untyped" (error type) at discovery time and forces a syntactic fallback — or is silently dropped entirely.

This blindness is fundamental to Roslyn's incremental generator model: a generator cannot see its own output because that would create a circular dependency (output depends on discovery, discovery depends on output).

## Precedent: Entity Accessors

The codebase already applies this principle correctly for entity accessors. The user declares:

```csharp
public partial IEntityAccessor<User> Users();
```

The generator supplies only the body via partial implementation. The **declaration** (including the return type `IEntityAccessor<User>`) is visible to the SemanticModel during discovery; the **implementation** is not, but it doesn't need to be.

## How `With<TDto>` Violated the Principle

Before this PR, the typed `With<TDto>` return was only declared in the generator-emitted partial class:

```csharp
// Generated code (invisible to discovery):
public new MyDb With<TDto>(IQueryBuilder<TDto> innerQuery) where TDto : class => ...;
```

The base class `QuarryContext.With<TDto>` returned `QuarryContext` — the chain drifted to the base type after `With<>`, and every subsequent method (`.Users()`, `.Join<>()`, `.Select()`) failed to resolve. The generator had a syntactic fallback (`DiscoverPostCteSites`) but it produced approximate types that caused interceptor signature mismatches.

## How `QuarryContext<TSelf>` Applies the Principle

By moving the typed return into a hand-written generic base class:

```csharp
public abstract class QuarryContext<TSelf> : QuarryContext
    where TSelf : QuarryContext<TSelf>
{
    public override TSelf With<TDto>(IQueryBuilder<TDto> innerQuery) => ...;
}
```

Key design decisions:
- **`virtual`/`override` instead of `new`**: The base `With<>` is `virtual`; the generic subclass uses `override` with covariant return type (`TSelf`). Unlike `new`, an `override` properly replaces the base method in Roslyn's member lookup — no candidate ambiguity, no error types.
- **Covariant return type**: `TSelf` (e.g., `CteDb`) derives from `QuarryContext`, satisfying C# 9+ covariant return rules. The SemanticModel sees the correct concrete return type.
- **Generated shadow still uses `new`**: The generator-emitted `new CteDb With<TDto>(...)` hides the `override` at compile time, serving as the interceptor target. During discovery (when the shadow is invisible), the `override` is used instead.

## Workaround Code the Principle Enables Deleting

Once all in-repo contexts migrate to `QuarryContext<TSelf>`, the following synthetic discovery code becomes dead:

| Code | File | ~Lines | Purpose |
|------|------|--------|---------|
| `DiscoverPostCteSites` | UsageSiteDiscovery.cs | ~240 | Synthetic sites for post-With chains |
| `TryResolveViaChainRootContext` callers | UsageSiteDiscovery.cs | ~40 | Fallback entity-accessor resolution |
| CTE candidate disambiguation | UsageSiteDiscovery.cs | ~45 | Selects most-derived `With<>` from ambiguous candidates |
| `AnalyzeSingleEntitySyntaxOnly` | ProjectionAnalyzer.cs | ~30 | Syntax-only projection for synthetic Select sites |
| Site deduplication in `EnrichAll` | DisplayClassEnricher.cs | ~15 | Dedup synthetic + normal discovery sites |

An analogous refactor for `NavigationList<T>` (user-declared partial navigation properties, generator emits body) would eliminate `DiscoverPostJoinSites` (~170 lines) by the same mechanism.

## Follow-Up Work

1. **Migrate all in-repo contexts to `QuarryContext<TSelf>`** and delete `DiscoverPostCteSites` + related fallback code. Contexts to migrate: `TestDbContext`, `PostgreSqlDbContext`, `MySqlDbContext`, `SqlServerDbContext`, sample apps.

2. **Apply the same principle to `NavigationList<T>`** — user-declared partial navigation properties, generator emits body. This would dissolve `DiscoverPostJoinSites` the same way `QuarryContext<TSelf>` dissolves `DiscoverPostCteSites`.

3. **CTE-to-join table resolution** — `Join<Order>` after `With<Order>` should reference the CTE name ("Order") instead of the underlying table ("orders"). Currently the CTE is defined but the JOIN doesn't use it.

4. **Diagnostic analyzer** nudging legacy-base users to migrate when it detects `With<>` chained with an entity accessor.
