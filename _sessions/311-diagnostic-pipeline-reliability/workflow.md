# Workflow: 311-diagnostic-pipeline-reliability

## Config
platform: github
base-branch: master

## State
phase: IMPLEMENT
status: active
issue: #311
pr:

## Problem Statement
Issue #311 — Diagnostic pipeline reliability. Combined finding from the 2026-07-07 multi-agent deep review. Four items sharing one root theme: error/trace side-channels and the deferred-diagnostic registry can silently lose diagnostics.

1. **(High)** Bind-stage QRY900 errors drained and discarded: `PipelineOrchestrator.AnalyzeAndGroupTranslated` calls `PipelineErrorBag.DrainErrors()` at entry (PipelineOrchestrator.cs:43), wiping the current compilation's bind errors before the reporting drain at QuarryGenerator.cs:543. Bind exceptions lose their compile-time diagnostic and surface as confusing runtime `InvalidOperationException`.
2. **(High)** Deferred-diagnostic silent drop, 3rd occurrence: ChainAnalyzer.cs:1840-1846 emits QRY900 via deferred `DiagnosticInfo`, but `InternalError` is not in `s_deferredDescriptors` (QuarryGenerator.cs:804-834); miss path is `continue` (:555) — silently dropped. Structural fix wanted: register it, fail loud on unregistered IDs, add registry-membership test.
3. **(Medium)** ThreadStatic side-channels vs incremental caching/cancellation: TraceCapture cleared at orchestrator entry loses `// [Trace]` comments for cached sites on warm runs; `ConsumedLambdaInnerSiteIds` cleared only after use — cancellation between populate and clear poisons next run's site filter; single-threaded-per-compilation assumption undocumented.
4. **(Low)** Stale diagnostic text referencing removed runtime-fallback model (QRY001/QRY019 sweep).

Acceptance criteria:
- [ ] Forced Bind-stage exception produces a QRY900 compile diagnostic (test).
- [ ] Every `DiagnosticInfo` ID emission is registry-checked by a test; unregistered IDs fail loudly at runtime in DEBUG.
- [ ] `.Trace()` comments survive incremental runs (test with warm driver + unchanged traced chain).
- [ ] Cancellation mid-analysis cannot poison the next run's site filtering.

Test baseline (2026-07-13): 3388 passed, 0 failed, 0 skipped. No pre-existing failures.

## Decisions
- 2026-07-13 **F1**: Bind errors flow through the value pipeline — Stage 3 outputs a wrapper (site OR failure), failures branch to a dedicated Collect()+report output node emitting QRY900. `PipelineErrorBag` deleted entirely; ChainAnalyzer's 3 catch sites switch to the deferred-diagnostics list.
- 2026-07-13 **F2**: Register `InternalError` (QRY900) and `NavigationTargetNotFound` (QRY063 — newly found 4th unregistered occurrence) in `s_deferredDescriptors`. Miss path reports QRY900 naming the unregistered ID in ALL builds (not just DEBUG), replacing the silent `continue` at both report loops.
- 2026-07-13 **F2 test**: User chose to SKIP the registry-membership test (source-scan / ctor assert both declined). The loud miss path is the sole guard; issue acceptance criterion 2's test portion is intentionally dropped.
- 2026-07-13 **F3**: Remove ThreadStatic side-channels. TraceCapture is populated and consumed within the orchestrator (results stored on `AssembledPlan.TraceLines`, already equality-excluded → cached groups keep traces). `ConsumedLambdaInnerSiteIds` returned from `Analyze` instead of ThreadStatic. Drop `CallSiteTranslator.cs:104`'s redundant trace log.
- 2026-07-13 **F4**: Sweep QRY001/QRY019 text + README + comments to match carrier-only reality; exact wording verified against actual behavior during implementation.

