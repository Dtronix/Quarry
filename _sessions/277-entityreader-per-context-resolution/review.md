# Review: 277-entityreader-per-context-resolution (Pass 2)

## Classifications

| # | Section | Finding (one line) | Sev | Rec | Class | Action Taken |
|---|---------|--------------------|-----|-----|-------|--------------|
| 1 | Correctness | Nested-type FQN edge case in `ResolvePerContextReaderFqn` — splits only on last `.`, ignores `+` for nested types | Low | C | A | Updated `ResolvePerContextReaderFqn` to also handle `+` separator (CLR nested-type encoding) by taking `Math.Max(LastIndexOf('.'), LastIndexOf('+'))`. XML doc updated. |
| 2 | Correctness | No generator test asserts the `IQueryBuilder<TIn, TOut>` chain shape (`<App.Pg.Product, App.Pg.Product>`) directly — only implicitly via cross-dialect compile | Low | A | A | Added two `Assert.That(..., Does.Match(...))` regex assertions to `Generator_PerContextEntityReader_RewritesFqnPerContext` locking in `IQueryBuilder<App.Pg.Product, App.Pg.Product>` and `IQueryBuilder<App.My.Product, App.My.Product>`. |
| 3 | Correctness | QRY026 message reports schema-namespace FQN even when actual reader emitted is per-context | Low | D | D | Out of scope. |
| 4 | Test Quality | No test exercises a context that LACKS a per-context reader → no assertion that the C# compile error actually happens (the load-bearing contract of the PR) | Medium | A | A | Added `Generator_PerContextEntityReader_MissingPerContextReader_ProducesCompileError` test that compiles a chain whose context (App.Pg) deliberately lacks `App.Pg.ProductReader`, runs the generator, and asserts the output compilation reports CS0234 or CS0246 against `ProductReader`. |
| 5 | Test Quality | No cross-dialect entity-reader coverage for set operations, CTE inner chains, or joined identity projections | Low | C | A | Added `Union_IdentityProjection_UsesCustomReader` and `Cte_FromCte_IdentityProjection_UsesCustomReader` to `CrossDialectEntityReaderTests.cs`. Both verify per-context reader runs after UNION / CTE on all four dialects. Joined-identity case skipped — Product has no FK relationships in the existing schema set, so the pattern is unnatural without fixture changes outside this PR's scope. |

Applied: 1→A, 5→A (user override "C->A Implement all A&C Now"). Final: **4A / 0B / 0C / 1D**.

Second-pass review of the 5-functional-commit branch (871fb73 -> b10edb9; +bb69cec/486a3c8 add session metadata) cross-referenced against `plan.md` and `workflow.md`. Re-examined `ResolvePerContextReaderFqn`, both call seams, the IR and incremental-cache plumbing, every changed test file, and the manifest deltas. The pass-1 review was disregarded as an anchor.

## Plan Compliance

No concerns.

