# Quarry v0.4.0

_Released 2026-04-30_

**Real-execution coverage now spans all four dialects on every cross-dialect test**, surfacing and fixing five generator bugs that previously slipped past mock-only assertions: silent `default(T)` parameter binding for captured variables in `OrderBy`/`GroupBy`/`Join`, invalid SQL Server `INSERT ... OUTPUT VALUES` shape, MySQL `LIKE` parse errors on default `sql_mode`, SQL Server `BIGINT` window-function readers, and FK `.Id` projection in CTE post-Select. Adds `QRY037` (generator self-check), `QRA403`/`QRA404` (analyzer warnings for dangling `ThenBy`/`Having`), and promotes `QRA503` from Warning to Error. 17 commits merged since v0.3.2.

---

## Highlights

- **Silent `default(T)` parameter binding fixed for captured variables in `OrderBy`/`GroupBy`/`Join`** — four clause-emitter paths were skipping carrier extraction; non-zero captured values silently bound `0`/`null!`. New `QRY037` generator self-check fails the build if any carrier `Px` field is declared without a matching assignment. (#293)
- **SQL Server `INSERT … OUTPUT … VALUES` placement fixed** — every entity-insert returning identity was emitting `INSERT … VALUES (…) OUTPUT INSERTED.[Id]` and failing at runtime with `Incorrect syntax near 'OUTPUT'` against real `SqlConnection`. (#276)
- **MySQL `LIKE` portable across all `sql_mode` settings** — generator no longer emits `LIKE '%foo\_bar%' ESCAPE '\'` (1064 syntax error on default `sql_mode`). New `MySqlBackslashEscapes` flag on `[QuarryContextAttribute]` (default `true`) drives backslash doubling. Underpinned by a `SqlDialectConfig` carrier that replaces the flat `SqlDialect` enum threaded through the generator. (#288)
- **SQL Server window-function int projections execute** — `ROW_NUMBER`/`RANK`/`DENSE_RANK`/`NTILE` and int-typed aggregate `OVER` projections wrap with `CAST(… AS INT)` on SQL Server (returns `BIGINT`; `SqlDataReader.GetInt32` does not auto-narrow). (#287)
- **DISTINCT + ORDER BY non-projected column portable across all four dialects** — generator now wraps in a derived table on every dialect; PostgreSQL no longer fails 42P10 at runtime. (#275)
- **Two new analyzer warnings + code fix**: `QRA403 ThenBy without preceding OrderBy` (with `ThenByToOrderByCodeFix`) and `QRA404 Having without preceding GroupBy`. (#289)
- **`QRA503` Error for dialect capability gaps** — MySQL `FullOuterJoin` and SQL Server `.Offset(N)` without `.OrderBy(...)` now fail compilation instead of warning. `QRY070`/`QRY071`/`QRY072` start actually surfacing (silently dropped by the generator pre-v0.4.0). (#279)
- **`IEntityAccessor<T>` chain-continuation methods** — `OrderBy`/`ThenBy`/`Limit`/`Offset`/`Having` and 12 set-op overloads added so `db.With<T>(...).FromCte<T>().OrderBy(...).Select(...)` compiles cleanly. (#284)
- **Per-context `[EntityReader]` resolution** — generator emits the reader FQN at `<contextNamespace>.<readerSimpleName>`. Multi-context, multi-namespace setups now match per-context entity types statically (was papered over with `Unsafe.As<>`). (#278)
- **Real-execution test coverage on all four dialects** — every `CrossDialect*Tests` site now executes against real `NpgsqlConnection` (PG 17), `MySqlConnection` (MySQL 8.4), and `SqlConnection` (SQL Server 2022) Testcontainers — not just SQLite. (#266, #271, #276, #279)
- **Source-generator pipeline benchmarks** — new `Quarry_GeneratorColdCompile`/`Quarry_Throughput_*`/`Quarry_Pipeline_*` benchmark methods publish to the existing dashboard. (#290)

---

## Breaking Changes

### API Changes

- **SQL Server `INSERT` shape changed** (#276). Pre-v0.4.0:
  ```sql
  INSERT INTO [tbl] (cols) VALUES (params) OUTPUT INSERTED.[Id]
  ```
  v0.4.0:
  ```sql
  INSERT INTO [tbl] (cols) OUTPUT INSERTED.[Id] VALUES (params)
  ```
  The old shape produced `Incorrect syntax near 'OUTPUT'` at runtime — no real `Microsoft.Data.SqlClient` consumer could have been using it. SQL-string assertions or schema audits pinned to the old shape need updating. PG/MySQL/SQLite shapes unchanged. Same restructure applies to batch insert.

- **`OrderBy(non-projected).Distinct().Select(proj)` row count changes on SQLite/MySQL** (#275). The flat shape SQLite/MySQL accepted produced one row per `proj` with an arbitrary `non-projected` value picked by the engine. The new wrap applies DISTINCT to `(proj, non-projected)`, so chains of this exact shape now return one row per `(proj, non-projected)` pair on every dialect. PostgreSQL/SQL Server users were already affected (PG returned 42P10; SS rejected under standard rules), so this surfaces as a row-count change only for SQLite/MySQL on the affected query shape.

- **Per-context `[EntityReader]` resolution** (#278). Multi-context consumers with schema and contexts in different namespaces (e.g. `App.Pg.PgDb`, `App.My.MyDb` referencing a schema in `App.Schemas`) must provide a per-context reader at `<contextNamespace>.<readerSimpleName>` inheriting `EntityReader<TPerContextEntity>`. Single-context consumers and consumers with schema/context in the same namespace see no change. The compile error replaces previously latent `Unsafe.As<>` runtime mismatch.

### Diagnostic / Analyzer Changes

- **`QRY037` (Error) — generator carrier-assignment self-check** (#293). New diagnostic that fires if the generator produces a carrier with a `Px` field but no matching `__c.Px = ...` assignment. By design — closes the silent `default(T)` parameter-binding hole. Should never surface in user code; if it does, it's a generator regression and a build break.

- **`QRA403` (Warning) — `ThenBy` without preceding `OrderBy`** (#289). Detects `.ThenBy(...)` whose receiver chain has no `.OrderBy(...)`. New `ThenByToOrderByCodeFix` (Quarry.Analyzers.CodeFixes) renames the method-name token while preserving generic type arguments.

- **`QRA404` (Warning) — `Having` without preceding `GroupBy`** (#289). Detects `.Having(...)` whose receiver chain has no `.GroupBy(...)`. No code fix (the intended grouping key cannot be inferred).

- **`QRA503` (Error) — capability gap for dialect** (#279). Promoted from `QRA502 Warning`. Fires for MySQL `FullOuterJoin<T>(...)` and SQL Server `.Offset(N)` without `.OrderBy(...)` at every execution terminal (`ExecuteFetchAllAsync`, `ToAsyncEnumerable`, etc.). The previous `QRA502 Warning` is preserved for genuine perf hints.

- **`QRY070` / `QRY071` / `QRY072` now actually surface** (#279). Pre-v0.4.0 the generator silently dropped these descriptors via a registration gap in `s_deferredDescriptors`. Code that compiled before with `IntersectAll`/`ExceptAll` on a non-PostgreSQL dialect, or with set-operation projection mismatches, now fails at compile time. The diagnostics already existed; only the silent-drop bug is fixed.

- **Stale SQLite RIGHT/FULL OUTER QRA502 rules removed** (#279). `Microsoft.Data.Sqlite 10.0.3+` ships SQLite 3.49.1, which executes both joins. The rules emitted false positives.

- **`QRY019` message format tightened** (#292). The doubled `"OrderBy clause contains an expression that cannot be translated to SQL clause could not be translated to SQL at compile time."` is now `"<clause-context>. The original runtime method will be used instead."`. Diagnostic ID unchanged; pure message-text change.

### Opt-In Upgrades

- **`MySqlBackslashEscapes`** (default `true`) on `[QuarryContextAttribute]`. Stock MySQL behavior; no action required for default-mode consumers (the underlying parse-error fix lands automatically). Consumers running MySQL with `NO_BACKSLASH_ESCAPES` in `sql_mode` should opt out:
  ```csharp
  [QuarryContext(Dialect = SqlDialect.MySQL, MySqlBackslashEscapes = false)]
  public partial class MyDb : QuarryContext { ... }
  ```

---

## New Features

### Query Engine

#### `IEntityAccessor<T>` chain-continuation methods (#284)

```csharp
var orders = await db
    .With<ActiveOrder>(orders => orders.Where(o => o.IsActive))
    .FromCte<ActiveOrder>()
    .OrderBy(o => o.Total, Direction.Descending)
    .Limit(10)
    .Select(o => o)
    .ExecuteFetchAllAsync();
```

- 13 default-throwing interface methods added: `OrderBy`, `ThenBy`, `Limit`, `Offset`, `Having`, plus 6 direct + 6 lambda set-op overloads.
- Removes the malformed `Order<Order>` interceptor shape (CS0308) on post-`FromCte<T>()` chains.
- The generator overrides each method only at discovered interceptable call sites; unintercepted chains throw `InvalidOperationException` at runtime (same pattern every other unintercepted `IEntityAccessor` method uses).

### Analyzers

#### `QRA403` + `ThenByToOrderByCodeFix` (#289)

```csharp
// QRA403 fires
var rows = await db.Users()
    .ThenBy(u => u.UserName)         // no preceding OrderBy
    .Select(u => u).ExecuteFetchAllAsync();
```

The code fix renames `ThenBy` → `OrderBy`, preserving any `TypeArgumentList` on a generic-name call. `BatchFixer` for FixAll across a document.

#### `QRA404` (#289)

```csharp
// QRA404 fires
var rows = await db.Orders()
    .Having(o => o.Total > 100m)     // no preceding GroupBy
    .Select(o => o).ExecuteFetchAllAsync();
```

No code fix — the intended grouping key cannot be inferred from the chain.

### Tooling

#### Source-generator pipeline benchmarks (#290)

Seven new BDN benchmark methods added to `Quarry.Benchmarks` (all `Quarry_*`-prefixed for the gh-pages publish filter):

- `Quarry_GeneratorColdCompile` — headline cold-compile number, fixture + Medium corpus.
- `Quarry_Throughput_{Small,Medium,Large}` — 10 / 50 / 200 query call sites; surfaces O(n²) regressions.
- `Quarry_Pipeline_{SchemaOnly,PlusQueries,PlusMigrations}` — cumulative shapes; cost differences attribute per-pipeline.

Powered by a new `Quarry.Benchmarks.GeneratorHarness` project (extracted to keep BDN/Dapper/EFCore out of `Quarry.Tests`'s runtime closure). No CI / publish-pipeline changes required — the existing `--filter '*'` discovery + `Quarry_*` filter pick them up automatically.

---

## Architecture

### `SqlDialectConfig` carrier (#288)

Internal `SqlDialect` enum threaded through the generator becomes `internal sealed record SqlDialectConfig(SqlDialect Dialect)` with per-context mode flags. Threaded through `SqlExprRenderer`, `SqlAssembler`, `ContextInfo`, `BoundCallSite`, and `TranslatedCallSite`. Existing `Render(SqlDialect)` overloads preserved for fragment paths that don't have a config. Foundation for additional `sql_mode` axes (`MySqlAnsiQuotes`, `PgStandardConformingStrings`, `SqlServerQuotedIdentifier`) — each follows the same additive shape.

### Carrier-assignment recorder + `QRY037` (#293)

`CarrierAssignmentRecorder` threaded through ~14 emit methods that write to carrier P-fields (`CarrierEmitter`, `ClauseBodyEmitter`, `JoinBodyEmitter`, `SetOperationBodyEmitter`, `TransitionBodyEmitter`). `FileEmitter.ProduceCarrierAssignmentDiagnostics` performs a post-emission gap scan; any `Px` field declared without a matching `__c.Px = …` assignment fires `QRY037` (Error) with carrier name + index. Closes the CS0649 hole — non-nullable reference-type captures (`string`, custom classes) suppressed CS8618 with `= null!` and silently shipped `null` parameter bindings before this PR.

### SQL Server window-function int wrap (#287)

`ProjectedColumn` gains `RequiresSqlServerIntCast`. `ProjectionAnalyzer` sets it for `int`-typed window functions excluding `Lag`/`Lead`/`FirstValue`/`LastValue` (those inherit the source column's type). `SqlAssembler.AppendProjectionColumnSql` and `ReaderCodeGenerator` emit `CAST(… AS INT)` on SQL Server only. PG/My/Lite emit unchanged.

### Per-context `[EntityReader]` resolution (#278)

`InterceptorCodeGenerator.ResolvePerContextReaderFqn` rewrites the schema-namespace FQN to `<contextNamespace>.<simpleName>`. `CollectEntityReaderInstances` and `ReaderCodeGenerator.GenerateReaderDelegate` thread the context namespace. When schema and context share a namespace, the per-context FQN equals the schema-namespace FQN — byte-identical emit preserved.

### `CarrierStructuralKey` dedup invariant (#272)

`CarrierStructuralKey` promoted from `private` to `internal`; doc comment now spells out load-bearing extractor fields (`MethodName` + `VariableName` + `VariableType` + `DisplayClassName` + `CaptureKind` + `IsStaticField`). Locks in the chained-`With<>` dispatch invariant against future contributors collapsing the dedup axes.

---

## Bug Fixes

### SQL Correctness

- **Silent `default(T)` parameter binding for captured vars in `OrderBy`/`GroupBy`/`Join`** (#293). Four emitter paths (`ClauseBodyEmitter.EmitOrderBy`, `ClauseBodyEmitter.EmitGroupBy`, `JoinBodyEmitter.EmitJoinedOrderBy`, `JoinBodyEmitter.EmitJoin`) hardcoded the lambda parameter to `_` (extraction body emitted against an undeclared name) and skipped the per-clause extraction plan in the generic-key carrier path. With non-zero captured values, the SQL referenced `@px` but no body wrote to `Px` — silently bound `0` (or `null!`). `QRY037` permanently locks the regression out.
- **SQL Server `INSERT … OUTPUT INSERTED.[Id]` placement** (#276). Generator now emits `INSERT … (cols) OUTPUT INSERTED.[Id] VALUES (…)` instead of the invalid `… VALUES (…) OUTPUT INSERTED.[Id]`. Same restructure for `RenderBatchInsertSql`. PG/My/Lite unchanged.
- **MySQL `LIKE` 1064 on default `sql_mode`** (#288). `LIKE '%foo\_bar%' ESCAPE '\'` parsed as a syntax error against stock MySQL because backslash inside string literals is the escape character. Generator now emits `LIKE '%foo\\_bar%' ESCAPE '\\'` (parses to literal `\`) on MySQL with `MySqlBackslashEscapes = true` (default).
- **DISTINCT + ORDER BY non-projected column emits subquery wrap** (#275). Pre-v0.4.0 this returned 42P10 on PostgreSQL, was rejected by SQL Server under standard rules, and produced implementation-defined results on SQLite/MySQL. Generator now wraps in a derived table on all four dialects when the ORDER BY references a column outside the projection. Detection compares each ORDER BY expression's rendered SQL against the projection-column reference set; chains where every term matches a projection column keep the flat form.
- **DISTINCT wrap detection cast-mismatch on SQL Server** (#292). `NeedsDistinctOrderByWrap` compared `RenderProjectionColumnRef`'s output (with `CAST(... AS INT)` wrap on SS) against `SqlExprRenderer.Render`'s output (no wrap). Same chain shape would unnecessarily fire the wrap path. New `forComparison` flag on `AppendProjectionColumnSql` removes the cast for comparison-side rendering only; emit-side preserves it.
- **SQL Server window-function int projections throw `InvalidCastException`** (#287). `ROW_NUMBER`/`RANK`/`DENSE_RANK`/`NTILE` (and int-typed aggregate `OVER`) return `BIGINT`; `SqlDataReader.GetInt32` doesn't auto-narrow. Now wrapped server-side with `CAST(… AS INT)`. `Lag`/`Lead`/`FirstValue`/`LastValue` excluded — they inherit the source column type.
- **FK `.Id` projection in CTE post-Select and non-CTE single-entity Select** (#285). `o.UserId.Id` was matched in two discovery paths and broke in slightly different ways in each — placeholder path emitted `ColumnName=""`; semantic-model path mis-classified the column kind during partial-type discovery. New `IsRefKeyAccess` flag on `ProjectedColumn` is the single signal both paths use; enrichment looks up the FK column, copies the registry's key type, and suppresses the `EntityRef<T,K>` wrap. Removes ~358 lines of dead `AnalyzeJoined` code in the same change.
- **Empty-alias on CTE post-Select** (#282). `SqlAssembler.AppendProjectionColumnSql` emitted `""."col"` (invalid SQL) when `TableAlias` was empty. One-line fix aligns with `ReaderCodeGenerator`'s convention.
- **Npgsql 10 `08P01: bind message supplies 0 parameters` on PostgreSQL** (#266). The redux of #258 — empirically traced to Npgsql's named-vs-positional mode switch on whether any `DbParameter.ParameterName` is set. Quarry now leaves `ParameterName` empty on PostgreSQL across all five generator paths (`MigrationRunner.InsertHistoryRowAsync`, entity insert, batch insert, scalar param emission, runtime-collection expansion in `CarrierEmitter`). PR #261's hypothesis ("strict name match") was wrong; v0.3.1 and v0.3.2 reproduce the same failure as v0.3.0 on Npgsql 10 + PG 17. Consumer rebuild required after upgrading — the generated `__pN.ParameterName` assignments change.

### Documentation Hardening

- **`QRY019` errorMessage punctuation contract documented** (commit `774476d`, no PR). Comments at the QRY019 `messageFormat` literal and at each `CallSiteTranslator.errorMessage` callsite note that `{0}` must be a complete clause without trailing punctuation. Prevents the doubled-period regression #292 fixed.

### Test Infrastructure

- **Real-execution coverage on PostgreSQL** (#266, Phase 9). 233 mirror sites added across 22 `CrossDialect*Tests.cs` files; `QueryTestHarness.Pg` moves off `MockDbConnection` onto a real `NpgsqlConnection` against Testcontainers PG 17. Surfaced and fixed the Npgsql binding bug, the QRY070/071/072 silent-drop, and the chained-`With<>` carrier dispatch hazard (#268, locked in via #272).
- **Real-execution coverage on MySQL** (#271). 259 mirror sites against Testcontainers MySQL 8.4 + `MySqlConnection`. Pinned `--collation-server=utf8mb4_bin` and `--sql-mode=…,NO_BACKSLASH_ESCAPES` at the container level to mask MySQL defaults for the suite (the proper LIKE-emit fix landed in #288).
- **Real-execution coverage on SQL Server** (#276). 208 mirror sites against Testcontainers MSSQL 2022 + `SqlConnection`. Pinned `COLLATE SQL_Latin1_General_CP1_CS_AS` at column declaration; substituted `default(DateTime)` with explicit dates where SqlClient's DATETIME range rejects `0001-01-01`. Surfaced and fixed the `INSERT…OUTPUT…VALUES` placement (#276) and `BIGINT` window-function readers (#287).
- **Cross-dialect test coverage** (#279). Eight `Integration/*.cs` SQLite-only test files migrated into `SqlOutput/CrossDialect*.cs` with the verbatim 4-dialect pattern. Surfaced and fixed the `s_deferredDescriptors` silent-drop (QRY070/071/072 → #279) and discovered the latent `[EntityReader]` cross-dialect resolution bug (→ #277/#278).

### Test Lock-in

- **Chained-`With<>` dispatch regression locked out** (#272). PR #266 worked around an emerging `MissingFieldException` at `.Prepare()` time by renaming a captured variable; investigation showed the generator's current `CarrierStructuralKey` already covers the dedup axis. PR #272 reverts the workaround, adds focused unit tests pinning `VariableName` / `DisplayClassName` / `VariableType` axes, and adds a `AssertEveryDispatchArrowResolves` helper that walks every `Chain_N.__ExtractVar_X(__target)` reference in interceptor bodies and asserts X is owned by Chain_N.

---

## Documentation & Tooling

- **`docs/articles/analyzer-rules.md`** updated for QRY037, QRA403, QRA404, QRA503, and the QRY019 message change.
- **`docs/articles/context-definition.md`** documents the new `MySqlBackslashEscapes` property on `[QuarryContextAttribute]`.
- **`llm.md`** updated with the per-context `[EntityReader]` resolution rules (#278) and the `QuarryContext` LIKE-emit guarantee (#288).

---

## Migration Guide from v0.3.2

### Required Changes

1. **MySQL `FullOuterJoin<T>(...)`** — was QRA502 Warning, now QRA503 Error (#279). The rewrite pattern is unchanged; consumers who previously suppressed the warning must now actually rewrite the chain to a `UNION` of `LEFT JOIN` + `RIGHT JOIN`:
   ```csharp
   // Before (suppressed warning, produced engine-rejected SQL at runtime)
   db.Users().FullOuterJoin<Order>((u, o) => u.UserId == o.UserId)…
   // After
   db.Users().LeftJoin<Order>((u, o) => u.UserId == o.UserId).Select(...)
       .Union(db.Users().RightJoin<Order>((u, o) => u.UserId == o.UserId).Select(...))…
   ```

2. **SQL Server `.Offset(N)` without `.OrderBy(...)`** — was QRA502 Warning, now QRA503 Error (#279). Add an `.OrderBy(...)` clause; QRA503 fires at every execution terminal, including `ToAsyncEnumerable` (the previous QRA502 missed this terminal — fixed in the same PR).
   ```csharp
   // Before
   db.Users().Offset(20).Select(u => u).ExecuteFetchAllAsync();
   // After
   db.Users().OrderBy(u => u.UserId).Offset(20).Select(u => u).ExecuteFetchAllAsync();
   ```

3. **Non-PostgreSQL `IntersectAll`/`ExceptAll`** — use `Intersect`/`Except` instead, or move that query to a PostgreSQL context. QRY070/QRY071 now actually surface (silently dropped pre-v0.4.0 and produced runtime SQL the dialect rejected).

4. **Set-operation projection mismatches** — pre-v0.4.0 these compiled and broke at runtime; QRY072 now fails the build.

5. **Multi-context `[EntityReader]` consumers (multi-namespace)** — provide a per-context reader inheriting `EntityReader<TPerContextEntity>` at `<contextNamespace>.<readerSimpleName>`. Per-context partial extensions on the entity may also be needed if the schema-namespace partial defines members that the per-context entity needs to expose. Single-context-same-namespace consumers see no change.

6. **Consumer rebuild required after upgrading** — the source generator re-emits interceptors. NuGet binary-only package bumps without a rebuild will not pick up the Npgsql / MySQL / SQL Server fixes.

### Optional Improvements

- Annotate MySQL contexts running `NO_BACKSLASH_ESCAPES`: `[QuarryContext(Dialect = SqlDialect.MySQL, MySqlBackslashEscapes = false)]`.
- Run tests against real database engines (not just SQLite) — the `QueryTestHarness` now supports it via Testcontainers; see the cross-dialect test fixtures for the pattern.
- Address `QRA403`/`QRA404` warnings — they catch chains that compile but emit semantically dubious SQL.

---

## Stats

- **17 commits / 16 PRs merged** since v0.3.2 (one direct commit on top of master, no PR)
- **3 new diagnostics**: QRY037 (Error), QRA403 (Warning), QRA404 (Warning)
- **1 promoted diagnostic**: QRA503 (Error) — was QRA502 (Warning) for capability gaps
- **3 silent-drop diagnostics now surfacing**: QRY070, QRY071, QRY072
- **5 generator bugs surfaced and fixed by real-execution coverage**: silent `default(T)` binding (#293), SQL Server INSERT shape (#276), MySQL LIKE escape (#288), SQL Server window-function int reader (#287), Npgsql 10 parameter binding (#266)
- **New benchmarks**: 7 generator-pipeline methods (`Quarry_GeneratorColdCompile`, `Quarry_Throughput_{Small,Medium,Large}`, `Quarry_Pipeline_{SchemaOnly,PlusQueries,PlusMigrations}`)
- **New project**: `Quarry.Benchmarks.GeneratorHarness`

---

## Full Changelog

### Query Engine

- Extend IEntityAccessor\<T\> with chain-continuation methods (#284, closes #281)
- Wide-tuple projection coverage (TRest) + fix empty-alias on CTE post-Select (#282)

### Generator / CodeGen

- Fix silent default(T) binding for captured vars in OrderBy/GroupBy/Join + add QRY037 (#293)
- Generator: replace flat SqlDialect with SqlDialectConfig carrier; fix MySQL LIKE-escape on default sql_mode (#288, closes #273)
- Fix: SQL Server window-function int projections throw InvalidCastException (BIGINT vs Int32) (#287, closes #274)
- Fix FK .Id projection in CTE post-Select and non-CTE single-entity Select (#285, closes #280)
- Generator: per-context [EntityReader] resolution by default (#278, closes #277)
- Fix DISTINCT + ORDER BY non-projected column emits subquery wrap (#275, closes #267)
- Fix: DISTINCT wrap detection cast-mismatch on SQL Server (#292, closes #286)
- Document QRY019 errorMessage punctuation contract (`774476d`)
- Lock out #268 chained-With dispatch regression with tests + doc (#272, closes #268)
- Fix Npgsql 10 parameter-binding mismatch on PostgreSQL (redux of #258) (#266, closes #258)

### Analyzers

- Analyzer: warn on ThenBy without OrderBy and Having without GroupBy (#289, closes #283)
- Cross-dialect test coverage + dialect-rule hardening (#279)

### Test Infrastructure

- Mirror PG execution coverage to SQL Server: Testcontainers.MsSql + cross-dialect mirror (#276, closes #270)
- Mirror PG execution coverage to MySQL: Testcontainers.MySql + cross-dialect mirror (#271, closes #269)

### Benchmarks

- Add source-generator pipeline benchmarks (#290)
