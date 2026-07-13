# Plan: 308-runtime-hotpath-fixes

Six runtime hot-path fixes from the 2026-07-07 deep review. Ordering: correctness first (item 1), then allocation/consistency fixes, then the small nits, and finally the ConfigureAwait sweep + CA2007 guard **last** so enabling `CA2007 = error` doesn't break intermediate commits.

Each step is independently committable. All commits stage `_sessions/` (`git add -A`). Baseline: 3628 tests green.

## Progress
- [x] Step 1 — Item 1: IN-cache exact length validation
- [ ] Step 2 — Item 2: NavigationList Unloaded singleton
- [ ] Step 3 — Item 3: gate OpId.Next on logger
- [ ] Step 4 — Item 5: cache RawSql mapper instances
- [ ] Step 5 — Item 6a: skip ToList when already a list
- [ ] Step 6 — Item 6b: widen ParameterNames cache
- [ ] Step 7 — Item 6c: drop dead func.Target read
- [ ] Step 8 — Item 6d: cached empty enumerator
- [ ] Step 9 — Item 6e: materialize before log in First
- [ ] Step 10 — Item 6f: PreparedQuery invariant comment
- [ ] Step 11 — Item 4: ConfigureAwait sweep + CA2007 guard

---

## Step 1 — Item 1: IN-cache exact length validation (correctness)

**Problem.** For multi-collection IN-list chains, the SQL cache is validated by `__cached.Hash == __colHash` where `__colHash` is an XOR of scaled lengths (`CarrierEmitter.cs:1214-1230`, check `:1243`). XOR-of-scaled-lengths is not injective: lengths `(16, 900)` and `(85, 41)` both hash to `-249261860` (independently reproduced). On a false hit the stale `ColParts` arrays (built for the wrong lengths) are reused; the bind loop runs to the *actual* length → `IndexOutOfRangeException` (non-PG) or a provider bind-count error. Carrier dedup shares the cache across call sites, so it is history-dependent and nondeterministic. Single-collection chains are safe (odd-constant multiply is bijective mod 2³²).

**Fix.** Replace the hash-equality cache check with an **exact per-collection length compare** using data the entry already stores (`ColParts[i].Length`). The `__colHash` computation and the `Hash` field stay (still cheap, still stored) — we simply stop *trusting* the hash as the validity gate and instead compare lengths exactly.

In `CarrierEmitter.cs` `EmitCollectionSqlCaching` (the cache-hit condition at `:1243`), change:

```csharp
// before
sb.AppendLine("        if (__cached != null && __cached.Hash == __colHash)");
```

to a condition that also verifies every collection's cached parts length equals the current length:

```csharp
sb.Append("        if (__cached != null && __cached.Hash == __colHash");
for (int i = 0; i < collections.Count; i++)
    sb.Append($" && __cached.ColParts[{i}].Length == __col{collections[i].GlobalIndex}Len");
sb.AppendLine(")");
```

Keeping `Hash == __colHash` as a fast pre-filter (cheap int compare, rejects most misses without touching arrays) and adding the exact length checks as the authoritative gate. Single-collection path (`collections.Count == 1`) already emits one length term; it is already safe but the exact compare is harmless and makes all paths uniform. No change to `CollectionSqlCache`.

**Files:** `src/Quarry.Generator/CodeGen/CarrierEmitter.cs`.

**Tests** (add to `src/Quarry.Tests/SqlOutput/CollectionParameterCollisionTests.cs`):
- `Where_TwoCollections_CollidingLengthPairs_NoStaleCacheReuse` — build the two-collection chain `orders.Where(o => orderIds.Contains(o.OrderId) && userIds.Contains(o.UserId))`, execute the **same** chain on `Lite` first with `orderIds`=16 / `userIds`=900 elements, then with `orderIds`=85 / `userIds`=41 (the colliding pair). Include known-matching ids (order 1 / user 1) in the second call's lists and assert the second execution succeeds and returns the correct row(s). Pre-fix this throws on the second execution; post-fix it passes. (916 params < SQLite's 32766 limit.)
- Assert both executions also produce correctly-shaped SQL via `ToDiagnostics()` for the differing lengths (parameter counts 16+900 vs 85+41).

