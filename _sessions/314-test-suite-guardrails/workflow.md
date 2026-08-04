# Workflow: 314-test-suite-guardrails

## Config
platform: github
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: #314
pr:

## Problem Statement
Issue #314 — Test suite guardrails, from the 2026-07-07 multi-agent deep review (tests perspective, grade B+). Seven findings plus bundled low items:

1. **(H, confirmed)** `IncrementalCachingTests` never exercises caching machinery on valid chains — anonymous-type projections hit QRY032/RuntimeBuild so interceptors are empty shells; unchanged-run test reuses the same `CSharpCompilation` instance (reference-equal inputs, model `.Equals` never invoked); per-file cache assertions too weak (`Does.Contain(Cached)` anywhere passes). No negative equality tests for `EntityRegistry`/`AssembledPlan`/`CarrierPlan`/`FileInterceptorGroup`.
2. **(H, confirmed)** No automated performance regression gate — benchmarks run post-merge only, publish with zero threshold/alert logic; PRs never run benchmarks; `select(.Statistics != null)` silently drops failed benchmarks from dashboard.
3. **(M, insurance)** Zero concurrency testing — no test executes two Quarry ops concurrently; current shared state verified safe by construction; regression insurance.
4. **(M)** 526 positional row-order assertions on PG/MySQL/SS without ORDER BY — `SortedByAsync` exists but used at only 31 call sites.
5. **(M)** Known generator bugs routed around instead of pinned (CrossDialectConditionalMaskTests cross-context misattribution; PostgresIntegrationTests PG entity-terminal skip); blanket CS9177 NoWarn hides unintercepted call sites.
6. **(M)** SQL-manifest goldens (`ManifestOutput/quarry-manifest.{dialect}.md`) have no CI enforcement — no test reads them, no `git diff --exit-code` step.
7. **(M)** Streaming/cancellation nearly untested — no early-break disposal test, `CancellationToken` in one file.

Low (bundled): suite runs fully sequentially despite documented parallelizability; display-class prediction tests validate predictor against itself (single Roslyn version).

### Baseline test results
2026-07-22, full `dotnet test Quarry.sln` with Docker available (all containers ran): **all green, no pre-existing failures.**
- Quarry.Tests: 3424 passed, 0 failed, 0 skipped (1m28s)
- Quarry.Migration.Tests: 201 passed
- Quarry.Analyzers.Tests: 146 passed
Note: pre-existing build warnings — NU1903 (System.Security.Cryptography.Xml 9.0.0 vulnerability) and CS0219 `__colShift` unused in generated MyDb/TestDbContext CrossDialectUpdateTests interceptors.

## Decisions
- 2026-07-22 — **Scope**: all 7 findings in this branch; defer the two low items (test parallelization enablement, display-class canary) — parallelization is risky/orthogonal, canary belongs to the display-class issue.
- 2026-07-22 — **Perf gate (F2)**: alert-only for perf — benchmark job auto-opens a GitHub issue on threshold breach (>15% mean / any allocation increase on `Quarry_*` series) instead of failing the workflow. A MISSING expected series (broken/crashed benchmark) DOES fail the workflow (infrastructure failure, not a perf judgment). NO PR-time allocation smoke tests.
- 2026-07-22 — **Row-order (F4)**: full sweep — all genuinely order-sensitive FetchAll sites → `SortedByAsync`; bare `.First()`/`FetchFirstAsync` order-sensitive sites get query-side ORDER BY where SQL assertions permit. Skip single-row, already-ORDER-BY'd, predicate-First sites.
- 2026-07-22 — **CS9177 (F5)**: pin + guard test — file issues for both routed-around bugs, add pinning tests, keep blanket NoWarn, add codegen guard test asserting the exact expected set of non-intercepted sites.
- 2026-07-23 — **CS9177 NoWarn REMOVAL (F5 revision)**: evidence showed the blanket NoWarn suppresses nothing (zero CS9177 in a full build with it overridden; the real mismatch is CS9144, an error). Step 7 removes the vestigial NoWarn and adds an interceptor-binding guard matrix instead of "targeted suppressions".
- 2026-07-23 — **Pin placement (F5 revision)**: #328's pin lives in CrossDialectConditionalMaskTests against the real contexts (synthetic isolation doesn't reproduce either bug); #328 retitled to the actual remaining defect (conditional Having not mask-gated). #329 has no compilable pin (the bug is a build error) — signal is the guard matrix + documented probe in the issue.
- 2026-08-03 — **#329 pin restored (revises the 2026-07-23 pin-placement decision)**: step 7 found the
  defect *is* reproducible in isolation — the emitter produces a two-arity receiver for a chain that
  never projects; it simply raises no CS9144 there. Pinned on the emitted text in
  `InterceptorBindingGuardTests.KnownBug_Issue329_EntityTerminal_EmitsTwoArityReceiver` rather than
  on a compiler diagnostic. The guard matrix stays as the regression net for clause shapes.

## Working Notes

### Step 13a (2026-08-03) — row-order sweep, remaining 21 files

**31 sites converted out of ~394 fetch calls.** Five of the eight file groups needed nothing, and
*why* they needed nothing is the useful part — it says the suite was already far more defensive
than the issue's "526 positional assertions" headline implies:

- **StringOp / Misc / Aggregate (98 sites, 0 converted)** — no `Has.Count.EqualTo(n)` with n>1
  exists anywhere in the three files. Everything is count-only, single-row, or `.First(predicate)`.
