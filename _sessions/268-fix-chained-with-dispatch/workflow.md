# Workflow: 268-fix-chained-with-dispatch

## Config
platform: github
remote: https://github.com/Dtronix/Quarry.git
base-branch: master

## State
phase: REMEDIATE
status: active
issue: #268
pr: #272
session: 1
phases-total: 3
phases-complete: 3

## Problem Statement
Source generator: chained-`With<>` dispatch resolves the wrong closure-field extractor when two `.With<>(...)` lambdas in the same compilation unit have the same closure structural shape but capture differently-named variables. The generator merges call sites by structural shape and uses the first encountered variable name as the canonical extractor field, so one call site is dispatched to a Chain_N whose `__ExtractVar_*` reads a field that does not exist on the actual closure target type — `MissingFieldException` at `.Prepare()`.

Observed concretely in `Cte_TwoChainedWiths_DistinctDtos_CapturedParams` (CrossDialectCteTests.cs:216): closure had `orderCutoff`+`activeFilter`, generator emitted Chain_3 with extractors for `cutoff`+`activeFilter` (names borrowed from `Cte_FromCte_CapturedParam` at line 74). The 2-chained-With case is what broke — the 3-chained-With variant (`Cte_ThreeChainedWiths_AllUsedDownstream`) routes correctly, suggesting the dispatch-key collision is specific to the 2-With chain hash.

PR #266 worked around this by renaming the captured variable in the failing test from `orderCutoff` to `cutoff`. The fix here is to make Chain_N dispatch closure-target-type-aware (key by closure type identity / actual field set rather than name-canonicalized structural shape) and revert the test rename.

### Test Baseline (pre-existing failures excluded from "all green" gate)
- `RunAsync_InsertsHistoryRow_OnPostgreSQL` (PostgresMigrationRunnerTests:46) — TearDown NullReferenceException, environment issue (Docker not available locally; setup didn't catch cleanly).
- 506 tests skipped/NotExecuted — `DockerUnavailableException`. PG-backed tests cannot run without Docker on this machine.
- All other tests pass (Quarry.Tests: 2489 passed; Analyzers.Tests: 117 passed; Migration.Tests: 201 passed).

## Decisions
- **2026-04-25 — Approach: regression tests + dispatch hardening, no live repro available.** Reverting the test rename locally (`cutoff` → `orderCutoff`) and rebuilding produces correct generator output: Chain_3 emits `__ExtractVar_orderCutoff_0` + `__ExtractVar_activeFilter_1` on `<>c__DisplayClass3_0`, Chain_4 emits its own three extractors on `<>c__DisplayClass4_0`, and each chain's With_X interceptor reads only its own variables. `CarrierStructuralKey` (FileEmitter.cs:917) already keys on `_extractors` via `EqualityHelpers.SequenceEqual`, and `CapturedVariableExtractor.Equals` checks `VariableName + DisplayClassName + VariableType + CaptureKind + IsStaticField + MethodName`, so dedup cannot merge two carriers whose closure types or variable names differ. The historic MissingFieldException from PR #266's compilation state is not reproducible against current main. User authorized treating the bug as latent: ship regression tests covering the failure pattern + audit dispatch path for any name-canonicalization, plus revert the workaround so the descriptive `orderCutoff` name returns.

- **2026-04-25 — Audit result: no name canonicalization on the chain-dispatch path.** Traced UsageSiteDiscovery → CallSiteTranslator → ChainAnalyzer → CarrierAnalyzer + per-clause `BuildExtractionPlans`. Each captured-variable extractor is built from its own `cs.DisplayClassName` and `p.CapturedFieldName` per clause site (CarrierAnalyzer.cs:359, 506). The only `CapturedFieldName`-keyed lookup is `displayClassByParam` (CarrierAnalyzer.cs:88-121), and it is consumed only as a type hint (line 173) — it does not feed extractor MethodName or DisplayClassName. CTE inner-extraction matches `CteDef` by entity name (CarrierAnalyzer.cs:444) but ChainAnalyzer rejects within-chain duplicates (QRY082) — cross-chain reuse is not possible because each chain owns its own `CteDefinitions`. CallSiteTranslator preserves `CapturedFieldName` on remap (line 380). No first-name-wins logic found in VariableTracer, ProjectionAnalyzer, or InterceptorRouter. Conclusion: the dispatch path is correct; the historic bug from PR #266 was either fixed incidentally by later changes or required a compilation state we no longer have. Hardening will add explicit `CarrierStructuralKey` unit tests asserting non-merge across name/display-class differences plus a full-pipeline regression test for chained-With with shape collisions, and will tighten the comment on `CarrierStructuralKey` so future contributors do not relax it to types-only dedup.

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE     | IMPLEMENT | Loaded issue #268, created worktree. INTAKE baseline: 1 env-related TearDown failure + 506 Docker-skipped (env-only — Docker unavailable on dev box). DESIGN traced full dispatch path; bug latent in current main. PLAN approved 4 items / 3 commits. IMPLEMENT shipped Phase 1 (CarrierStructuralKey internal + 3 unit tests + doc), Phase 2 (2 generator-pipeline regression tests), Phase 3 (workaround revert). All 3001+117+201 tests green at end of IMPLEMENT — Docker-dependent tests now actually executing on this run. |
