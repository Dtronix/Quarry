# Plan: add-sqloutput-tests

Four implementation phases, each independently committable. The branch lands additional `Quarry.Tests/SqlOutput/` test coverage for areas the suite under-exercises today, plus a single new schema (`TagSchema`) that unlocks Phase 3.

Every test must run against all four dialects (SQLite, PostgreSQL, MySQL, SQL Server). When a dialect genuinely lacks the feature (only confirmed case: MySQL has no `FULL OUTER JOIN` — the analyzer rejects via `QRA503`), the test runs on the supporting dialects with a one-line comment naming the skipped dialect and the reason.

Each phase ends green: build, then the SQLite-only path of `dotnet test` (Testcontainers paths require Docker; full cross-dialect verification falls to CI on the PR — see workflow.md baseline note).

---

## Phase 1 — 5/6-table mixed-kind joins + higher-arity Cross/FullOuter

**Gap.** `CrossDialectJoinTests.cs` covers 5- and 6-table chains only with all-`Join` (inner). `LeftJoin`/`RightJoin`/`CrossJoin` are tested at 2 tables; `FullOuterJoin` at 2 tables. The T4-generated `IJoinedQueryBuilder5/6` and `JoinedCarrierBase5/6` mixed-kind paths have no SQL-output regression coverage.

**File.** Append a new region to `CrossDialectJoinTests.cs` (near the existing `5-Table Join` / `6-Table Join` regions) titled `Mixed-Kind 5/6-Table Joins`. Keeping the tests in one file matches the suite convention and lets reviewers see arity progression in one place.

**Test methods (all four dialects unless noted).**
1. `Join_FiveTable_Mixed_InnerLeftRightCross` — Users `Join` Order `LeftJoin` OrderItem `RightJoin` Shipment `CrossJoin` Warehouse. Asserts the exact JOIN-kind ordering and `t0`-`t4` aliasing in SQL. Executes against SQLite seed with a result-count assertion (no specific row asserts because outer-join row counts vary).
2. `Join_SixTable_Mixed_WithCrossJoin` — adds `Join<Account>` after the chain. Tests that arity-6 supports a CROSS JOIN in any position.
3. `Join_FiveTable_Mixed_WithFullOuter` — ends in `FullOuterJoin<Warehouse>`. Skips MySQL with the same comment pattern as `FullOuterJoin_OnClause` (line 670–676): `// MySQL is intentionally excluded: it has no FULL OUTER JOIN support, and the analyzer (QRA503) rejects MySQL FullOuterJoin call sites.`
4. `Join_SixTable_AllLeftJoin` — six-table chain with every join as `LeftJoin`. Verifies cascading nullability through the join chain and that `t0`-`t5` aliases stay correct.
5. `Join_FiveTable_Mixed_WithWhere_CapturedParam` — same Inner/Left/Cross mix as test 1 plus a `Where((u,o,oi,s,w) => o.Total > minTotal)` with a captured `decimal minTotal`. Verifies parameter ordering at higher arity (`@p0`, `$1`, `?`, `@p0` per dialect).

**Dialects skipped, with reason.** Tests 3 only: MySQL — see comment above. All other tests cover all four dialects.

**Pattern to follow.** `CrossDialectJoinTests.Join_FiveTable_Select` (line 972) and `FullOuterJoin_OnClause` (line 669). Use `QueryTestHarness.AssertDialects` with explicit per-dialect SQL strings. For the FullOuter test, follow the existing pattern of separate per-dialect `Is.EqualTo` asserts (since `AssertDialects` requires four strings).

**Risk.** Mixed-kind joins generate cascading nullability via `ChainAnalyzer.cs:2018` (`a RIGHT/FULL OUTER join at position i makes all tables 0..i nullable`). The reader codegen wraps reads with `IsDBNull` guards on the nullable side. If the asserts compare scalar values and a row has nulls, the projection will need to use `?` types — keep selectors to `(Name, Total)` etc. and assert tuple counts/orderings only, mirroring `LeftJoin_Select` (line 291).

---

## Phase 2 — Conditional-mask boundary stress

