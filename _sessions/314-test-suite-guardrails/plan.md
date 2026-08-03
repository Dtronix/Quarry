# Plan: 314-test-suite-guardrails

Implements issue #314 (all 7 findings; the two low items are deferred per Decisions). Each step is independently committable and leaves the suite green. Baseline: 3771 tests, all passing.

## Key concepts

- **Hollow interceptors**: anonymous-type projections are classified failed (`ProjectionAnalyzer.cs:216`), converted to `OptimizationTier.RuntimeBuild` (`ChainAnalyzer.cs:1315`), skipped at emission (`QuarryGenerator.cs:694`) with QRY032. Valid shapes: named tuple, DTO, single column, entity identity.
- **Tracking names**: Roslyn's `IncrementalValueProvider<T>.WithTrackingName(name)` labels a pipeline node so `GeneratorRunResult.TrackedSteps[name]` exposes per-node run reasons (`New/Modified/Cached/Unchanged`). The generator currently has zero — tests can only see unnamed output steps.
- **Fresh-tree re-parse**: `CSharpSyntaxTree.ParseText` of identical text produces reference-distinct trees, forcing the incremental driver to actually invoke model `.Equals` instead of short-circuiting on reference equality.
- **Bug-pin convention** (new to this repo): an *active* test that asserts the current buggy behavior, named `KnownBug_Issue{N}_...`, with a comment stating "when this test fails, the bug is fixed — remove the workaround and this pin". It signals via failure exactly when the bug is fixed. Where the buggy behavior can't be asserted stably, fall back to `[Ignore("pinned: #{N}")]` on a test asserting the correct behavior.
- **Row-order sweep rules**: convert multi-row positional asserts on `pg*/my*/ss*` lists to `.SortedByAsync(key)`. Skip: single-row (`Count==1`) sites; tests whose SQL already has top-level ORDER BY (re-sorting would mask a dropped-ORDER-BY regression); predicate `.First(pred)/.Single(pred)`. Sort key comes from the projection's first stable identifying column (entity key or first tuple item); each file's keys reviewed on main context before commit.

## Steps

- [x] **1. F6 — Manifest golden drift check in CI**
  Add a step to `.github/workflows/ci.yml` after `Test`: `git diff --exit-code -- src/Quarry.Tests/ManifestOutput` (build regenerates goldens in place via `QuarrySqlManifestPath`). Also run a local build + diff to prove the current goldens are clean.
  Tests: none added; full suite must stay green. Verify the step logic locally with an intentional golden touch (not committed).

- [x] **2. F1a — Add `WithTrackingName` to generator pipeline stages**
  In `QuarryGenerator.cs` `Initialize`, name the load-bearing nodes: context declarations, entity registry, raw call sites, enriched sites, bound sites, translated sites, per-file groups (and the manifest node). Names as constants (e.g. `TrackingNames` static class in the generator) so tests reference them typed, not stringly.
  Tests: existing suite green (names are inert metadata). No new tests yet — consumed in step 3.
  Depends on: nothing.

