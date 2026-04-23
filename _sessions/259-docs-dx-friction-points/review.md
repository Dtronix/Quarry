# Review: #259

## Classifications

| # | Section | Finding (one line) | Sev | Rec | Class | Action Taken |
|---|---------|---------------------|-----|-----|-------|--------------|
| 1 | Plan Compliance | `RawCallSite.EntityNamespace` field from plan was not added; Phase 2 filters by `RawSqlTypeInfo.IsNestedType` instead | Info | D | D | Dismissed: functionally equivalent outcome. |
| 2 | Plan Compliance | Analyzer not registered "alongside `QuarryQueryAnalyzer`"; ships as a standalone `[DiagnosticAnalyzer]` class | Info | D | D | Dismissed: Roslyn discovery works identically. |
| 3 | Correctness | `CheckRowEntityMaterializability` does not reject abstract classes or interfaces used as `T` | Minor | B | B | Fixed in `be224dd` — abstract class + interface rejection added to `CheckRowEntityMaterializability`; docs updated in `25f0b5e`. |
| 4 | Correctness | QRY043 suppression covers both the interceptor and the struct emission (positive observation) | Info | D | D | Positive observation, no action. |
| 5 | Test Quality | No test for nested row type taking the struct-reader fallback branch | Minor | B | B | Fixed in `be224dd` — added nested-row struct-reader test covering `SanitizeForIdentifier` + FQN struct emission. |
| 6 | Test Quality | Namespace-level-row regression does not assert the `using Rows;` directive is emitted | Minor | B | B | Fixed in `be224dd` — regression now asserts the `using TestApp.Rows;` directive. |
| 7 | Test Quality | QRY044 with `build_property.InterceptorsNamespaces` explicitly null is not directly tested | Minor | B | B | Fixed in `be224dd` — added explicit-null property test pinning null/empty convergence. |
| 8 | Codebase Consistency | QRY044 uses `Category = "QuarryAnalyzer"` while neighboring QRY042 uses `"QuarryMigration"` | Info | D | D | Defensible: analyzer-emitted, not migration-related. |
| 9 | Integration | Nested-type FQN emission uses Roslyn `global::`-prefixed names (positive observation) | Info | D | D | Positive observation, no action. |

## Plan Compliance

The plan specified adding an `EntityNamespace` (string?) field to `RawCallSite` and wiring `FileEmitter` to consume that directly (plan.md lines 82, 116-121). The actual Phase 2 implementation skipped that field — `RawCallSite` only gained `MaterializabilityError` (`src/Quarry.Generator/IR/RawCallSite.cs:237-240`). Instead, `FileEmitter.Emit()` (`src/Quarry.Generator/CodeGen/FileEmitter.cs:92-97`) filters out nested sites by `s.RawSqlTypeInfo?.IsNestedType != true` and still runs the old `GetNamespaceFromTypeName` path. Functionally equivalent, but it drifts from the plan's "resolve the actual namespace from the Roslyn symbol and store it" approach.

Plan also specified registering the analyzer alongside `QuarryQueryAnalyzer` (plan.md line 234); actual impl ships `InterceptorsNamespacesAnalyzer` as an independent `[DiagnosticAnalyzer]` class (`src/Quarry.Analyzers/InterceptorsNamespacesAnalyzer.cs:20-21`). Roslyn discovery works on attribute, so this is benign. It is placed at the assembly root (next to `QuarryQueryAnalyzer.cs`, `RawSqlMigrationAnalyzer.cs`) rather than under `Rules/` — consistent with how the other top-level analyzer classes are organized.

| Finding | Severity | Why It Matters |
|---|---|---|
| `RawCallSite.EntityNamespace` field from plan was not added; Phase 2 instead filters by `RawSqlTypeInfo.IsNestedType` in `FileEmitter.cs:92-97` | Info | Plan drift. Outcome is equivalent for the RawSql path, but a future maintainer reading plan.md will not find the promised field. |
| Analyzer not registered "alongside `QuarryQueryAnalyzer`"; ships as standalone class with `[DiagnosticAnalyzer]` | Info | Works identically for consumers; discrepancy with plan text only. |

## Correctness

