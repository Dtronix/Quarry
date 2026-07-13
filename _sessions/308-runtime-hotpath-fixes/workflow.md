# Workflow: 308-runtime-hotpath-fixes
## Config
platform: github
base-branch: master
## State
phase: IMPLEMENT
status: active
issue: #308
pr:
## Problem Statement
Combined finding from the 2026-07-07 multi-agent deep review (runtime-perf + generated-code perspectives). Six items on the emitted execution hot path (`CarrierEmitter` / `EntityCodeGenerator` / runtime internals):

1. (high, correctness) Collection IN-list SQL cache validated by a collidable XOR hash — wrong SQL reuse reachable. Fix: compare `ColParts[i].Length` per collection instead of hash.
2. (high, allocation) Per-row `NavigationList<T>` allocation for every `Many<T>` navigation. Fix: shared immutable `Unloaded` singleton.
3. (medium) Single-row Insert terminal generates `OpId.Next()` unconditionally. Fix: gate on `__logger != null`.
4. (medium) `QuarryContext` raw-SQL paths omit `ConfigureAwait(false)`. Fix: add throughout.
5. (medium) RawSql readers instantiate custom `TypeMapping` objects per row per column. Fix: cached static mapper fields.
6. (low, bundled nits) ToList copy, ParameterNames 256 cap, dead func.Target read, boxed enumerator, First log-before-materialize ordering, PreparedQuery invariant comment.

**Baseline (2026-07-13):** All tests green — Quarry.Tests 3281, Quarry.Migration.Tests 201, Quarry.Analyzers.Tests 146 (3628 total). No pre-existing failures.

## Decisions
- 2026-07-13: **Item 4 scope = full `src/Quarry` sweep** — add `ConfigureAwait(false)` to every real await (QuarryContext, QueryExecutor await-using disposals, MigrationRunner). Satisfies the grep acceptance criterion literally.
- 2026-07-13: **Await regression guard = enable CA2007** as a build warning/error scoped to `Quarry.csproj` (runtime project only). Idiomatic Roslyn rule, no custom code.
- 2026-07-13: **Item 6 = all six nits** included in this PR (incl. subtle c = dead `func.Target` read, e = First/FirstOrDefault log-before-materialization ordering).
- 2026-07-13: **Item 1 fix = per-collection `ColParts[i].Length` compare** (not storing a lengths array) — same cost, no `CollectionSqlCache` struct change.
- 2026-07-13: **Item 2 fix = cached singleton behind `Unloaded()`** — keeps public API and generator emit (`.Unloaded()`) unchanged.

