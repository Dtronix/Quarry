# Work Handoff: 187-cte-derived-tables

## Key Components
- **CteDef / CteColumn / CteNameHelpers** (`src/Quarry.Generator/IR/CteDef.cs`): CTE definition + the shared `ExtractShortName(string?)` helper that strips both `global::` and namespace prefixes. **Both** `ChainAnalyzer` and `TransitionBodyEmitter` MUST use this helper for CTE name comparisons — divergent helpers caused a real silent-failure bug in pass #2.
- **QueryPlan.CteDefinitions** (`src/Quarry.Generator/IR/QueryPlan.cs`): List of CTEs attached to a query plan
- **CteDtoResolver** (`src/Quarry.Generator/IR/CteDtoResolver.cs`): Resolves `INamedTypeSymbol` → `CteColumn[]`. Pass #1 deleted the dead `Resolve()` method.
- **QuarryContext CTE methods** (`src/Quarry/Context/QuarryContext.cs`): `With<TDto>()`, `With<TEntity, TDto>()`, `FromCte<TDto>()`. Improved error message + XML doc warning about extension method ambiguity.
- **Context CTE methods** (`src/Quarry.Generator/Generation/ContextCodeGenerator.cs`): `new`-shadowed overrides returning concrete type
- **InterceptorKind.CteDefinition / FromCte** (`src/Quarry.Generator/Models/InterceptorKind.cs`)
- **CTE discovery** (`src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs`): `DetectCteInnerChain` (with candidate fallback), `DiscoverCteSite`, `TryResolveViaChainRootContext`, `DiscoverPostCteSites`, `DiscoverPreparedTerminalsForCteChain`
- **Two-pass chain analysis** (`src/Quarry.Generator/Parsing/ChainAnalyzer.cs`): Forward iteration of clauseSites for CTE site processing. Inner-chain analysis catch uses exception filter + diagnostics?.Add. CTE/FromCte error reporting uses **QRY080/QRY081** via `diagnostics?.Add(new DiagnosticInfo(...))` — pass #2 replaced the prior `PipelineErrorBag.Report` calls (which surfaced as `QRY900 InternalError`).
- **WITH clause rendering** (`src/Quarry.Generator/IR/SqlAssembler.cs`)
- **CTE interceptor bodies** (`src/Quarry.Generator/CodeGen/TransitionBodyEmitter.cs`): `EmitCteDefinition` (copies inner-carrier params P0..P(N-1) into outer carrier P{ParameterOffset}..P{ParameterOffset+N-1} via QueryPlan-keyed `operandCarrierNames` lookup), `EmitFromCte`. Multi-CTE first-match limitation documented in code; ambiguity resolution belongs to #206.
- **CallSiteBinder** (`src/Quarry.Generator/IR/CallSiteBinder.cs`): When entity lookup misses, resolves dialect from `registry.AllContexts` by class name, with single-context fallback when ContextClassName is null.
- **ProjectionAnalyzer.BuildColumnInfoFromTypeSymbol** (`src/Quarry.Generator/Projection/ProjectionAnalyzer.cs`): EntityRef FK detection by *type* (Quarry namespace + 2 type args), with `Nullable<T>` unwrap. Replaces the broken "ends with Id" heuristic that produced FK columns with null `ReferencedEntityName`.
- **DiagnosticDescriptors** (`src/Quarry.Generator/DiagnosticDescriptors.cs`): QRY080 `CteInnerChainNotAnalyzable`, QRY081 `FromCteWithoutWith`. Both registered in `s_deferredDescriptors` (`QuarryGenerator.cs`).

