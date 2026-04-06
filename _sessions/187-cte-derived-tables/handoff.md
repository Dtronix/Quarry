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

### Pass #2 remediation (WIP commit 61a0ee5 — NOT yet on green CI; tests pending)
- Consolidated short-name helpers into `CteNameHelpers.ExtractShortName` (strips both `global::` and namespace prefixes). Closes the global-namespace DTO bug that would have re-introduced the captured-param drop fixed in pass #1.
- Added QRY080/QRY081 dedicated diagnostic descriptors. ChainAnalyzer reports via `diagnostics?.Add(...)` instead of the QRY900 InternalError side-channel.
- Tightened EntityRef FK detection (Quarry namespace check + Nullable unwrap).
- CallSiteBinder single-context dialect fallback when ContextClassName is null.
- IsCteInnerChain detection scans all clause sites (was [0] only).
- Documented cteInnerResults span-key uniqueness invariant.
- Documented multi-CTE first-match limitation in EmitCteDefinition.
- Replaced fully-qualified `System.Collections.Generic.Dictionary` with `using` + short name in TransitionBodyEmitter.

## Previous Session Completions
- Sessions 1-3: Phases 1-8 (IR, API, discovery, binding, chain analysis, SQL assembly, code gen) + Phase 9 partial (SQLite test, then multi-dialect fix)
- Session 4: Fixed DetectCteInnerChain candidate symbols fallback, expanded CrossDialectCteTests to 4-dialect, added TryResolveViaChainRootContext/DiscoverPostCteSites/DiscoverPreparedTerminalsForCteChain, fixed With<TEntity,TDto> interceptor signature, marked CteDtoResolver.Resolve() TODO, created issues #205/#206/#207, completed first review.
- Session 5: Rebased on origin/master (5 commits), resolved conflicts, all tests passing, PR #208 created and CI'd. Suspended awaiting merge confirmation.
- Session 6 (current, mid-flight): Two more review passes triggered by user. Pass #1 found and fixed 3 critical bugs + 8 smaller items, committed b02a13e, CI'd. Pass #2 found 16 items (1 Medium silent-bug regression of pass #1 fix, 1 Medium QRY900 misclassification, 1 Medium negative-test gap, 13 Lower). 7 of 8 pass #2 remediation tasks done in WIP commit 61a0ee5. Suspended before adding new tests and final commit.

## Progress
- Phases 1-9: implementation complete (3 of 8 originally planned tests delivered)
- Pass #1 review + remediation: complete + CI'd
- Pass #2 review: complete
- Pass #2 remediation: ~80% complete (WIP commit 61a0ee5)
- 20 commits on branch (19 non-WIP + 1 WIP)
- PR #208 open. CI green at b02a13e (run 24036554463). Will re-run on push of 61a0ee5 (or replacement non-WIP commit).
- Last verified test totals: 2778 main + 103 analyzer + 79 migration = 2960 passing — at b02a13e. NOT verified after WIP changes.

## Current State
PR #208 has 19 non-WIP commits + 1 WIP on top. The pass #2 changes in 61a0ee5 are complete on the source side and the generator builds clean. Full test suite has NOT been run after the WIP changes. The next session must add new tests, run the full suite, replace 61a0ee5 with a non-WIP commit, push, and re-prompt for merge.

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
1. **(Test Quality #1, Medium)** Add diagnostic tests for QRY080 and QRY081. Pattern: `src/Quarry.Tests/Generation/CarrierGenerationTests.cs` already has QRY033 tests using `RunGeneratorWithDiagnostics(compilation)` + `diagnostics.FirstOrDefault(d => d.Id == "QRYxxx")`. Build test sources that:
   - For QRY080: invoke `db.With<TDto>(someMethodGroup)` or pass an external variable instead of an inline chain. The challenge is that the inner-chain-not-analyzable path is hit when `cteInnerResults` lookup misses — i.e., when `DetectCteInnerChain` doesn't find a matching arg span. May require deliberately constructing a chain shape that `DetectCteInnerChain` won't classify.
   - For QRY081: `db.FromCte<X>().Select(...)` without any preceding `With<X>(...)`. This should be straightforward.
2. **(Plan Compliance #1, Low)** Add `Cte_FromCte_AllColumns` test in `CrossDialectCteTests.cs` that exercises identity FromCte without tuple projection.
3. **(Test Quality #2, Low)** In existing `Cte_FromCte_CapturedParam`, after the first execution, mutate `cutoff` and re-execute the SAME prepared `lt` (don't create a new `lt2`).
4. **(Correctness #1 verification)** Add a test using a global-namespace DTO (no `namespace` in source) to verify the consolidated `CteNameHelpers.ExtractShortName` handles `global::Foo` end-to-end. May not be feasible inside the test project (which is in `Quarry.Tests`); could instead be a `CarrierGenerationTests` source-text test that verifies the captured-param assignment is emitted.
5. **Run full test suite**: `dotnet test src/Quarry.Tests src/Quarry.Analyzers.Tests src/Quarry.Migration.Tests`. Must be all green. Manifests will regenerate (commit them).
6. **Convert WIP commit 61a0ee5 to non-WIP**: Per Suspend rules, do NOT amend. Add the test additions in a NEW commit, then either accept the 2-commit history (one WIP, one tests) or interactively rebase to squash. **Recommended**: just create the new commit with the tests + manifests, write a final summary commit message that covers all of pass #2 (the WIP message is informal). Push as new HEAD.
7. **Update PR #208 body** Review Remediation section to mention pass #2 fixes.
8. **Re-prompt user for FINALIZE/merge** once CI is green on the new HEAD.
