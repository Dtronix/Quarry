## Summary
- Closes #259
- Turns three separate "author-experience against the guide" DX gaps into clear, authoring-time Quarry diagnostics instead of cryptic Roslyn errors.
- Full scope: generator diagnostics (QRY043), generator behavior change (nested row types), NuGet packaging (auto-opt-in `.targets`), analyzer (QRY044), and documentation.

## Reason for Change
Issue #259 consolidates three unrelated-looking build failures that all share the same root cause: the guide didn't warn the author and the failure mode was a generic Roslyn error (CS7036 / CS8852 / CS0138 / CS9137) against generated code rather than a Quarry diagnostic pointing at the real issue.

## Impact
Authors adopting Quarry on a new project now see:
1. **QRY043** (error) naming the row-entity type when it can't be materialized — positional record, init-only property, abstract class, or interface — with the recommended workaround (`Select(x => new Dto { ... })` on a chain query).
2. **Compiling generator output for nested row types** — row records declared inside an enclosing class no longer hit CS0138. The generator emits the `global::`-prefixed FQN in the interceptor body.
3. **`Quarry.Generated` auto-opted-in** via the shipped `build/Quarry.targets` — authors no longer hit CS9137 for the Quarry-internal namespace they can't reasonably discover.
4. **QRY044** (warning) pointing at each `[QuarryContext]` class whose namespace is missing from `<InterceptorsNamespaces>`, with the exact csproj line to paste.

## Plan items implemented as specified
- Phase 1: **QRY043** diagnostic, detection in `DisplayClassEnricher`, reporting via `PipelineOrchestrator.CollectTranslatedDiagnostics`, emission suppressed for affected sites so QRY043 is the only error reported.
- Phase 2: Nested row type support via a new `IsNestedType` field on `RawSqlTypeInfo`. `ResultTypeName` carries the final display form (FQN for nested, short for not); `FileEmitter` branches on `IsNestedType` to skip emitting a bad `using <EnclosingType>;` directive. Struct-reader identifier is sanitized.
- Phase 3: `build/Quarry.targets` auto-registers `Quarry.Generated` in `<InterceptorsNamespaces>`. `Quarry.Generator.props` exposes `InterceptorsNamespaces` as `CompilerVisibleProperty` for the Phase 4 analyzer.
- Phase 4: **QRY044** analyzer in `Quarry.Analyzers`, diagnostic-only (no code fix — the fix target is the `.csproj`, not a source document).
- Phase 5: `llm.md` gains an updated `InterceptorsNamespaces` paragraph and a row-entity shape note under Raw SQL. `src/Quarry.Generator/README.md` and `llm.md` diagnostic tables updated.

## Deviations from plan implemented
- The plan called for adding `RawCallSite.EntityNamespace` populated from `typeArgSymbol.ContainingNamespace`. The implementation instead flags nested sites via `RawSqlTypeInfo.IsNestedType` and skips them in the namespace-collection path. Functionally equivalent, smaller surface area, avoids a parallel namespace representation on `RawCallSite`.
- The plan called for a separate `RawSqlTypeInfo.FullyQualifiedResultTypeName` field consumed by `RawSqlBodyEmitter`. The implementation collapsed the branch by always storing the final display form in `ResultTypeName` at discovery time (`UsageSiteDiscovery.ResolveRawSqlTypeInfo`), making a second FQN field dead data. The field was introduced initially and then removed during the second REMEDIATE round — no emitter ever consumed it.

## Gaps in original plan implemented
- `CheckRowEntityMaterializability` also rejects **abstract classes** and **interfaces** used as `T` (added during REMEDIATE round 1). The plan only listed parameterless-ctor and init-only property cases. Without this guard, abstract/interface `T` would fail downstream with CS0144 against generated code — exactly the kind of cryptic error the issue was filed to eliminate.
- `InterceptorsNamespacesAnalyzer.HasQuarryContextAttributeSyntactic` now handles `AliasQualifiedNameSyntax` (added during REMEDIATE round 2). Without this branch, `[global::QuarryContextAttribute]` and extern-alias attribute forms fell through the syntactic pre-filter and QRY044 silently skipped the class — defeating the analyzer's purpose for those call sites.
- Test coverage added during REMEDIATE for paths the initial implementation threaded through but never exercised end-to-end: alias-qualified `[QuarryContext]` (QRY044), struct row types with init-only properties (QRY043 property-loop path on structs), and a mixed `RawSqlAsync<UserRow>` + `RawSqlScalarAsync<int>` case pinning the `Kind is RawSqlAsync or RawSqlScalarAsync` branches in `PipelineOrchestrator` and `FileEmitter`.

## Migration Steps
None for consumers of the existing shipped Quarry packages — all changes are additive. The new `build/Quarry.targets` takes effect automatically on package upgrade; consumers whose `.csproj` already listed `Quarry.Generated` in `<InterceptorsNamespaces>` compose with the targets file (semicolon-separated list deduplicates semantically). Existing in-repo sample/test projects that manually list `Quarry.Generated` are left alone — redundant but harmless.

## Performance Considerations
No runtime change. Generator-time: one additional `ITypeSymbol`-level check per `RawSqlAsync<T>` call site (O(properties on T) for the init-only scan, which already ran). Analyzer QRY044 is symbol-based and runs once per class declaration with an early syntactic filter on the attribute name.

## Security Considerations
`InterceptorsNamespacesAnalyzer` reads `build_property.InterceptorsNamespaces` and interpolates the caller's namespace symbol (not the raw property value) into the diagnostic message — no injection vector. No new runtime surface, no new dependencies.

## Breaking Changes
### Consumer-facing
None.

### Internal
- `RawSqlTypeInfo` ctor gains one optional parameter (`isNestedType = false`). All existing callers compile unchanged and opt into the default. A second optional parameter (`fullyQualifiedResultTypeName`) was added and subsequently removed as dead data; the final ctor signature grows by one arg, not two.
- `RawCallSite` gains a mutable post-construction `MaterializabilityError` property, propagated through all three `With*` copy methods. Excluded from `Equals`/`GetHashCode` to avoid incremental-generator cache instability — consistent with how `DisplayClassName`, `RawSqlTypeInfo`, and other enrichment fields are handled.