## Completions (This Session — Session 6)
### Pass #1 remediation (committed b02a13e, CI'd run 24036554463)
- `EmitCteDefinition` copies CTE inner captured params from inner carrier into outer carrier. Was silently dropping captured values.
- `CallSiteBinder` resolves dialect from context (was hardcoded PG fallback corrupting MySQL/SS WITH-clause quoting).
- `ProjectionAnalyzer.BuildColumnInfoFromTypeSymbol` FK detection by type, not name. Removed name heuristic that NRE'd on tuple projections of properties ending in "Id".
- `ChainAnalyzer` forward iteration of clauseSites for CteDefinition/FromCte (was reverse).
- Diagnostics for CteDefinition without inner-chain analysis and FromCte without preceding With (initially via PipelineErrorBag.Report — replaced in pass #2).
- Inner-chain analysis catch uses exception filter + PipelineErrorBag instead of bare swallow.
- Renamed `ChainAnalyzer.GetShortTypeName` → (later replaced by shared helper).
- `:cte-inner:` marker explicit lookup (was LastIndexOf(':')).
- Deleted `CteDtoResolver.Resolve()` dead code.
- Improved `QuarryContext.With` error message; XML doc warning about extension ambiguity.
- Added `Cte_FromCte_CapturedParam` and `Cte_FromCte_DedicatedDto` tests.

### Pass #2 remediation (source: WIP 61a0ee5; tests + final summary in session 7 commit)
- Consolidated short-name helpers into `CteNameHelpers.ExtractShortName` (strips both `global::` and namespace prefixes). Closes the global-namespace DTO bug that would have re-introduced the captured-param drop fixed in pass #1.
- Added QRY080/QRY081 dedicated diagnostic descriptors. ChainAnalyzer reports via `diagnostics?.Add(...)` instead of the QRY900 InternalError side-channel.
- Tightened EntityRef FK detection (Quarry namespace check + Nullable unwrap).
- CallSiteBinder single-context dialect fallback when ContextClassName is null.
- IsCteInnerChain detection scans all clause sites (was [0] only).
- Documented cteInnerResults span-key uniqueness invariant.
- Documented multi-CTE first-match limitation in EmitCteDefinition.
- Replaced fully-qualified `System.Collections.Generic.Dictionary` with `using` + short name in TransitionBodyEmitter.

### Pass #2 test additions (session 7)
- `Cte_With_NonInlineInnerArgument_EmitsQRY080` (CarrierGenerationTests.cs): With<T>() takes a field reference rather than an inline chain — DetectCteInnerChain cannot classify, lookup misses, QRY080 must fire. Also asserts NO QRY900 (the prior surfacing).
- `Cte_FromCte_WithoutPrecedingWith_EmitsQRY081` (CarrierGenerationTests.cs): FromCte<T>() with no preceding With<T>() — QRY081 must fire, no QRY900.
- `Cte_With_GlobalNamespaceDto_StripsGlobalPrefix` (CarrierGenerationTests.cs): two-source compilation with `global::GlobalOrderDto`, asserts the generated SQL constant uses bare `GlobalOrderDto` (verbatim-quoted form) and the captured-param copy `Pn = __inner.P0` is emitted. Regression for pass #2 Correctness #1.
- `Cte_FromCte_AllColumns` (CrossDialectCteTests.cs): identity FromCte<Order>().Select(o => o) (vs the existing tuple-projection tests) across all 4 dialects. Closes Plan Compliance #1.
- Modified `Cte_FromCte_CapturedParam`: added comment documenting that PreparedQuery is a snapshot at construction (no Bind/SetParameter API), so reuse-with-mutation isn't possible — the lt2 pattern is correct. Test Quality #2 reclassified A→D in review.md.

## Previous Session Completions
- Sessions 1-3: Phases 1-8 (IR, API, discovery, binding, chain analysis, SQL assembly, code gen) + Phase 9 partial (SQLite test, then multi-dialect fix)
- Session 4: Fixed DetectCteInnerChain candidate symbols fallback, expanded CrossDialectCteTests to 4-dialect, added TryResolveViaChainRootContext/DiscoverPostCteSites/DiscoverPreparedTerminalsForCteChain, fixed With<TEntity,TDto> interceptor signature, marked CteDtoResolver.Resolve() TODO, created issues #205/#206/#207, completed first review.
- Session 5: Rebased on origin/master (5 commits), resolved conflicts, all tests passing, PR #208 created and CI'd. Suspended awaiting merge confirmation.
- Session 6 (current, mid-flight): Two more review passes triggered by user. Pass #1 found and fixed 3 critical bugs + 8 smaller items, committed b02a13e, CI'd. Pass #2 found 16 items (1 Medium silent-bug regression of pass #1 fix, 1 Medium QRY900 misclassification, 1 Medium negative-test gap, 13 Lower). 7 of 8 pass #2 remediation tasks done in WIP commit 61a0ee5. Suspended before adding new tests and final commit.

## Progress
- Phases 1-9: implementation complete (4 of 8 originally planned tests delivered + dedicated CTE diagnostic tests)
- Pass #1 review + remediation: complete + CI'd at b02a13e (run 24036554463)
- Pass #2 review: complete
- Pass #2 remediation: COMPLETE — source in WIP 61a0ee5 (CI'd at 243034d run 24043045000), tests + final summary in session 7 commit
- PR #208 open, awaiting FINALIZE confirmation
- Verified test totals at session 7: 2782 main + 103 analyzer + 79 migration = **2964 passing**, all green

## Current State
PR #208 has 19 non-WIP commits + WIP 61a0ee5 + suspend artifact 243034d + session 7 final commit (this commit) on top. All pass #2 source remediation and tests are in place. Full test suite verified green locally (2964) and on CI at 243034d. Awaiting user FINALIZE confirmation before merge. The WIP and suspend commits will collapse into the squash merge — no rebase needed.

## Known Issues / Bugs
1. **CTE+Join chain cascade** (#205): With() returns QuarryContext during source generation, blocks Users() resolution
2. **Carrier conflict for multiple CTEs** (#206): Each With() creates new carrier, discarding previous
3. **Discovery boilerplate duplication** (#207): DiscoverPostCteSites/DiscoverPreparedTerminalsForCteChain duplicate patterns

## Dependencies / Blockers
None for the CTE work itself. Pass #2 depends only on adding the missing tests and re-running CI.

## Architecture Decisions
- **CTE inner chains kept as standalone AnalyzedChain** (Phase 6 plan deviation): the user may invoke the inner expression as a standalone query, so we cannot suppress its standalone interception. Documented in `ChainAnalyzer.cs` Pass 1 comment.
- **CTE name = short type name** of the DTO. Helper consolidated to `CteNameHelpers.ExtractShortName` after pass #2 found a divergence between two local implementations.
- **CTE inner parameter copy in EmitCteDefinition** uses the same QueryPlan-keyed `operandCarrierNames` lookup that set operations use. CTE inner chains are NOT operand chains but they ARE registered in `operandCarrierNames` so that the lookup works.
- **CTE/FromCte user errors are QRY080/QRY081**, not QRY900. Reported via the deferred-diagnostics channel (`diagnostics?.Add(new DiagnosticInfo(...))`).
- **EntityRef FK detection is type-based**, requires Quarry namespace + 2 type args, and unwraps `Nullable<T>`. Name heuristics are unreliable (would flag primary keys and DTO ID columns).

## Open Questions
None.

## Next Work (Priority Order)
All pass #2 remediation work is complete (source changes in WIP 61a0ee5; tests + final summary in this session 7 commit). Remaining steps:
1. Push session 7 final commit to origin/187-cte-derived-tables.
2. Update PR #208 body Review Remediation section to mention pass #2 fixes + tests.
3. Wait for CI on the new HEAD; verify green.
4. Re-prompt user for FINALIZE/merge.