---

## Step 2 — Item 2: NavigationList shared `Unloaded` singleton (allocation)

**Problem.** Generated entities emit `public NavigationList<X> Orders { get; internal set; } = NavigationList<X>.Unloaded();` (`EntityCodeGenerator.cs:330`); `Unloaded()` is `new()` (`NavigationList.cs:89`), run per row in the entity reader. N rows × M `Many<T>` navigations = N×M throwaway ~32-byte objects. The unloaded instance is deeply immutable (`_items = null` readonly, get-only `IsLoaded`), never replaced or populated (the join API discards the navigation lambda), and nothing depends on instance identity → a shared singleton is safe.

**Fix.** Back `Unloaded()` with a cached static field so the public API and the generator emit (`.Unloaded()`) stay unchanged:

```csharp
private static readonly NavigationList<T> _unloaded = new();

/// <summary>
/// Returns the shared unloaded navigation list. The returned instance is deeply
/// immutable (no backing items, IsLoaded == false) and shared across all entities;
/// it MUST NOT be mutated. A join produces a new loaded instance via Loaded(...).
/// </summary>
public static NavigationList<T> Unloaded() => _unloaded;
```

No generator change required. `Loaded(...)` paths are untouched.

**Files:** `src/Quarry/Navigation/NavigationList.cs`.

**Tests** (add to the existing NavigationList test file, or a new `NavigationListTests.cs` if none):
- `Unloaded_ReturnsSharedSingleton` — `ReferenceEquals(NavigationList<int>.Unloaded(), NavigationList<int>.Unloaded())` is true; `IsLoaded` false; `Count` 0.
- `Unloaded_DistinctPerTypeArgument` — `NavigationList<int>.Unloaded()` and `NavigationList<string>.Unloaded()` are independent (generic static holds per closed type).
- Existing entity-fetch/navigation tests must stay green (behavior unchanged).

---

## Step 3 — Item 3: gate `OpId.Next()` on logger presence (consistency)

**Problem.** `EmitCarrierInsertTerminal` emits `var __opId = OpId.Next();` unconditionally (`CarrierEmitter.cs:974`), unlike the query preamble (`:613`) and batch-insert terminal (`TerminalBodyEmitter.cs:543`) which gate on `__logger != null`. `OpId.Next()` is `Interlocked.Increment` on a shared static (`OpId.cs:17`) → cross-core cache-line contention per insert even with logging disabled. The `QuarryContext` raw-SQL paths (`:251, 326, 382, 417, 452`) have the same unconditional call. Verified: `__opId` is only ever *observed* when a logger is enabled (`QueryExecutor` ignores `0`; `CheckSlowQuery` guards on `IsEnabled`), so gating to `0` when the logger is null is behavior-preserving.

**Fix.**
- `CarrierEmitter.cs:974`: `var __opId = OpId.Next();` → `var __opId = __logger != null ? OpId.Next() : 0;` (`__logger` is already in scope from `:972`).
- `QuarryContext.cs` five sites: `var opId = OpId.Next();` → `var opId = LogsmithOutput.Logger != null ? OpId.Next() : 0;`.

**Files:** `src/Quarry.Generator/CodeGen/CarrierEmitter.cs`, `src/Quarry/Context/QuarryContext.cs`.

**Tests:**
- Existing insert / raw-SQL tests must stay green (correctness unchanged).
- `Insert_WithLoggingDisabled_GatesOpId` — inspect the emitted interceptor `.g.cs` for the insert terminal and assert it contains the gated pattern `__logger != null ? OpId.Next() : 0` (mirror any existing generated-code-inspection test; otherwise read the file from `obj/GeneratedFiles`). Covers acceptance criterion "insert path shows no Interlocked traffic with logging disabled (inspect emitted code)."

---

## Step 4 — Item 5: cache RawSql custom-mapper instances (allocation)