**Gap.** `ConditionalCarrierTests` (Generation/) verifies carrier emission and mask-variant-count for up to 4-bit chains, plus mutually-exclusive single-bit groups. There is no `SqlOutput/` test that asserts the **per-mask SQL string** at boundary configurations: 8 independent bits (max), depth-2 nested `if/else`, and mutually-exclusive groups interleaved with independent bits, across all four dialects.

**File.** New file `src/Quarry.Tests/SqlOutput/CrossDialectConditionalMaskTests.cs`. Existing `CrossDialectDiagnosticsTests.cs` is single-dialect (SQLite via `_db = new TestDbContext(new MockDbConnection())`); a new file lets us use `QueryTestHarness` for four-dialect coverage without disturbing the existing fixture. Add `#pragma warning disable CS0162` at the top, matching `CrossDialectDiagnosticsTests.cs:6`, since the tests use `if (true)` / `if (false)` literals.

**Algorithmic note on bit allocation.** Per llm.md §"Conditional Clause Masking": each conditional clause site gets a `BitIndex` 0–7. Clauses inside `if (true) { ... }` are conditional-active (bit set); `if (false) { ... }` are conditional-inactive (bit unset, fragment elided from SQL). Each test uses a deterministic mix of `true`/`false` literals to land on a chosen mask value, then asserts the resulting SQL string per dialect.

**Test methods (all four dialects).**
1. `Mask_AllEightBitsActive_RendersAllClauses` — eight `if (true)` blocks, each adding a distinct `Where(u => …)`. Asserts the SQL has all eight predicates joined with `AND`.
2. `Mask_NoBitsActive_RendersBareSelect` — eight `if (false)` blocks. Asserts the SQL has no `WHERE`.
3. `Mask_AlternatingBits_RendersOnlyActiveTerms` — `if (true)` / `if (false)` alternating across eight slots. Asserts only the four active predicates appear.
4. `Mask_DepthTwoNesting_RendersInnerOnlyWhenOuterTrue` — `if (true) { if (true) { Where(…) } }` and a sibling `if (true) { if (false) { Where(…) } }`. Asserts only the first inner predicate is rendered.
5. `Mask_MutuallyExclusiveOrderBy_RendersOneBranch` — `if (sortByName) { OrderBy(UserName) } else { OrderBy(UserId) }` with a literal `sortByName = true`. Asserts the SQL has `ORDER BY` on `UserName` and not `UserId`. Add a sibling test with `sortByName = false`.
6. `Mask_ConditionalWhere_PlusUnconditionalOrderByLimit` — one `if (true) Where(…)` plus an unconditional `.OrderBy(…).Limit(10)`. Asserts the rendered SQL has both the active predicate and the trailing `ORDER BY ... LIMIT 10` (`TOP 10` on SQL Server, `OFFSET ... ROWS FETCH NEXT ... ROWS ONLY` if applicable — verify by reading existing CrossDialect Limit tests). The point is to confirm that mask-conditional and unconditional terms render correctly together.
7. `Mask_ConditionalHaving_WithGroupBy` — `GroupBy` (unconditional) + `if (true) Having(…)`. Asserts the SQL has both `GROUP BY` and `HAVING`.

**Dialects skipped.** None. All tests run against all four.

