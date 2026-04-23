# Review: #260

## Classifications

| # | Section | Finding (one line) | Sev | Rec | Class | Action Taken |
|---|---------|---------------------|-----|-----|-------|--------------|
| 1 | Plan Compliance | `RawSqlTypeInfo.FullyQualifiedResultTypeName` is written but never read | Minor | B | B | Removed `FullyQualifiedResultTypeName` from `RawSqlTypeInfo` (ctor param, property, `Equals`) and its two call-site passes in `UsageSiteDiscovery`. |
| 2 | Plan Compliance | `RawCallSite.EntityNamespace` plan field not added; filter via `IsNestedType` instead | Info | D | D | Dismissed: functionally equivalent; carried from prior review. |
| 3 | Plan Compliance | Analyzer is standalone rather than "alongside `QuarryQueryAnalyzer`" | Info | D | D | Dismissed: Roslyn discovery is attribute-based. |
| 4 | Correctness | `HasQuarryContextAttributeSyntactic` misses `AliasQualifiedNameSyntax` (`[global::...]`) — QRY044 silently skipped | Minor | A | A | Added `AliasQualifiedNameSyntax` branch in the attribute-name switch so `[global::QuarryContextAttribute]` and extern-alias forms pass the syntactic pre-filter. |
| 5 | Correctness | QRY043 fires for entity (registered) types since they land in `RawSqlTypeKind.Dto` | Info | D | D | Dismissed: by design — the shape check is correct for entities too. |
| 6 | Correctness | QRY043 suppresses both interceptor and struct emission (positive observation) | Info | D | D | Positive observation, no action. |
| 7 | Test Quality | QRY044 `AliasQualifiedNameSyntax` attribute form is untested | Minor | B | B | Added `EmitsQRY044_WhenAttributeUsesAliasQualifiedName` covering `[global::QuarryContextAttribute]`. |
| 8 | Test Quality | Struct row types with init-only properties not tested against QRY043 | Minor | B | B | Added `RawSqlAsync_StructRowWithInitOnlyProperty_EmitsQRY043` pinning the property-loop path on structs. |
| 9 | Test Quality | `RawSqlScalarAsync<T>` threaded through QRY043 suppression paths but never exercised in tests | Minor | B | B | Added `RawSqlScalarAsync_DoesNotEmitQRY043_AndEmitsInterceptor_WhenMixedWithFailingRawSqlAsync` exercising the scalar branch in `PipelineOrchestrator` + `FileEmitter`. |
| 10 | Test Quality | Packaging (`Quarry.targets`) unverified by automated tests (by design) | Info | D | D | Dismissed: documented tradeoff in plan.md. |
| 11 | Codebase Consistency | QRY044 `Category = "QuarryAnalyzer"` vs neighboring QRY042 `"QuarryMigration"` | Info | D | D | Dismissed: defensible — analyzer-emitted, not migration-related. |
| 12 | Integration | QRY043 is Error — replaces `CS7036`/`CS8852` with a clearer message (positive observation) | Info | D | D | Positive observation, no action. |
| 13 | Integration | `Quarry.targets` package path matches NuGet convention (positive observation) | Info | D | D | Positive observation, no action. |

## Plan Compliance

The branch delivers all five planned phases — QRY043 (`DiagnosticDescriptors.cs:558-574`, `DisplayClassEnricher.cs:263-269`, `PipelineOrchestrator.cs:133-145`), nested row types (`UsageSiteDiscovery.cs:4015-4094`, `FileEmitter.cs:91-94, 269-274`), `Quarry.targets` (`src/Quarry/build/Quarry.targets`, `Quarry.csproj:32-35`), QRY044 (`AnalyzerDiagnosticDescriptors.cs:216-232`, `InterceptorsNamespacesAnalyzer.cs`), and the docs (`llm.md`, `src/Quarry.Generator/README.md`, `src/Quarry.Generator/llm.md`). Decisions in workflow.md are honored: diagnostic IDs are QRY043 (generator-side) and QRY044 (analyzer-side), skipping the already-used QRY042. The remediation commits (`be224dd`, `25f0b5e`) extend `CheckRowEntityMaterializability` to reject interfaces and abstract classes with updated diagnostic docs — a true superset of the plan.

