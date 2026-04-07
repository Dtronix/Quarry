# Plan: CTE+Join chains via `QuarryContext<TSelf>` (#205)

## Goal

Fix issue #205: CTE chains that combine `With<TDto>(...)` with entity accessors and further builder methods (`.With<A>(inner).Users().Join<Order>(...).Select(...)`) are silently dropped by the source generator because `QuarryContext.With<TDto>` returns the base class, causing the receiver type to drift and the discovery SemanticModel to fail resolving subsequent chain methods.

Approach: introduce a new hand-written generic subclass `QuarryContext<TSelf> : QuarryContext where TSelf : QuarryContext<TSelf>` that shadows the base `With<...>` overloads with typed `new TSelf With<...>` versions. Users opt in by changing inheritance from `: QuarryContext` to `: QuarryContext<MyDb>`. Because the typed signature lives in the hand-written base library, the generator's own discovery SemanticModel sees the correct return type and the rest of the chain resolves normally — no discovery workarounds needed for migrated contexts.

## Key concept: where "typed" lives determines whether discovery can see it

The source generator's discovery pass runs against a SemanticModel that contains only the **user's source** — the generator's own output for the current compilation is invisible to itself. Whenever a typed member exists only in generator-emitted source, every chain that walks through it drops into "untyped" at discovery time and forces a syntactic fallback.

The codebase already applies the correct principle for entity accessors: the user declares `public partial IEntityAccessor<User> Users();` in their own source, and the generator supplies only the body via `partial` implementation. The declaration is visible to the SemanticModel; the implementation is not, but it doesn't need to be for discovery.

`With<TDto>` broke this principle: the typed return (`MyDb`) was only declared in the generator-emitted partial class. Discovery couldn't see it, so the chain drifted to base `QuarryContext` after `With<>`, and every subsequent method failed to resolve. Rather than teach discovery to simulate what it can't see (`DiscoverPostCteSites`), we move the typed declaration into hand-written base library code via `QuarryContext<TSelf>`.

## Algorithm sketch: the `new TSelf With<TDto>` shadow

```csharp
public abstract class QuarryContext<TSelf> : QuarryContext
    where TSelf : QuarryContext<TSelf>
{
    protected QuarryContext(IDbConnection connection) : base(connection) { }
    protected QuarryContext(IDbConnection connection, bool ownsConnection)
        : base(connection, ownsConnection) { }
    protected QuarryContext(
        IDbConnection connection,
        bool ownsConnection,
        TimeSpan? defaultTimeout,
        IsolationLevel? defaultIsolation)
        : base(connection, ownsConnection, defaultTimeout, defaultIsolation) { }

    public new TSelf With<TDto>(IQueryBuilder<TDto> innerQuery) where TDto : class
        => throw new NotSupportedException(
            "CTE methods must be intercepted by the Quarry source generator. " +
            "If you reach this exception your context variable is typed as the abstract " +
            "base class instead of your generated derived type — interceptors only fire " +
            "when the call site resolves to the derived overload.");

    public new TSelf With<TEntity, TDto>(IQueryBuilder<TEntity, TDto> innerQuery)
        where TEntity : class where TDto : class
        => throw new NotSupportedException(/* same message */);
}
```

`FromCte<TDto>()` stays on the non-generic `QuarryContext` unchanged because it already returns `IEntityAccessor<TDto>` (a base-library type) — no drift. The generic subclass inherits it.

Why `new` and not `override`: the base `With<>` overloads are not virtual, and introducing virtual here would affect every non-migrated subclass. `new` is the idiomatic C# mechanism for type-system-level shadowing that changes static dispatch at the call site.

Why `TSelf : QuarryContext<TSelf>` and not a simpler constraint: without the self-referencing constraint, `TSelf` could be any reference type, and the throw-body couldn't safely cast to `TSelf` if it ever needed to. The CRTP constraint also documents intent ("this type parameter is the derived context") and catches mistakes at the base clause (`: QuarryContext<SomeOtherClass>` fails to compile).

## Generator integration (Path 2: conditional emission)

`ContextCodeGenerator.GenerateCteMethods` currently emits a `public new {ContextClass} With<TDto>(...)` shadow on every generated partial. For a user whose base is already `QuarryContext<TSelf>`, that shadow would be redundant: the typed `With<>` is already inherited from the generic base. Emitting it would trigger compiler warning CS0109 ("the member does not hide an inherited member; the new keyword is not required").

