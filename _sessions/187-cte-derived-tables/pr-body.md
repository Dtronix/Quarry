## Summary
- Closes #187
- Adds CTE (Common Table Expression) and derived table support via `With<TDto>()` / `FromCte<TDto>()` API
- Tracking issues for known follow-up work: #205 (CTE+Join chains), #206 (carrier conflict for multiple CTEs), #207 (discovery boilerplate refactor)
- Three review remediation passes: pass #1 fixed silent runtime data corruption when an inner CTE captured a variable and corrected MySQL/SQL Server WITH-clause identifier quoting; pass #2 consolidated divergent CTE-name helpers, replaced QRY900 InternalError surfacing with dedicated QRY080/QRY081 user diagnostics, hardened EntityRef FK detection, and added regression tests for the global-namespace DTO failure mode plus the two new diagnostics.

## Reason for Change
CTEs and derived tables are required for wrapping subquery results (e.g., window functions from #186 that need `WHERE rn = 1`). This adds "query-as-entity" support where a subquery's projection defines a virtual table, using a DTO class to describe the CTE column shape.

## Impact
New public API on `QuarryContext`:
- `With<TDto>(IQueryBuilder<TDto> innerQuery)` — defines a CTE from an inner query chain
- `With<TEntity, TDto>(IQueryBuilder<TEntity, TDto> innerQuery)` — defines a CTE from an inner chain with Select projection
- `FromCte<TDto>()` — sets the CTE as the primary FROM source (derived table pattern)

The `FromCte` pattern (CTE as primary FROM source) works end-to-end across all 4 SQL dialects. The CTE+Join pattern (joining a CTE to a real table) is blocked by a semantic model limitation (#205) and will be addressed in a follow-up.

## Plan items implemented as specified
- **Phase 1**: IR foundation — `CteDef`, `CteColumn` types, `InterceptorKind.CteDefinition`/`FromCte`, `ClauseRole` extensions, `QueryPlan.CteDefinitions`
- **Phase 2**: Runtime API — `With<TDto>()`, `With<TEntity, TDto>()`, `FromCte<TDto>()` on generated context class with `new` keyword shadowing
- **Phase 3**: CTE DTO resolution — `CteDtoResolver` resolves `INamedTypeSymbol` to `CteColumn[]` from public properties
- **Phase 4**: Discovery — CTE chain recognition in `UsageSiteDiscovery`, inner chain tagging via `IsCteInnerChain`, `DetectCteInnerChain` with candidate symbol fallback for multi-dialect support
- **Phase 5**: Binding — CTE DTO column resolution during discovery, columns stored on `RawCallSite.CteColumns`
- **Phase 6**: Chain analysis — Two-pass analysis (inner chains first, outer chains second), CTE composition in `ChainAnalyzer`, parameter merging
- **Phase 7**: SQL assembly — `WITH "Name" AS (inner_sql)` prefix rendering in `SqlAssembler`, CTE parameter index offset handling
- **Phase 8**: Code generation — `EmitCteDefinition` and `EmitFromCte` interceptor bodies in `TransitionBodyEmitter`, inner chain carrier suppression, `builderTypeName` for two-type-arg overload

## Deviations from plan implemented
- **Phase 5**: Instead of registering CTE DTOs in `EntityRegistry` as pseudo-entities, CTE columns are resolved directly during discovery and stored on `RawCallSite.CteColumns`. This avoids polluting the entity registry with non-schema types.
- **Phase 6**: Inner chain matching uses `CteInnerArgSpanStart` (syntax span position) rather than terminal syntax node references, which proved more reliable across incremental compilation. Inner chains are kept as standalone `AnalyzedChain`s (with their own carriers and interceptors) instead of being suppressed, since users may invoke the inner expression as a standalone query.
- **Phase 8**: `With()` creates the carrier (not `Users()`/`FromCte()` as originally considered). `FromCte()` is a no-op type transition via `Unsafe.As<IEntityAccessor<TDto>>`. `EmitCteDefinition` copies inner-chain parameter values from the inner carrier into the outer carrier (added during pass #1 remediation — see Review Remediation section below).
- **Phase 9**: Implemented 4 test cases — `Cte_FromCte_SimpleFilter`, `Cte_FromCte_CapturedParam`, `Cte_FromCte_DedicatedDto`, `Cte_FromCte_AllColumns` — plus 3 dedicated CTE diagnostic tests in `CarrierGenerationTests`. The remaining 4 originally planned cases (CTE+Join, multi-CTE) are blocked by #205 and #206.

## Review Remediation

### Pass #1: critical correctness fixes
- **CTE inner captured parameters were silently dropped at runtime.** `EmitCteDefinition` previously did `new {carrier} { Ctx = @this }` without copying any inner-chain parameter slots, so any captured variable in an inner `Where`/`Having` would bind to `default(T)`. Now copies `__inner.P0..P(N-1)` into `__outer.P{ParameterOffset}..P{ParameterOffset+N-1}` using a `QueryPlan`-keyed carrier-name lookup, mirroring the same pattern used by set-operation operand chains. Regression-tested by `Cte_FromCte_CapturedParam`.
- **`CallSiteBinder` fell back to PostgreSQL when entity lookup missed.** This silently corrupted dialect-specific identifier quoting on MySQL and SQL Server CTE chains (the WITH name was rendered with ANSI double quotes regardless of dialect) because CTE chains legitimately reference DTO types that are not schema entities. The binder now resolves the dialect from the chain's context class via `EntityRegistry.AllContexts`. Regression-tested by `Cte_FromCte_DedicatedDto`.
- **`ProjectionAnalyzer.BuildColumnInfoFromTypeSymbol` flagged primary keys and DTO properties as foreign keys.** A name-only "ends with Id" heuristic was used, which produced FK columns with null `ReferencedEntityName` and an NRE in `EmitDiagnosticsConstruction` whenever such a column flowed through a `Select` tuple projection on `.ToDiagnostics()`. Foreign keys are now detected by `EntityRef<TEntity, TKey>` *type* rather than name, eliminating the false positives.

### Pass #1: defensive diagnostics
- `ChainAnalyzer` now reports a generator diagnostic when a `CteDefinition` site has no matching inner-chain analysis result, instead of silently dropping the CTE. (Surfaced via `QRY900` initially; redirected to dedicated `QRY080` in pass #2.)
- `ChainAnalyzer` now reports a generator diagnostic when a `FromCte<T>()` site has no matching `With<T>` earlier in the same chain, instead of silently rewriting `primaryTable` to an undeclared CTE name. (Surfaced via `QRY900` initially; redirected to dedicated `QRY081` in pass #2.)
- The Pass 1 inner-chain analysis catch block now uses `catch (Exception ex) when (ex is not OperationCanceledException) { ... }`, mirroring the outer pass — was previously a bare `catch { }` that swallowed all failures.

### Pass #1: quality / consistency
- Renamed `ChainAnalyzer.GetShortTypeName` → `ExtractShortTypeName` to avoid name collision with `InterceptorCodeGenerator.GetShortTypeName` (different semantics). (Pass #2 then consolidated this with the emitter helper into `CteNameHelpers.ExtractShortName`.)
- Replaced `LastIndexOf(':')` based suffix parsing in CTE inner-chain matching with explicit `":cte-inner:"` marker lookup.
- Deleted dead `CteDtoResolver.Resolve()` method (only `ResolveColumns` is used).
- Improved `QuarryContext` base-class CTE error message to point at the derived-context typing requirement.
- Added XML doc warning on `QuarryContext.With` about extension-method ambiguity risk.

### Pass #2: helper consolidation, dedicated diagnostics, hardening
- **Consolidated short-name helpers.** `ChainAnalyzer.ExtractShortTypeName` and `TransitionBodyEmitter.ExtractDtoShortName` were near-duplicates that diverged on `global::` handling — the chain analyzer left the prefix in place while the emitter stripped it, so for a global-namespace DTO the `cteDef.Name == siteCteName` comparison was always false and the captured-param copy loop silently broke (re-introducing the pass #1 silent failure). Both helpers were removed and replaced with `CteNameHelpers.ExtractShortName` in `IR/CteDef.cs`, which strips both `global::` and namespace prefixes. Regression-tested by `Cte_With_GlobalNamespaceDto_StripsGlobalPrefix`.
- **Dedicated CTE user diagnostics QRY080 / QRY081.** The pass #1 defensive diagnostics for "CTE inner chain not analyzable" and "FromCte without preceding With" were emitted via `PipelineErrorBag.Report(...)`, which routes through `QRY900 InternalError` — misclassifying user-input mistakes as generator bugs. Added dedicated descriptors `CteInnerChainNotAnalyzable` (QRY080) and `FromCteWithoutWith` (QRY081), registered them in `s_deferredDescriptors`, and switched `ChainAnalyzer` to report them via `diagnostics?.Add(new DiagnosticInfo(...))`. Regression-tested by `Cte_With_NonInlineInnerArgument_EmitsQRY080` and `Cte_FromCte_WithoutPrecedingWith_EmitsQRY081`.
- **Hardened EntityRef FK detection.** The pass #1 type-based FK heuristic in `ProjectionAnalyzer` matched any type whose `Name == "EntityRef"` regardless of namespace, and did not unwrap `Nullable<T>`. Tightened to verify `ContainingNamespace == Quarry` via a new `IsQuarryNamespace` helper, and to unwrap `Nullable<T>` so `EntityRef<X,Y>?` still resolves as a foreign key.
- **Single-context dialect fallback in `CallSiteBinder`.** The pass #1 dialect-from-context fix only applied when `ContextClassName` was non-null. For sites whose context resolution failed AND there is exactly one registered context, the binder now falls back to that single context's dialect.
- **`IsCteInnerChain` detection in `FileEmitter` now scans all clause sites** instead of inspecting only `chain.ClauseSites[0]`.
- Documented the `cteInnerResults` span-key per-source-file uniqueness invariant in `ChainAnalyzer`.
- Documented the multi-CTE first-match limitation in `EmitCteDefinition` (#206 tracks the resolution).
- `TransitionBodyEmitter` switched to short-name `Dictionary<,>?` parameter via `using System.Collections.Generic`.

### Pass #2: review-finding reclassification
- **Test Quality #2** (originally classified A — "re-execute the SAME prepared chain after mutating cutoff") was reclassified to D during remediation. PreparedQuery in this codebase is a SNAPSHOT at chain construction — the generated `Where_xxx` interceptor extracts the captured variable into the carrier P0 field at the `.Where()` call, before `Prepare()` runs, and there is no Bind/SetParameter API to re-execute the same instance with a new value. The lt2 pattern in `Cte_FromCte_CapturedParam` is the correct way to test "different captured value" semantics. The test is unchanged in behavior but the comment now documents the snapshot semantics.

## Gaps in original plan implemented
- `DiscoverPostCteSites` — forward-scans the chain after `FromCte()` to discover clause/terminal methods that Roslyn cannot resolve (mirrors the existing post-navigation-join discovery pattern)
- `DiscoverPreparedTerminalsForCteChain` — handles `.Prepare()` terminals on CTE chains
- `TryResolveViaChainRootContext` — resolves context class/namespace from chain root for CTE chains where the root is a `With()` call (not a standard entity accessor)
- Multi-dialect `DetectCteInnerChain` candidate symbols fallback — handles cases where `GetSymbolInfo` returns candidates instead of a resolved symbol

## Migration Steps
None — purely additive. No schema changes, no breaking changes to existing APIs.

## Performance Considerations
- CTE SQL is assembled at compile time and embedded as a static string constant. No runtime overhead for CTE prefix rendering.
- Inner chain parameters are merged into the outer carrier's parameter list at compile time. Parameter extraction at runtime uses the same field-access pattern as regular parameters.

## Security Considerations
- CTE names are derived from DTO class names (compile-time constants) and quoted through `SqlFormatting.QuoteIdentifier`. No user-supplied strings flow into SQL identifiers.
- Inner CTE SQL is fully assembled by the generator pipeline — no injection vectors.

## Breaking Changes
- Consumer-facing: None. New methods on `QuarryContext` throw `NotSupportedException` without interception, consistent with all other Quarry methods.
- Internal: `InterceptorKind` and `ClauseRole` enums gain new values. `QueryPlan` and `RawCallSite` constructors gain new optional parameters (all default to null/false). No existing code affected.