- `CheckRowEntityMaterializability` (`DisplayClassEnricher.cs:281-314`): the struct branch skips the constructor check. Safe — C# requires a public parameterless constructor on a struct regardless of declared visibility (the implicit one always exists as public). `typeArgSymbol.TypeKind == TypeKind.TypeParameter || TypeKind.Error` is filtered out earlier at line 211, so generics/errors cannot reach this method. However, the check does not reject abstract classes or interfaces — if a user were able to pass an abstract class as `T`, `CheckRowEntityMaterializability` would see a public parameterless ctor and approve, and `new T()` would then fail downstream with `CS0144`. Unlikely in practice because `RawSqlAsync<T>` is generic without a `new()` constraint but also without special handling.
- `RawSqlTypeInfo.Equals`: includes all new fields. `GetHashCode` (`RawSqlTypeInfo.cs:99`) omits `FullyQualifiedResultTypeName`, which is acceptable since it is derived-correlated with `ResultTypeName` and including it would not materially change distribution.
- `FileEmitter.SanitizeForIdentifier` (`FileEmitter.cs:888-901`): for inputs beginning with `global::`, strips the prefix, then replaces every non-alphanumeric/underscore. For the nested-type FQNs produced by `ResolveRawSqlTypeInfo` the first character is always a letter (the root namespace), so leading-digit identifiers are not possible in practice. Empty input returns the original empty string unchanged (early return at 890-891); but callers only reach this with a non-empty `ResultTypeName`, so this is not exercisable.
- `InterceptorsNamespacesAnalyzer.HasQuarryContextAttributeSyntactic` (`InterceptorsNamespacesAnalyzer.cs:72-89`): matches `QuarryContext`/`QuarryContextAttribute`, walking `QualifiedNameSyntax` to its `Right`. Catches `[QuarryContext]`, `[Quarry.QuarryContext]`, `[global::Quarry.QuarryContext]`. It does NOT guard against attributes with `AliasQualifiedNameSyntax` (e.g., `using Foo = Quarry.QuarryContextAttribute; [Foo]`) — rare but the semantic-level check at line 44 catches it. Assembly-level `[assembly: QuarryContext]` is irrelevant here because the analyzer only registers for `SyntaxKind.ClassDeclaration`.
- QRY043 suppression (`FileEmitter.cs:505-510`): suppresses the interceptor method itself. Struct emission is also gated by `site.MaterializabilityError == null` at `FileEmitter.cs:246`, so the `file struct IRowReader<T>` is not emitted either. Consistent.
- `PipelineOrchestrator.CollectTranslatedDiagnostics` (`PipelineOrchestrator.cs:136-145`): reports QRY043 using `raw.ResultTypeName ?? raw.EntityTypeName`. For RawSql sites `resultTypeName == entityTypeName` (`UsageSiteDiscovery.cs:3989-3990`), so both render the same FQN.

| Finding | Severity | Why It Matters |
|---|---|---|
| `CheckRowEntityMaterializability` does not reject abstract classes or interfaces used as `T` | Minor | Unlikely in practice (most users supply concrete DTOs), but if it occurs the user will see a downstream CS0144 against generated code rather than a clear QRY043. |
| QRY043 suppression covers both the interceptor and the struct emission (`FileEmitter.cs:246` and `:508-510`) | Info | Positive observation — no "half-emitted" state. |

## Security

No new runtime surface. `build_property.InterceptorsNamespaces` is read by the analyzer and echoed back into the compiler-time warning message as the literal `<InterceptorsNamespaces>$(InterceptorsNamespaces);{1}</InterceptorsNamespaces>` template (`AnalyzerDiagnosticDescriptors.cs:226-228`); only the namespace symbol name (not the raw property value) is interpolated, so malformed MSBuild values cannot escape into a misleading message.

No concerns.

## Test Quality

Phase 1/2/4 tests cover the intended happy-path and failure-mode triggers:
- QRY043: positional record (`RawSqlGeneratorPipelineTests.cs:381-431`), init-only (`:433-479`), plain-class negative (`:481-524`). Severity and message contents are asserted.
- Nested row type: positive compile + FQN emission (`:256-316`); regression confirming namespace-level types still use short name + `using` (`:318-375`).
- QRY044: opted-in, missing, global namespace, multiple-contexts-mixed, non-context (`InterceptorsNamespacesAnalyzerTests.cs:50-151`).

Gaps:
- Nested row type + struct-reader fallback is not tested: the nested test SQL `"SELECT Id, Name FROM users"` resolves via `RawSqlColumnResolver` and emits a static lambda; `SanitizeForIdentifier` and the `file struct RawSqlReader_TestApp_Host_NestedRow_0` path never run. A test with an unresolvable SQL (e.g. `"SELECT id*2 FROM users"`) on a nested row type would close this.
- The "namespace-level row still uses short name" assertion (`RawSqlGeneratorPipelineTests.cs:371-374`) checks for `new UserRow()` and absence of `global::TestApp.Rows.UserRow`. Correct for the happy-path but it does not verify that a `using TestApp.Rows;` directive is emitted. A grep for the `using` would complete the regression guarantee.
- QRY044 when `build_property.InterceptorsNamespaces` is entirely absent (null, not empty) combined with a `[QuarryContext]` class is not tested. Production code at `InterceptorsNamespacesAnalyzer.cs:57-62` treats null identically to empty, which is correct — but a test asserting "null + context in `MyApp.Data` → QRY044" would pin the behavior.