**Problem.** `RawSqlBodyEmitter.GeneratePropertyAssignment` (`:265`) emits `new {CustomTypeMappingClass}().FromDb(...)` inside the per-row Read of all three RawSql reader shapes: the `file struct : IRowReader<T>` (`:24`), the fallback lambda (`:149`), and the static-ordinal lambda (`:195`). One mapper allocation per row per mapped column. The chain path already caches mappers as `private static readonly` fields (`FileEmitter.cs:409`, keyed by `InterceptorCodeGenerator.GetMappingFieldName`).

**Fix.** Reuse the existing `GetMappingFieldName` naming and the "file-scoped emit unit owns its own field" precedent (documented at `InterceptorCodeGenerator.cs:124-128` for the Patch binder):
1. `GeneratePropertyAssignment`: emit `{GetMappingFieldName(m)}.FromDb(...)` instead of `new {m}().FromDb(...)`.
2. `EmitRowReaderStruct`: for each distinct `CustomTypeMappingClass` among the struct's props, declare `static readonly {m} {GetMappingFieldName(m)} = new();` inside the `file struct` (it cannot reach the interceptor class's private fields — same constraint as the Patch binder).
3. `CollectMappingInstances` (`InterceptorCodeGenerator.cs:109`): add a branch collecting `site.RawSqlTypeInfo.Properties[].CustomTypeMappingClass` so the two lambda readers' referenced file-scope fields are emitted. (The lambda readers live in the interceptor class and *can* reach those private fields.)

The same field name in two scopes (struct-local vs file-class-private) does not conflict.

**Files:** `src/Quarry.Generator/CodeGen/RawSqlBodyEmitter.cs`, `src/Quarry.Generator/Generation/InterceptorCodeGenerator.cs`. (Possibly `FileEmitter.cs` if `GetMappingFieldName` needs to be reachable — it is `internal static` already.)

**Tests** (extend `src/Quarry.Tests/SqlOutput/CrossDialectRawSqlTests.cs` or add a focused test):
- A RawSql query projecting into a DTO with a `Mapped<T>` / custom-`TypeMapping` column. Assert the emitted reader `.g.cs` contains no `new {Mapper}(` inside the Read path and instead references the cached field. Covers acceptance criterion "RawSql over an entity with `Mapped<T>` columns allocates no mapper instances per row."
- Behavioral: the RawSql query still round-trips the mapped column correctly (execute on `Lite`).

---

## Step 5 — Item 6a: skip `ToList` when batch entities are already a list

**Problem.** `TerminalBodyEmitter.cs:546` emits `var __entities = System.Linq.Enumerable.ToList(__c.BatchEntities!);` unconditionally; `BatchEntities` is typed `IEnumerable<T>?` (`CarrierAnalyzer.cs:251`), so a caller passing a `List<T>` still pays a full copy.

**Fix.** Emit a fast-path guard:
```csharp
var __entities = __c.BatchEntities as System.Collections.Generic.IReadOnlyList<{entityType}>
    ?? System.Linq.Enumerable.ToList(__c.BatchEntities!);
```
Verify downstream usage of `__entities` (in the batch-insert emit that follows) uses only `.Count` and indexer/`foreach` — all available on `IReadOnlyList<T>`. If any `List<T>`-specific member is used, keep those on a materialized copy instead.

**Files:** `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs`.

**Tests** (extend `CrossDialectBatchInsertTests.cs`):
- Batch insert given a `List<T>` and given a non-list `IEnumerable<T>` (e.g. a `Where`/iterator) both produce correct rows on `Lite`. (Optimization is behavior-preserving; existing tests cover the common path.)

---

## Step 6 — Item 6b: widen `ParameterNames` precomputed cache

**Problem.** `ParameterNames` precomputes `@p0..@p255` and `$1..$256` (`ParameterNames.cs:10-11`); batch inserts bind up to 2100 parameters, so indices ≥256 fall back to per-call string concat.