- **Composition / WindowFunction (99 sites, 0 converted)** — already written order-independently:
  count-only, `.All(...)`, `.First(pred)`, or explicitly re-sorted into a second local before the
  positional asserts. Every `OVER (ORDER BY ...)` is window-internal and correctly does **not**
  count as a top-level order; only
  `WindowFunction_ParamArgs_WithParameterizedLimit_NumbersPaginationByGlobalSlot` has a genuine
  top-level ORDER BY.
- **ConditionalMask (0 converted)** — every non-inline pg/my/ss chain carries an unconditional
  `.OrderBy(u => u.UserId)`. Converting them would have masked precisely the mask-gated-pagination
  regression the fixture exists to catch.
- **OrderBy / DistinctOrderBy (21 sites, 0 converted)** — all-skip confirmed test by test, as
  predicted: a top-level ORDER BY is the thing they pin.
- Converted: Nullable 12, HasMany 6 (incl. 2 single-scalar `s => s` keys), FkKey 6 (normalizations),
  Prepare 6, EntityReader 3.

- **`Select_HasManyThrough_Max_InTuple` was the trap-1 near-miss in this group**: the obvious
  "second column" key `MaxAddrId` runs 2 then 1 (descending) and would have flipped the rows.
  Keyed on `UserName` instead.
- **Two-statement re-sort idiom left alone** (9 sites in 3 WindowFunction tests): they fetch into
  `pgResults`, assert `Has.Count` on it, then build `pgByRowNum = pgResults.OrderBy(...).ToList()`
  for the positional asserts. Semantically identical to `SortedByAsync` but collapsing it means
  deleting the intermediate local and rewriting assertion lines. Not worth the churn; they are
  already order-safe.

### Step 13b (2026-08-03) — query-side remediation

- **Nine `CrossDialectJoinTests` → filed as #332 rather than changed** (user decision 2026-08-03,
  after confirming no existing issue covered it). Both candidate fixes are written up in the issue
  with the seed-data analysis; the key point recorded there is that
  `.SortedByAsync(r => (r.UserName, r.Total))` compiles, reads as correct, and silently swaps rows
  `[0]` and `[1]` — so a future sweep must not "fix" these mechanically. A `<remarks>` block on
  `CrossDialectJoinTests` now says exactly that and points at #332, so the trap is documented where
  someone would hit it rather than only in the tracker.
- **Six query-side ORDER BY fixes applied** (user-approved): the four `Pagination_*` tests with
  `LIMIT/OFFSET`, `NoSelect_ExecuteFetchFirstAsync_ReturnsFirstEntity`, and
  `Where_CollectionPlusScalar_WithPagination_ReturnsCorrectRows`.
- **`OrderBy` goes *after* `Select` in this API** and takes the source-entity lambda, not the
  projected tuple: `Select(u => (u.UserId, u.UserName)).OrderBy(u => u.UserId)` renders
  `... ORDER BY "UserId" ASC ...`. Pattern taken from `CrossDialectOrderByTests:20`.
- **The rendered SQL matched prediction exactly on all four dialects** — no derived-table wrap
  (that only happens with `Distinct()` over a non-projected order column), and **parameter indices
  are unchanged** (`$1`/`$2`, `@p0`/`@p1`) because ordering on a literal column adds no parameter.
  So the mixed literal/parameterized pagination tests still pin the same index assignment they
  were written to pin.
- **SQL Server gains real coverage here**: `ORDER BY (SELECT NULL) OFFSET n ROWS FETCH NEXT m ROWS`
  became `ORDER BY [UserId] ASC OFFSET n ROWS FETCH NEXT m ROWS`. The old form satisfies the T-SQL
  grammar requirement for OFFSET/FETCH while imposing no order at all, so those tests were
  asserting against an ordering the query never promised.
- `Where_CollectionPlusScalar_WithPagination_ReturnsCorrectRows` asserted only a row count, so the
  ORDER BY let it be **strengthened** with an actual value assertion rather than merely stabilised.
- **These edits DO regenerate the `ManifestOutput` goldens** (unlike the pure sweep, which only
  appends post-terminal calls). All four dialect goldens are committed with the change; the diff is
  large because entries are keyed by chain signature and adding `.OrderBy(...)` re-sorts them.
  Five `ORDER BY (SELECT NULL)` entries remain in the SQL Server golden — those belong to
  count-only `Limit`/`Offset` tests in Complex/Where deliberately left alone (see below).
- **Deliberately not fixed** — `CrossDialectComplexTests.Where_Select_Limit` (`Limit(5)` over 2 rows)
  and `_LimitOffset` (`Limit(10).Offset(20)`, returns nothing): the limit exceeds the row count, so
  nothing is actually nondeterministic, and adding an ORDER BY would churn pinned SQL for no gain.

### Step 12 (2026-08-03) — row-order sweep, second file group

Only **27 lines** across the four files. Two of the four needed nothing at all, for opposite
reasons, and both reasons are worth carrying into step 13:

- **`CrossDialectWideTupleTests` — 0 conversions, every test already carries a top-level
  `ORDER BY`.** All 21 pg/my/ss sites hit the primary skip rule. `Tuple_PostCteWideProjection_OrderBy`
  is the sharpest case: `r => r.OrderId` ascending *would* coincidentally reproduce the asserted
  order (OrderId 1 → Total 250, OrderId 3 → Total 150) while silently ceasing to test the
  `Direction.Descending` ORDER BY it exists to pin. That is exactly the regression-masking the
  skip rule protects against.
- **`CrossDialectSetOperationTests` — 0 conversions, already hardened by a different mechanism.**
  Its authors evidently hit this problem first: every multi-row value-asserting test sorts inline
  with `.OrderBy(r => r.UserName).ToList()` on all four sides, and every other test asserts count
  only. 75 sites, all correctly skipped.
