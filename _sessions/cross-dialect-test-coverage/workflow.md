# Workflow: cross-dialect-test-coverage

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: discussion
pr:
session: 3
phases-total: 12
phases-complete: 10

## Problem Statement

The Quarry test suite has rich SQLite-only execution coverage in `src/Quarry.Tests/Integration/*.cs`
(joins, GroupBy, set ops, window funcs, CTEs, navigation subqueries, pagination, DateTimeOffset,
RawSql, Logging, EntityReader, Prepare). The PostgreSQL / MySQL / SQL Server containers introduced
by PRs #266, #270, #271, #275, #276 are exercised by only a narrow regression-guard slice
(single-entity INSERT, batch INSERT, IN(collection)) — leaving every other feature untested at
runtime on the three non-SQLite dialects.

The existing `src/Quarry.Tests/SqlOutput/CrossDialect*.cs` pattern already runs `Prepare()` +
`AssertDialects(...)` + `ExecuteFetchAllAsync()` on all four dialects in one method. Adopting the
same pattern across the gap closes the coverage difference.

A prerequisite for "compiles ⇒ executes on every dialect" (which removes the need for any
`if (supported)` skip logic in test bodies) is tightening dialect-specific analyzer rules:
`SuboptimalForDialectRule` (QRA502) currently emits Warnings for cases that can never execute
(MySQL FULL OUTER JOIN, SQL Server OFFSET/FETCH without ORDER BY) and emits warnings for cases
that DO execute on modern engines (SQLite ≥ 3.39 supports RIGHT/FULL OUTER JOIN — needs
verification against the version `Microsoft.Data.Sqlite` ships).

A second gap noted at `GeneratorTests.cs:1366-1370`: QRY070 / QRY071 (INTERSECT ALL / EXCEPT ALL
not supported) have no end-to-end pipeline tests — only descriptor-existence tests.

### Baseline
- Build: succeeds.
- Tests: 3340/3340 passing (Quarry.Analyzers.Tests 117, Quarry.Migration.Tests 201, Quarry.Tests 3022).
- No pre-existing failures.

### Scope
1. Promote QRA502 cases that produce unexecutable SQL to Error (MySQL FULL OUTER JOIN,
   SQL Server OFFSET-no-ORDERBY). Verify SQLite ≥ 3.39 capability and remove or downgrade the
   stale SQLite RIGHT/FULL OUTER rules accordingly.
2. Add full-pipeline analyzer integration tests for QRA502 using
   `AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(source)`.
3. Add full-pipeline generator integration tests for QRY070 / QRY071 using
   `RunGeneratorWithDiagnostics(compilation)` — closes the explicit gap noted at
   `GeneratorTests.cs:1366-1370`.
4. Convert SQLite-only `Integration/*.cs` files into 4-dialect `SqlOutput/CrossDialect*.cs` tests
   using the existing verbatim pattern from `CrossDialectSelectTests.cs`. New file mapping:
   - `JoinedCarrierIntegrationTests.cs` → extend `CrossDialectJoinTests.cs`
   - `JoinNullableIntegrationTests.cs` → extend `JoinNullableProjectionTests.cs` or `CrossDialectJoinTests.cs`
   - `ContainsIntegrationTests.cs` → extend `CrossDialectWhereTests.cs`
   - `CollectionScalarIntegrationTests.cs` → extend `CrossDialectWhereTests.cs`
   - `DateTimeOffsetIntegrationTests.cs` → extend `CrossDialectTypeMappingTests.cs`
   - `PrepareIntegrationTests.cs` → extend `PrepareTests.cs`
   - `EntityReaderIntegrationTests.cs` → new `CrossDialectEntityReaderTests.cs`
   - `RawSqlIntegrationTests.cs` → new `CrossDialectRawSqlTests.cs`
   - `LoggingIntegrationTests.cs` → new `CrossDialectLoggingTests.cs`