There is modest drift from the plan's mechanical sketch:
1. Plan said add `RawCallSite.EntityNamespace` and use it to drive `FileEmitter.Emit()`'s `entityNamespaces` (plan.md:82, 116-121). Implementation instead filters by `RawSqlTypeInfo.IsNestedType` and retains `InterceptorCodeGenerator.GetNamespaceFromTypeName` (`FileEmitter.cs:91-97`). Functionally identical.
2. Plan specified branching in `RawSqlBodyEmitter` on `IsNestedType` between `ResultTypeName` and `FullyQualifiedResultTypeName` (plan.md:125-129). Implementation collapses the branch by always storing the final display form (FQN for nested, short for not) in `ResultTypeName` at discovery (`UsageSiteDiscovery.cs:4023`). Consequence: `FullyQualifiedResultTypeName` is set and persisted in state but never consumed by any emitter — it is dead on the read side (grep shows only assignments in `RawSqlTypeInfo.cs:28`, `UsageSiteDiscovery.cs:4036, 4093`, and `DisplayClassEnricher.cs:253, 376`; no reads). Minor bookkeeping noise and incremental-cache surface for no benefit.
3. Plan said register QRY044 "alongside `QuarryQueryAnalyzer`" (plan.md:234). Implementation ships as an independent `[DiagnosticAnalyzer]`-attributed class (`InterceptorsNamespacesAnalyzer.cs:20`). Roslyn discovery works identically; this is stylistic drift only.

No scope creep beyond the design decisions.

| Finding | Severity | Why It Matters |
|---|---|---|
| `RawSqlTypeInfo.FullyQualifiedResultTypeName` is written but never read — `RawSqlBodyEmitter` always consumes `ResultTypeName` which already carries the final form (`RawSqlTypeInfo.cs:79`, grep confirms zero readers) | Minor | Dead data on an `IEquatable<T>` model participates in incremental cache comparisons and clutters the public-surface ctor; either consume it in emitters per the plan or drop it from the type. |
| Plan called for a `RawCallSite.EntityNamespace` field; implementation filters via `RawSqlTypeInfo.IsNestedType` in `FileEmitter.cs:91-94` | Info | Equivalent outcome; future maintainers reading plan.md will not find the promised field. |
| Analyzer is standalone rather than "alongside `QuarryQueryAnalyzer`" (`InterceptorsNamespacesAnalyzer.cs:20-21`) | Info | Discovery works via `[DiagnosticAnalyzer]` attribute, so functionally equivalent. |

## Correctness