- **`CrossDialectCteTests` — 3 conversions.** `Cte_FromCte_SimpleFilter` was the one test the
  earlier partial pass missed; the other 18 sites were already converted.
- **`CrossDialectWhereTests` — 21 conversions** (7 tests), all `r => r.UserId`. No test in the
  file asserts any SQL containing `ORDER BY`, so the primary skip rule never fired; the 14 skips
  are all single-row or `Does.Contain`/count-only assertions.

- **Ad-hoc `(await q.ExecuteFetchAllAsync()).OrderBy(k).ToList()` idiom — normalized where it is
  a fetch line.** `RowOrderExtensions`' own doc comment asks for `SortedByAsync` instead, so the
  three pg/my/ss sites in `Tuple_PostCteWideProjection` were converted. A repo-wide grep found
  **6 more at `SqlOutput/FkKeyProjectionTests.cs:63,68,73,116,120,124`** → normalize in step 13.
  The SQLite side of those tests is left on the inline form, matching the helper's documented
  contract (real-provider sides only).
  **Not** normalized: 12 sites in `CrossDialectSetOperationTests` (`CrossEntity_Union_*`,
  `_UnionAll_`, `_Except_`, `_Union_WithParameters`) sort into a *separate* `pgValues`/`myValues`/
  `ssValues` local rather than on the fetch line, so converting them means editing
  assertion-adjacent lines. They are already order-safe; leave them.
- **New latent flake for step 13**:
  `Where_CollectionPlusScalar_WithPagination_ReturnsCorrectRows` applies `Limit(1)` with no
  ORDER BY on all four dialects. Harmless today (count-only assertion), but it cannot be
  strengthened without a query-side ORDER BY.

### Step 14 (2026-08-03) — benchmark regression gate

- **Gate runs before publishing, not after.** The comparison step sits between "Merge benchmark
  results" and "Generate merged HTML report", and fetches the previous `data.js` from the
  benchmarks repo's gh-pages over raw.githubusercontent (same public path the `check` job
  already uses for `runs.json`). Comparing before publish means a crashed run never becomes the
  stored baseline, and it avoids coupling to the publish step's in-place append.
- **`gh` CLI is not assumed present** on the self-hosted `debian-benchmark` runner. Issue lookup
  and creation go through `curl` + the REST API, which the workflow already relies on for commit
  metadata. `jq` is likewise already a hard dependency of the merge step.
- **Dedupe is on issue title alone, not on the label** — a maintainer who relabels the tracking
  issue should still suppress duplicates. `select(.pull_request == null)` is required because
  `/issues` returns PRs too.
- `select(.Statistics != null)` in the merge step is **kept**, not removed: downstream jq would
  otherwise have to null-guard. What changed is that the drop is no longer silent — a series
  present in the last published run and absent here fails the workflow.
- **Dry-run verified locally with jq 1.7.1** against synthetic fixtures
  (`scratchpad/perfgate-dryrun.sh`), covering: +20% mean → breach; allocation 100→120 B with
  only +1% mean → breach; +5% mean with flat allocation → clean; removed series → missing;
  newly added series → ignored; non-`Quarry_*` methods → filtered out. Also confirmed the
  no-baseline paths (`entries` key absent, empty entries array, `benches: null`) all yield an
  empty previous set rather than a jq error. YAML re-parsed after editing; step order and the
  two `if:` guards confirmed.
- Not verifiable locally: the `issues: write` token scope and the actual REST calls. First real
  exercise is post-merge on master.

### Step 11 (2026-08-03) — row-order sweep, first file group

- **The 526-assertion estimate does not translate into 526 conversions.** Across
  SelectTests / SubqueryTests / JoinTests only **87 fetch lines (29 test methods)** were
  convertible. The raw positional-access count is inflated because most tests assert several
  fields per row, and a large share of pg/my/ss sides assert only `Has.Count` while the
  positional asserts live on the SQLite side (which we deliberately never touch). Expect the
  same ratio in steps 12–13 — the sweep is much smaller than the issue implies.
- **JoinTests is mostly *unfixable* by sorting, and that is the important finding.** 9 tests
  (`Join_InnerJoin_OnClause`, `Join_WithWhere_OnLeftTable`, `Join_InnerJoin_NamedTupleProjection`,
  `Join_ThreeTable_NamedTupleProjection`, `Join_WithWhere_TwoCapturedParams_BooleanBetween_...`,
  `Where_BeforeJoin_GetsTableAliasQualification`, `Select_Joined_Many_Sum_OnLeftTable`,
  `Select_Joined_Many_Count_OnLeftTable`, `Select_Joined_HasManyThrough_Max_OnLeftTable`)
  assert `[0]=(Alice,250.00), [1]=(Alice,75.50), [2]=(Bob,150.00)` on an unordered users→orders
  join. The order they encode is `orders.OrderId` ascending, but `OrderId` is **not projected** —
  the only discriminator present is `Total`, which runs *descending* within the Alice group. So
  no ascending key over the projected columns reproduces the asserted order, and a naive
  `(UserName, Total)` key turns them red. These need a **query-side** fix (project + ORDER BY
  the join key, or relax to `Is.EquivalentTo`) → deferred to step 13.
- **Pagination cluster is a latent flake sorting cannot reach.** `Pagination_LimitOffset`,
  `_LiteralLimit_ParameterizedOffset`, `_ParameterizedLimit_LiteralOffset`, `_BothParameterized`
  all do `LIMIT 2 OFFSET 1` with no ORDER BY and assert they get exactly Bob and Charlie; on
  PG/MySQL any 2-row subset is legal, and SQL Server's generated `ORDER BY (SELECT NULL)`
  satisfies the T-SQL grammar without imposing an order. *Which* rows come back is
  nondeterministic, so C# sorting cannot help → step 13. `Pagination_LimitOnly` **was**
  converted: `LIMIT 5` over 3 rows returns the whole table, so sorting fully determinises it.