Fix: give `ContextInfo` a new `HasGenericContextBase` flag, set by `ContextParser` during its existing base-type walk, and have `ContextCodeGenerator.GenerateCteMethods` skip emission of the two `With<>` blocks when that flag is true. The `FromCte<TDto>` block keeps being emitted — it still shadows the non-generic `QuarryContext.FromCte<TDto>` through the inheritance chain, and its `new` is still valid.

`ContextParser.InheritsFromQuarryContext` already walks the full base chain and naturally returns `true` for `: QuarryContext<MyDb>` (it matches at hop 2 — `QuarryContext<MyDb>`'s own base is `QuarryContext`). No change to detection logic for *recognizing* a context; only an additional "while we're walking, note if we saw a `QuarryContext<>` closed generic before reaching the non-generic base".

`ContextInfo` is `sealed` and value-equality via `IEquatable`. Adding a field requires updating the constructor, `Equals`, and `GetHashCode` to keep incremental-generator caching correct. Any cached node that differs in this flag must be recomputed.

## Test context strategy

Rather than migrating `TestDbContext` (which is consumed by hundreds of tests across the suite), add a new dedicated `CteChainTestDbContext : QuarryContext<CteChainTestDbContext>` that exposes a small subset of entity accessors (`Users`, `Orders`, `OrderItems`). Reuses the existing `UserSchema`, `OrderSchema`, `OrderItemSchema` — zero new schema boilerplate. Keeps blast radius on existing fixtures at zero and isolates the new-path tests in their own fixture.

Full migration of `TestDbContext`, sample apps, and the other dialect contexts happens later, in the follow-up cleanup PR that also deletes `DiscoverPostCteSites`.

## Session artifact for PR description

The user explicitly requested that the eventual PR body capture the architectural findings for future use. Write `_sessions/205-cte-join-with-return-type/architectural-findings.md` during implementation containing:

1. **The principle:** "Typed API declarations belong in the hand-written base library; implementations belong in generator output."
2. **Why it exists:** generator discovery SemanticModel blindness to its own output.
3. **Precedent:** entity accessors already follow the principle.
4. **Workarounds the principle dissolves once applied:** `DiscoverPostCteSites` (~155 lines), `TryResolveViaChainRootContext` (~40 lines) callers, and (via analogous refactor) `DiscoverPostJoinSites` (~170 lines).
5. **Follow-up work identified:** (a) migrate in-repo contexts to `QuarryContext<TSelf>` and delete `DiscoverPostCteSites`; (b) apply the same pattern to `NavigationList<T>` via user-declared partial navigation properties to eliminate `DiscoverPostJoinSites`.

The REMEDIATE phase's PR creation step reads all session artifacts and transcribes relevant sections into the PR body (see workflow.md PR Creation rules).

---

## Phases

### Phase 1 — Runtime library: add `QuarryContext<TSelf>`

**Files:**
- `src/Quarry/Context/QuarryContext.cs` — append the generic subclass at the bottom of the existing file (keeps both types colocated). Alternatively a new file `QuarryContextGeneric.cs` — decide during implementation based on file length after change.

**Content:**
- `public abstract class QuarryContext<TSelf> : QuarryContext where TSelf : QuarryContext<TSelf>`
- Three protected constructors mirroring the non-generic base, delegating to `base(...)`
- `public new TSelf With<TDto>(IQueryBuilder<TDto> innerQuery) where TDto : class` — throws `NotSupportedException` with the same "must be intercepted" message as the base
- `public new TSelf With<TEntity, TDto>(IQueryBuilder<TEntity, TDto> innerQuery) where TEntity : class where TDto : class` — same
- XML doc on the class explaining: what problem it solves, why users should prefer it over the non-generic base for new contexts, the CRTP constraint, and a one-line reference to the architectural principle with a pointer to the findings document.

**Tests to add:** none in this phase. The class compiles but nothing depends on it yet. Phase 3 adds the first consumer.

**Commit:** `Add QuarryContext<TSelf> generic subclass for typed CTE chains`

---

### Phase 2 — Generator: `ContextInfo` flag, parser detection, conditional emission

**Files:**
- `src/Quarry.Generator/Models/ContextInfo.cs`
  - Add `public bool HasGenericContextBase { get; }`
  - Add constructor parameter `bool hasGenericContextBase` and assign
  - Update `Equals` to include the new field
  - Update `GetHashCode` to include the new field
- `src/Quarry.Generator/Parsing/ContextParser.cs`
  - Refactor `InheritsFromQuarryContext` from `bool` to return a small struct or two-tuple: `(bool Inherits, bool ViaGeneric)`. During the existing base-type walk, if any `baseType` along the way has `Name == "QuarryContext"` and `Arity == 1` (i.e., the closed generic `QuarryContext<TSelf>`), set `ViaGeneric = true`. Continue walking until reaching the non-generic `Quarry.QuarryContext` as the existing code does — both conditions must be true for a successful match.
  - Update the single call site in `ParseContext` to consume the new return shape and pass `hasGenericContextBase` into `new ContextInfo(...)`.
- `src/Quarry.Generator/Generation/ContextCodeGenerator.cs`
  - In `GenerateCteMethods`, wrap the two `With<TDto>` / `With<TEntity, TDto>` shadow blocks in `if (!context.HasGenericContextBase) { ... }`. Leave the `FromCte<TDto>` block outside the conditional — it's always emitted.

**Tests to add in `src/Quarry.Tests/Generation/` (new file `ContextCodeGeneratorCteShadowTests.cs` or extend an existing one):**
- Legacy context (`: QuarryContext`) — assert the generated partial class source text contains `new {ContextClass} With<TDto>`.
- Generic context (`: QuarryContext<Ctx>`) — assert the generated partial class source text does NOT contain `new {ContextClass} With<TDto>` and does NOT contain a CS0109 warning (run the generated code through a compile and inspect diagnostics for absence of CS0109).
- Both variants — assert `new IEntityAccessor<TDto> FromCte<TDto>` is present (unchanged).

**Commit:** `Detect QuarryContext<TSelf> base and skip redundant With<> shadow emission`

---

### Phase 3 — Test fixture: `CteChainTestDbContext`

**Files:**
- `src/Quarry.Tests/Samples/CteChainTestDbContext.cs` (new)
  - `[QuarryContext(Dialect = SqlDialect.SQLite)]`
  - `public partial class CteChainTestDbContext : QuarryContext<CteChainTestDbContext>`
  - Partial entity accessor declarations for `Users`, `Orders`, `OrderItems` (reuses existing schemas from `Samples/`)
  - Single `(IDbConnection connection)` constructor delegating to base

**Tests to add:** none yet — the fixture is consumed by Phase 4 tests. Phase 3's only validation is that the generator successfully processes the new context and emits the expected partial (entity accessor implementations present, `With<>` shadow absent, `FromCte` shadow present).

**Commit:** `Add CteChainTestDbContext fixture inheriting QuarryContext<TSelf>`

---

### Phase 4 — Integration tests: `With/Users/Join/Select` chain shapes

**Files:**
- `src/Quarry.Tests/SqlOutput/CteWithEntityAccessorTests.cs` (new)
  - Uses `CteChainTestDbContext` from Phase 3
  - Harness pattern mirrors `CrossDialectCteTests.cs` but SQLite-only (the fixture only covers SQLite); dialect coverage comes later when the other fixtures migrate

**Tests to add:**
1. `Cte_Users_Select` — baseline: `.With<Order>(inner).Users().Select(u => (u.UserId, u.UserName))` emits expected `WITH ... SELECT` SQL and executes.
2. `Cte_Users_Where_Select` — `.With<Order>(inner).Users().Where(u => u.UserName != null).Select(...)`.
3. `Cte_Users_Join_Select` — the core regression target: `.With<Order>(inner).Users().Join<Order>((u, o) => u.UserId == o.UserId.Id).Select((u, o) => (u.UserName, o.Total))`. Verify both the generated SQL and the runtime rowset.
4. `Cte_Users_Join_Where_Select` — chain with Where after Join.
5. `Cte_Users_Join_Select_Prepare` — prepared-terminal variant.
6. `Cte_With_FromCte_StillWorks_OnGenericBase` — regression guard: the existing `.With<A>(inner).FromCte<A>().Select(...)` pattern keeps working when the context uses the generic base.
7. `Cte_ProjectedInnerQuery_Users_Join` — the two-parameter `With<TEntity, TDto>` overload followed by entity accessor + join.

**Pre-existing tests to confirm unchanged:**
- All `CrossDialectCteTests.cs` tests against `TestDbContext` (legacy base) must still pass — they exercise the `With/FromCte` path that the legacy `DiscoverPostCteSites` workaround supports.

**Commit:** `Add integration tests for With/entity-accessor/Join CTE chains`

---

### Phase 5 — Cross-reference docs on the non-generic base

**Files:**
- `src/Quarry/Context/QuarryContext.cs` — update XML doc on `With<TDto>` and `With<TEntity, TDto>` to mention that users who want to combine CTEs with entity accessors should inherit `QuarryContext<MyDb>` instead of the non-generic base. Keep it short — one `<remarks>` block, one `<seealso cref="QuarryContext{TSelf}" />`.

**Tests to add:** none (docs only).

**Commit:** `Cross-reference QuarryContext<TSelf> in non-generic With<> XML docs`

---

### Phase 6 — Architectural findings artifact

**Files:**
- `_sessions/205-cte-join-with-return-type/architectural-findings.md` (new)

**Content sections:**
1. The principle statement and its rationale
2. Why the generator's discovery SemanticModel is blind to its own output
3. Precedent in the codebase (partial entity accessors)
4. How `QuarryContext<TSelf>` applies the principle to `With<TDto>`
5. Measurement of workaround code the principle enables deleting, with concrete line counts and file refs
6. Follow-up work identified (migration + `NavigationList<T>` refactor) with enough detail that a future issue author can create tracking issues without re-doing the analysis

This file is read during REMEDIATE PR creation and its key passages transcribed into the PR body under **Reason for Change** and a dedicated **Architectural findings** section.

**Tests to add:** none.

**Commit:** `Capture architectural findings for PR body transcription`

---

## Dependency graph

```
Phase 1 (runtime type)
   │
   ├── Phase 2 (generator awareness) — independent of Phase 3, depends on Phase 1 only for compilation
   │
   ├── Phase 3 (test context) — depends on Phase 1 (needs the type) AND Phase 2 (needs the generator to stop emitting the shadow that would otherwise warn)
   │      │
   │      └── Phase 4 (integration tests) — depends on Phase 3
   │
   ├── Phase 5 (docs) — independent, can happen anytime after Phase 1
   │
   └── Phase 6 (findings artifact) — independent, best committed near the end so it can reference any late-discovered details
```

Recommended commit order: **1 → 2 → 3 → 4 → 5 → 6**. Each commit leaves the repo in a green state (all tests passing) and is independently reviewable.

## Out of scope (explicitly deferred)

- **Migration of `TestDbContext`, `PostgreSqlDbContext`, `MySqlDbContext`, `SqlServerDbContext`, and the sample apps** to `QuarryContext<TSelf>`. Filed as follow-up during REMEDIATE classification.
- **Deletion of `DiscoverPostCteSites`** and its callers. Depends on the migration above.
- **Navigation property refactor** (`NavigationList<T>` → user-declared partial properties) that would eliminate `DiscoverPostJoinSites`. Separate issue.
- **Diagnostic analyzer** that nudges legacy-base users to migrate when it detects a `With<>` chained with an entity accessor. Would be nice-to-have but is independent work.

## Risks / things to verify during implementation

- **Incremental generator caching.** Adding a field to `ContextInfo` and updating `Equals`/`GetHashCode` must be done together — forgetting one produces stale cached nodes. Verify by running `IncrementalCachingTests.cs` (in `src/Quarry.Tests/`) after Phase 2.
- **CS0109 absence verification.** The Phase 2 generator test must assert CS0109 is NOT present for the generic-base case, not merely that the shadow text is absent — a missing assertion would let a regression slip through if someone re-adds emission.
- **AOT sample** (`src/Samples/Quarry.Sample.Aot`) still uses the legacy base. Verify `dotnet build` on the solution still succeeds; AOT-specific runtime behavior is not touched in this PR.
- **Carrier-typed-to-derived-context invariant.** `TransitionBodyEmitter.EmitCteDefinition` already uses `site.ContextClassName` (the user's concrete type, e.g. `MyDb`) for the interceptor signature and `Unsafe.As<contextClass>(__c)` for the return. That already works for generic-base contexts (the concrete class name is `MyDb`, not `QuarryContext<MyDb>`), so no emitter change is needed. Verify by reading the generated interceptor source in Phase 4 tests via `GetInterceptorSource` helpers if any exist, otherwise by inspecting a debug build of the test assembly.
- **Receiver compatibility in the Roslyn interceptor binding.** The interceptor at the call site binds with `this MyDb @this` receiver. When the user's call site resolves `db.With<A>(inner)` via member lookup on `MyDb` and finds the `new TSelf With<TDto>` on `QuarryContext<MyDb>`, the bound method is `QuarryContext<MyDb>.With<TDto>`. The interceptor extension `this MyDb` must be callable in place of that bound call. Roslyn's interceptor spec allows this because `MyDb` is the original receiver type and `TSelf` in the bound method is also `MyDb` after closing the generic. Confirm by actually running the Phase 4 tests — runtime success is the definitive check.