All six phases landed in the planned dependency order. The Phase 5 manifest verification was folded into the Phase 4 commit because the manifest deltas (`postgresql/mysql/sqlserver` +86 lines from new cross-dialect chains; `sqlite` -1 from the removed Integration suite) are direct consequences of the Phase 4 conversion. The `pr-body.md` "Gaps in original plan implemented" entry truthfully records that the planned `IQueryBuilder<TIn, TOut>` `TOut` fix turned out to be a no-op because `chain.EntityTypeName` already carried the per-context FQN at the projection-analysis layer; this matches what the generated `PgDb.Interceptors.*.g.cs` shows on disk (`IQueryBuilder<Quarry.Tests.Samples.Pg.Product, Quarry.Tests.Samples.Pg.Product>`).

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `ResolvePerContextReaderFqn` (`src/Quarry.Generator/Generation/InterceptorCodeGenerator.cs:218-226`) splits on the **last `.`** only and silently drops any nested-type containment. A user-declared schema-namespace reader nested inside an outer type (e.g. `App.Schemas.Container.ProductReader`) yields simple-name `ProductReader` and produces per-context FQN `<contextNamespace>.ProductReader` — looking for a top-level reader, not a nested one. Same for `+` separator (the field-name function `GetEntityReaderFieldName` strips both `.` and `+`, suggesting `+` could appear in some path; if it ever does, `LastIndexOf('.')` would mis-split there too). | Low | Nested readers are unusual but not forbidden, and the C# compile error a user would get ("type or namespace 'ProductReader' not found in namespace 'App.Pg'") is actionable. Worth a one-line note in `llm.md` or a defensive check that `schemaReaderFqn` actually contains only `.` and emits a clearer diagnostic if a nested reader is detected. |
| `Generator_PerContextEntityReader_RewritesFqnPerContext` (`src/Quarry.Tests/GeneratorTests.cs:931-1044`) verifies the **cached field FQN** rewrite for `PgDb` and `MyDb` but does not assert the **`IQueryBuilder<TIn, TOut>` chain shape** — the very issue the user surfaced from #277. The shape is implicitly enforced by the cross-dialect tests (a regressed `TOut` would fail to compile), but no generator-level test directly locks in `IQueryBuilder<App.Pg.Product, App.Pg.Product>`. A future change to projection analysis that inadvertently re-binds `TOut` to the schema-namespace entity would be caught by `CrossDialectEntityReaderTests` failing to *compile* — a confusing failure mode without a focused assertion. | Low | The PR's `pr-body.md` ("Gaps in original plan implemented") explicitly claims `TOut` already binds correctly, which is true today but is the exact thing that should have a regression test. A one-line `Assert.That(pgCode, Does.Match("IQueryBuilder<App\\.Pg\\.Product,\\s*App\\.Pg\\.Product>"))` in the existing test would close this. |
| QRY026 informational diagnostic at `src/Quarry.Generator/QuarryGenerator.cs:303-307` continues to report `entity.CustomEntityReaderClass` (the **schema-namespace** FQN). After this PR the actual reader executed at runtime is the per-context one, so a multi-context consumer sees a QRY026 message naming `App.Schemas.ProductReader` even though only `App.Pg.ProductReader` and `App.My.ProductReader` are ever instantiated. | Low | Cosmetic. The diagnostic still correctly indicates that a custom reader is configured at the schema. Fixing requires either suppressing QRY026 in multi-namespace cases or rewording the message. Not material. |

`ResolvePerContextReaderFqn`'s null/empty `contextNamespace` fallback path is sound (returns the schema-namespace FQN unchanged); the `lastDot >= 0` guard correctly handles a root-namespace reader whose FQN has no dots; both call seams pass `_contextNamespace`/`group.ContextNamespace` from the same upstream `FileInterceptorGroup.ContextNamespace`, and `PipelineOrchestrator.BuildFileInterceptorGroups` (`src/Quarry.Generator/IR/PipelineOrchestrator.cs:387-391`) groups by `(ContextClassName, FilePath)` so a single FileEmitter cannot mix sites from different contexts.

`AssembledPlan` instances do not cross context groups: the filter at `PipelineOrchestrator.cs:411-420` (`plan.ExecutionSite.Bound.ContextClassName == contextClassName && plan.ExecutionSite.Bound.Raw.FilePath == filePath`) keeps each plan in exactly one group. The `if (assembled.ReaderDelegateCode == null)` guard at `QuarryGenerator.cs:601` is therefore not a correctness hazard — the cross-context-mutation scenario it could leak doesn't occur in practice.

`AssembledPlan.Equals` (`src/Quarry.Generator/IR/AssembledPlan.cs:269`) includes `ReaderDelegateCode`. After this PR the same schema referenced from two contexts produces two AssembledPlans with **different** `ReaderDelegateCode` (per-context-rewritten field names) — but those plans were already non-equal because their `ExecutionSite.Bound.ContextNamespace` differed (`BoundCallSite.Equals:125`). No new false-equal or false-non-equal cases are introduced; the incremental-generator cache invalidates correctly when context namespaces change.

Carrier and terminal emission paths (`CarrierEmitter.cs:374`, `TerminalBodyEmitter.cs:95,167`) consume the precomputed `chain.ReaderDelegateCode` string — they do not re-derive the field name, so they automatically inherit the rewrite. No separate seam was missed.

## Security

No concerns.

