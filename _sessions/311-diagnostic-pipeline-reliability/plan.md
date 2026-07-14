# Plan: 311-diagnostic-pipeline-reliability

## Overview

Issue #311 identifies one root theme: diagnostics that travel through side-channels (`[ThreadStatic]` bags) or the deferred string-ID registry can be silently lost. The fix direction, confirmed in DESIGN, is to make every diagnostic flow through equatable values in the incremental pipeline (thread-safe, cache-correct) and to make the one remaining lookup-based path (deferred descriptor registry) fail loud instead of silent.

## Key concepts

- **Deferred diagnostics** (`DiagnosticInfo`): pipeline stages record (ID, location, args); `EmitFileInterceptors` resolves the ID via `s_deferredDescriptors` at report time. Unregistered IDs are currently dropped by `if (descriptor == null) continue;` — at `QuarryGenerator.cs:555` and `:786`.
- **PipelineErrorBag**: `[ThreadStatic]` side-channel for Stage-3 bind exceptions (no site to attach errors to). Drained-and-discarded at orchestrator entry (`PipelineOrchestrator.cs:43`) before the reporting drain (`QuarryGenerator.cs:543`) can see current-compilation errors. Also thread-affine and unreported when a file produces no interceptor group.
- **BindStageResult** (new): Stage 3 will output a wrapper that is either a `BoundCallSite` or a bind failure (file/line/column/message). Successes flow to Stage 4 as today; failures branch to a dedicated `Collect()` + `RegisterImplementationSourceOutput` node that reports QRY900 directly. This removes `PipelineErrorBag` entirely.
- **TraceCapture**: `[ThreadStatic]` trace-line accumulator. Populated inside the orchestrator (ChainAnalyzer `LogSiteTrace`/`LogChainTrace`, SqlAssembler) but read at emission in a *different* output node (`QuarryGenerator.cs:651`) — cross-thread and cache-skew fragile. `AssembledPlan.TraceLines` is already a settable property excluded from equality, so populating it inside the orchestrator makes cached groups keep their traces.
- **ConsumedLambdaInnerSiteIds**: `[ThreadStatic]` set populated in `ChainAnalyzer.Analyze` (`:159-163`), consumed and cleared in the orchestrator (`PipelineOrchestrator.cs:135-143`). A cancellation between populate and clear leaks stale UniqueIds into the next run's site filter. Returning the set from `Analyze` removes the state entirely.
- **Carrier-only reality** (for text sweep): non-intercepted builder calls hit default-interface throw stubs (`IEntityAccessor.cs`); a clause whose translation failed has its interceptor skipped (`ShouldSkipNonTranslatableClause`) so the call throws `InvalidOperationException` at runtime. There is no runtime fallback.

## Steps

### Step 1 — F2: register missing deferred IDs + loud miss path
- [x] Add `DiagnosticDescriptors.InternalError` (QRY900) and `DiagnosticDescriptors.NavigationTargetNotFound` (QRY063) to `s_deferredDescriptors` (`QuarryGenerator.cs:804-834`).
- [x] Change `ChainAnalyzer.cs:2531` to use `DiagnosticDescriptors.NavigationTargetNotFound.Id` instead of the raw string `"QRY063"`.
- [x] Replace the silent `continue` miss path in both deferred report loops (`QuarryGenerator.cs:555` and `:786`) with reporting a QRY900 `InternalError` that names the unregistered ID and carries the original location (factor a small internal helper so both loops share it). QRY900 itself is registered by this step, so the miss report cannot recurse. (Helper: `ReportDeferredDiagnostic` + internal `TryGetDeferredDescriptor`; `GetDescriptorById` removed.)
- Tests: registry-membership assertions for QRY900/QRY063 via internal access; unit test for the miss-path helper (unregistered ID in a `DiagnosticInfo` → QRY900 naming it, registered ID → normal report).