## Working Notes
- **Item 1 (IN-cache hash):** Verified `CarrierEmitter.cs:1214-1230` builds XOR-of-scaled-lengths `__colHash`, checked at `:1243`. `CollectionSqlCache` stores `Hash/Sql/ColShift/ColParts` — lengths recoverable as `ColParts[i].Length`. Fix = replace hash equality with per-collection `__cached.ColParts[i].Length == __col{gi}Len` compare (issue-recommended; same cost, no struct change). Single-collection path is bijective → leave as-is (or fold into same length compare).
- **Item 2 (NavigationList alloc):** `NavigationList<T>.Unloaded()` = `new()` (`NavigationList.cs:89`), emitted per-row in entity initializer (`EntityCodeGenerator.cs:330`). Unloaded state deeply immutable (`_items=null` readonly, get-only `IsLoaded`) → shared singleton is thread-safe. Plan: back `Unloaded()` with a cached static field so API + generator emit stay unchanged.
- **Item 3 (OpId gating):** Generator insert terminal `CarrierEmitter.cs:974` emits unconditional `OpId.Next()`; query preamble (`:613`) and batch-insert (`TerminalBodyEmitter.cs:543`) already gate on `__logger != null`. `__opId` only consumed by logging + `QueryExecutor` (which ignores 0 when not logging). QuarryContext raw-SQL sites (`:251,326,382,417,452`) also unconditional; verified `opId` only observed when `Logger != null` (CheckSlowQuery `:517` guards on `IsEnabled`) → gating to 0 is safe there too.
- **Item 4 (ConfigureAwait):** Bare awaits (excl. generated): QuarryContext 13, QueryExecutor 9 (all `await using var _cmd = command;` disposals), MigrationRunner 40, Sql.cs 2 (both inside XML-doc comments — false positives, ignore). QueryExecutor's actual operation awaits already have ConfigureAwait; only the `await using` disposals lack it.
- **Item 5 (RawSql mapper caching):** `GeneratePropertyAssignment` (`RawSqlBodyEmitter.cs:265`) emits `new {Mapper}().FromDb(...)`, shared across 3 reader shapes: (a) `file struct : IRowReader<T>` (`:24`, own type scope — can hold `static readonly` mapper fields), (b) fallback lambda (`:149`), (c) static-ordinal lambda (`:195`). Lambdas need a static field at enclosing partial-class scope (cf. FileEmitter Cached Fields region `:409`). Most intricate item — three shapes, one shared assignment helper.
- **Test harness:** `QueryTestHarness.CreateAsync()` → `(Lite, Pg, My, Ss)`. `Lite` = real in-memory SQLite (execution). Bundled SQLite param limit 32766 (>>916). Seeded: users(1..3), orders(1..3, UserId 1/1/2). `ExecuteFetchAllAsync()` executes; `ToDiagnostics()` returns SQL+params without executing. Existing `SqlOutput/CollectionParameterCollisionTests.cs` is the home for the item-1 regression test.
  - **Item 1 test design:** Two-collection chain on `orders`: `orderIds.Contains(o.OrderId) && userIds.Contains(o.UserId)`. Execute SAME chain (same carrier/cache) first with lengths (orderIds=16, userIds=900), then (orderIds=85, userIds=41) — `__colHash` collides (`-249261860`). Pre-fix: 2nd exec reuses stale ColParts → IndexOutOfRange/SQLite error. Post-fix: 2nd exec succeeds. Seed known ids (e.g. order 1 / user 1) into the 2nd call's lists to assert correct row returned. 916 params < SQLite limit.
  - **Item 5 resolution:** Existing pattern `GetMappingFieldName(mapper)` → stable field name; `CollectMappingInstances` (`InterceptorCodeGenerator.cs:109`) emits file-scope `private static readonly` fields but does NOT currently walk `RawSqlTypeInfo.Properties`. Precedent (comment `:124-128`): file-scoped emit units (Patch binder on carrier class) that can't reach the interceptor class's private fields emit their OWN per-unit mapper field. Plan: (1) `GeneratePropertyAssignment` emits `{GetMappingFieldName(m)}.FromDb(...)` instead of `new {m}()`; (2) `EmitRowReaderStruct` declares `static readonly {m} {GetMappingFieldName(m)} = new();` inside the `file struct` (mirrors carrier-Patch precedent); (3) add a RawSql branch to `CollectMappingInstances` so the two lambda readers' referenced file-scope fields exist. Same field name in two scopes is fine (no conflict).