## Working Notes
- 2026-07-13 DESIGN exploration findings (all verified against source):
  - **F1 confirmed**: `PipelineOrchestrator.cs:42-43` — `TraceCapture.Clear()` + `PipelineErrorBag.DrainErrors()` at entry. Bind catch reports to bag at `QuarryGenerator.cs:109-117`; emission drain at `QuarryGenerator.cs:543`. ChainAnalyzer bag reports at `ChainAnalyzer.cs:115/208/241` (post-drain, survive). Extra hazard beyond the issue: if a file's ONLY site fails bind, no TranslatedCallSite exists → no FileInterceptorGroup → `EmitFileInterceptors` never runs → even without the entry drain the error is unreported. Also the emission drain runs in the FIRST group that emits — arbitrary association, and bind runs on threadpool transform threads while emission may run elsewhere (ThreadStatic = cross-thread loss).
  - **F2 confirmed**: `ChainAnalyzer.cs:1889-1894` emits `InternalError.Id` (QRY900) deferred; not in `s_deferredDescriptors` (`QuarryGenerator.cs:804-834`); miss path `continue` at `:555` and also `:786` (EmitDiagnostics loop). **NEW: 4th occurrence found — `ChainAnalyzer.cs:2531` emits raw string `"QRY063"` (NavigationTargetNotFound), also unregistered → silently dropped.**
  - **F3 confirmed**: `TraceCapture.Log` callers: `CallSiteTranslator.cs:104` (Stage 4, per-site cached — lost on warm runs), `ChainAnalyzer.cs:3221/3309` (retroactive LogSiteTrace/LogChainTrace, inside orchestrator — recomputed each orchestrator run), `SqlAssembler.cs:143-145` (inside orchestrator). Emission reads `TraceCapture.Get(execUid)` at `QuarryGenerator.cs:651` — DIFFERENT output node (`EmitFileInterceptors` combined with CompilationProvider, re-runs every compilation even when group cached; orchestrator may not re-run) → trace also thread-affinity + cache-skew fragile. `AssembledPlan.TraceLines` already exists as settable property EXCLUDED from equality (like MySqlBindOrder) — populating it inside the orchestrator is cache-correct and cached groups retain trace. CallSiteTranslator:104's info also flows via `Clause.ErrorMessage` into LogSiteTrace (`:3295-3296`), so that log call is redundant.
  - `ConsumedLambdaInnerSiteIds`: populated `ChainAnalyzer.cs:159-163`, consumed+cleared `PipelineOrchestrator.cs:135-143`. Only 1 prod caller of Analyze + 1 test caller (`UsageSiteDiscoveryTests.cs:1467`) — returning the set from Analyze instead of ThreadStatic is feasible.
  - **F4 confirmed**: QRY001 description (`DiagnosticDescriptors.cs:23-25`) and QRY019 messageFormat+description (`:351-357`) say "The original runtime method will be used instead". Reality: failed clauses are SKIPPED at plan build (`ChainAnalyzer.cs:1014` `if IsSuccess`), RuntimeBuild tier → QRY032 compile error, builder methods are throw stubs. Also `README.md:507` "QRY001 ... (runtime fallback)". QRY041's "runtime ordinal discovery" text is legit (different mechanism, still exists).
  - llm.md `### Error Propagation & QRY900` section (~line 149-163) documents the current two-channel model incl. "single-threaded per compilation" claim — must be updated with whatever design is chosen.
  - `BoundCallSite` ctor requires context/entity — a bind-failure sentinel site would need nullability relaxation; a wrapper result type (site-or-failure) at Stage 3 is cleaner.

## Suspend State

## Session Log
| Date | Phases | Summary |
|------|--------|---------|
| 2026-07-13 | INTAKE | Loaded issue #311, created worktree/branch, baseline 3388 tests green. |
| 2026-07-14 | DESIGN, PLAN | Verified all 4 findings + found 4th unregistered ID (QRY063). Decisions: BindResult value pipeline, loud miss path, ThreadStatic removal, registry test skipped. Plan (6 steps) approved via fast path. |