### Step 2 — F1: bind errors flow through the value pipeline (depends on Step 1)
- [x] Add equatable `BindStageResult` in `IR/` wrapping either `BoundCallSite` or a failure (FilePath, Line, Column, Message). Equality must include the failure fields so error-state changes invalidate caches (mirrors `TranslatedCallSite.PipelineError`).
- [x] Stage 3 (`QuarryGenerator.cs:105-118`): transform outputs `BindStageResult`s; the catch produces one failure result instead of `PipelineErrorBag.Report` + empty array. Split downstream: successes (`.Where`/`.Select`) feed Stage 4 unchanged; failures are `Collect()`ed into a new `RegisterImplementationSourceOutput` that reports QRY900 for each. (Also: the catch now excludes `OperationCanceledException` — previously a cancelled bind would have been recorded as an error.)
- [x] Remove the emission drain (`QuarryGenerator.cs:543-549`) and the orchestrator entry drain (`PipelineOrchestrator.cs:43`).
- [x] Reroute ChainAnalyzer's three catch sites (`:115/:208/:241`) from `PipelineErrorBag.Report` to `diagnostics?.Add(new DiagnosticInfo(DiagnosticDescriptors.InternalError.Id, …))` with the site's location — reliable now that QRY900 is registered (Step 1).
- [x] Delete `IR/PipelineErrorBag.cs`. (ProjectionAnalyzer's doc-comment reference to it updated — its own ThreadStatic is drained within a single transform call and is out of scope.)
- [x] Add an internal test hook to force a bind exception (same pattern as `ChainAnalyzer.TestCapturedChains`). (`CallSiteBinder.TestThrowOnMethodName`.)
- Tests (acceptance criterion 1): forced bind exception → generator run surfaces a QRY900 compile diagnostic; also covers the group-less case (a file whose only site fails bind still reports).

### Step 3 — F3a: trace lines stored on the equatable model
- [x] In `AnalyzeAndGroupTranslated`, wrap the produce region in try/finally: after SQL assembly (and chain analysis), set `assembled.TraceLines = TraceCapture.Get(execUid)` for `IsTraced` plans; `TraceCapture.Clear()` in `finally` (keep the entry `Clear()` as defense). (Body extracted to `AnalyzeAndGroupTranslatedCore` to avoid re-indenting.)
- [x] `EmitFileInterceptors` (`QuarryGenerator.cs:647-654`): stop reading `TraceCapture`; gate emission of already-populated `TraceLines` on `hasQuarryTrace` (null them out or gate the consumer — decide against actual consumer code in FileEmitter/CarrierEmitter). (Chose consumer gating: `FileEmitter` ctor takes `emitTraceComments`; the cached plan is never mutated, so defining QUARRY_TRACE later still finds the lines.)
- [x] Delete the redundant Stage-4 trace log at `CallSiteTranslator.cs:104` (its content flows via `Clause.ErrorMessage` into the retroactive `LogSiteTrace`).
- Tests (acceptance criterion 3): incremental warm-run test (pattern from `IncrementalCachingTests.cs`) — run driver with a traced chain in file A, edit unrelated file B, re-run, assert file A's generated output still contains `// [Trace]` lines.

### Step 4 — F3b: consumed-lambda IDs returned from Analyze
- [x] Change `ChainAnalyzer.Analyze` to return the consumed-lambda-inner site IDs to the caller (out param or result object) instead of the `[ThreadStatic]` set; delete the ThreadStatic. (Out param before the optional `diagnostics`.)
- [x] Update `PipelineOrchestrator.cs:135-143` (no more clear-after-use) and the test caller (`UsageSiteDiscoveryTests.cs:1467`). (Added `ChainAnalyzer_LambdaInnerSites_ReturnedInConsumedSet` asserting the out-set contract and no cross-run carryover.)
- Tests (acceptance criterion 4): existing lambda-inner filtering tests stay green; the poisoning hazard is gone by construction (no cross-run state to leak) — assert Analyze returns the expected set directly.

### Step 5 — F4: stale text sweep
- [x] QRY001 description (`DiagnosticDescriptors.cs:23-25`): replace "The original runtime method will be used instead." with the carrier-only consequence (calls on the chain are not intercepted and throw `InvalidOperationException` at runtime).
- [x] QRY019 messageFormat + description (`:351-357`): same correction; honor the no-trailing-punctuation contract with `CallSiteTranslator` error messages. (4 comment references in CallSiteTranslator updated too.)
- [x] `ShouldSkipNonTranslatableClause` doc comment (`InterceptorCodeGenerator.Utilities.cs:131-134`), `README.md:507`, and a repo-wide sweep for "runtime method"/"falls back to runtime" phrasing (QRY041's "runtime ordinal discovery" is legitimate and stays). (Also updated `docs/articles/analyzer-rules.md` QRY019 row and `ProjectionAnalyzer` comment. Discovered dead `TypeMappingRegistry` runtime-fallback machinery — recorded in Working Notes, deferred to REVIEW as separate-issue candidate. Release notes intentionally untouched.)
- Tests: update any tests asserting the old message text.

### Step 6 — docs: llm.md error-propagation section
- [x] Rewrite `Quarry.Generator/llm.md` "Error Propagation & QRY900" (~:149-163): channels are now (1) `TranslatedCallSite.PipelineError`, (2) `BindStageResult` failures → dedicated output node, (3) deferred `DiagnosticInfo` with loud miss path, (4) emission catch. Remove the ThreadStatic-lifecycle paragraph and the "single-threaded per compilation" claim. (Also: pipeline diagram Stage 3a row, QRY048 registration note, Key Design Decisions #2.)
- [x] Update the `DiagnosticInfo` table row (llm.md:424 "unregistered IDs are silently dropped") and any TraceCapture emission-read description. (File tables: PipelineErrorBag row → BindStageResult row; TraceCapture row rewritten.)
- Tests: none (docs).

## Dependencies
Step 2 depends on Step 1 (ChainAnalyzer reroute needs QRY900 registered). Steps 3 and 4 are independent but both touch ChainAnalyzer/orchestrator — do sequentially. Steps 5–6 last.
