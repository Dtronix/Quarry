# Quarry v0.3.0

_Released 2026-04-21_

**Common Table Expressions, window functions, set operations, navigation joins, and a cross-ORM migration toolkit.** This release rounds out Quarry's SQL surface with the major analytical constructs users have been asking for (CTEs, `ROW_NUMBER`/`LAG`/`LEAD`, `UNION`/`INTERSECT`/`EXCEPT`), adds `One<T>`/`HasManyThrough` navigation joins and expands explicit joins to six tables, delivers the new `Quarry.Migration` package for automated conversion from EF Core/Dapper/ADO.NET/SqlKata, ships an opt-in SQL manifest feature for living query documentation, and fixes a silent-WHERE-drop bug for `Nullable<T>.Value` that was serious enough to hold up the release on its own. 99 commits merged since v0.2.1.

---

## Highlights

- **Common Table Expressions** — `.With<TDto>(dto => …)` / `.FromCte<TDto>()` across all four dialects, including multi-CTE chains and lambda-form set-op inputs. Enabled by a new `QuarryContext<TSelf>` base class that types the accessor chain.
- **Window functions** — `Sql.RowNumber/Rank/DenseRank/Ntile/Lag/Lead/FirstValue/LastValue` plus `Sum/Count/Avg/Min/Max(col, over => …)` aggregate-OVER overloads with fluent `PartitionBy`/`OrderBy`. Non-column arguments are parameterized at compile time.
- **Set operations** — `Union/UnionAll/Intersect/IntersectAll/Except/ExceptAll` on query and joined-query builders, with automatic subquery wrapping for post-union `Where`/`GroupBy`/`Having` and cross-entity support.
- **Navigation joins** — `One<T>` + `HasOne<T>()` schema declaration and `HasManyThrough<TTarget, TJunction>` many-to-many skip navigation. Explicit joins extended from 4 to **6 tables**. New `CrossJoin<T>()` and `FullOuterJoin<T>()`.
- **`Many<T>` aggregate subqueries** — `Sum`, `Min`, `Max`, `Avg`/`Average` on `Many<T>` properties, following the existing `Count()` pattern.
- **`RawSqlAsync<T>` now streams** — returns `IAsyncEnumerable<T>` with compile-time column resolution, ordinal-cached readers, case-insensitive column matching, and a new `QRY042` analyzer + code fix that detects Raw SQL convertible to the chain API.
- **SQL manifest emission** — opt-in `<QuarrySqlManifestPath>` property produces per-dialect markdown manifests documenting every generated SQL statement, parameter table, and conditional variant. Zero overhead when disabled.
- **`Quarry.Migration` package** — new NuGet package with converters for EF Core, Dapper, ADO.NET, and SqlKata. `quarry convert --from <tool>` CLI command, Roslyn analyzers (QRM001/011/021/031 families) with IDE code fixes.
- **Join-aware nullable readers** — columns from the nullable side of LEFT/RIGHT/FULL OUTER joins now get `IsDBNull` guards in generated readers, preventing `InvalidCastException` on unmatched rows.
- **Live benchmark dashboard** — run-over-run trends with per-commit HTML reports, light/dark themes, per-chart unit selection, std-dev bands, and a memory axis, published to [Quarry-benchmarks](https://dtronix.github.io/Quarry-benchmarks/).

---

## Breaking Changes

### API Changes

- **`RawSqlAsync<T>` return type changed from `Task<List<T>>` to `IAsyncEnumerable<T>`** (#174). Every call site must migrate:
  ```csharp
  // Before
  List<User> users = await db.RawSqlAsync<User>("SELECT ...", args);
  // After
  List<User> users = await db.RawSqlAsync<User>("SELECT ...", args).ToListAsync();
  // Or, preferred, stream:
  await foreach (var u in db.RawSqlAsync<User>("SELECT ...", args)) { … }
  ```
  The reflection-based fallback is removed. New `QRY031` compile error fires when `T` is an unresolvable open generic parameter.
- **`QueryBuilder<T>` removed** (#139). The generated interceptor pipeline no longer references it. Consumers that did not reference it directly are unaffected.
- **Direct-argument `With<TDto>(IQueryBuilder<TDto>)` CTE overloads removed** (#218). Migrate to lambda form:
  ```csharp
  // Before
  db.With<ActiveUsers>(db.Users().Where(u => u.IsActive)).FromCte<ActiveUsers>()…
  // After
  db.With<ActiveUsers>(users => users.Where(u => u.IsActive)).FromCte<ActiveUsers>()…
  ```
- **`ReadAsync` errors are no longer wrapped in `QuarryQueryException` on the buffered multi-row path** (#173). Raw `DbException`s propagate. `try`/`catch (QuarryQueryException)` handlers around buffered reads must be widened or removed.
- **`QRY073` diagnostic ID removed** (#210). `#pragma warning disable QRY073` directives now emit "unknown diagnostic ID" warnings — delete them.
- **Boolean negation SQL output changed** (#243). `NOT (col)` → `col = 0` / `col = FALSE` per dialect. The change fixes invalid SQL Server syntax (`NOT (bit_col)` isn't legal) and triggers a one-time query-plan cache invalidation on upgrade.
- **MySQL and SQL Server aggregate identifier quoting fixed** (#225). Window-function and aggregate column identifiers now emit backticks/brackets per dialect instead of the double-quoted form, which previously required non-default `ANSI_QUOTES`/`QUOTED_IDENTIFIER` session settings to not error. No action needed unless you relied on the non-default session state.
- **`QuarryContext.With<TDto>()` is now `virtual`** (#214). Source-compatible; binary-breaking (`call` → `callvirt`). Recompile against 0.3.0.

### Opt-In Upgrades

- **`QuarryContext<TSelf>` typed accessor chains** (#214). Required to chain `.With<…>().Users().Join<…>()`. Existing non-generic `QuarryContext` continues to work; opt in only when you need post-`With` accessors:
  ```csharp
  // Before
  public partial class AppDb : QuarryContext { … }
  // After — enables db.With<Dto>(…).Users().Where(…)…
  public partial class AppDb : QuarryContext<AppDb> { … }
  ```

---

## New Features

### Query Engine

#### Common Table Expressions (#208, #214, #218, #212, #219, #220)

```csharp
public record ActiveUser(int UserId, string UserName);

var results = await db
    .With<User, ActiveUser>(users => users
        .Where(u => u.IsActive)
        .Select(u => new ActiveUser(u.UserId, u.UserName)))
    .FromCte<ActiveUser>()
    .Where(a => a.UserName.StartsWith("a"))
    .ExecuteFetchAllAsync();
```

- Single and multi-CTE chains (`.With<A>(…).With<B>(…)`) with per-CTE parameter-space isolation.
- `With<TEntity, TDto>(entities => entities.Select(…))` form projects only the DTO columns in the inner `SELECT`, not all entity columns.
- Lambda form also powers new set-operation inputs — Union/Intersect/Except can be authored as nested lambdas.
- New diagnostics: `QRY080` (CTE inner query not analyzable), `QRY081` (`FromCte` without matching `With`), `QRY082` (duplicate CTE name in chain).

#### Window Functions (#225, #234, #236, #237)

```csharp
var ranked = await db.Sales()
    .Select(s => new {
        s.Region,
        s.Amount,
        Rank = Sql.Rank(over => over.PartitionBy(s.Region).OrderByDescending(s.Amount)),
        RunningTotal = Sql.Sum(s.Amount, over => over.PartitionBy(s.Region).OrderBy(s.SaleDate)),
        Previous = Sql.Lag(s.Amount, 1, 0m, over => over.PartitionBy(s.Region).OrderBy(s.SaleDate)),
    })
    .ExecuteFetchAllAsync();
```

- Ranking: `RowNumber`, `Rank`, `DenseRank`, `Ntile`.
- Offsetting / value: `Lag`, `Lead`, `FirstValue`, `LastValue`.
- Aggregates with `OVER`: `Sum`, `Count`, `Avg`, `Min`, `Max`.
- Non-column arguments (offsets, default values, NTILE buckets) are parameterized at compile time rather than inlined as C# source.
- ROWS/RANGE frame specs are deferred to a later release.

#### Set Operations (#201, #210)

```csharp
var all = await db.Orders().Select(o => new IdAmount(o.Id, o.Amount))
    .Union(db.Quotes().Select(q => new IdAmount(q.Id, q.Estimate)))
    .Where(x => x.Amount > 100)
    .OrderBy(x => x.Amount)
    .ExecuteFetchAllAsync();
```

- `Union`, `UnionAll`, `Intersect`, `IntersectAll`, `Except`, `ExceptAll` on `IQueryBuilder<T>` and `IQueryBuilder<TEntity, TResult>`.
- Automatic subquery wrapping when `Where`/`GroupBy`/`Having` follows a set operation.
- Cross-entity set operations (e.g., `Users.Select(…).Union(Products.Select(…))`).
- New diagnostics: `QRY070` (IntersectAll dialect restriction), `QRY071` (ExceptAll dialect restriction), `QRY072` (projection column-count or type mismatch).

#### Navigation Joins (#158, #197, #198, #164, #165)

Schema declaration:

```csharp
public class OrderSchema : Schema
{
    public Key<int> OrderId => Identity();
    public Ref<UserSchema, int> UserId { get; }
    public One<User> User => HasOne<User>();                    // reverse One<T> navigation
    public Many<OrderLine> Lines => Has<OrderLine>();            // existing
    public Many<Tag> Tags => HasManyThrough<Tag, OrderTag>();    // many-to-many skip
}
```

- `One<T>` + `HasOne<T>()` produces a nullable `T?` navigation property; generated readers are `IsDBNull`-guarded on the nullable join side.
- `HasManyThrough<TTarget, TJunction>` compiles to correlated subqueries with the junction-to-target join implicit in `Count()`/`Any()` terminals.
- Navigation lambdas need null-forgiving for now: `u.Profile!.DisplayName`.
- Explicit joins extended from 4 to 6 tables (`IJoinedQueryBuilder5`, `IJoinedQueryBuilder6`).
- `CrossJoin<T>()` and `FullOuterJoin<T>(condition)` on entity/query/joined-query builders.
- Join-aware nullable projection: columns from nullable sides of LEFT/RIGHT/FULL OUTER joins emit `IsDBNull` guards in generated reader code.
- New diagnostics: `QRY060`–`QRY065` for navigation misconfiguration; `QRA502` warns about FULL OUTER JOIN on SQLite/MySQL.

#### `Many<T>` Aggregates (#195)

```csharp
var totals = await db.Users()
    .Select(u => new {
        u.UserName,
        OrderTotal = u.Orders.Sum(o => o.Amount),
        BiggestOrder = u.Orders.Max(o => o.Amount),
        AverageOrder = u.Orders.Average(o => o.Amount),
    })
    .ExecuteFetchAllAsync();
```

Both `Avg` and `Average` are accepted. Follows the existing `Count()` correlated-subquery pattern.

#### New Query Terminals (#145)

- `ExecuteFetchAllAsync`, `ExecuteFetchFirstAsync`, `ExecuteFetchFirstOrDefaultAsync`, `ExecuteFetchSingleAsync`, `ExecuteScalarAsync`, `ToAsyncEnumerable` added directly on `IQueryBuilder<T>` (no longer need a `.Select(x => x)` stub first).
- **`ExecuteFetchSingleOrDefaultAsync`** added across every builder type.

#### `RawSqlAsync<T>` Streaming + Compile-Time Resolution (#174, #173, #193, #196, #199, #200)

- Returns `IAsyncEnumerable<T>`; unified with the streaming codepath.
- Compile-time column resolution: when the SQL is a string literal that the shared parser can resolve, the generator emits a static lambda with hardcoded ordinals. Falls back to a struct-based ordinal-cached reader for non-literal SQL.
- Case-insensitive column matching (`ToLowerInvariant`).
- No per-row lambda or closure allocation.
- New diagnostics: `QRY031` (unresolvable generic `T` — error), `QRY041` (unresolvable column — warning), `QRY042` (RawSql convertible to chain — info, with code fix).

### SQL Manifest (#155, #161)

Opt-in compile-time emission of markdown files documenting every generated SQL statement. Enable in your `.csproj`:

```xml
<PropertyGroup>
  <QuarrySqlManifestPath>$(MSBuildProjectDirectory)/sql-manifest</QuarrySqlManifestPath>
</PropertyGroup>
```

The generator writes `quarry-manifest.sqlite.md`, `quarry-manifest.postgresql.md`, `quarry-manifest.mysql.md`, `quarry-manifest.sqlserver.md` — one per dialect your project targets. Each manifest lists every chain's SQL, parameter table (including pagination `LIMIT`/`OFFSET` rows), bitmask-labeled conditional variants, and summary stats. A `WriteIfChanged` guard prevents spurious git diffs. Zero overhead when the property is unset. Failures surface as `QRY040` warnings. See [SQL Manifest](../sql-manifest.md).

### `Quarry.Migration` Package (#202, #209, #247, #248, #249, #251, #194)

New NuGet package that automates migration from existing data-access code. Install the package in a project that already references the source library, then run:

```bash
dotnet tool install --global Quarry.Tool
quarry convert --from dapper   --project src/MyApp
quarry convert --from efcore   --project src/MyApp
quarry convert --from adonet   --project src/MyApp
quarry convert --from sqlkata  --project src/MyApp
```

- Discovers call sites via Roslyn, parses embedded SQL with a shared recursive-descent parser in `Quarry.Shared` (`#if QUARRY_GENERATOR`-gated; zero runtime surface), resolves table and column identifiers against your Quarry entities, and emits equivalent Quarry chain API code.
- Supports SELECT/WHERE/INNER/LEFT/RIGHT/CROSS/FULL OUTER joins/GROUP BY/HAVING/ORDER BY/LIMIT/aggregates/IN/BETWEEN/IS NULL/LIKE, plus DELETE/UPDATE/INSERT translation. `Sql.Raw` fallback for constructs the converter cannot translate.
- Per-source Roslyn analyzers with IDE code fixes light up only when the target framework type is present in the compilation (no spurious diagnostics in projects that don't use the source tool).
- Diagnostic families: `QRM001`–`QRM003` (Dapper), `QRM011`–`QRM013` (EF Core), `QRM021`–`QRM023` (ADO.NET), `QRM031`–`QRM033` (SqlKata).
- `IConversionDiagnostic` / `IConversionEntry` interfaces for uniform consumption across all four converters.
- ADO.NET detector uses the **last** `CommandText` assignment before each `Execute*` call and positionally filters parameters, correctly handling reused `DbCommand` variables across multiple executions.
- New sample: `src/Samples/4_DapperMigration` demonstrating an end-to-end Dapper → Quarry conversion.

---

## Performance

### Carrier List Terminal Single State Machine (#255)

The list terminal was wrapping `ToCarrierAsyncEnumerableWithCommandAsync` in an outer `Task<List<T>>` state machine, so every row paid both `MoveNextAsync` / `ValueTask` dispatch and outer state-machine overhead. Collapsed to a single async method with a flat `while (Read) list.Add(materialize)` loop matching Dapper's shape.

Generator-selected `CommandBehavior`: SQLite always uses `SingleResult` only (SequentialAccess buys nothing on SQLite and adds per-column state); other dialects add `SequentialAccess` only when the projection contains `byte[]` or `Stream`. `CommandTimeout` setter guarded against redundant writes; `EnsureConnectionOpenAsync` short-circuits when the connection is already open.

**Result on `WindowRunningSumBenchmarks`**: 351.6 µs → 217.0 µs (**−38%**), now within 5 µs of hand-written Raw and ~55 µs of Dapper, at identical 15.67 KB / 15.95 KB allocations.

### Incremental SQL Mask Rendering (#252)

For chains with N conditional terms (2ᴺ SQL variants, up to 256), the generator now pre-renders the shared prefix/suffix once and assembles each variant via `StringBuilder.Append` rather than rebuilding from scratch. Build-time wins on SELECT and DELETE multi-mask chains.

### RawSqlAsync Reader (#193)

Replaced per-row delegate allocation with a `file struct IRowReader<T>` codegen pattern. `GetName` is called once per result set (not per row); zero lambda or closure allocations on the reader path.

### Carrier Dedup (#253)

Structurally-identical carrier classes are deduplicated at emission, shrinking the generated-code surface for projects with many near-identical query chains. Carrier class numbering gaps may change — the numbering is not a stable contract.

### Build-Time Wins (#137, #145, #254)

- Supplemental compilation replaces ~700 lines of manual type-tracing in the discovery stage. Pipeline-A outputs (entity classes, context accessors) feed into the semantic model used during Pipeline-B discovery, eliminating the error-fallback heuristic chain. Fixes a latent incremental-caching bug in `EntityRegistry.Equals`/`GetHashCode`.
- Generator consolidation (#137): `TypeClassification` becomes the single source of truth for CLR type classification; unified `ExtractParameters` and `ExtractSubqueryPredicateParams` paths; hardened `EscapeString` for control characters.
- `AssembledPlan.Equals` / `QueryParameter.Equals` each gained missing properties, preventing stale cached output (#254). Caching on `GetClauseEntries`, `ResolveSiteParams`, and `BuildParamConditionalMap` cuts redundant generator work in large projects.

---

## Architecture

### Supplemental Compilation (#145)

The generator's type-resolution stage now builds a supplemental compilation that adds Pipeline-A outputs (entity classes, context accessors) to the semantic model used during Pipeline-B discovery. This replaces four earlier workaround PRs (#120, #122, #126, #133/#135) that threaded error-type fallback heuristics through the compiler pipeline. Net effect: types that used to go through manual `TryResolveErrorType` / `TryQualifyErrorTypeFromUsings` now resolve directly with standard Roslyn APIs.

### Shared SQL Parser (#194)

`Quarry.Shared/Sql/Parser/` houses a tokenizer, recursive-descent parser, AST, and walker, all gated behind `#if QUARRY_GENERATOR` so zero runtime surface leaks into consumer assemblies. Foundational for `RawSqlAsync` compile-time column resolution (#199), `QRY042` convertibility detection (#200), and the `Quarry.Migration` converters (#202).

### Dialect-Agnostic `ProjectedColumn` (#229, #230)

`ProjectedColumn.SqlExpression` now holds `{identifier}` placeholders resolved at render time, and the type has been converted from a class to a `record` with `with`-expression updates. The `SqlDialect` parameter was dropped from 15 internal analysis methods; dialect concerns are isolated to the final rendering stage.

### CodeGen Helpers (#211)

CTE/post-join discovery boilerplate was extracted into `TryGetInterceptableLocationData` / `TryGetCallSiteLocationInfo` helpers used across 8 sites. In the process, a previously-unreachable syntactic-fallback path at `TryDiscoverExecutionSiteSyntactically` (guarded by `#if ROSLYN_4_12_OR_GREATER`, never defined) was re-enabled under `#if QUARRY_GENERATOR` — chains that used to silently miss `[InterceptsLocation]` attributes now get them.

### Navigation Pipeline (#158)

`NavigationAccessExpr` is threaded through parse → bind → translate → assemble → emit. Implicit join dedup merges redundant joins introduced via navigation lambdas. T4 templates generate the `IJoinedQueryBuilder5`/`IJoinedQueryBuilder6` interfaces. A `KnownDotNetMembers` exclusion prevents `.ToString()` / `.Equals()` and friends from being parsed as navigation access.

---

## Bug Fixes

### SQL Correctness

- **Silent WHERE clause drop for `Nullable<T>.Value` and `.HasValue`** (#139). Queries like `Where(x => nullable.Value > 0)` were silently dropping the predicate and returning **all** rows. Security-adjacent: could bypass authorization filters. Also fixes `Set()` column expressions (`.Set(u => u.FullName = u.FirstName + " " + u.LastName)`).
- **Collection parameter collision with scalar indices in mixed WHERE** (#141). `array.Contains(col) && x == @p` produced overlapping `@p0` names, crashing SQLite and silently mis-matching on other dialects. Contains expansion now happens at compile time via inline `StringBuilder`.
- **`RawSqlAsync<T>` no-op reader for generated entity types** (#153). Rows were materialized with all default values; types now resolve correctly via Stage 2.5 after supplemental compilation is built.
- **`HasManyThrough.Count()` / `Any()` without predicate skipping the junction→target JOIN** (#165). Was counting junction rows instead of target rows.
- **Boolean negation `NOT (col)` invalid on SQL Server** (#243). Now emits `col = 0` / `col = FALSE` per dialect.
- **Projection parameter collisions in set-op operands** (#232, #236). Window functions and other projection parameters now merge correctly through `AnalyzeOperandChain`.
- **Manifest parameter table missing LIMIT/OFFSET rows** (`33547f6`, `0c1d077`).

### Code Generation

- **Nullable reference type default in object initializers** (#166). Generated code was emitting `default` for `string?` fields, producing CS1031. Now emits `null` for nullable refs and `default(T?)` for nullable value types.
- **Cross-chain nullable collection element type in `Contains` fallback** (#148, #159). `long?[]` against `Col<long?>` produced `IReadOnlyList<long>`, causing CS0030. Multi-closure scenarios now preserve nullability.
- **Captured variable typed as `object` for identity `Select(f => f)`** (#135). Subsumed by the #145 supplemental-compilation work.
- **Captured variable namespace resolution across schema and context namespaces** (#133). Also subsumed by #145.
- **RawSqlAsync `byte[]` column cast** (#160). Bare `r.GetValue(i)` produced CS0266; now emits `r.GetFieldValue<byte[]>(i)`.
- **Sign-mismatched integer reader casts** (#130). `uint`/`ushort`/`ulong`/`sbyte` projections produced CS0266.
- **Generator-internal types made public** (#129, #130). `OpId`, `QueryLog`, `ParameterLog`, `QueryExecutor`, `RawSqlAsyncWithReader`, `RawSqlScalarAsyncWithConverter` promoted to `public` with `[EditorBrowsable(Never)]` so generated interceptors compile in consuming projects. Fixes CS0122 in downstream code.
- **`NavigationAccessExpr` mis-parsed on well-known .NET methods** (#158). `.ToString()` / `.Equals()` etc. excluded via `KnownDotNetMembers`.

### Analyzer False Positives

- **`QRY032` false positives** (#131). No longer fires when a `QuarryContext` is passed to a helper method or when a chain lives entirely inside a loop body. Also fixes CS9144/CS0029 nullable-return-type mismatch on value-type `FirstOrDefault` projections — the interface's `TResult?` for value types is **not** `Nullable<T>`.
- **`QRY032`/`QRY033` false positives and `QRY900` crash in branched/nested control flow** (#134). `if`/`else`, `try`/`catch`, and 3+ levels of `if` nesting no longer trip the chain-analysis fork heuristic. `ChainId` now captures the innermost statement; nesting depth checks use relative depth.
- **`QRY032`/`QRY033` incorrectly attached to CTE inner chains** (#219). Set-op lambda context resolution fixed when an entity is registered in multiple contexts.

### RawSqlAsync / Contains

- **`IEnumerable<T>` support in `Contains` / `IN`** (#132). Previously only `IReadOnlyList<T>` worked; LINQ iterators crashed with `InvalidCastException`. Empty-collection guard emits `IN (SELECT 1 WHERE 1=0)`.
- **Projection-parameter merging extended to `AnalyzeOperandChain`** (#236). Window functions and aggregates in set-op operands no longer collide with outer-query parameter indices.

---

## Documentation & Tooling

- **Release notes** now live under [`docs/articles/releases/`](./) and are linked from the site TOC.
- **SQL Manifest article** — new article [SQL Manifest](../sql-manifest.md) covering the opt-in feature.
- **[Live benchmark dashboard](https://dtronix.github.io/Quarry-benchmarks/)** replaces the static tables in the benchmarks article. Run-over-run trends with per-commit HTML reports, light/dark themes, per-chart unit selection, std-dev bands, and a memory axis.
- **Benchmark CI workflow** with historical tracking, dry-run support, failure gating, and cross-repo push to `Quarry-benchmarks`.
- **New sample**: `src/Samples/4_DapperMigration` — end-to-end Dapper → Quarry conversion using the new `quarry convert --from dapper` flow.
- **Structural carrier-shape tests** (#250) and **ManifestEmitter edge-case coverage** (#161).
- **Code-fix test coverage** for all four migration converters (#251).
- **Updated LLM reference files** — `llm.md`, `llm-migrate.md`, and `src/Quarry.Generator/llm.md` refreshed for the new surface.

---

## Migration Guide from v0.2.1

### Required Changes

1. **`RawSqlAsync<T>` calls** — add `.ToListAsync()` or migrate to `await foreach`:
   ```csharp
   // Before
   List<User> users = await db.RawSqlAsync<User>("SELECT ...", args);
   // After
   List<User> users = await db.RawSqlAsync<User>("SELECT ...", args).ToListAsync();
   ```

2. **CTE call sites** — migrate direct-argument `With` to lambda form:
   ```csharp
   // Before
   db.With<ActiveUsers>(db.Users().Where(u => u.IsActive)).FromCte<ActiveUsers>()…
   // After
   db.With<ActiveUsers>(users => users.Where(u => u.IsActive)).FromCte<ActiveUsers>()…
   ```

3. **`try`/`catch (QuarryQueryException)` around buffered multi-row reads** — `ReadAsync` errors propagate as raw `DbException` now. Widen the catch or restructure.

4. **Delete `#pragma warning disable QRY073` directives** — the diagnostic was removed.

5. **Opt in to `QuarryContext<TSelf>`** if you plan to use post-`With` accessor chains:
   ```csharp
   public partial class AppDb : QuarryContext<AppDb> { … }
   ```

### Optional Improvements

- Replace manual SQL with chain-API equivalents where `QRY042` suggests.
- Adopt `One<T>` / `HasManyThrough` navigations to collapse hand-rolled joins.
- Enable `<QuarrySqlManifestPath>` to generate living SQL documentation.
- Run `quarry convert --from <tool>` on legacy call sites in downstream projects.

---

## Stats

- **99 commits merged** since v0.2.1
- **4 new sub-packages / modules**: `Quarry.Migration`, `Quarry.Shared/Sql/Parser`, benchmark dashboard (`scripts/benchmark-pages`), `4_DapperMigration` sample
- **All 4 dialects supported**: SQLite, PostgreSQL, MySQL, SQL Server
- **New diagnostics added**: QRY031, QRY036, QRY040, QRY041, QRY042, QRY060–065, QRY070–072, QRY080–082, QRY900; QRM001–003, QRM011–013, QRM021–023, QRM031–033 — plus QRA305, QRA502
- **Diagnostic retired**: QRY073

---

## Full Changelog

### Chain API Additions

- Add CTE and derived table support (#208)
- Add QuarryContext\<TSelf\> for typed CTE+entity accessor chains (#214)
- Fix CTE carrier creation conflict for multiple With() calls (#212)
- Refactor CTE discovery boilerplate into shared helpers (#211)
- Reduce lambda CTE inner SELECT to projected DTO columns (#220)
- Add lambda-form With\<T\> CTE overloads and set-op lambda infrastructure (#218)
- Fix set-op lambda context resolution for multi-context entities (#219)
- Add UNION/INTERSECT/EXCEPT set operations to chain API (#201)
- Add cross-entity set operation support and fix per-context entity resolution (#210)
- Support window functions in Select projections (#225)
- Parameterize window function scalar arguments (#234)
- Extend projection parameter merging to AnalyzeOperandChain (#236)
- Add tests for variable window function args in joined queries (#237)
- Add CROSS JOIN and FULL OUTER JOIN support (#197)
- Add navigation joins (One\<T\>), HasManyThrough, and extend explicit joins to 6 tables (#158)
- Remove duplicate BindContext rebuild and ImplicitJoinInfo in HasManyThrough path (#164)
- Fix HasManyThrough Count()/Any() without predicate skipping junction-to-target join (#165)
- Add join-aware nullable projection analysis for outer joins (#198)
- Add Many\<T\> aggregate extensions: Sum, Min, Max, Avg (#195)
- Replace manual type tracing with supplemental compilation, add IQueryBuilder\<T\> terminals and ExecuteFetchSingleOrDefaultAsync (#145)

### RawSqlAsync

- Unify buffered multi-row execution onto IAsyncEnumerable codepath (#173)
- Emit QRY031 error for unresolvable RawSqlAsync\<T\> and migrate to IAsyncEnumerable (#174)
- Optimize RawSqlAsync reader with struct-based ordinal caching (#193)
- Support case-insensitive column name matching in RawSqlAsync readers (#196)
- RawSqlAsync compile-time column resolution (#199)
- Add RawSqlAsync to chain query analyzer diagnostic (QRY042) (#200)
- Fix RawSqlAsync\<T\> no-op reader delegate for generated entities (#153)
- Fix RawSqlAsync\<T\> byte[] reader cast and add GetValue fallback (#160)
- Fix sign-mismatched integer reader casts and make RawSql helpers public (#130)
- Add CommandBehavior flags to ExecuteReaderAsync calls (#172)
- Add CommandBehavior flags to Raw benchmark ExecuteReaderAsync calls (#176)

### Quarry.Migration Package

- Create Quarry.Migration package with Dapper converter (#202)
- Implement shared SQL parser in Quarry.Shared (#194)
- Add DELETE/UPDATE/INSERT translation to Quarry.Migration (#209)
- Add EF Core, ADO.NET, and SqlKata migration converters (#247)
- Fix ADO.NET detector to use last CommandText before Execute (#248)
- Add shared interfaces for migration converter result types (#249)
- Add code fix tests for EF Core, ADO.NET, SqlKata, and Dapper converters (#251)

### SQL Manifest

- Add opt-in SQL manifest emission for query documentation (#155)
- Add deeper test coverage for ManifestEmitter edge-case paths (#161)
- Fix manifest parameter table missing LIMIT/OFFSET parameters (33547f6, 0c1d077)

### Generator / CodeGen

- Generator consolidation: TypeClassification, pipeline errors, emitter dedup, subquery fixes (#137)
- Refactor SqlExpression to dialect-agnostic representation (#229)
- Refactor ProjectedColumn to record with `with` expressions (#230)
- Improve carrier codegen efficiency and fix boolean negation (#243)
- Fix generator bugs, improve build perf, and consolidate code (#254)
- Deduplicate structurally identical carrier classes (#253)
- Incremental SQL mask rendering for compile speed (#252)
- Collapse carrier list path to one state machine and drop SequentialAccess (#255)
- Make interceptor-referenced types public for consuming projects (#129)
- Add structural unit tests for generated carrier code shape (#250)

### Bug Fixes

- Fix silent WHERE clause drop for Nullable\<T\>.Value, Set() column expressions, and QueryBuilder\<T\> removal (#139)
- Fix collection parameter collision with scalar indices in mixed WHERE (#141)
- Fix nullable reference type handling in entity readers (#166)
- Fix nullable collection element type in Contains() for multi-closure methods (#159)
- Fix nullable collection element type in Contains fallback path (#148)
- Add IEnumerable\<T\> collection support in Contains/IN clauses (#132)
- Fix captured variable typed as object for identity Select projections (#135)
- Fix captured variable namespace resolution and chain result type inference (#133)
- Fix QRY032/QRY033 false positives and QRY900 crash for chains in branched and nested control flow (#134)
- Fix QRY032 false positives and FirstOrDefault nullable return type mismatch (#131)
- Fix failure-mode tests for malformed OVER clause lambdas (#228)
- Fix failure-mode tests for joined OVER clause lambdas (#231)
- Fix QRY080 diagnostic tests for lambda form (#221)

### Benchmarks & Tooling

- Add benchmark CI workflow with historical tracking (2bbc79e)
- Fix benchmark failures and split benchmark classes for correct baselines (#239)
- Add advanced benchmarks for window functions, CTEs, subqueries, and set operations (#235)
- Add benchmark constant-inlining variants and remove unused views (#151)
- Add merged HTML reports, custom dashboard, and run history pages (1b09c7a)
- Add light/dark mode support to benchmark pages (77030fa)
- Add std-dev band, per-chart unit selection, and larger cards to dashboard (f7db324)
- Add memory axis to dashboard and extract allocated in workflow (297f23c)
- Move benchmark results to live dashboard, remove static tables (5593c46)
- Add README.md for Quarry-benchmarks repo and deploy it from CI (fd917c6)
- Skip benchmarks on irrelevant changes; link dashboard from docs (e1f13b0)
- Polish benchmark reports and remove dry run option (3b765e5)
- Mark raw-SQL fallback benchmarks and align report columns (444c72b)

### Documentation

- Document ownsConnection support in LLM reference files (11e7bc9)
- Enhance llm.md: architecture, error propagation, caching, and completeness (#138)
- Update benchmark docs for 2026-04-04 run and add EnumerableOverheadBenchmarks (45ec37b)
- Update comparison table with features added since last revision (2994ccd)
- Regenerate SQL manifests after navigation joins merge (88af7bc)