`contextNamespace` is sourced from `[QuarryContext]`-annotated `INamedTypeSymbol.ContainingNamespace` strings — Roslyn-validated identifiers, never user-supplied at runtime. The string concatenation `contextNamespace + "." + simpleName` cannot produce invalid C# unless an upstream parser bug introduced one (out of scope).

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No generator test exercises a context that **lacks** a per-context reader when one is required. The pass-2 prompt called this out explicitly: "what happens when ONE context has a per-context reader and ANOTHER context referencing the same schema does NOT?" The branch's premise is "missing per-context readers surface as ordinary C# compile errors" — but no test confirms the C# compile error actually happens in that case. The three new generator tests all set up valid per-context readers; the negative path is unverified. | Medium | This is the load-bearing user-facing contract of the PR. A test that compiles a chain whose context lacks a per-context reader and asserts the resulting Compilation has a `CS0234`/`CS0246` (type-or-namespace not found) diagnostic on the generated tree would make the contract executable. The existing `RunGenerator` test infrastructure can do this — `RunGeneratorWithDiagnostics` plus `compilation.AddSyntaxTrees(result.GeneratedTrees).GetDiagnostics()` would surface the user-visible compile error. |
| `CrossDialectEntityReaderTests.cs` does not exercise an identity entity projection inside a set operation, CTE inner chain, or join — paths that flow through a different ChainAnalyzer seam (e.g., `ChainAnalyzer.cs:769` for lambda-inner CTE projections, which carry `CustomEntityReaderClass` from the inner plan). The set-operation cross-dialect coverage uses tuple projections, which do not trigger custom-reader emission. If a future change in CTE/lambda-inner enrichment dropped the `CustomEntityReaderClass` propagation, no entity-reader test would catch it. | Low | Niche but real: lambda-inner chains carry `lambdaInnerPlan.Projection.CustomEntityReaderClass` through the reduced-projection rebuild at `ChainAnalyzer.cs:765-770`. A regression here would be invisible to current coverage. Not strictly required for this PR's scope but would harden the deferred-Phase-10 conversion claim ("end-to-end validation in real interceptor execution"). |
| The deleted `Integration/EntityReaderIntegrationTests.cs` had `Select_IdentityWithWhere_UsesCustomReader` which the workflow log calls "functionally identical" to `Select_IdentityProjection_UsesCustomReader` and drops. Reading both source bodies confirms they ran identical assertions on the same `ProductId == 1` filter — the dedup is correct, no coverage was lost. | — | Not a finding; recorded for transparency since the user questioned the test deletion. The test count is `9 -> 8` per dialect, and each surviving test now runs across 4 dialects (effective `9*1 -> 8*4 = 32` logical runs vs. `9` previously). |

The 8 cross-dialect tests cover identity projection (3 variants: single-row, NULL handling, multi-row), tuple/single-column negative cases, `ExecuteFetchFirst{,OrDefault}`, and an Insert+Select round-trip. The 3 generator pipeline tests cover multi-namespace rewrite, single-context-same-namespace preservation, and cached-field deduplication. Test names and region grouping match the cross-dialect-test-coverage convention.

## Codebase Consistency

No concerns.

`ResolvePerContextReaderFqn` follows existing utility-method conventions in `InterceptorCodeGenerator` (`internal static`, brief XML doc, dialect-agnostic). The optional `string? contextNamespace = null` parameter pattern matches `GenerateInterceptorsFile`'s existing signature. New tests live in the established `GeneratorTests.cs` regions and follow the `RunGenerator` / `RunGeneratorWithDiagnostics` helpers used by neighboring QRY026/QRY027/QRY070/QRY071 tests. `PerContextProductReaders.cs` mirrors the global `ProductSchema.cs` reader's `Read` body verbatim across `Pg`/`My`/`Ss` — no divergence in column ordering, NULL handling, or `DisplayLabel` formatting that could cause cross-dialect assertion drift.

## Integration / Breaking Changes

No concerns.

The behavior change is the documented strict improvement: single-context-same-namespace consumers see byte-identical emit (per-context FQN equals schema-namespace FQN by construction); multi-context-different-namespace consumers previously got `Unsafe.As<>`-papered-over chain shape mismatches and now get either a correct chain shape (with per-context readers provided) or an explicit C# compile error against the missing class (without). The `llm.md` "EntityReader" section documents this rule clearly. No public API changes; the two new optional parameters on `CollectEntityReaderInstances` and `GenerateReaderDelegate` are `internal static` and default to `null` (preserving prior behavior for any external caller).

Manifest deltas (`+86` lines on PG/My/Ss, `-1` on Lite, total-discovered counts updated) match the Phase 4 test conversion exactly — 9 Insert/Select chain shapes added per non-Lite dialect (some chain shapes dedupe with existing entries, accounting for the +86 delta vs. the raw 9 new tests * ~5 chain shapes each), 1 unique SQLite-only chain shape removed.
