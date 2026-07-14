## Summary
- Closes #311

Makes every diagnostic channel in the generator a value in the incremental pipeline instead of `[ThreadStatic]` side-state, and makes the deferred-diagnostic registry fail loud instead of silently dropping unregistered IDs. All four issue findings addressed, plus a fifth occurrence of the silent-drop trap found during design (QRY063 emitted as a raw string, unregistered).

## Reason for Change
Issue #311 (multi-agent deep review, adversarially verified) identified one root theme: error/trace side-channels and the deferred-diagnostic registry could silently lose diagnostics, and the loss modes had already shipped three times (QRY048 in #304, then QRY900 and QRY063 found here). Bind-stage QRY900s were drained-and-discarded before reporting; `TraceCapture` and `ConsumedLambdaInnerSiteIds` were ThreadStatic state crossing pipeline-node (and thus thread/cache) boundaries; QRY001/QRY019 text still promised a runtime fallback that no longer exists.

## Impact
- **Bind failures** (Stage 3 exceptions) flow as `BindStageResult` values to a dedicated report node — they surface as QRY900 even when a file's only site fails bind (previously: never reported). `PipelineErrorBag` is deleted.
- **Unregistered deferred diagnostic IDs** are reported as QRY900 naming the ID (previously: `continue` — silent drop). QRY900 and QRY063 are now registered; QRY063 fires for the first time.
- **Deferred diagnostics whose file has no interceptor group** are collected into a synthetic `OrphanDiagnostics` group and still report.
- **`.Trace()` output** is captured onto `AssembledPlan.TraceLines` inside the orchestrator (equality-excluded), so cached file groups keep their `// [Trace]` comments on incremental runs; the QUARRY_TRACE gate moved to a `FileEmitter` flag so cached plans are never mutated.
- **Cancellation** can no longer poison later runs: bind/translate catches exclude `OperationCanceledException`, `ConsumedLambdaInnerSiteIds` is returned from `Analyze` instead of stored ThreadStatic, and `TraceCapture` clears in a `finally`.

Acceptance criteria from #311: all four covered by tests (forced bind exception → QRY900; unregistered-ID fallback unit-tested — see Deviations for the skipped registry-scan test; warm-run trace persistence incl. a lifecycle assertion that fails on the old code; cancellation poisoning impossible by construction, pinned by the out-param contract test).

## Plan items implemented as specified
- Step 1 — register QRY900/QRY063 in `s_deferredDescriptors`, replace the silent miss path with a QRY900 report (`ReportDeferredDiagnostic`/`ResolveDeferredReport`).
- Step 2 — `BindStageResult` value pipeline, dedicated failure report node, both drains removed, ChainAnalyzer catches rerouted to deferred diagnostics, `PipelineErrorBag` deleted, forced-bind-exception tests.
- Step 3 — trace lines captured in the orchestrator with try/finally; emission no longer reads `TraceCapture`; redundant Stage-4 trace log removed.
- Step 4 — consumed-lambda-inner IDs returned via out param; ThreadStatic deleted.
- Step 5 — stale "original runtime method will be used" text swept from QRY001/QRY019, comments, README, analyzer-rules (QRY041's "runtime ordinal discovery" is a live mechanism and stays; v0.4.0 release notes left as historical record).
- Step 6 — generator `llm.md` error-propagation section rewritten for the value-channel model.

## Deviations from plan implemented
- The plan's registry-membership source-scan test was explicitly skipped by decision during design review; the loud miss path (plus its unit tests) is the guard instead.
- Bind catch additionally excludes `OperationCanceledException` (agreed in-flight): previously a cancelled bind would have been recorded as an error.

## Gaps in original plan implemented
Post-implementation review (15 findings, all classified; see below for the two that became scope):
- **Orphan-diagnostic group** — review found ChainAnalyzer QRY900s could still vanish when the failing chain's file had no interceptor group; fixed structurally in `GroupTranslatedIntoFiles`.
- **Sticky cancellation QRY900** — the Stage 4 translate catch also swallowed `OperationCanceledException` into the cached `PipelineError`; fixed.
- **QRY001 and QRY019 raised from Warning to Error** (reclassified from "separate issue" to in-scope by review decision). Under the carrier-only model both mean the call site gets no interceptor and throws `InvalidOperationException` at runtime. A probe confirmed supported multi-hop variable chains analyze clean (no known false-positive shape).
- **Dead runtime-fallback machinery removed** — `TypeMappingRegistry` (`TryConvert`/`TryConfigureParameter` had no production callers; the doc-claimed consumer `NormalizeParameterValue` does not exist), `ITypeMappingConverter`, and their tests. `TypeMapping` no longer registers into a registry. `IDialectAwareTypeMapping` is unchanged — generated code calls `ConfigureParameter` directly.
- Hardening: `BindStageResult` ctor null guards, `FileEmitter` trace-comment default flipped to fail-safe, `ChainAnalyzer.Analyze` `diagnostics` parameter made required.

## Performance Considerations
No hot-path changes. Stage 3 allocates one small wrapper per bound site (build-time only); trace capture moved, not added; the orphan-diagnostic scan is O(diagnostics) per orchestrator run.

## Breaking Changes
- Consumer-facing (release notes for the next version should call these out):
  - **QRY001 and QRY019 are now Errors** (were Warnings). Code that compiled with these warnings was already broken at runtime (`InvalidOperationException` on execution); it now fails at compile time.
  - **QRY900 surfaces where builds previously succeeded silently** — a generator-internal bind/analysis error now fails the build instead of manifesting as a confusing runtime exception. QRY900 is severity Error by design.
  - **QRY063 (navigation target entity not found, Warning) fires for the first time** — it was previously constructed and silently dropped. New warning under `TreatWarningsAsErrors`.
  - **QRY019 message text changed** to "…The clause is not intercepted and the call will throw InvalidOperationException at runtime." ID-based suppressions keep working; text-matching log filters will not.
  - `TypeMapping<TCustom,TDb>` no longer implements the internal `ITypeMappingConverter` and has no registering constructor — no public API surface removed.
- Internal:
  - `ChainAnalyzer.Analyze` signature: consumed-lambda-inner IDs out param; `diagnostics` required.
  - `CallSiteBinder.Bind` errors are carried by `BindStageResult`; `PipelineErrorBag` and `TypeMappingRegistry` deleted.
  - Bind-stage cancellation is no longer recorded as an error (cancelled runs are discarded by Roslyn anyway).