**Fix.** Introduce a named constant `private const int CacheSize = 2100;` (SQL Server's parameter ceiling) and use it for both arrays. Update the XML doc ranges. Tradeoff: ~4200 preallocated strings at startup (one-time, negligible), eliminating per-call concat for realistic batch sizes.

**Files:** `src/Quarry/Internal/ParameterNames.cs`.

**Tests** (add `ParameterNamesTests.cs` if none):
- `AtP` / `Dollar` return correct values at boundaries: index 0, 255, 256, 2099 (last cached), 2100 (first fallback), and a large index (e.g. 5000, exercising the concat fallback). Verify `$` output is 1-based.

---

## Step 7 — Item 6c: drop dead `func.Target` read when all captures are static

**Problem.** Three sites emit `var __target = {param}.Target!;` before the extractor loop (`CarrierEmitter.cs:286, 504, 529`); each extractor uses `extractor.IsStaticField ? "null!" : "__target"`. When *all* extractors are static, `__target` is assigned and never read (dead; may trip CS0219).

**Fix.** At each site, only emit the `__target` line when at least one extractor needs it:
```csharp
if (extractionPlan.Extractors.Any(e => !e.IsStaticField))
    sb.AppendLine($"        var __target = {delegateParamName}.Target!;");
```
The per-extractor `null!` vs `__target` selection is unchanged, so mixed static/instance plans still emit `__target`.

**Files:** `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` (three sites — factor a tiny helper if it reads cleanly).

**Tests:**
- Existing clause/order-by/CTE tests with captured instance variables stay green (still emit `__target`).
- If an all-static-capture scenario is readily constructible (a clause referencing only `static` fields), add a test asserting the emitted `.g.cs` for it contains no `var __target =` for that carrier. If not cleanly constructible, rely on existing coverage + the build being warning-clean, and note this in the commit.

---

## Step 8 — Item 6d: cached empty enumerator for unloaded `NavigationList`

**Problem.** `NavigationList<T>.GetEnumerator()` returns `(_items ?? Enumerable.Empty<T>()).GetEnumerator()` (`NavigationList.cs:78`); the unloaded path allocates a boxed enumerator on every enumeration. Unloaded is the common case for un-joined navigations.

**Fix.** Return a shared cached empty enumerator when `_items is null`. Introduce a minimal private cached empty `IEnumerator<T>` (a singleton whose `MoveNext()` is always `false`, `Current` throws, `Reset()`/`Dispose()` are no-ops — no meaningful state, so sharing is safe):
```csharp
public IEnumerator<T> GetEnumerator() => _items?.GetEnumerator() ?? EmptyEnumerator.Instance;
```
where `EmptyEnumerator` is a nested sealed type exposing a static readonly `Instance`. (Note: `List<T>.GetEnumerator()` returned via the interface still boxes when loaded — that is unavoidable and out of scope; this targets the unloaded per-navigation path the issue flags.)

**Files:** `src/Quarry/Navigation/NavigationList.cs`.

**Tests:**
- `Unloaded_GetEnumerator_YieldsEmpty` — `foreach` over an unloaded list yields nothing; `Any()` false.
- `Unloaded_GetEnumerator_ReturnsCachedInstance` — two `GetEnumerator()` calls on unloaded lists return the same instance (`ReferenceEquals`), proving no per-call allocation.
- Loaded enumeration still returns correct items.

---

## Step 9 — Item 6e: materialize before logging completion in `First`

**Problem.** `ExecuteCarrierFirstWithCommandAsync` (`QueryExecutor.cs`) calls `FinalizeQuery(...)` (:71) *before* `reader(dbReader)` (:72). If materialization throws, `FetchCompleted` is logged for a row never returned, and timing excludes materialization. `ExecuteCarrierFirstOrDefaultWithCommandAsync` (:101-105) already does it correctly (materialize → log).

**Fix.** Reorder the `First` variant to match `FirstOrDefault`:
```csharp
if (await dbReader.ReadAsync(ct).ConfigureAwait(false))
{
    var result = reader(dbReader);
    FinalizeQuery(opId, ctx, startTimestamp, 1, command.CommandText);
    return result;
}
```

**Files:** `src/Quarry/Internal/QueryExecutor.cs`.

**Tests:**
- Existing First/FirstOrDefault tests stay green.
- If a logger capture harness exists, add `First_MaterializationThrows_NoSpuriousCompletionLog` — a projection that throws during materialization does not emit `FetchCompleted`. If no such harness exists, verify by inspection and rely on the reordering being observably equivalent for the success path (note in commit).

---

## Step 10 — Item 6f: document the `PreparedQuery<T>` invariant (doc only)

**Problem.** `PreparedQuery<T>` (`Query/PreparedQuery.cs:22`) is sealed with all bodies throwing `NotSupportedException`, replaced by generator-emitted subclasses that the runtime reaches via an `Unsafe.As<PreparedQuery<T>>` reinterpret cast (in generated code). The safety invariant — sealed, stateless, stubs never touch `this` — is load-bearing but undocumented.

**Fix.** Add an XML/`<remarks>` comment on the class stating the invariant: the type must stay sealed and stateless, and its stub bodies must never dereference `this`, because generated code reinterpret-casts foreign instances to it via `Unsafe.As`. No code change.

**Files:** `src/Quarry/Query/PreparedQuery.cs`. No test (documentation only).

---

## Step 11 — Item 4: ConfigureAwait sweep across `src/Quarry` + CA2007 guard

**Done last** so `CA2007 = error` does not break intermediate commits.

**Problem.** Runtime awaits missing `ConfigureAwait(false)`: `QuarryContext` (13, incl. `await using var command`), `QueryExecutor` (9 `await using var _cmd = command;` disposals), `MigrationRunner` (40). Deadlock hazard + scheduler pressure under a `SynchronizationContext`. (`Sql.cs`'s 2 matches are inside XML-doc comments — ignore.)

**Fix.**
1. Add `ConfigureAwait(false)` to every real `await` in `src/Quarry` (non-generated):
   - Plain `await expr;` → `await expr.ConfigureAwait(false);`.
   - `await using var _x = disposable;` where `_x` is only a disposal handle → `await using var _x = disposable.ConfigureAwait(false);`.
   - `await using var x = ...;` where `x` is *used* → split: `var x = ...; await using var _ = x.ConfigureAwait(false);` (preserves `x`'s type).
   - `await foreach (var x in src)` → `await foreach (var x in src.ConfigureAwait(false))`.
2. Add a **project-scoped** guard: create `src/Quarry/.editorconfig` with `root = true` and `[*.cs] dotnet_diagnostic.CA2007.severity = error`. Scoped to the runtime project only (tests/generator/benchmarks/samples legitimately don't need it). `.NET analyzers` are enabled by default for net10; if a build check shows CA2007 isn't active, add `<EnableNETAnalyzers>true</EnableNETAnalyzers>` to `src/Quarry/Quarry.csproj`.
3. Build `src/Quarry` — CA2007 = error surfaces any missed await; fix until clean.

**Files:** `src/Quarry/Context/QuarryContext.cs`, `src/Quarry/Internal/QueryExecutor.cs`, `src/Quarry/Migration/MigrationRunner.cs`, any other `src/Quarry` files with bare awaits; new `src/Quarry/.editorconfig`; possibly `src/Quarry/Quarry.csproj`.

**Tests / verification:**
- The build itself (CA2007 = error) is the guard and the acceptance-criterion verification ("ConfigureAwait(false) on all awaits in src/Quarry, verified by grep or analyzer").
- Full test suite green (behavior unchanged; `Migration.Tests` exercises `MigrationRunner`).

---

## Final

- Run the full suite (`Quarry.Tests`, `Quarry.Migration.Tests`, `Quarry.Analyzers.Tests`) — all green.
- Proceed to REVIEW (rebase on `origin/master`, analysis pass, classification).

## Step dependency notes
- Steps 1–10 are independent of each other and of step 11.
- Step 11 must be last (CA2007 = error would otherwise fail earlier commits that add awaits — though only step 4/item-5 touches generator code, not runtime awaits; ordering last is the safe default).
- Steps 2 and 8 both edit `NavigationList.cs` — do 2 before 8 (8 builds on the file); no conflict but sequential.
- Steps 1, 3, 5, 7 all edit `CarrierEmitter.cs`/generator emitters — sequential commits, watch for line drift.
