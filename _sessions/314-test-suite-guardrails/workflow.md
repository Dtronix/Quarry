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

## Working Notes

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

## Session Log
| Date | Phases | Summary |
|------|--------|---------|
| 2026-07-22 | INTAKE | Loaded issue #314, created worktree `314-test-suite-guardrails`, baseline test run started. |
| 2026-07-22 | DESIGN | 4-agent exploration of all findings; baseline green (3771 tests). Scope/gate/sweep/CS9177 decisions recorded; design approved. |
| 2026-07-23 | PLAN→IMPLEMENT | 15-step plan.md approved; implementation started. |