- **`ExecuteFetchFirstAsync` on a multi-row predicate**:
  `NoSelect_ExecuteFetchFirstAsync_ReturnsFirstEntity` matches 2 rows and asserts `UserId == 1`.
  Same class of defect, same step-13 remediation.
- **Sort keys are seed-dependent in the `Select_Many_*` block** (SubqueryTests, 7 tests / 21
  sites): those projections omit `UserId`, so `UserName` is the only identifying column.
  Total for the current seed (Alice/Bob/Charlie distinct) but it would stop being total if a
  second "Alice" were ever seeded. Acceptable; noted so a future seed change knows to look.
- Inferred tuple element names work as sort keys — `(u.UserId, u.UserName)` admits
  `r => r.UserId`, matching the committed reference at `CrossDialectCteTests.cs:119`. The
  94-test green run is the proof: a wrong key would have reordered rows and failed the asserts.
- `SortedByAsync` needs no `using` — `Quarry.Tests.SqlOutput` is nested inside `Quarry.Tests`.
- Sweep did not regenerate `ManifestOutput` goldens (no new chains, only post-terminal calls).

### Step 10 (2026-08-03) — cancellation

- **Mid-stream cancellation is only observable when the provider awaits I/O.** With the three
  seeded rows, PostgreSQL delivers the whole result set in one response, so
  `while (await reader.ReadAsync(ct))` never awaits again and never sees the token — enumeration
  runs to completion after `Cancel()`. SQLite does surface OCE. Split accordingly: a
  connection-usability test across all four dialects (universal, and the leak guard), plus a
  strict OCE assertion on SQLite only, with the reason documented in the test. Making PG/MySQL/SS
  stream for real would need bulk inserts of thousands of rows via four dialect-specific
  statements — judged not worth it for insurance coverage. **Raise at REVIEW** as a known coverage
  limit.
- **Bite-verified.** Dropping the token (`ExecuteReaderAsync(behavior, CancellationToken.None)` and
  `ReadAsync(CancellationToken.None)` at `QueryExecutor.cs:32`, `:38`, `:314`) fails both
  `PreCancelledToken_EveryFetchTerminal_...` and `MidStreamCancellation_SurfacesOperationCanceled_...`.
- **Second generator constraint found (distinct from step 8): a *partial* chain passed as a method
  argument is not intercepted.** Handing `Lite.Users().OrderBy(...).Select(...)` to a helper that
  applies the terminal fails at **runtime** with
  `NotSupportedException: Entity accessor methods must be intercepted by the Quarry source generator` —
  no build-time diagnostic. The chain must terminate at the call site; pass the terminal's result
  (`IAsyncEnumerable<T>`, `Task<T>`) to helpers instead. This is why the cancellation helpers take
  an already-started operation rather than a builder.
- OCE propagates unwrapped by design: every executor's failure catch is filtered
  `when (ex is not OperationCanceledException)`. For a *pre-cancelled* token the throw comes from
  `ExecuteReaderAsync` — outside that try — so the filter itself is only load-bearing for
  cancellation observed mid-read.

### Step 9 (2026-08-03) — streaming disposal

- **Bite-verified.** Removing `await using` from the reader in both streaming overloads
  (`QueryExecutor.cs:311` and `:352`) makes `ToAsyncEnumerable_AbandonedAfterFirstRow_...` and
  `ToAsyncEnumerable_EnumeratorDisposedEarly_...` fail. The follow-up query on the same harness
  connection is what detects the leak, exactly as designed.
- **The rollback test does NOT detect a leak** — it still passed under the mutation, because the
  providers tolerate a rollback with a reader outstanding. Kept (the plan asked for it) but its
  doc comment now says plainly what it does and does not guard. Do not treat it as disposal
  coverage.
- Adding chains to the test project **regenerates the ManifestOutput goldens** — they must be
  committed with the change or the step-1 CI drift check fails.

### Step 8 (2026-08-03) — concurrency suite

- **New generator limitation found: chains inside doubly-nested lambdas fail to compile.**
  The natural way to write a parallel worker —
  `harnesses.Select((h, i) => Task.Run(async () => { var name = $"Worker{i}"; ... .Set(u => u.UserName = name) ... }))`
  — makes the generator emit interceptors that reference `name` / `threshold` directly, but those
  locals live in a display class the interceptor cannot see: **CS0103 "The name 'x' does not exist
  in the current context"** in the generated `*.Interceptors.*.g.cs` for all four contexts.
  Workaround used: each worker body is a named `private static async Task<T> Run...WorkerAsync(...)`
  method, so the chain's captures are ordinary method locals. Not yet isolated to a minimal repro
  (one lambda vs. two, `async` lambda vs. plain) — do that before filing; raise at REVIEW as a
  candidate follow-up issue.
- Harness facts that shaped the suite: `QueryTestHarness.SqlAsync`/`CreateSchema`/`SeedData` are
  **SQLite-only** — PG/MySQL/SQL Server use a pre-seeded shared baseline plus a per-harness
  transaction rolled back on dispose. So concurrent **writes** must stay on SQLite (private
  in-memory DB per harness); concurrent writes on the container dialects would contend on row
  locks in the shared baseline and produce timeouts rather than findings. Container dialects are
  exercised read-only.
- Harnesses are created **sequentially**, only the Quarry operations run in parallel — racing
  container first-call initialization would test the fixtures, not the library.