**Pattern to follow.** Use `QueryTestHarness.AssertDialects` and replicate the same `if (cond) query = query.Where(…)` pattern for each of `Lite`, `Pg`, `My`, `Ss` from `CrossDialectDiagnosticsTests.ToDiagnostics_ConditionalWhereActive_ClauseIsConditionalAndActive` (line 68) — but four times across dialects. Each test asserts the SQL string per dialect via `AssertDialects`. Skip the `Clauses[].IsActive` flag asserts (that's covered in `CrossDialectDiagnosticsTests`) — the per-mask SQL string is the new gap.

**Risk.** `if (true)` literals produce `CS0162` (already disabled by pragma in the existing file — replicate). Some tests may produce conditional clauses that the optimizer collapses (e.g., `Where(u => true)`) — match the existing pattern of starting with a no-op `Where(u => true)` to anchor the chain.

---

## Phase 3 — TagSchema + 3-level deeply-nested navigation subqueries

**Gap.** Existing schemas chain at most two levels of `Many` (User.Orders.Items terminating). Per llm.md §"Subquery & Aggregate Support", "Nested subqueries are supported (e.g., `u.Orders.Any(o => o.Items.Any(i => ...))`)." Two-level `.Any(.Any())` is tested (e.g., `CrossDialectSubqueryTests.cs:724`). Three-level is unreachable with current schemas.

**Sub-step 3a: add `TagSchema` and DDL.** New schema with `Many<TagSchema> Tags` on `OrderItemSchema`:

```csharp
// src/Quarry.Tests/Samples/TagSchema.cs (new)
public class TagSchema : Schema
{
    public static string Table => "tags";
    public Key<int> TagId => Identity();
    public Ref<OrderItemSchema, int> OrderItemId => ForeignKey<OrderItemSchema, int>();
    public Col<string> TagName => Length(50);
    public Col<string> TagValue => Length(100);
}
```

Then add `Many<TagSchema> Tags => HasMany<TagSchema>(t => t.OrderItemId);` on `OrderItemSchema` (additive — existing tests are unaffected).

Register the entity on every context (`TestDbContext`, `Pg.PgDb`, `My.MyDb`, `Ss.SsDb`):
```csharp
public partial IEntityAccessor<Tag> Tags();
```

DDL across the four dialect baselines:
- `QueryTestHarness.cs` — append `CREATE TABLE "tags" (...)` after the `shipments` table block (around line 575) and add an `INSERT INTO "tags" ...` after the `shipments` seed (around line 660). Seed ~6 rows: 2 tags per OrderItem for items 1–3.
- `Integration/PostgresTestContainer.cs` — same DDL+seed in PG style.
- `Integration/MySqlTestContainer.cs` — same DDL+seed in MySQL style (backticks).
- `Integration/MsSqlTestContainer.cs` — same DDL+seed in T-SQL style (brackets).

The manifest output (`ManifestOutput/quarry-manifest.*.md`) will regenerate from the new entity and may need a one-time update after first build. Treat regeneration as expected output, not a test failure.

**Sub-step 3b: add the tests.** New file `src/Quarry.Tests/SqlOutput/CrossDialectNestedSubqueryTests.cs`. Why a new file: `CrossDialectSubqueryTests.cs` is already 1000+ lines; a focused file makes the deep-nesting cases easy to find.

**Test methods (all four dialects).**
1. `Where_ThreeLevel_Any_AllowsDeepCorrelation` — `Where(u => u.Orders.Any(o => o.Items.Any(i => i.Tags.Any(t => t.TagName == "urgent"))))`. Asserts SQL has three nested `EXISTS` blocks with `sq0`/`sq1`/`sq2` aliases and proper correlation (`sq0.UserId = users.UserId`, `sq1.OrderId = sq0.OrderId`, `sq2.OrderItemId = sq1.OrderItemId`).
2. `Where_ThreeLevel_Any_All_Mix` — three-level with `.All(...)` at the deepest level. Verifies that `All` translates to `NOT EXISTS (... AND NOT pred)` per llm.md §"Subquery & Aggregate Support".
3. `Where_ThreeLevel_MixedAggregates` — `Orders.Any(o => o.Items.Sum(i => i.UnitPrice) > 100)`. Verifies a 2-level chain where the outer is `.Any` and the inner predicate uses `.Sum`. (3-level all-aggregate is harder — keep this 2-level + aggregate to stay realistic.)
4. `Where_ThreeLevel_CapturedParam_AcrossLevels` — `Where(u => u.Orders.Any(o => o.Items.Any(i => i.Tags.Any(t => t.TagValue == capturedTag))))` with a captured outer variable. Verifies that the captured parameter is plumbed correctly through three levels of subquery binding. Asserts parameter index in SQL (`@p0`, `$1`, `?`).
5. `Select_TwoLevel_NavSum_NestedNavCount` — projection-side subquery: `Select(u => (u.UserName, OrderTotal: u.Orders.Sum(o => o.Total), TagCount: u.Orders.Sum(o => o.Items.Sum(i => i.Tags.Count(t => t.TagName == "urgent")))))`. Validates two simultaneous projection-side subqueries with mixed nesting depths.

**Dialects skipped.** None.

**Pattern.** `CrossDialectSubqueryTests.Where_Any_Parameterless` (line 14) for layout. Use `QueryTestHarness.AssertDialects` with the literal SQL per dialect. The `sq{N}` alias counter is per-chain — verify by reading `SqlExprClauseTranslator` if assert-strings turn out wrong. For executions: seed two tags per item so result counts are predictable; assert at least the result count.

**Risk.** New schema means manifest regeneration. The first build after adding `TagSchema` will rewrite `ManifestOutput/*.md` files — commit those alongside the schema in sub-step 3a so the diff is clean. Schema-qualified contexts (`Samples/SchemaQualifiedContexts.cs`) may also need a `Tags()` accessor — read that file and replicate. PG/MySQL/SS containers seed via separate DDL — their files must be kept in sync; mismatches surface as runtime failures only on those dialects.

---

## Phase 4 — Bundled gaps: ToAsyncEnumerable, Computed-column dialect parity, multi-context-per-file

Three small gaps that don't justify a phase each.

**Gap A: `ToAsyncEnumerable` streaming terminal.** `IEntityAccessor<T>.ToAsyncEnumerable(CancellationToken)` is the streaming reader path. Greppable across the runtime and used in `IJoinedQueryBuilder.g.cs`, but not asserted in `SqlOutput/`.

**Gap B: Computed-column dialect parity.** `CrossDialectSchemaTests.Insert_ComputedColumnExcluded` (line 134) only asserts SQLite — three dialects unverified against a feature where DDL paths differ (`GENERATED ALWAYS AS (...) STORED` on PG/SQLite vs `AS (...) PERSISTED` on SQL Server). Existing `ProductSchema.DiscountedPrice => Computed<decimal>()` and DDL on all four dialects already exist; only the test assertions are missing.

**Gap C: Multi-context-per-file carrier isolation.** `FileInterceptorGroup` is keyed by `(context, source file)` (per llm.md §"Caching Boundaries"). No test asserts that two `[QuarryContext]` classes declared in the same source file emit independent interceptor groups with non-colliding `Chain_N` numbering. This is a generator-driver test, not SqlOutput.

**Files.**
- `src/Quarry.Tests/SqlOutput/CrossDialectStreamingTests.cs` (new) — Gap A.
- `src/Quarry.Tests/SqlOutput/CrossDialectSchemaTests.cs` (modify) — expand `Insert_ComputedColumnExcluded` and add `Update_ComputedColumnExcluded`, `BatchInsert_ComputedColumnExcluded` — Gap B.
- `src/Quarry.Tests/Generation/MultiContextPerFileTests.cs` (new) — Gap C, generator-driver style mirroring `ConditionalCarrierTests.cs`.

**Test methods.**

*Gap A — streaming:*
1. `ToAsyncEnumerable_BasicSelect_StreamsAllRows` — `Lite/Pg/My/Ss.Users().Select(u => u.UserName).ToAsyncEnumerable()`. Iterates and asserts the count plus the result list. SQL string asserted via a parallel `.Prepare().ToDiagnostics()` for diagnostic capture (since `ToAsyncEnumerable` is the terminal — no `ToDiagnostics`).
2. `ToAsyncEnumerable_WithCancellation_StopsEarly` — break out of the iteration after N rows; assert only N rows materialize. Cancellation behavior is runtime-only; SQL string identical to test 1.
3. `ToAsyncEnumerable_Conditional_RendersCorrectMask` — `if (true) query = query.Where(…)` then `.ToAsyncEnumerable()`. Verifies that conditional masks integrate with the streaming terminal (separate carrier dispatch path).

*Gap B — computed columns:*
4. Replace the existing `Insert_ComputedColumnExcluded` with a four-dialect version using `AssertDialects`. Keep the assertion that `DiscountedPrice` is absent from the column list and from `VALUES`.
5. **Add QRY075 diagnostic + tests.** Current behavior: `Update().Set(p => p.DiscountedPrice, value)` compiles and emits `SET "DiscountedPrice" = @p0`, which the DB engine rejects at runtime. INSERT filters computed columns silently (`InsertInfo.cs:56`), but UPDATE has no filter. Decision: add compile-time `QRY075` (Error) — *"Cannot SET a computed column 'X' on entity 'Y'. Computed columns are read-only."* Implementation:
   - `Quarry.Generator/DiagnosticDescriptors.cs`: register `QRY075` with the message above.
   - `Quarry.Generator/IR/CallSiteTranslator.cs`: in `Set` / `UpdateSet` / `UpdateSetAction` clause translation, after resolving the target column, check `column.Modifiers.IsComputed`. If true, attach a `DiagnosticInfo` to the `TranslatedCallSite` and skip rendering. Use `column.PropertyName` and `entity.Name` in the message.
   - `EntityCodeGenerator.cs:78` already emits `init` for computed properties — so the `Set(Action<T>)` POCO-mutation form already fails to compile via the C# compiler. QRY075 covers the typed-lambda form (`Set(p => p.X, value)`) where `init` doesn't help.
   - Tests: `CrossDialectDiagnosticsTests.QRY075_UpdateSet_ComputedColumn_Reports`. Use a generator-driver pattern (synthesize source, run `QuarryGenerator`, assert the diagnostic was reported with the correct location). One test per Set form (typed lambda + UpdateSet variant). Also add a positive test asserting non-computed columns still work.
   - Update `llm.md` §"Diagnostics (QRY Codes)" to add the QRY075 row.

6. `BatchInsert_ComputedColumnExcluded` — `BatchInsert(p => (p.ProductName, p.Price))` with a batch of products. Assert SQL excludes the computed column.

*Gap C — multi-context:*
7. `MultiContextPerFile_GeneratesIndependentInterceptorGroups` — generator-driver test. Synthesize a single source file containing two `[QuarryContext]` classes. Run `QuarryGenerator`. Assert that the generated output contains **two** distinct interceptor `.g.cs` files (one per context) and that the carrier class names don't collide. Pattern: `ConditionalCarrierTests.GenerateInterceptors` (line 98), but inspect `result.GeneratedTrees` for two interceptor files.

**Dialects skipped.** None for tests 1–6. Test 7 is dialect-agnostic — synthesized contexts use `SqlDialect.SQLite`, but the test isn't about SQL output.

**Risk.** The exact `Update().Set()` behavior for computed columns isn't documented in llm.md — must be verified against `CallSiteTranslator.cs` before writing the assert. If the behavior is "compile error" rather than "silent drop", test 5 becomes a `CrossDialectDiagnosticsTests`-style diagnostic assertion instead. `ToAsyncEnumerable` cancellation behavior is runtime-only — test 2 must be careful to not leak resources via half-iterated streams (use `await foreach` with `break`, not partial enumeration).

---

## Phase dependencies

- Phase 1, 2 — independent; can land in any order.
- Phase 3 sub-step 3a (schema/DDL) **must** land before sub-step 3b (tests). They can be one commit or two; one commit avoids an orphaned-schema commit but doubles the diff size. Recommended: single commit per phase.
- Phase 4 — independent of 1, 2, 3.

## Test commands

For each phase:
```
dotnet build src/Quarry.Tests/Quarry.Tests.csproj
dotnet test src/Quarry.Tests/Quarry.Tests.csproj --filter "FullyQualifiedName~SqlOutput.<NewClass>" --logger "console;verbosity=normal"
```

Full pre-PR run:
```
dotnet test src/Quarry.Tests/Quarry.Tests.csproj
```

Full run requires Docker for cross-dialect verification. Docker is now running locally, so each phase will be smoke-tested with `dotnet test --filter FullyQualifiedName~SqlOutput.<NewClass>` before commit. CI is the final gate.