- **Item 6 recon (exact sites):**
  - **6a ToList:** `TerminalBodyEmitter.cs:546` `ToList(__c.BatchEntities!)`. `BatchEntities` typed `IEnumerable<T>?` (`CarrierAnalyzer.cs:251`) → always copies. Fix: emit `__c.BatchEntities as IReadOnlyList<{T}> ?? ToList(...)` (verify downstream uses only `.Count`/indexer).
  - **6b ParameterNames:** `src/Quarry/Internal/ParameterNames.cs` — cap 256 (`BuildArray("@p",0,256)` / `("$",1,256)`), fallback per-call concat. Fix: bump cap to 2100 (SQL Server param ceiling) via named const. Tradeoff: ~4200 preallocated strings at startup (negligible, one-time).
  - **6c func.Target:** 3 sites emit `var __target = {param}.Target!;` then per-extractor `extractor.IsStaticField ? "null!" : "__target"` — `CarrierEmitter.cs:286, 504, 529`. Dead when ALL extractors static. Fix: only emit `__target` line if `Extractors.Any(e => !e.IsStaticField)`. (Note dead read may currently trip CS0219.)
  - **6d enumerator:** `NavigationList.cs:76-79` `(_items ?? Enumerable.Empty<T>()).GetEnumerator()` boxes. No cached-empty pattern exists in repo. Fix: introduce a cached empty `IEnumerator<T>` singleton for the unloaded path.
  - **6e First ordering:** `QueryExecutor.cs` `ExecuteCarrierFirstWithCommandAsync` logs `FinalizeQuery` (:71) BEFORE `reader(dbReader)` (:72); FirstOrDefault (:101-105) already correct (materialize then log). Fix: swap to materialize-then-log in First variant.
  - **6f PreparedQuery:** `Query/PreparedQuery.cs:22` sealed `PreparedQuery<TResult>`, all bodies throw `NotSupportedException` (generator-replaced). No non-generic type; no `Unsafe.As<PreparedQuery<T>>` in `src/Quarry` (cast lives in generated code). Fix: doc-comment the invariant (sealed/stateless/stubs never touch `this`). Doc-only, no test.
- **Analyzer config:** No `.editorconfig` anywhere. Root `Directory.Build.props` has global props only (no analyzer settings). CA2007 not enabled/suppressed. Decision: scope CA2007 to runtime project ONLY via a new `src/Quarry/.editorconfig` (`dotnet_diagnostic.CA2007.severity = error`) — NOT root (tests/generator/samples legitimately don't need it). No global `TreatWarningsAsErrors`, so severity must be `error` to act as a real guard. **CA2007 also fires on `await using`/`await foreach`** → `await using var x = cmd;` disposals need `.ConfigureAwait(false)` (for unused disposal handles: `await using var _x = cmd.ConfigureAwait(false);`; where the var is used, split into `var cmd = ...; await using var _ = cmd.ConfigureAwait(false);`).
- **ConfigureAwait sweep + CA2007 ordered LAST** so CA2007=error doesn't break intermediate commits; all other code changes land first.

## Implementation Notes
- **Step 1 done (2026-07-13):** Fix at `CarrierEmitter.cs` cache-hit condition — kept `Hash == __colHash` as cheap pre-filter, added `&& __cached.ColParts[i].Length == __col{gi}Len` per collection. Verified `ColParts[i]` always `new string[len]` (never null). Test `Where_TwoCollections_CollidingLengthPairs_NoStaleCacheReuse` verified with teeth: without fix → `IndexOutOfRangeException`; with fix → passes. Full Quarry.Tests green (3282).
  - **GOTCHA (cost me a cycle):** To reproduce the collision the SAME call site must run twice (shared per-carrier `_sqlCache`) — two textually-distinct `.Where()` sites compile to SEPARATE carriers, so the first test version passed even without the fix. Also, putting the query in a **local function** breaks the generator's `[UnsafeAccessor]` capture extraction (`MissingFieldException: <>c__DisplayClass._itemIds`) because local-function params lay out differently than captured method locals. Correct pattern: one `.Where()` statement inside a `for` loop, reassigning method-scope captured locals between iterations.

## Suspend State
## Session Log
| Date | Phases | Summary |
|------|--------|---------|
| 2026-07-13 | INTAKE→DESIGN | Loaded issue #308, created worktree, green baseline (3628 tests), starting design exploration. |
| 2026-07-13 | DESIGN→PLAN | Verified all 6 items against source; reproduced item-1 collision; 3 scope decisions confirmed (full ConfigureAwait sweep, CA2007 guard, all item-6 nits). Wrote 11-step plan.md. |