| Finding | Severity | Why It Matters |
|---|---|---|
| No test for nested row type taking the struct-reader fallback branch | Minor | `SanitizeForIdentifier` and `EmitRowReaderStruct` with an FQN `ResultTypeName` are untested; a regression could ship silently. |
| Namespace-level-row regression does not assert the `using Rows;` directive is emitted | Minor | Currently confirms the short name is used but not that the namespace is imported; a bug that forgot the `using` would still pass this test. |
| QRY044 with `build_property.InterceptorsNamespaces` explicitly null is not directly tested | Minor | Covered indirectly (null and empty converge at line 57-62), but an explicit test would pin the contract. |

## Codebase Consistency

- `CheckRowEntityMaterializability` is a new method; no earlier equivalent in the repo (DTO projection validation at the chain level lives in `ProjectionAnalyzer`, which looks at projection lambdas, not row-type materializability). No reusable utility existed to share.
- `RawSqlTypeInfo.IsNestedType`/`FullyQualifiedResultTypeName` are used only by the RawSql emitter — correct scoping. Other emitters (`ClauseBodyEmitter`, `JoinBodyEmitter`, etc.) work off `EntityTypeName` from `TranslatedCallSite`, which is already FQN-capable via `ToFullyQualifiedDisplayString()`. No lurking inconsistency.
- `DiagnosticDescriptors.RowEntityNotMaterializable` (`DiagnosticDescriptors.cs:564-576`) matches the style of the existing QRY0xx entries: `id`, `title`, `messageFormat` with argument placeholders, `category = "Quarry"`, severity, and a description that restates the remediation. Placement in a grouped region (`// ─── RawSql row-shape diagnostics (QRY043) ───`) matches the file's organization convention.
- `AnalyzerDiagnosticDescriptors.InterceptorsNamespaceMissing` (`AnalyzerDiagnosticDescriptors.cs:223-232`) is placed under the `Category = "QuarryAnalyzer"` (same as the QRA rules) rather than the `MigrationCategory = "QuarryMigration"` used for QRY042. That categorization is defensible (analyzer-emitted, not migration-related) but inconsistent with QRY042, the immediate neighbor — both are QRY-prefixed in the analyzer assembly and could reasonably share a category.

| Finding | Severity | Why It Matters |
|---|---|---|
| QRY044 uses `Category = "QuarryAnalyzer"` while neighboring QRY042 uses `"QuarryMigration"` | Info | Both live in `Quarry.Analyzers` but fall under different IDE rule-set groupings. Users filtering by category may miss one. |

## Integration / Breaking Changes

- `RawSqlTypeInfo` ctor: both new parameters (`isNestedType`, `fullyQualifiedResultTypeName`) are optional with defaults. All existing call sites compile unchanged. Verified in `PatchWithColumnMetadata` (`DisplayClassEnricher.cs:361-369`) and the scalar/DTO callers in `UsageSiteDiscovery.cs:4029-4036, 4087-4093` — all pass the new args explicitly.
- `RawCallSite`: added mutable `MaterializabilityError` property. Excluded from `Equals`/`GetHashCode` (`RawCallSite.cs:237-240` comment confirms), consistent with other mutable enrichment fields like `RawSqlTypeInfo` and `DisplayClassName`. All three `With*` copy methods propagate it (`RawCallSite.cs:306, 371, 439`).
- `Quarry.targets`: uses `<InterceptorsNamespaces>$(InterceptorsNamespaces);Quarry.Generated</InterceptorsNamespaces>` (`src/Quarry/build/Quarry.targets:12`). NuGet imports package `.targets` *after* the consuming project's `<PropertyGroup>`, so a consumer that already sets `<InterceptorsNamespaces>MyApp.Data</InterceptorsNamespaces>` ends up with `MyApp.Data;Quarry.Generated`. No override risk. Existing sample/test projects all use the `$(InterceptorsNamespaces);` prefix pattern (`src/Samples/*/*.csproj`, `src/Quarry.Tests/Quarry.Tests.csproj:11`), so everything composes.
- QRY044 severity is `Warning` on a condition that would otherwise surface as CS9137 error. This does not clutter output — the CS9137 would fire once per namespace per build; QRY044 fires once per context class per build (typically 1-2). QRY044 is also emitted earlier (IDE authoring time) rather than only at compile time, which is the explicit design goal.
- `Quarry.Generator.props` adds `InterceptorsNamespaces` as a `CompilerVisibleProperty` (`build/Quarry.Generator.props:7`). Existing consumers of this props file (all Quarry consumers, since `Quarry.Generator` is a PackageReference) see the property surface to analyzers with no action required.

| Finding | Severity | Why It Matters |
|---|---|---|
| Nested-type FQN emission assumes the FQN includes `global::` prefix (`RawSqlBodyEmitter.cs:57, 59`). `typeSymbol.ToFullyQualifiedDisplayString()` in Roslyn returns `global::`-prefixed names, so `new global::Outer.Row()` is emitted for nested types — legal C# syntax. | Info | Positive observation — no parsing issues. |

## Issues Created

(Leave empty — to be filled during classification.)