5. Delete the now-redundant `Integration/*.cs` files.
6. Keep narrow provider-regression guards: `PostgresIntegrationTests.cs`,
   `MySqlIntegrationTests.cs`, `SqlServerIntegrationTests.cs`, `NpgsqlParameterBindingTests.cs`,
   `MsSqlContainerSmokeTests.cs`, and the `*TestContainer.cs` plumbing.

### Invariants
- Pattern: explicit verbatim pattern (no helper) — matches the existing 1000+ tests.
- Divergence: no `if (supported)` skips. Dialect features that can't execute are analyzer errors
  at compile time; valid chains execute on every configured dialect.
- Layout: cross-dialect tests live in `SqlOutput/CrossDialect*.cs`, not `Integration/*`.

## Decisions

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-04-25 | Source: current discussion | User selected (1) Current discussion. |
| 2026-04-25 | Test home: SqlOutput/CrossDialect*.cs | One uniform home for SQL-shape verification + 4-dialect execution; delete the SQLite-only Integration/* files once mirrored. |
| 2026-04-25 | Pattern: explicit verbatim | Consistent with existing CrossDialect* tests; no new abstraction to learn. |
| 2026-04-25 | Divergence handling: none — promote to analyzer error | If Quarry can generate invalid SQL for a configured dialect that's a compile-time bug, not a runtime concern. |
| 2026-04-25 | PR scope: single large PR | One sweep, single review. |
| 2026-04-25 | Add full-pipeline analyzer integration tests for QRA502 | Existing DialectRuleTests.cs only unit-tests the rule with synthetic contexts. End-to-end through real Roslyn analysis is missing. |
| 2026-04-25 | Add full-pipeline generator integration tests for QRY070/QRY071 | GeneratorTests.cs:1366-1370 explicitly notes this gap; descriptor-existence tests are insufficient. |
| 2026-04-25 | Remove stale SQLite RIGHT/FULL OUTER QRA502 rules | Empirically verified Microsoft.Data.Sqlite 10.0.3 ships SQLite 3.49.1; both joins execute. The rules date from when the bundled SQLite was older. |
| 2026-04-25 | QRA502: keep MySQL RIGHT JOIN as Warning | MySQL fully supports RIGHT JOIN — the warning is a perf hint about the query planner, not a capability issue. SQL still executes. |
| 2026-04-25 | QRA502: promote MySQL FULL OUTER JOIN to Error | MySQL has never supported FULL OUTER JOIN; SQL physically cannot execute. |
| 2026-04-25 | QRA502: promote SqlServer OFFSET-no-ORDERBY to Error | SQL Server rejects OFFSET/FETCH without ORDER BY at parse time; SQL physically cannot execute. |
| 2026-04-26 | Phase 10 expanded to include generator-level fix for [EntityReader] cross-dialect resolution | Investigation during Phase 10 revealed a pre-existing generator bug: the [EntityReader] attribute resolves the reader class only in the schema's namespace, so PgDb / MyDb / SsDb interceptors emit `<Quarry.Tests.Samples.Product>` (the SQLite-context type) for the Reader's T even though the call site's `Pg.Products()` returns `IEntityAccessor<Pg.Product>` (per-context type). The interceptor "compiles" via `Unsafe.As<>` casts but the static type at the call site is still `Pg.Product` (without the global Product's partial extensions like DisplayLabel). The fix needs both Layer 1 (per-context partials + per-context reader classes) and Layer 2 (generator emits per-context lookup of the reader by name). |
| 2026-04-26 | **Phase 10 deferred — no Layer 1 or Layer 2 in this PR.** | The Layer 2 generator fix touches 5+ files (SchemaParser, IR EntityRef/QueryPlan, EntityInfo, ProjectionInfo, InterceptorCodeGenerator), adds new IR fields propagating across stages, and changes `[EntityReader]` resolution for every consumer — orthogonal to "convert SQLite-only tests to cross-dialect." Layer 1 alone is unusable without Layer 2. Outcome: keep `Integration/EntityReaderIntegrationTests.cs` as SQLite-only; advance to Phase 11. Filed follow-up issue **#277** with full repro, location pointers, and the two-layer fix plan. |

## Suspend State

- **Current phase / sub-step**: IMPLEMENT, Phase 10 of 12, scope expanded mid-phase to include a generator-level fix.
- **In progress**: nothing committed yet. Working tree clean at `d05be19` (Phase 9 commit). Initial CrossDialectEntityReaderTests.cs draft was deleted because it didn't compile — the failure surfaced the architectural issue captured below.
- **Immediate next step**: Implement Phase 10 with both layers (user picked "Layer 1 + Layer 2" via AskUserQuestion). See the detailed plan below.
- **WIP commit hash**: n/a (working tree clean).
- **Test status**: 3023 / 3023 in Quarry.Tests; 3351 / 3351 total. Same as Phase 9 commit baseline.
- **Why suspended**: substantial conversation length (Phases 5–9 + Phase 10 deep generator investigation). Suspending preserves cache and lets a fresh session focus on the generator changes.

### Phase 10 — full plan for next session

**The bug** (verified by reading `src/Quarry.Tests/obj/GeneratedFiles/Quarry.Generator/Quarry.Generators.QuarryGenerator/PgDb.Interceptors.*CrossDialectEntityReaderTests.g.cs`):

Per-context entity types are *separate generated classes* in their own namespaces (`Quarry.Tests.Samples.Pg.Product`, `My.Product`, `Ss.Product`) — confirmed via `obj/GeneratedFiles/Quarry.Tests.Samples.Pg.Product.g.cs` etc. They share the same column properties (ProductId, ProductName, Price, Description, DiscountedPrice) but live in different namespaces.

The `[EntityReader(typeof(ProductReader))]` attribute on `ProductSchema` (in the global `Quarry.Tests.Samples` namespace) resolves `ProductReader` to `Quarry.Tests.Samples.ProductReader`, which inherits `EntityReader<Quarry.Tests.Samples.Product>` (the global Product, the one with the `DisplayLabel` partial). When `InterceptorCodeGenerator` emits the interceptor for `PgDb.Products()...Select(p => p).ExecuteFetchAllAsync()`, it produces:

```csharp
public static IQueryBuilder<Pg.Product, Quarry.Tests.Samples.Product> Select_28a51103(...)
public static Task<List<Quarry.Tests.Samples.Product>> ExecuteFetchAllAsync_809b9cdd(...)
return QueryExecutor.ExecuteCarrierWithCommandAsync<Quarry.Tests.Samples.Product>(__opId, __ctx, __cmd, static (DbDataReader r) => _entityReader_Quarry_Tests_Samples_ProductReader.Read(r), ...);
```

The chain is `IQueryBuilder<Pg.Product, GlobalProduct>` — input `Pg.Product`, output `GlobalProduct`. This compiles thanks to `Unsafe.As<>` casts, but the **call site's static type for the variable** is still `Pg.Product` (no `DisplayLabel`), causing test compilation to fail when the test asserts `pgProduct.DisplayLabel`.

**Layer 1 fix (test-side, mechanical):**

Add per-context partial extensions and reader classes:

1. New file or appended partial declarations:
   - `namespace Quarry.Tests.Samples.Pg; public partial class Product { public string DisplayLabel { get; set; } = ""; }`
   - Same for `Quarry.Tests.Samples.My` and `Quarry.Tests.Samples.Ss`.
2. New per-context reader classes (3 new types — Pg/My/Ss), each `EntityReader<Product>` (resolving to per-context Product) with the same `Read` body as the global `ProductReader`.

**Layer 2 fix (generator-side, structural):**

Goal: when `InterceptorCodeGenerator` emits a reader instance for an entity used in a particular DbContext, look up the reader class by **name** in the context's namespace first; fall back to the schema's namespace.

1. **`src/Quarry.Generator/Parsing/SchemaParser.cs`** (`ResolveEntityReaderAttribute` ~ line 294):
   - Currently extracts `readerFqn` (full name). Add: `readerSimpleName` (e.g., `"ProductReader"`).
   - Update `EntityReaderResolution` to carry both `ReaderClassFqn` (current behavior, used as fallback) and `ReaderClassSimpleName` (for per-context lookup).
2. **IR changes** (`src/Quarry.Generator/IR/EntityRef.cs`, `src/Quarry.Generator/IR/QueryPlan.cs`, `src/Quarry.Generator/Models/EntityInfo.cs`, `src/Quarry.Generator/Models/ProjectionInfo.cs`):
   - Add `CustomEntityReaderSimpleName` alongside the existing `CustomEntityReaderClass` (FQN). All four files store the resolved reader info — keep the FQN for back-compat and add the simple name for per-context resolution.
3. **`src/Quarry.Generator/Generation/InterceptorCodeGenerator.cs`** (around lines 175–193):
   - Currently uses `site.ProjectionInfo.CustomEntityReaderClass` (FQN) directly.
   - Change to: compute a per-context FQN by checking if `<context-namespace>.<reader-simple-name>` exists in the compilation. If so, use that. Else, fall back to the original FQN.
   - The context namespace can be derived from the DbContext type the interceptor is being emitted for. The compilation reference is needed — verify whether `InterceptorCodeGenerator` already has access to `Compilation` or `INamedTypeSymbol` lookup. If not, thread that through.
4. **Interceptor's projection type** (the `IQueryBuilder<TIn, TOut>` chain):
   - Currently the `TOut` of the Select interceptor and the `T` of `ExecuteCarrierWithCommandAsync<T>` are bound to the global Product. Once the per-context reader is in scope, change them to the per-context Product (e.g., `Pg.Product`).
   - This likely lives in the same `InterceptorCodeGenerator` projection / chain emit path. Trace from the bad output (`Select_28a51103` returning `IQueryBuilder<Pg.Product, Quarry.Tests.Samples.Product>`) back to where the projection's output type is chosen.
5. **Tests for Layer 2** (in `src/Quarry.Tests/GeneratorTests.cs` or a new file):
   - Verify per-context resolution: a schema with `[EntityReader(typeof(ProductReader))]` and per-context `Pg.ProductReader` etc. — each context's interceptor uses its own reader.
   - Verify fallback: if no per-context reader exists, the global one is used (current behavior).
   - Verify error case: if per-context reader exists but doesn't inherit `EntityReader<PerContextEntity>`, surface an analyzer error.
6. **Manifest output**: `quarry-manifest.{dialect}.md` files may change because the per-context reader resolution affects the manifest. Re-run tests; if manifests change, check they're correct.

**After both layers land:**

7. Write `src/Quarry.Tests/SqlOutput/CrossDialectEntityReaderTests.cs` (the file I deleted) — the previous draft is in the git history at the failed test attempt; the structure was 8 cross-dialect tests in 4 regions (Identity / Tuple / FetchFirst / RoundTrip). The earlier draft is not committed — re-derive it from `Integration/EntityReaderIntegrationTests.cs` (still present in worktree) using the verbatim Phase 4–9 conversion pattern.
8. Delete `src/Quarry.Tests/Integration/EntityReaderIntegrationTests.cs`.
9. Run `dotnet test src/Quarry.Tests` — all 4 dialects' Identity-projection tests should now populate DisplayLabel via their per-context reader. Tuple/single-column projection tests should remain unchanged (they don't use the custom reader).

### Phase 11–12 (after Phase 10 lands)

- Phase 11: Convert `RawSqlIntegrationTests.cs` (~15 tests) to `SqlOutput/CrossDialectRawSqlTests.cs` with per-dialect SQL strings (`@p0` Lite/Ss, `$1` Pg, `?` MySQL).
- Phase 12: Convert `LoggingIntegrationTests.cs` (~30 tests, process-wide `LogsmithOutput.Logger`) to `SqlOutput/CrossDialectLoggingTests.cs` with `[NonParallelizable]`.
- Then REVIEW (delegate 6-section analysis), REMEDIATE, REBASE, PR, FINALIZE.

## Session Log

| # | Phase Start | Phase End | Summary |
|---|-------------|-----------|---------|
| 1 | INTAKE | DESIGN | Confirmed problem from discussion. Created branch + worktree. Baseline 3340/3340 passing. Auto-transitioned to DESIGN. |
| 1 | DESIGN | PLAN | Verified SQLite 3.49.1 supports RIGHT/FULL OUTER via empirical probe. Locked rule decisions, conversion pattern, file mapping. User approved. |
| 1 | PLAN | IMPLEMENT | Wrote 12-phase plan in plan.md (Track A: phases 1–3 hardening; Track B: phases 4–12 conversion). User approved. |
| 1 | IMPLEMENT P1 | IMPLEMENT P1 | Phase 1 complete — added QRA503 (Error) descriptor; SuboptimalForDialectRule emits QRA502 (perf) for MySQL RIGHT JOIN, QRA503 (capability) for MySQL FULL OUTER + SqlServer OFFSET-no-ORDERBY; removed stale SQLite rules; updated DialectRuleTests; pruned MySQL clause from CrossDialectJoinTests.FullOuterJoin_OnClause + JoinNullableIntegrationTests.FullOuterJoin_SqlVerification. Tests: 3341/3341 (was 3340 — net +1 test). |
| 1 | IMPLEMENT P2 | IMPLEMENT P2 | Phase 2 complete — added 9 full-pipeline analyzer integration tests via AnalyzerTestHelper covering MySQL/PG/Ss/SQLite × FULL OUTER JOIN and SqlServer × OFFSET (with/without ORDER BY) plus three negative dialects. Tests: 3350/3350 (Analyzers 127). |
| 1 | IMPLEMENT P3 | IMPLEMENT P3 | Phase 3 complete — added 8 full-pipeline generator integration tests for QRY070/QRY071 covering INTERSECT ALL and EXCEPT ALL across 4 dialects. **Discovered + fixed silent diagnostic drop**: QuarryGenerator.s_deferredDescriptors was missing IntersectAllNotSupported/ExceptAllNotSupported/SetOperationProjectionMismatch — GetDescriptorById returned null and the diagnostics were dropped at QuarryGenerator.cs:524. Removed the obsolete "cannot test these" note. Tests: 3358/3358 (Quarry.Tests +8). |
| 1 | IMPLEMENT P4 | IMPLEMENT P4 | Phase 4 complete — converted ContainsIntegrationTests.cs to 7 cross-dialect tests in CrossDialectWhereTests (4 SELECT) + CrossDialectDeleteTests (3 DELETE). Exercise the runtime collection-expansion path (List<int>, IEnumerable<int>) on all 4 dialects. Original Integration file deleted. Tests: 3358/3358 (count unchanged: 7 deleted + 7 added; each new test runs on 4× dialects). |
| 1 | IMPLEMENT P4 | IMPLEMENT (suspended) | Suspended after Phase 4 to preserve cache. Track A complete; Track B 1/9 done. handoff.md has the per-phase resumption guide. |
| 2 | IMPLEMENT (resume) | IMPLEMENT P5 | Resumed from suspend. Verified baseline 3358/3358. Phase 5 complete — converted CollectionScalarIntegrationTests.cs (7 SQLite-only) → 7 cross-dialect execution tests appended to CrossDialectWhereTests.cs in a new "Collection + scalar — runtime parameter mixing (4-dialect execution)" region. Deleted original. Tests: 3030/3030 in Quarry.Tests (count unchanged, but each new test runs on 4× dialects). |
| 2 | IMPLEMENT P6 | IMPLEMENT P6 | Phase 6 complete — JoinedCarrierIntegrationTests.cs was already 4-dialect (the plan's premise was wrong). Reduced phase to deduplicate + relocate: 4 of 8 tests were exact duplicates of existing CrossDialectJoinTests.cs (TwoTable/ThreeTable/FourTable basic + PreJoinWhere). Moved the 4 unique tests (captured-param Where after join, 5-table, 6-table joins, 3-table COUNT terminal) into CrossDialectJoinTests.cs as new regions. Deleted Integration file. Tests: 3026/3026 (-4 from dedup as expected). |
| 2 | IMPLEMENT P7 | IMPLEMENT P7 | Phase 7 complete — JoinNullableIntegrationTests.cs had 8 tests: 6 SQLite-only execution (LEFT JOIN null materialization for decimal / multi-column / WhereOnLeft / entity / int / enum) + 2 already-cross-dialect (RightJoin + FullOuterJoin SQL+metadata, both fully redundant with existing RightJoin_Select / FullOuterJoin_OnClause + JoinNullableProjectionTests RightJoin_LeftSideColumnsJoinNullable / FullOuterJoin_BothSidesJoinNullable). Converted the 6 SQLite-only tests to 4-dialect Prepare+AssertDialects+ExecuteFetchAllAsync in a new "Left Join — null materialization (4-dialect execution)" region of JoinNullableProjectionTests.cs. Deleted the 2 redundant tests + the original file. Tests: 3024/3024 (-8 deleted +6 added = -2 net). |
| 2 | IMPLEMENT P8 | IMPLEMENT P8 | Phase 8 complete — added Events() accessor to PgDb / MyDb / SsDb (the events table was already seeded in all 3 containers but the DbContexts didn't expose it). Converted DateTimeOffsetIntegrationTests.cs (3 SQLite-only tests) to 4 cross-dialect tests in a new "DateTimeOffset Round-Trip Tests" region of CrossDialectTypeMappingTests.cs. Used UTC-zero offsets for insert+select tests (cross-dialect parity). For the existing seeded Review row (offset +02:00), uses UTC-instant comparison and excludes MySQL with a documented note (MySQL DATETIME seed strips the offset → different UTC instant; storage limitation, not a Quarry bug). Deleted Integration file. Tests: 3025/3025 (-3 deleted +4 added). |
| 2 | IMPLEMENT P9 | IMPLEMENT P9 | Phase 9 complete — PrepareIntegrationTests.cs already had 4-dialect SQL coverage but only executed on SQLite. Added 6 new "Prepare — 4-dialect execution" tests to PrepareTests.cs covering: SingleTerminal FetchAll, SingleTerminal FetchFirst, MultiTerminal Diagnostics+FetchAll, Delete (no-match), Update (no-match), BatchInsert SQL+verification. Dropped 2 redundant Integration tests (MultiTerminal_ToSqlAndFetchAll just adds a no-where variant; MultiTerminal_DiagnosticsAndToSql_SameSql is fully covered by existing PrepareTests SQL=SQL parity tests). Deleted original. Tests: 3023/3023 (-8 deleted +6 added = -2 net). |
| 3 | IMPLEMENT (resume) | IMPLEMENT P10 | Resumed session 3. Verified baseline 3351/3351. Investigated Phase 10 [EntityReader] bug: confirmed via `obj/GeneratedFiles/.../PgDb.Interceptors.*.g.cs` line 33 that interceptor emits `IQueryBuilder<Pg.Product, Quarry.Tests.Samples.Product>` — invalid for `Pg.Products().Select(p => p)`. User chose to defer the architectural fix. Filed issue **#277** with full repro + two-layer fix plan. Phase 10 closed without conversion; the Integration file stays as SQLite-only coverage until #277 lands. |
| 2 | IMPLEMENT P10 (investigation) | IMPLEMENT (suspended) | Started Phase 10. Wrote a CrossDialectEntityReaderTests.cs draft; failed to compile because Pg.Product / My.Product / Ss.Product are separate generated types that lack the global Product's DisplayLabel partial extension. Investigation revealed a pre-existing source generator bug: [EntityReader] attribute resolves the reader class only in the schema's namespace, so the interceptor uses GlobalProduct as the reader's T even when the call site expects per-context Product. User chose Layer 1 + Layer 2 fix scope (test-side partials/readers + generator-side per-context name lookup). Reverted to clean Phase 9 baseline (3023/3023) and suspended for fresh-context implementation. Full plan in Suspend State above. |
