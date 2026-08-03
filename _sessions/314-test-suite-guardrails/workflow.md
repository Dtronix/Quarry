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
- **Position**: IMPLEMENT, plan steps 1–6 of 15 complete and committed (last commit ad7ee18). Next: step 7 (F5c — interceptor-binding guard matrix).
- **In progress**: nothing mid-flight; working tree clean.
- **Immediate next step — step 7 specifics (revised by step-6 findings, see Decisions)**:
  1. REMOVE the vestigial `<NoWarn>CS9177</NoWarn>` from `Quarry.Tests.csproj:13-14` (verified: zero CS9177 in full build with it overridden).
  2. New `Generation/InterceptorBindingGuardTests.cs`: synthetic-compilation matrix over entity-terminal shapes (First/FirstOrDefault/Single/All/NonQuery-style terminals × with/without Where × root-context and sub-namespace cross-context) asserting NO CS9144/CS9177 in the output compilation (all currently pass in isolation — guards regressions and future arity mismatches). Reuse the reference list + fixture pattern from PipelineModelEqualityTests; MUST include `System.ComponentModel.Primitives.dll` (see Working Notes — its absence flips generator classification).
  3. Full build check: `dotnet build src/Quarry.Tests -p:NoWarn=NU1903` should stay CS9177/CS9144-free.
- **Then**: step 8 (concurrency suite), 9/10 (streaming/cancellation), 11–13 (row-order sweep), 14 (benchmark gate), 15 (docs+final).
- **WIP commit**: none (all work committed).
- **Test status**: all green — Quarry.Tests 3438 (full run after step 6), Migration.Tests 201, Analyzers.Tests 146 (after step 3; untouched since).
- **Unrecorded context**: none — all discoveries in Working Notes (step-6 block is essential reading: #328 retitled, #329 CS9144 facts, vestigial NoWarn evidence). For step 14: previous benchmark entry lives in Quarry-benchmarks repo gh-pages `dev/bench/data.js`; `dev/bench/runs/runs.json` is the manifest. For steps 11–13: sweep rules in plan.md Key concepts; sort keys reviewed on main context before commit.
- **Suspend trigger**: IMPLEMENT context check (≥3 steps completed this session — steps 4, 5, 6).

## Session Log
| Date | Phases | Summary |
|------|--------|---------|
| 2026-07-22 | INTAKE | Loaded issue #314, created worktree `314-test-suite-guardrails`, baseline test run started. |
| 2026-07-22 | DESIGN | 4-agent exploration of all findings; baseline green (3771 tests). Scope/gate/sweep/CS9177 decisions recorded; design approved. |
| 2026-07-23 | PLAN→IMPLEMENT | 15-step plan.md approved; implementation started. |
| 2026-07-23 | IMPLEMENT | Steps 1–3 done (manifest CI check; tracking names; caching-test rewrite + inline DisplayClassEnricher stale-tree crash fix + two #310 pins). Suspended per ≥3-step context check; branch pushed. |
| 2026-07-23 | IMPLEMENT | Resumed same-session (baseline still green from pre-suspend full run); continuing at step 4. |
| 2026-07-23 | IMPLEMENT | Steps 4–6 done (pipeline-model equality tests; issues #328/#329 filed; conditional-Having coverage + #328 pin — misattribution found stale, real defect is unmasked Having; #329 confirmed as CS9144; NoWarn CS9177 found vestigial). Suspended per ≥3-step check; branch pushed. |
| 2026-08-03 | IMPLEMENT (resumed) | Resumed from suspend at step 7/15. Re-ran full baseline before continuing. |