- Cost: 3 tests, 24 harnesses total, ~35s. Notable against a ~78s Quarry.Tests baseline; `Workers`
  is a single const if CI time needs trimming.

### Step 7 (2026-08-03) — #329 IS synthetically pinnable (corrects step 6)

- **Step 6's conclusion that entity-terminal shapes "emit CORRECT interceptors in isolation" is
  wrong.** Dumping the generated source for `db.Users().Where(...).ExecuteFetchAllAsync()` in an
  isolated `CSharpCompilation` shows the emitter produces
  `public static Task<List<User>> ExecuteFetchAllAsync_...(this IQueryBuilder<User, User> builder, ...)`
  while the preceding `Where_...` interceptor returns `IQueryBuilder<User>`. That is exactly the
  #329 mismatch — it just does not raise CS9144 in an isolated compilation, which is why step 6
  read it as correct. Same shape in the full test project *is* a CS9144 error (hence the
  `.Select(...)` workarounds). So #329 does have a compilable pin after all:
  `KnownBug_Issue329_EntityTerminal_EmitsTwoArityReceiver` asserts the two-arity receiver text
  and fails when the emitter is fixed.
- **Corollary — do not rely on synthetic CS9144 for terminal mismatches.** A hand-written
  `[InterceptsLocation]` interceptor with a deliberately wrong receiver arity also produces no
  CS9144 in an isolated compilation. The attribute *is* recognized (a garbage `data` argument
  yields CS9231, and a probe colliding with the generator's own interceptor yields CS9153), so
  the silence is the compiler's interceptor-matching rule for these shapes, not a broken harness.
  Assert on emitted text for terminal-receiver defects.
- **Guard-matrix bite-verification**: mutating the single decision point
  `CarrierEmitter.ResolveCarrierReceiverType` (`CarrierEmitter.cs:250`) to return the two-arity
  form makes the *whole project* fail to build with real CS9144 errors on clause interceptors
  (`Distinct()`, `Limit(int)`, `Union(...)`). That proves the matrix's CS9144/CS9177 assertion is
  not vacuous for clause shapes (the matrix includes `Distinct_FetchAll` / `Limit_FetchFirst` for
  this reason). It cannot be bite-verified end-to-end *inside* the synthetic harness, because the
  mutation breaks the Quarry.Tests build before any test runs. Revert such mutations with
  `git checkout --` (see the step-6 mtime gotcha below).
- Interceptors are emitted into the **context's own namespace**, so a synthetic compilation must
  enable every fixture context namespace via the `InterceptorsNamespaces` parse-option feature
  (`"TestApp;TestApp.Sub"`) or the compiler rejects the generated `[InterceptsLocation]`s.
- `IEntityAccessor<T>` exposes **no terminals of its own** — every chain must pass through one
  builder-returning method (`Where`/`OrderBy`/`Limit`/`Distinct`) before terminating. Relevant
  when constructing minimal entity-terminal fixtures.
- **Blanket `NoWarn` CS9177 removed** from `Quarry.Tests.csproj`, confirming the step-6 finding:
  a non-incremental build of Quarry.Tests without it reports 0 errors and zero CS9177/CS9144.

### Step 6 (2026-07-23) — major empirical corrections to finding 5
- **#328 misattribution is STALE**: split conditional-Having chain now binds to the correct context (probe on My rendered correct backtick SQL). Likely fixed by #307/#322. But a REAL bug remains in the same shape: **conditional Having is not mask-gated** — HAVING renders unconditionally (verified SQL + execution, all 4 dialects). #328 retitled to that defect; taken-branch regression test + untaken-branch active pin added to CrossDialectConditionalMaskTests. (Pin uses `int.Parse("0") == 1` for runtime-false to dodge constant-branch analysis.)
- **#329 is REAL but CS9144 (error), not CS9177 (warning)**: probe (removing .Select in PostgresIntegrationTests) → `CS9144: cannot intercept IQueryBuilder<Address>.ExecuteFetchFirstOrDefaultAsync with ...(IQueryBuilder<Address, Address>, ...)`. Identity-projection interceptor emitted for entity-terminal receiver. Workarounds are load-bearing (build error). All workaround comments now reference #329.
- **Blanket NoWarn CS9177 is VESTIGIAL**: full build with suppression overridden (`-p:NoWarn=NU1903`) → zero CS9177 anywhere. csproj comment mislabeled the diagnostic. To be removed in step 7 with the guard matrix (deviation from 'keep NoWarn' decision — evidence-based).
- **No synthetic repro for #329**: entity-terminal shapes (incl. sub-namespace cross-context, deconstructed harness receiver, captured awaited local, QUARRY_TRACE define) emit CORRECT interceptors in isolated CSharpCompilation. Mismatched identity-projection emission reproduced in isolation only under degraded semantics (missing metadata refs → TypeKind.Error fallback). Real-project trigger likely entity-type resolution degradation. Both issues commented with findings; KnownBugPinTests.cs (synthetic pins) deleted — pin for #328 lives in CrossDialectConditionalMaskTests instead; #329's signal = step-7 guard + documented probe.
- QRY033 gotcha (new): a chain consumed by both ToDiagnostics() and a terminal needs `.Prepare()` — "consumed by multiple execution paths" build error otherwise.
- Missing `System.ComponentModel.Primitives.dll` reference in synthetic compilations degrades semantics enough to flip generator classification (identity-projection fallback) — include it in codegen test references.

### Step 5 (2026-07-23)
- Filed **#328** (conditional-Having GroupBy split misattribution → wrong context/dialect) and **#329** (entity-terminal chains not intercepted, CS9177 arity mismatch, blanket NoWarn). Pins in step 6 reference these numbers.

### Discovered during step 3 (2026-07-23)
- **Real generator crash found by the fresh-tree unchanged-run test**: persistent driver + re-parsed identical text (compiler-server warm rebuild) → cached RawCallSite holds nodes of superseded trees; `DisplayClassEnricher.EnrichAll` (line 93) calls `compilation.GetSemanticModel(oldTree)` → ArgumentException → CS8785 → generator contributes NOTHING (all interceptors silently vanish). Not covered by issues 309/310/318 as such. **Decision 2026-07-23: fix inline** (user-approved) — recover equivalent node from current compilation by FilePath+span in EnrichAll; noted as plan deviation for review.
- Roslyn tracked-steps semantics learned: named nodes that are wholesale-skipped (inputs untouched) record NO steps — an absent stage in `TrackedSteps` is itself a cached signal. On "Unchanged", the driver KEEPS the previous output instance (this is what leaves stale tree references in cached sites).
- **#310 mutation defect empirically confirmed** via ModifyOneFile test: emission output action mutates cached `AssembledPlan.ReaderDelegateCode` (QuarryGenerator.cs:663) which participates in `AssembledPlan.Equals` (AssembledPlan.cs:284) → recomputed pristine group ≠ cached-then-mutated group → unchanged file's per-file group reports Modified instead of Cached. Pinned in test with #310 reference (text-identity assertions remain the hard guardrail).
- **#310 defect 1 (cross-partial ordinal shift → stale display-class name) reproduced and pinned**: incremental emission keeps `<>c__DisplayClass1_0` while a clean driver on identical final source emits `<>c__DisplayClass2_0`.
- CS8785 is Warning severity — health assertions must check for it explicitly, not just Severity.Error.

### Exploration facts (2026-07-22)

**F1 incremental caching (corroborated, with one correction):**
- Anonymous projections: `ProjectionAnalyzer.cs:216-219` marks failed (QRY014 reason); `ChainAnalyzer.cs:1315-1323` → `MakeRuntimeBuildChain` (tier RuntimeBuild, line 2913); emission skips RuntimeBuild plans (`QuarryGenerator.cs:694`) and reports QRY032 (:728-746) → hollow interceptor files. Supported shapes: DTO `new MyDto{...}`, tuple `(u.Id, u.Name)` (incl. named), single column, aggregates, entity `u => u` / no-Select identity.
- Pipeline wiring `QuarryGenerator.cs:60-233`; stages: context discovery → entity/context codegen (RegisterSourceOutput per-context) → EntityRegistry (Collect barrier) → call-site discovery → display-class enrichment → per-site bind → per-site translate → Stage-5 collected analysis + per-file grouping → per-file interceptor emission (RegisterImplementationSourceOutput) + manifest + migrate outputs. **Zero `WithTrackingName` calls repo-wide** — tests rely on unnamed TrackedOutputSteps.
- All 4 models hand-written `IEquatable<T>`: EntityRegistry (IR/EntityRegistry.cs:207-233), AssembledPlan (IR/AssembledPlan.cs:275-303), CarrierPlan (CodeGen/CarrierPlan.cs:90-110), FileInterceptorGroup (Models/FileInterceptorGroup.cs:49-67).
- **CORRECTION vs issue text:** `EntityRegistry.Equals` ALREADY compares `_allContexts` (bug fixed in current code). `_byEntityType` not compared but derived — benign. Negative tests = regression insurance, will pass. No llm.md post-mortem note about the old bug exists (issue implied one; absent).
- EntityRegistryTests.cs:68-76 is the only equality test (positive only). No AssembledPlan/CarrierPlan/FileInterceptorGroup test files at all. Repo DOES have negative-equality precedent for CallSite, QueryPlan, SqlExpr, CarrierStructuralKey, etc.
- File "hash" is actually a path-derived tag (`FileHasher.ComputeFileTag`, Utilities/FileHasher.cs:17-50 — sanitizer, not digest). Output filename `{Context}.Interceptors.{FileTag}.g.cs` (QuarryGenerator.cs:798).
- Schema edits flow through a distinct output (RegisterSourceOutput per-context, line 72-73) but EntityRegistry's Collect barrier means a schema edit invalidates ALL interceptor analysis.
- Valid fixture shapes (from CarrierGenerationTests): named tuple `Select(u => (Id: u.UserId, Name: u.UserName))` asserted non-hollow at lines 1953-1962; single column; entity identity.

**F4 row-order sweep (corroborated, refined numbers):**
- Naming convention: `<lt|pg|my|ss><Noun>` lists; SQLite (`lt*`) intentionally positional as reference shape. Total PG/My/Ss positional index accesses: 1019 across 26 files (430 non-zero index = unambiguously order-sensitive; 592 `[0]`). Top files: CrossDialectSelectTests 225, SubqueryTests 141, JoinTests 105, WideTupleTests 69, WhereTests 66, CteTests 60.
- `SortedByAsync` real call sites: 29, in 4 files only (CteTests 18, NavigationJoinTests 5, SelectTests 3, SchemaTests 3).
- ~60-70% (~330-370) of positional assertions are genuinely order-sensitive multi-row without ORDER BY. Skip: single-row `Count==1` (~280 count-assert sites), tests already carrying SQL ORDER BY (OrderByTests/DistinctOrderByTests — re-sorting in C# would MASK a dropped ORDER BY regression), predicate-based `.First(pred)`/`.Single(pred)`.
- Sub-patterns needing judgment: sort-key selection per projection (Item1 vs named field vs entity key); bare `.First()` on multi-row is order-sensitive but not List-shaped; `ExecuteFetchFirstAsync` on multi-row match without ORDER BY can't be fixed by SortedByAsync (needs query-side ORDER BY) — 130 occurrences of First/Single/Scalar terminals across 11 files.
- Distinct fetch sites needing conversion is materially lower than 526 (multi-field asserts inflate raw counts 2-3× per row). Regex-only rewrite unsafe; semi-automated sweep + per-site key inference is the way.

**F5 pinned bugs / CS9177 (corroborated, with corrections):**
- Bug A — conditional-Having context misattribution: NOT at CrossDialectConditionalMaskTests.cs:386-390; the actual note is a trailing NOTE at lines 1170-1174. A `GroupBy` chain split across a reassigned local then conditionally extended with `.Having(...)` loses the chain-root context type; with two contexts exposing `IEntityAccessor<Order> Orders()` (CteDb + TestDbContext) it binds to the wrong one. Handled by omitting the test entirely. No issue ID exists ("Filed as follow-up" cites nothing).
- Bug B — entity-terminal interceptor signature mismatch: chains terminating on `IQueryBuilder<T>` (no explicit `.Select`) generate an interceptor whose arity/signature doesn't match → not intercepted (CS9177 arity / CS9144 signature family). Worked around by always adding explicit `.Select(...)`; comment duplicated in PostgresIntegrationTests.cs:45-48, MySqlIntegrationTests.cs:63-66, SqlServerIntegrationTests.cs:42-45 + shortened variants in InsertBatch tests. No issue ID.
- CS9177 = interceptor generic-arity mismatch (combined arity of generic method on generic receiver, e.g. `ExecuteScalarAsync<TKey>` on `IInsertBuilder<T>` needs `<T, TKey>`). Generator commentary: TerminalBodyEmitter.cs:362-372, 465-466; JoinBodyEmitter.cs:303. CS9144 is the signature-mismatch cousin (CallSiteBinder.cs:93, DiagnosticDescriptors.cs:682).
- The blanket NoWarn CS9177 is unique to Quarry.Tests.csproj — no other project (incl. Samples using interceptors) suppresses it.
- Repo has NO existing bug-pinning convention: zero `[Ignore(...)]` attributes; all `Assert.Ignore` uses are Docker-unavailability only.

**F7 streaming/cancellation (corroborated):**
- `CrossDialectStreamingTests.cs` — 3 tests, all on `ToAsyncEnumerable`. Only `ToAsyncEnumerable_BreakAfterFirst_YieldsOrderedFirstRow` breaks early, and its own doc comment admits it doesn't prove streaming or disposal.
- Streaming impl: `QueryExecutor.cs:298/338` (`ToCarrierAsyncEnumerableWithCommandAsync`, delegate + struct-reader variants). Disposal on early break relies on `await using` of command (line 304) and reader (line 311) inside the iterator; `FinalizeQuery` only runs on natural completion. Untested.
- ALL terminals (`ExecuteFetchAllAsync/FirstAsync/FirstOrDefaultAsync/SingleAsync/SingleOrDefaultAsync/ScalarAsync/NonQueryAsync/ToAsyncEnumerable` + 3 RawSql terminals) accept CancellationToken; NO runtime cancellation test exists anywhere. All executors rethrow-filter `ex is not OperationCanceledException` — untested.
- Harness: one long-lived connection per dialect per harness; a leaked reader poisons subsequent commands on that connection (MySqlConnector forbids second command with open reader; SS needs MARS). Rollback in `DisposeAsync` could also be affected. So the natural disposal test is: early-break, then run another query on the same harness connection and assert success.
- Existing CT mentions in tests are all generator-signature detection (`HasCancellationToken`) or `CancellationToken.None` placeholders — no runtime cancellation.

## Suspend State
- **Position**: IMPLEMENT, plan steps 1–12 and 14 of 15 complete, committed and pushed
  (last commit `bcf2d54`). **Remaining: step 13, then step 15.**
- **In progress**: nothing mid-flight; working tree clean.
- **Test status**: all green — full `dotnet test Quarry.sln` with Docker available after step 12:
  Quarry.Tests 3484, Migration.Tests 201, Analyzers.Tests 146. No pre-existing failures.
- **WIP commit**: none.

### Immediate next step — step 13 (row-order sweep, remaining files + query-side fixes)

Two distinct halves; do them as two commits.

**13a — remaining file sweep.** Same delegated procedure as steps 11–12. The rules doc lives at
`scratchpad/sweep-rules.md` (regenerate it from the step-11/12 Working Notes if the scratchpad is
gone — it must include the "Lessons from the first file group" section, which is what stopped the
second pass repeating the JoinTests mistake). Inventory of remaining files, pg/my/ss FetchAll
sites vs. non-zero positional accesses:

| File | FetchAll | `[1+]` | File | FetchAll | `[1+]` |
|---|---:|---:|---|---:|---:|
| StringOpTests | 63 | 0 | CompositionTests | 60 | 9 |
| WindowFunctionTests | 36 | 15 | ComplexTests | 27 | 0 |
| ConditionalMaskTests | 27 | 0 | NullableValueTests | 21 | 13 |
| AggregateTests | 21 | 0 | EntityReaderTests | 21 | 0 |
| NestedSubqueryTests | 20 | 12 | MiscTests | 14 | 0 |
| OrderByTests | 12 | 24 | EnumTests | 12 | 0 |
| TypeMappingTests | 12 | 0 | DistinctOrderByTests | 9 | 0 |
| JoinNullableProjectionTests | 9 | 0 | DeleteTests | 6 | 0 |
| HasManyThroughTests | 6 | 5 | StreamingTests | 6 | 0 |
| PrepareTests | 6 | 3 | NavigationJoinTests | 3 | 7 |
| FkKeyProjectionTests | 3 | 9 | | | |

Expect `OrderByTests` / `DistinctOrderByTests` to be all-skip (a top-level ORDER BY is the thing
they pin). Real work concentrates in Composition, WindowFunction, NullableValue, NestedSubquery.
Also normalize the ad-hoc idiom at `FkKeyProjectionTests.cs:63,68,73,116,120,124`
(`(await q.ExecuteFetchAllAsync()).OrderBy(k).ToList()` → `SortedByAsync`), real-provider sides only.

**13b — query-side remediation** for the sites sorting cannot reach (all identified, all in
already-swept files; details in the step-11/12 Working Notes):
1. Nine `CrossDialectJoinTests` tests that encode `orders.OrderId` order through an unprojected
   column — needs the join key projected + ordered, or `Is.EquivalentTo`.
2. Four `CrossDialectSelectTests` pagination tests doing `LIMIT 2 OFFSET 1` with no ORDER BY.
3. `NoSelect_ExecuteFetchFirstAsync_ReturnsFirstEntity` — `First` over a 2-row predicate.
4. `Where_CollectionPlusScalar_WithPagination_ReturnsCorrectRows` — `Limit(1)`, no ORDER BY.
Per plan: add query-side ORDER BY where the SQL assertion permits (update expected SQL
accordingly); where it does not permit, leave it and note it in review.

### Then — step 15
Full `dotnet test Quarry.sln`; update `llm-testing.md` (SortedByAsync as the default pattern,
concurrency/streaming/cancellation suites, bug-pin convention, manifest CI enforcement) and
`src/Quarry.Generator/llm.md` (tracking names). → REVIEW.

### Carry-forward context
- Adding chains regenerates `ManifestOutput` goldens — commit them or the step-1 CI drift check
  fails. (The row-order sweep does *not* regenerate them: it only appends post-terminal calls.)
- **Raise at REVIEW** — candidate follow-up issue: chains inside doubly-nested lambdas emit
  uncompilable interceptors (CS0103), not yet isolated to a minimal repro (step-8 Working Notes).
- **Raise at REVIEW** — known coverage limit: mid-stream cancellation OCE asserted on SQLite only
  (step-10 Working Notes).
- **Raise at REVIEW** — coverage gap, not a row-order defect: several tests assert only
  `Has.Count` on the pg/my/ss sides while the SQLite side asserts values
  (`Join_FiveTable_Select`, and ~11 tests in `CrossDialectSetOperationTests`). Immune to order
  flake but blind to wrong-rows regressions. Candidate separate issue.
- Step-14 gate is committed but only dry-run verified; its first real exercise is post-merge.
- **Unrecorded context**: none — everything is in Working Notes.
- **Suspend trigger**: IMPLEMENT context check (≥3 steps completed this session — 11, 14, 12).

## Session Log
| Date | Phases | Summary |
|------|--------|---------|
| 2026-07-22 | INTAKE | Loaded issue #314, created worktree `314-test-suite-guardrails`, baseline test run started. |
| 2026-07-22 | DESIGN | 4-agent exploration of all findings; baseline green (3771 tests). Scope/gate/sweep/CS9177 decisions recorded; design approved. |
| 2026-07-23 | PLAN→IMPLEMENT | 15-step plan.md approved; implementation started. |
| 2026-07-23 | IMPLEMENT | Steps 1–3 done (manifest CI check; tracking names; caching-test rewrite + inline DisplayClassEnricher stale-tree crash fix + two #310 pins). Suspended per ≥3-step context check; branch pushed. |
| 2026-07-23 | IMPLEMENT | Resumed same-session (baseline still green from pre-suspend full run); continuing at step 4. |
| 2026-07-23 | IMPLEMENT | Steps 4–6 done (pipeline-model equality tests; issues #328/#329 filed; conditional-Having coverage + #328 pin — misattribution found stale, real defect is unmasked Having; #329 confirmed as CS9144; NoWarn CS9177 found vestigial). Suspended per ≥3-step check; branch pushed. |
| 2026-08-03 | IMPLEMENT (resumed) | Resumed from suspend at step 7/15. Re-ran full baseline before continuing (3438/201/146 green). |
| 2026-08-03 | IMPLEMENT | Steps 7–9 done. 7: interceptor-binding guard matrix + vestigial NoWarn CS9177 removed + #329 pin recovered (corrects step 6). 8: concurrency suite; found chains in doubly-nested lambdas emit uncompilable interceptors. 9: streaming abandonment disposal tests, bite-verified. Suite 3480/201/146 green. Suspended per ≥3-step check; branch pushed. |
| 2026-08-03 | IMPLEMENT | Step 10 done (runtime cancellation coverage, commit `1aae06a`) — session ended without a suspend write. |
| 2026-08-03 | IMPLEMENT (resumed) | New session, resumed at step 11/15. Stale Suspend State cleared; full baseline re-run (3484/201/146 green). |
| 2026-08-03 | IMPLEMENT | Steps 11, 14, 12 done. 11: 87 sites swept in Select/Subquery/Join; found two clusters sorting cannot fix (9 JoinTests encoding order via an unprojected column, 4 LIMIT/OFFSET pagination tests) → deferred to 13b. 14: alert-only benchmark regression gate, jq dry-run verified. 12: 27 sites in Where/Cte/WideTuple; WideTuple and SetOperation needed nothing. Suite 3484/201/146 green. Suspended per ≥3-step check; branch pushed. |