- [x] **3. F1b — Rewrite `IncrementalCachingTests`** *(deviation: includes user-approved inline fix of a generator crash found by the new unchanged-run test — DisplayClassEnricher stale-tree recovery; plus two #310 pins instead of correct-behavior assertions, see Working Notes)*
  - Fixtures: named-tuple / single-column projections; add an assertion helper that the run produced no QRY014/QRY032 and that each interceptor file contains a real body (`Does.Contain("file sealed class Chain_")` / SQL text), killing the hollow-shell failure mode.
  - `PerFileOutput_UnchangedCompilation_AllOutputsCached`: build the second compilation from **freshly parsed trees** of identical text; assert all tracked stages report Cached/Unchanged **per stage name** from step 2.
  - `PerFileOutput_ModifyOneFile_OtherFileCached`: assert the unchanged file's per-file group output is specifically Cached and the modified file's is Modified/New (keyed by FileTag in the output name).
  - `PerFileOutput_ModifyQuery_RegeneratesAffectedFile`: actually compare captured before/after interceptor text — changed file's text differs, unchanged file's text identical.
  - New: schema-only edit test — editing the Schema class regenerates entity output and invalidates interceptor analysis (registry barrier) while a no-op whitespace edit to an unrelated file leaves everything cached.
  - New: cross-partial ordinal-shift scenario from the issue (two partial-context files where an edit shifts interceptor ordinals) — investigate exact shape during implementation; if it proves to belong to the display-class issue, record that in Working Notes and drop with a note in review.
  Tests: the rewritten fixture itself. Full suite green.
  Depends on: step 2.

- [x] **4. F1c — Negative equality tests for pipeline models**
  New `IR/PipelineModelEqualityTests.cs` (or extend `EntityRegistryTests`): for `EntityRegistry`, `AssembledPlan`, `CarrierPlan`, `FileInterceptorGroup` — inequality when each constituent differs (esp. `EntityRegistry` with a different `_allContexts` set — regression pin for the previously-shipped bug), plus hash-consistency checks. Follow the existing negative-test precedent (`CarrierStructuralKey` etc.).
  Tests: the new file. Depends on: nothing.

- [x] **5. F5a — File tracking issues for the two routed-around bugs** *(→ #328, #329)*
  Two `gh issue create` calls with full Issue Body template: (a) conditional-Having GroupBy/Having variable-split context misattribution (`CrossDialectConditionalMaskTests.cs:1170-1174` NOTE); (b) entity-terminal interceptor signature/arity mismatch (comments in 3 integration suites; cause of blanket CS9177 NoWarn). Record issue numbers in workflow.md.
  Tests: none. Depends on: nothing.

- [x] **6. F5b — Pinning tests for both bugs** *(major deviation, see Working Notes: #328's misattribution was stale — replaced by taken-branch regression test + not-mask-gated pin in the real suite; #329 pin not compilable (build error) — signal moves to step-7 guard + issue-documented probe)*
  - Bug (a): active pin — build the conditional-Having split chain in a codegen-only test, assert the misattributed binding (current buggy output); failure ⇒ bug fixed.
  - Bug (b): active pin — codegen test on an entity-terminal chain asserting the call is currently NOT intercepted (CS9177 present / no matching InterceptsLocation); failure ⇒ fixed. Update the three integration-suite comments to reference the issue numbers.
  Tests: the pins themselves. Depends on: step 5 (issue numbers).

- [x] **7. F5c — CS9177 guard test** *(deviations: the csproj NoWarn was removed rather than kept — it was vestigial, see step-6 Working Notes; and the "exact expected set of CS9177" is empty, so the guard asserts zero CS9144/CS9177 across a shape matrix plus proof that an interceptor was actually emitted. Additionally recovered a compilable #329 pin — see step-7 Working Notes)*
  New `Generation/InterceptorBindingGuardTests.cs`: 18 chain shapes (entity terminals, generic terminals on generic receivers, modification terminals) each compiled in its own synthetic compilation with interceptors enabled, asserting no CS8785/CS9144/CS9177 and that the terminal's interceptor was emitted; the 9 entity-terminal shapes re-run against a cross-namespace second context. Plus `KnownBug_Issue329_EntityTerminal_EmitsTwoArityReceiver` pinning the mismatched receiver the emitter currently produces.
  Tests: the guard test. Depends on: step 6 (shares fixture shapes; can merge into one commit with 6 if natural).

- [x] **8. F3 — Concurrency suite** *(placed in `Integration/` — the tests assert execution behaviour, not SQL text; writes are SQLite-only because the container dialects share one baseline schema, see Working Notes)*
  New `Integration/ConcurrencyTests.cs`: (a) 8 parallel harnesses running mixed SELECT/UPDATE/Patch with per-worker parameter values, asserting no worker reads back another's parameter; (b) barrier-synchronized first touch of one shared carrier chain across 8 workers; (c) 8 parallel contexts on separate connections querying all four dialects read-only. Worker bodies are named methods — chains in doubly-nested lambdas do not compile (Working Notes).
  Tests: the new suite; full suite green. Depends on: nothing.

- [x] **9. F7a — Streaming early-break disposal tests** *(bite-verified by removing `await using` from the reader in both streaming overloads: the two follow-up-query tests fail, the rollback test does not — see Working Notes)*
  Extend `CrossDialectStreamingTests`: early-`break` after first row, then issue a follow-up query **on the same harness connection** and assert success on all 4 dialects (leaked reader ⇒ MySqlConnector/SqlClient failure); also `await using` enumerator explicit-dispose variant, and dispose-mid-enumeration followed by harness rollback (dispose path already exercised by harness teardown — assert no throw).
  Tests: new tests. Depends on: nothing.

- [x] **10. F7b — Cancellation tests** *(deviation: mid-stream OCE is asserted on SQLite only — a provider that has buffered the result set never awaits I/O again and so never observes the token, so the all-dialect test asserts connection usability instead. Bite-verified by dropping the token in the executor. See Working Notes)*
  New tests: (a) pre-cancelled token into each fetch terminal (`FetchAll/First/FirstOrDefault/Single/SingleOrDefault/Scalar/NonQuery`) ⇒ `OperationCanceledException`, connection remains usable; (b) mid-stream cancellation of `ToAsyncEnumerable` via token cancelled after first `MoveNextAsync` ⇒ OCE and subsequent command on same connection succeeds; (c) raw-SQL streaming overload with token. Keep per-dialect where the harness makes it cheap; SQLite + PG minimum, all 4 where stable.
  Tests: new tests. Depends on: step 9 (same file/patterns).

- [x] **11. F4a — Row-order sweep: CrossDialectSelectTests, CrossDialectSubqueryTests, CrossDialectJoinTests** *(deviation: 87 sites across 29 tests converted, not ~471 — the raw access count is inflated by multi-field asserts and by pg/my/ss sides that only assert `Has.Count`. JoinTests is largely NOT sortable: 9 tests encode `orders.OrderId` order via an unprojected column, and the 4 LIMIT/OFFSET pagination tests are nondeterministic in row *selection*. Both clusters deferred to step 13 as query-side fixes — see Working Notes)*
  Delegated mechanical conversion per sweep rules (~471 raw accesses); sort keys reviewed on main context. Commit per this file group.
  Tests: converted files re-run green (`--filter` per file), then full suite at step end.

- [ ] **12. F4b — Row-order sweep: CrossDialectWideTupleTests, CrossDialectWhereTests, CrossDialectCteTests, CrossDialectSetOperationTests**
  Same procedure (~240 accesses; CteTests partially converted already).

- [ ] **13. F4c — Row-order sweep: remaining ~17 files + First/FetchFirst ORDER BY remediation**
  Remaining files per sweep rules. Then bare `.First()` / `ExecuteFetchFirstAsync`-on-multi-row sites: add query-side `ORDER BY` where the SQL assertion permits (update expected SQL accordingly); where it doesn't permit, leave and note in review.

- [x] **14. F2 — Benchmark regression gate (alert-only) in `benchmark.yml`** *(deviation: issue lookup/creation uses `curl` + REST rather than `gh`, which is not guaranteed on the self-hosted benchmark runner; the gate runs before the publish steps so a crashed run never becomes the stored baseline)*
  After the merge step: jq-compare current combined results vs previous data.js entry per `Quarry_*` series — mean regression >15% or any `allocated` increase ⇒ collect breaches; if any, `gh issue create` (label `performance-regression`, body listing series/old/new/delta; dedupe: skip if an open issue with the same title exists). Separately: expected-series check — every series present in the previous entry must exist in the current results with non-null `Statistics`, else **fail the workflow** (replaces silent `select(.Statistics != null)` drop). Needs `issues: write` permission and jq logic only — no compiled tooling.
  Tests: none runnable locally beyond `jq` dry-runs against committed artifact samples + `act`-style reasoning; validated post-merge. Verify yml with `gh workflow view`/yaml lint.

- [ ] **15. Final pass — full suite, docs touch-ups**
  Full `dotnet test Quarry.sln`; update `llm-testing.md` (SortedByAsync now the default pattern; concurrency/streaming/cancellation suites; bug-pin convention; manifest CI enforcement) and `src/Quarry.Generator/llm.md` (tracking names). → REVIEW.

## Step dependencies
2→3; 5→6→7; 9→10; 11/12/13 independent of others; 1, 4, 8, 14 free-floating. Sequence as numbered for minimal churn.