`CheckRowEntityMaterializability` (`DisplayClassEnricher.cs:281-314`) covers the documented cases: interface, abstract class, missing parameterless ctor, init-only property. The struct branch correctly skips the ctor check (C# always synthesizes a public parameterless ctor for structs). The property walk also excludes `IsStatic` and `IsIndexer`, which is correct.

However, the check is scoped only to `RawSqlTypeKind.Dto` (`DisplayClassEnricher.cs:263`), and `ResolveRawSqlTypeInfo` classifies *every* non-scalar type as `Dto` — including registered entity types. That is correct for QRY043 coverage (entities are also materialized via `new T()`), but it means entity-type errors (abstract entity with `[QuarryContext]` registry entry) will also trigger QRY043, which may be the first-ever QRY043 a user sees from a code path they were not reaching via RawSql. No tests exercise this combination. Low likelihood in practice.

`RawSqlTypeInfo.GetHashCode` (`RawSqlTypeInfo.cs:99`) includes `IsNestedType` but omits `FullyQualifiedResultTypeName` — acceptable because when `IsNestedType` is true, `ResultTypeName` already equals the FQN (via `displayName` at `UsageSiteDiscovery.cs:4023`), so the two fields are correlated and both redundant in the hash. `Equals` (`:90-91`) correctly includes both.

`FileEmitter.SanitizeForIdentifier` (`:884-900`) strips the leading `global::` before sanitizing. Inputs only originate from `ResolveRawSqlTypeInfo` where the FQN starts with a namespace segment (always a letter), so leading-digit outputs are impossible in practice. The empty-string guard returns early, which is harmless (the caller guards structName emission behind a non-empty `ResultTypeName`).

QRY043 suppression is complete: both the interceptor body (`FileEmitter.cs:505-510`) and the struct reader (`:246`) are gated by `site.MaterializabilityError == null`. No "half-emitted" state where one appears without the other.

`InterceptorsNamespacesAnalyzer.HasQuarryContextAttributeSyntactic` (`:72-89`) matches `QuarryContext` / `QuarryContextAttribute` across `QualifiedNameSyntax` and `SimpleNameSyntax`. An attribute written via `AliasQualifiedNameSyntax` (`[global::Quarry.QuarryContextAttribute]`) or a C# `using` alias (`using Ctx = Quarry.QuarryContextAttribute; [Ctx]`) is not matched syntactically — but the follow-up semantic check at `:44` catches the semantic-alias case by examining `AttributeClass.Name`. The `global::`-qualified case decomposes into `AliasQualifiedNameSyntax` wrapping a `QualifiedNameSyntax`; the syntactic pre-filter falls through to `_ => null` and returns false, causing the diagnostic to be missed on that exact spelling. Rare but reachable.

`PipelineOrchestrator.CollectTranslatedDiagnostics` (`:136-145`) reports QRY043 using `raw.ResultTypeName ?? raw.EntityTypeName`. These are equal for RawSql sites (both set to the same `ToFullyQualifiedDisplayString` result at `UsageSiteDiscovery.cs:3999-4001`). The fallback is defensive and harmless.

`PipelineOrchestrator` emits QRY043 via `continue`, ensuring no further QRY0xx diagnostic fires for the same site. This matches the pattern used by QRY031 immediately above. Good.

| Finding | Severity | Why It Matters |
|---|---|---|
| `HasQuarryContextAttributeSyntactic` pre-filter does not handle `AliasQualifiedNameSyntax` (e.g. `[global::Quarry.QuarryContextAttribute]`), causing the syntactic fast-path to fail and the analyzer to skip the class entirely (`InterceptorsNamespacesAnalyzer.cs:72-89`) | Minor | A user who writes the attribute with an explicit `global::` alias will silently not get QRY044, defeating the analyzer's purpose for that call site. |
| QRY043 is also raised for entity (registered) types that fail the shape check because they land in `RawSqlTypeKind.Dto` (`DisplayClassEnricher.cs:263`) | Info | Benign — the shape check is correct for entities too — but not covered by a test; entity-authors who make their entity abstract will see QRY043 on every RawSqlAsync. |
| QRY043 suppresses both interceptor and struct emission (`FileEmitter.cs:246, 505-510`) — positive observation | Info | Avoids "half-emitted" shape where one appears without the other. |

## Security

The analyzer reads `build_property.InterceptorsNamespaces` from `GlobalOptions` (`InterceptorsNamespacesAnalyzer.cs:56-58`) and does not echo the raw MSBuild value into the diagnostic message — only the symbol-derived `namespaceName` is interpolated (`:65-68`), so a malicious or malformed property value cannot inject escaped characters into the warning message. `Quarry.targets` is a trivial property-setter with no custom `<Exec>` or `<Import>`. No new dependencies are introduced. No runtime changes.

No concerns.

## Test Quality

Phase 1/2/4 are well tested. `RawSqlGeneratorPipelineTests.cs` adds:
- Nested row type positive compile (`:256-316`)
- Nested row type struct-reader fallback with `SELECT Id*2, Name` (`:320-375`) — covers `SanitizeForIdentifier` + FQN struct emission
- Namespace-level row regression asserting `using TestApp.Rows;` (`:377-434`)
- QRY043 positional record (`:438-488`), init-only (`:490-536`), abstract class (`:538-582`), interface (`:584-628`), plain-class negative (`:630-672`)

`InterceptorsNamespacesAnalyzerTests.cs` covers: opted-in negative, opted-out, global namespace (skipped), multiple contexts with mixed opt-in, explicit-null property, non-context class. All six cases pass clear assertions on severity and message contents.

Gaps:
- No test for `CheckRowEntityMaterializability` with a struct that has `init`-only properties. Structs skip the ctor branch (`DisplayClassEnricher.cs:297`) so the property loop is the only path. A `struct { int Id { get; init; } }` would exercise the init-only path on structs — currently untested.
- `RawSqlScalarAsync<T>` path for QRY043 is enumerated in `PipelineOrchestrator.cs:137` and `FileEmitter.cs:508` but only `RawSqlAsync<T>` is exercised in tests. Scalars skip the materializability check entirely (`DisplayClassEnricher.cs:263`) so no diagnostic should be reached via `RawSqlScalarAsync<UserRow>`; this implicit contract is not asserted.
- QRY044 with `AliasQualifiedNameSyntax` attribute form (`[global::Quarry.QuarryContextAttribute]`) is not tested — this ties to the Correctness finding above and would have caught the gap.
- `Quarry.targets` itself is declared untestable in plan.md:188-193 ("Packaging is difficult to unit-test"). No E2E verification that the file lands in the nupkg or that `InterceptorsNamespaces` resolves as expected in a downstream consumer. Reasonable tradeoff.

| Finding | Severity | Why It Matters |
|---|---|---|
| QRY044 `AliasQualifiedNameSyntax` attribute form (`[global::Quarry.QuarryContextAttribute]`) is untested | Minor | The correctness gap above would be caught by a test asserting the diagnostic fires on the global-prefixed attribute spelling. |
| Struct row types with init-only properties are not tested against QRY043 | Minor | Structs skip the ctor branch, so the property-only path is not pinned; a regression that lost the property loop would silently pass existing tests. |
| `RawSqlScalarAsync<T>` is threaded through QRY043 suppression paths but never exercised end-to-end in tests | Minor | Scalars don't hit `CheckRowEntityMaterializability`, but the explicit `Kind is RawSqlAsync or RawSqlScalarAsync` branch in `PipelineOrchestrator.cs:137` is dead unless tested. |
| Packaging (`Quarry.targets`) is unverified by automated tests — by design | Info | Plan explicitly scopes this as manual verification; documented tradeoff. |

## Codebase Consistency

`DiagnosticDescriptors.RowEntityNotMaterializable` (`DiagnosticDescriptors.cs:558-574`) matches existing descriptor styling: grouped region header (`// ─── RawSql row-shape diagnostics (QRY043) ───`), `Category = "Quarry"`, `DiagnosticSeverity.Error`, and a `description` that restates the remediation. Placement between QRY041 and QRY060-65 (navigation join) is natural.

`AnalyzerDiagnosticDescriptors.InterceptorsNamespaceMissing` (`:216-232`) uses `Category` (= `"QuarryAnalyzer"` per the file header) while neighboring QRY042 (`RawSqlConvertibleToChain`, lines 204-214) uses `MigrationCategory` = `"QuarryMigration"`. That is defensible (QRY044 is not migration-related) but inconsistent with the file's immediate neighbor; the file mixes the two categories by intent.

`InterceptorsNamespacesAnalyzer` lives at the assembly root next to `QuarryQueryAnalyzer.cs` and `RawSqlMigrationAnalyzer.cs` rather than under `Rules/`, consistent with how the other top-level `[DiagnosticAnalyzer]` classes are placed. `internal sealed class : DiagnosticAnalyzer` matches sibling modifiers.

`RawSqlTypeInfo` ctor uses the existing "all args optional with defaults" pattern (`:16-20`); call sites that don't care about the new fields compile unchanged. `RawCallSite.MaterializabilityError` is deliberately excluded from `Equals`/`GetHashCode` (comment at `:237-240`), matching the mutable-enrichment-field convention already used by `RawSqlTypeInfo`, `DisplayClassName`, and `EnrichmentInvocation`. All three `With*` copy methods propagate the new field (`:306, 371, 439`) — good.

`FileEmitter.SanitizeForIdentifier` is a private static helper added to `FileEmitter` — no pre-existing sanitizer utility existed that could be reused. A similar identifier-derivation helper exists in other emitters for struct naming but for different domains; duplication is minimal.

`llm.md` and `src/Quarry.Generator/README.md`/`llm.md` all updated with QRY043 and QRY044 entries, consistent with how prior diagnostics are documented (table rows + inline prose).

| Finding | Severity | Why It Matters |
|---|---|---|
| QRY044 uses `Category = "QuarryAnalyzer"` while the immediately adjacent QRY042 uses `Category = "QuarryMigration"` (`AnalyzerDiagnosticDescriptors.cs:204, 224`) | Info | Consumers filtering analyzer diagnostics by category in their IDE rule-set may see the two rules in different groupings despite living in the same file. |

## Integration / Breaking Changes

- `RawSqlTypeInfo` ctor gains two optional parameters (`isNestedType`, `fullyQualifiedResultTypeName`) with defaults (`RawSqlTypeInfo.cs:16-20`). All internal call sites pass them explicitly; external callers (there are none — type is `internal`) would compile unchanged.
- `RawCallSite.MaterializabilityError` is a new mutable property excluded from equality, preserving incremental-cache stability.
- `TranslatedCallSite.MaterializabilityError` surfaces `Bound.Raw.MaterializabilityError` through the existing indirection pattern (`:85`). Consistent with neighboring passthroughs.
- `Quarry.targets` uses the `$(InterceptorsNamespaces);Quarry.Generated` append pattern. NuGet imports package `.targets` after consumer `<PropertyGroup>` elements, so a consumer that sets `<InterceptorsNamespaces>MyApp.Data</InterceptorsNamespaces>` lands on `MyApp.Data;Quarry.Generated`. In-repo test projects already use `$(InterceptorsNamespaces);` prefix (`src/Samples/*/*.csproj`, `src/Quarry.Tests/Quarry.Tests.csproj:11`), so composition works.
- `Quarry.Generator.props` adds `InterceptorsNamespaces` as a `CompilerVisibleProperty` (`:5`). Existing consumers of the props file (all Quarry consumers) see the new surface with no action required.
- QRY044 severity is `Warning` on a condition that would eventually surface as `CS9137` (Error) — this is an early surfacing, not a new build-breaker. Warning is the right level because a `Directory.Build.props` the analyzer cannot see could legitimately set `InterceptorsNamespaces`.
- QRY043 severity is `Error` — breaking for code that currently hits `CS7036`/`CS8852` at compile time. Behavior net-better: users get a Quarry-level diagnostic at the RawSqlAsync call site rather than two Roslyn errors inside generated code. No users see a pass-to-fail regression because those code paths never compiled.
- `FullyQualifiedResultTypeName` added to the `IEquatable<RawSqlTypeInfo>` identity set (`:91`). Because it's always equal to `ResultTypeName` when `IsNestedType` is true and defaults to `ResultTypeName` otherwise, it does not practically widen the cache key.

| Finding | Severity | Why It Matters |
|---|---|---|
| QRY043 is `Error` — by design, and replaces `CS7036`/`CS8852` with a clearer message (`DiagnosticDescriptors.cs:568`) | Info | Positive — no existing code could have compiled in these shapes, so there is no silent-break scenario. |
| `Quarry.targets` package path `build\` matches NuGet convention; no conflict with the existing `Quarry.Generator.props` packaged under `build\` of a separate package (`Quarry.csproj:32-35`, `Quarry.Generator.csproj:36-38`) | Info | Positive observation — the two packages each own their own build asset. |

## Issues Created

(Leave empty — to be filled during classification.)
