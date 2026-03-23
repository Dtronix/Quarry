# Quarry Generator: Big Bang Compiler Switchover Plan

## 1. Overview

This plan replaces the incremental adapter-based migration (impl-plan-compiler-finishes.md) with a single coordinated switchover. Instead of maintaining parallel old/new pipelines with fragile adapter layers, we build the complete new pipeline end-to-end and switch in one shot. The 2,943-test suite validates the result.

**Why big bang over incremental:** The adapter approach (converting TranslatedCallSite → UsageSiteInfo → re-enrichment) is fundamentally broken. The old and new types have different data shapes, different nullability contracts, and different enrichment timing. Every adapter boundary introduces fidelity gaps that cascade into test failures. The only reliable path is: build the new system completely, delete the old system completely, fix gaps against the test suite.

**End state pipeline:**
```
RawCallSite (discovery)
  → BoundCallSite (entity binding)
    → TranslatedCallSite (SQL translation)
      → [.Collect()]
        → ChainAnalyzer → QueryPlan (chain grouping + tier classification)
          → SqlAssembler → AssembledPlan (SQL rendering per mask)
            → CarrierAnalyzer → CarrierPlan (carrier optimization)
              → FileEmitter → generated C# source
```

No UsageSiteInfo, no PrebuiltChainInfo, no ClauseInfo, no adapters.

---

## 2. Constraints (unchanged from original plan)

- **netstandard2.0 target**: No `record` types, no `init` properties, no `Span<T>`.
- **Roslyn 5.0 API surface**: `CreateSyntaxProvider`, `Combine`, `Collect`, `SelectMany`, `Select`, `RegisterSourceOutput`.
- **All pipeline types must implement `IEquatable<T>`** with correct `Equals`/`GetHashCode`.
- **No `SyntaxNode` in cached values**: Value-type `DiagnosticLocation` only.
- **Transforms must be pure functions**: No mutable shared state.

---

## 3. Current State

**Already built (new IR):**
- `RawCallSite` (224 lines) — discovery result with chain analysis fields
- `BoundCallSite` (92 lines) — entity-bound call site
- `TranslatedCallSite` / `TranslatedClause` (129 lines) — translated with SqlExpr + parameters
- `CallSiteBinder` (175 lines) — binds RawCallSite + EntityRegistry → BoundCallSite
- `CallSiteTranslator` (283 lines) — translates BoundCallSite → TranslatedCallSite
- `EntityRegistry` (266 lines) — multi-key entity metadata lookup
- `EntityRef` (87 lines) — lightweight entity metadata reference
- `QueryPlan` (465 lines) — chain analysis output type (exists, not yet populated by ChainAnalyzer)
- `AssembledPlan` (95 lines) — SQL assembly output type (exists, not yet populated)
- `SqlExprParser` (582 lines), `SqlExprBinder` (278 lines), `SqlExprRenderer` (336 lines), `SqlExprClauseTranslator` (288 lines) — expression pipeline
- `PipelineOrchestrator` (257 lines) — collected stage orchestrator (currently consumes UsageSiteInfo)
- `CarrierAnalyzer` (243 lines) — carrier eligibility (currently consumes old types)

**Already built (codegen emitters — currently consume UsageSiteInfo):**
- `FileEmitter` (683 lines), `RawSqlBodyEmitter` (165 lines), `TransitionBodyEmitter` (175 lines), `ClauseBodyEmitter` (1,244 lines), `JoinBodyEmitter` (770 lines), `TerminalBodyEmitter` (666 lines), `CarrierEmitter` (1,039 lines)

**Not yet built:**
- `SqlAssembler` — clean-room replacement for CompileTimeSqlBuilder
- `CarrierPlan` — replaces CarrierClassInfo

**To delete (old pipeline):**
- `UsageSiteInfo` (610 lines), `ClauseInfo` (425 lines), `PendingClauseInfo` (61 lines), `ChainAnalysisResult` (264 lines), `PrebuiltChainInfo` (177 lines), `ChainParameterInfo` (141 lines), `CarrierClassInfo` (219 lines)
- `ClauseTranslator` (1,067 lines), `ExpressionSyntaxTranslator` (1,569 lines), `ExpressionTranslationContext` (386 lines), `ExpressionTranslationResult` (186 lines), `SubqueryScope` (31 lines)
- `CompileTimeSqlBuilder` (976 lines), `SqlFragmentTemplate` (217 lines), `CarrierClassBuilder` (200 lines)
- Old pipeline paths in `QuarryGenerator.cs` (~500 lines), `UsageSiteDiscovery.cs` (~800 lines of old discovery), `ChainAnalyzer.cs` (1,159 lines — rewrite)

**Pipeline wiring (already partially done):**
- `QuarryGenerator.Initialize()` already wires Stages 1-4 (RawCallSite → Bind → Translate)
- Stage 5 (collected) still delegates to old pipeline via adapter — this is what we're replacing

---

## 4. Work Breakdown

All work happens in a worktree branch. Tests may be broken during intermediate steps. The only gate is: all 2,943 tests pass at the end.

### Step 1: Extract shared enums and types from old models

Before deleting old types, extract enums and small types that are used by both old and new code into standalone files.

**Extract to standalone files:**
- `InterceptorKind`, `BuilderKind` → `Models/InterceptorKind.cs`
- `QueryKind` → `Models/QueryKind.cs` (if not already standalone)
- `OptimizationTier`, `ClauseRole`, `BranchKind` → `Models/OptimizationTier.cs`
- `ClauseKind`, `JoinClauseKind` → `Models/ClauseKind.cs`
- `CarrierField`, `CarrierStaticField`, `CarrierInterfaceStub`, `FieldRole` → `Models/CarrierField.cs`
- `SetActionAssignment` → `Models/SetActionAssignment.cs`
- `ParameterInfo` → ensure standalone (may already be)
- `ProjectionInfo`, `InsertInfo`, `RawSqlTypeInfo` → ensure standalone

**Why first:** Everything else depends on these types. Extracting them lets us delete the old model files without losing shared definitions.

### Step 2: Rewrite ChainAnalyzer to consume TranslatedCallSite

Rewrite `Parsing/ChainAnalyzer.cs` (~1,159 lines → ~550 lines).

**New signature:**
```csharp
internal static class ChainAnalyzer
{
    public static IReadOnlyList<QueryPlan> Analyze(
        ImmutableArray<TranslatedCallSite> sites,
        EntityRegistry registry,
        CancellationToken ct);
}
```

**Algorithm:**
1. Group sites by `ChainId` (assigned during discovery in Stage 2).
2. Within each chain, identify the execution terminal.
3. Order clause sites by source location.
4. Read conditional info from `RawCallSite.ConditionalInfo` (no syntax tree walking).
5. Read disqualifier flags from `RawCallSite.IsInsideLoop`, `.IsInsideTryCatch`, `.IsCapturedInLambda`.
6. Classify tier (PrebuiltDispatch / PrequotedFragments / RuntimeBuild).
7. Build `QueryPlan` terms from `TranslatedClause` data.
8. Compute conditional masks.
9. Extract global parameters with remapped indices.

**Key difference from old:** No `SemanticModel` dataflow analysis. No syntax tree walking. All chain metadata comes from `RawCallSite` fields populated during Stage 2 discovery.

### Step 3: Create SqlAssembler

New file: `IR/SqlAssembler.cs` (~500 lines).

Clean-room replacement for `CompileTimeSqlBuilder`. Takes `QueryPlan` + metadata → `AssembledPlan` with SQL variants per mask.

**Algorithm:**
- For each mask value, determine active terms, compute parameter offsets, render SQL via `SqlExprRenderer`.
- Handle Select, Update, Delete, Insert query kinds.
- Pre-render unconditional fragments, render only conditional terms per mask.
- Compute `MaxParameterCount`, `ReaderDelegateCode`.

See original plan §7 Step 5.2 for full algorithm detail.

### Step 4: Create CarrierPlan type and update CarrierAnalyzer

New file: `CodeGen/CarrierPlan.cs` (~130 lines).

**CarrierPlan** replaces `CarrierClassInfo` + `CarrierStrategy`. Self-contained: ClassName, BaseClassName, IsEligible, Fields, StaticFields, Parameters, MaskType, MaskBitCount.

Update `CarrierAnalyzer.Analyze()` to accept `AssembledPlan` → produce `CarrierPlan`.

See original plan §7 Step 5.3 for eligibility gates and field construction.

### Step 5: Rewrite PipelineOrchestrator for new types

Rewrite `IR/PipelineOrchestrator.cs` to accept `TranslatedCallSite[]` and orchestrate the new pipeline:

```csharp
public ImmutableArray<FileInterceptorGroup> AnalyzeAndGroup(
    ImmutableArray<TranslatedCallSite> sites)
```

Flow:
1. Collect diagnostics from `TranslatedCallSite` properties.
2. Call `ChainAnalyzer.Analyze()` → `QueryPlan[]`.
3. For each `QueryPlan`, call `SqlAssembler.Assemble()` → `AssembledPlan`.
4. For each `AssembledPlan`, call `CarrierAnalyzer.Analyze()` → `CarrierPlan`.
5. Group into `FileInterceptorGroup` with new type lists.

### Step 6: Update FileInterceptorGroup

Change `FileInterceptorGroup` to hold:
- `IReadOnlyList<TranslatedCallSite> Sites` (was `UsageSiteInfo`)
- `IReadOnlyList<AssembledPlan> Chains` (was `PrebuiltChainInfo`)
- `IReadOnlyList<CarrierPlan> CarrierPlans` (new)
- `IReadOnlyList<TranslatedCallSite> ChainMemberSites` (was `UsageSiteInfo`)

### Step 7: Migrate all emitters to new types

Migrate all six body emitters + FileEmitter + CarrierEmitter to consume `TranslatedCallSite`, `AssembledPlan`, and `CarrierPlan` directly. No adapter layer.

**Add convenience accessors on TranslatedCallSite** to reduce verbosity:
```csharp
public string UniqueId => Bound.Raw.UniqueId;
public InterceptorKind Kind => Bound.Raw.Kind;
public string EntityTypeName => Bound.Raw.EntityTypeName;
public SqlDialect Dialect => Bound.Dialect;
// etc.
```

**Emitter changes (all follow the same pattern):**
- `UsageSiteInfo site` → `TranslatedCallSite site`
- `site.ClauseInfo` → `site.Clause` (TranslatedClause)
- `clauseInfo.SqlFragment` → `SqlExprRenderer.Render(site.Clause.ResolvedExpression, site.Dialect)`
- `PrebuiltChainInfo chain` → `AssembledPlan plan`
- `chain.SqlMap` → `plan.SqlVariants`
- `CarrierClassInfo carrier` → `CarrierPlan carrier`

**Migration order:** RawSqlBodyEmitter → TransitionBodyEmitter → ClauseBodyEmitter → JoinBodyEmitter → TerminalBodyEmitter → CarrierEmitter → FileEmitter

**FileEmitter** constructor changes:
```csharp
// Old
public FileEmitter(string contextClassName, string? contextNamespace, string fileTag,
    IReadOnlyList<UsageSiteInfo> sites, IReadOnlyList<PrebuiltChainInfo>? chains)
// New
public FileEmitter(string contextClassName, string? contextNamespace, string fileTag,
    IReadOnlyList<TranslatedCallSite> sites, IReadOnlyList<AssembledPlan>? plans,
    IReadOnlyList<CarrierPlan>? carrierPlans)
```

### Step 8: Rewire QuarryGenerator.Initialize() — final form

Replace the current `GroupByFileAndProcessTranslated` (which uses adapters) with the clean pipeline:

```csharp
public void Initialize(IncrementalGeneratorInitializationContext context)
{
    // Stage 1: Context Discovery (unchanged)
    var contextDeclarations = context.SyntaxProvider
        .CreateSyntaxProvider(
            predicate: static (node, _) => IsContextCandidate(node),
            transform: static (ctx, ct) => GetContextInfo(ctx, ct))
        .Where(static info => info is not null)
        .Select(static (info, _) => info!);

    context.RegisterSourceOutput(contextDeclarations,
        static (spc, contextInfo) => GenerateEntityAndContextCode(contextInfo, spc));

    context.RegisterSourceOutput(contextDeclarations.Collect(),
        static (spc, contexts) => CheckDuplicateTypeMappings(contexts, spc));

    // Build EntityRegistry from collected contexts
    var entityRegistry = contextDeclarations.Collect()
        .Select(static (contexts, ct) => EntityRegistry.Build(contexts, ct));

    // Stage 2: Raw Call Site Discovery
    var rawCallSites = context.SyntaxProvider
        .CreateSyntaxProvider(
            predicate: static (node, _) => UsageSiteDiscovery.IsQuarryMethodCandidate(node),
            transform: static (ctx, ct) => DiscoverRawCallSite(ctx, ct))
        .Where(static site => site is not null)
        .Select(static (site, _) => site!);

    // Stage 3: Per-Site Binding
    var boundCallSites = rawCallSites
        .Combine(entityRegistry)
        .SelectMany(static (pair, ct) =>
            CallSiteBinder.Bind(pair.Left, pair.Right, ct));

    // Stage 4: Per-Site Translation
    var translatedCallSites = boundCallSites
        .Select(static (bound, ct) =>
            CallSiteTranslator.Translate(bound, ct));

    // Stage 5: Collected Analysis + File Grouping
    var perFileGroups = context.CompilationProvider
        .Combine(entityRegistry)
        .Combine(translatedCallSites.Collect())
        .SelectMany(static (data, ct) =>
            AnalyzeAndGroup(data.Left.Left, data.Left.Right, data.Right, ct));

    context.RegisterSourceOutput(perFileGroups,
        static (spc, group) => EmitFileInterceptors(spc, group));

    // Migration Discovery (unchanged)
    // ...
}
```

**`AnalyzeAndGroup`** is a new method that:
1. Collects diagnostics from TranslatedCallSite properties.
2. Delegates to `PipelineOrchestrator` which calls ChainAnalyzer → SqlAssembler → CarrierAnalyzer → file grouping.
3. Returns `ImmutableArray<FileInterceptorGroup>`.

**`EmitFileInterceptors`** is updated to work with the new `FileInterceptorGroup` (which now carries `TranslatedCallSite` and `AssembledPlan`).

### Step 9: Update UsageSiteDiscovery

Simplify `UsageSiteDiscovery.DiscoverRawCallSite` to be the primary (only) discovery path:
- Remove `DiscoverUsageSite` (old path returning `UsageSiteInfo`).
- `DiscoverRawCallSite` becomes `DiscoverCallSite` — the sole entry point.
- Remove all `ClauseTranslator` calls from discovery.
- Remove `TrySyntacticAnalysis` fallback (SqlExpr is the only path).
- Parse all clause lambdas directly via `SqlExprParser.ParseWithPathTracking()`.
- Parse navigation join lambdas too (for property name extraction in binding).

### Step 10: Delete old pipeline code

Delete all old types and translation infrastructure:

| File | Lines |
|---|---|
| `Models/UsageSiteInfo.cs` | 610 |
| `Models/ClauseInfo.cs` | 425 |
| `Models/PendingClauseInfo.cs` | 61 |
| `Models/ChainAnalysisResult.cs` | 264 |
| `Models/PrebuiltChainInfo.cs` | 177 |
| `Models/ChainParameterInfo.cs` | 141 |
| `Models/CarrierClassInfo.cs` | 219 |
| `Translation/ClauseTranslator.cs` | 1,067 |
| `Translation/ExpressionSyntaxTranslator.cs` | 1,569 |
| `Translation/ExpressionTranslationContext.cs` | 386 |
| `Translation/ExpressionTranslationResult.cs` | 186 |
| `Translation/SubqueryScope.cs` | 31 |
| `Sql/CompileTimeSqlBuilder.cs` | 976 |
| `Sql/SqlFragmentTemplate.cs` | 217 |
| `Generation/CarrierClassBuilder.cs` | 200 |
| Old methods in `QuarryGenerator.cs` | ~500 |
| Old discovery in `UsageSiteDiscovery.cs` | ~800 |
| **Total deleted** | **~7,829** |

Also remove `FromTranslatedCallSite` adapter on `UsageSiteInfo`, the `GetUsageSiteInfo` wrapper, `GroupByFileAndProcess`, `EnrichUsageSiteWithEntityInfo`, `TranslatePendingClause`, `DiscoverNavigationJoinChainMembers`, `BuildPrebuiltChainInfo`, `BuildPrebuiltChainInfoForJoin`, `GetNonTranslatableClauseKind`, and all related helpers.

### Step 11: Fix test suite

After the switchover, run the full 2,943-test suite. Fix failures in two categories:

**Category A: End-to-end tests (majority — ~2,800 tests)**
These compile source code, run the generator, and verify generated output. They should continue to work if the new pipeline produces correct output. Fix any gaps:
- SQL output differences (formatting, quoting, parameter indices)
- Missing clause translations (expressions the SqlExpr pipeline doesn't handle yet)
- Navigation join resolution gaps
- Carrier eligibility differences
- Diagnostic reporting differences

**Category B: Internal unit tests (~100-150 tests)**
Tests that directly construct or assert on old types (`UsageSiteInfo`, `ClauseInfo`, `PrebuiltChainInfo`, `ChainAnalysisResult`, `CarrierClassInfo`). These must be rewritten to use new types:
- `ClauseTranslationTests` → rewrite to test through `CallSiteTranslator`
- `ExpressionTranslationTests` → rewrite to test through SqlExpr pipeline
- `ChainAnalyzerTests` → rewrite for new `ChainAnalyzer.Analyze()` signature
- `CompileTimeSqlBuilderTests` → replace with `SqlAssemblerTests`
- `CarrierStrategyTests` → rewrite for `CarrierPlan` assertions
- Emitter unit tests → rewrite fixtures using `TestCallSiteBuilder`, `TestPlanBuilder`, `TestCarrierPlanBuilder`

**Test builders (new helpers for unit tests):**
- `TestCallSiteBuilder` — fluent API to construct `TranslatedCallSite` with sensible defaults
- `TestPlanBuilder` — fluent API to construct `AssembledPlan`
- `TestCarrierPlanBuilder` — fluent API to construct `CarrierPlan`

---

## 5. Files Changed Summary

| File | Action |
|---|---|
| `QuarryGenerator.cs` | Rewrite Initialize() and collected stage; delete old methods |
| `Parsing/UsageSiteDiscovery.cs` | Simplify to RawCallSite-only discovery; delete old paths |
| `Parsing/ChainAnalyzer.cs` | Rewrite for TranslatedCallSite input → QueryPlan output |
| `IR/SqlAssembler.cs` | **New** — renders SQL from QueryPlan |
| `CodeGen/CarrierPlan.cs` | **New** — replaces CarrierClassInfo |
| `CodeGen/CarrierAnalyzer.cs` | Rewrite for AssembledPlan → CarrierPlan |
| `IR/PipelineOrchestrator.cs` | Rewrite for TranslatedCallSite input |
| `Models/FileInterceptorGroup.cs` | Update type lists |
| `CodeGen/FileEmitter.cs` | Consume TranslatedCallSite + AssembledPlan |
| `CodeGen/RawSqlBodyEmitter.cs` | Consume TranslatedCallSite |
| `CodeGen/TransitionBodyEmitter.cs` | Consume TranslatedCallSite + CarrierPlan |
| `CodeGen/ClauseBodyEmitter.cs` | Consume TranslatedCallSite + AssembledPlan |
| `CodeGen/JoinBodyEmitter.cs` | Consume TranslatedCallSite + AssembledPlan |
| `CodeGen/TerminalBodyEmitter.cs` | Consume TranslatedCallSite + AssembledPlan |
| `CodeGen/CarrierEmitter.cs` | Consume CarrierPlan + AssembledPlan |
| `Generation/InterceptorCodeGenerator.cs` | Update utility signatures |
| `IR/TranslatedCallSite.cs` | Add convenience accessors |
| `IR/CallSiteBinder.cs` | Fix nav join resolution, RawSqlTypeInfo passthrough |
| `IR/CallSiteTranslator.cs` | Fix literal expression handling |
| `Models/InterceptorKind.cs` | **New** — extracted enum |
| `Models/ClauseKind.cs` | **New** — extracted enums |
| `Models/CarrierField.cs` | **New** — extracted types |
| `Models/SetActionAssignment.cs` | **New** — extracted type |
| 15 old files | **Delete** (see Step 10) |
| ~15 test files | Rewrite fixtures for new types |
| `Testing/TestCallSiteBuilder.cs` | **New** — test helper |
| `Testing/TestPlanBuilder.cs` | **New** — test helper |
| `Testing/TestCarrierPlanBuilder.cs` | **New** — test helper |

---

## 6. Validation

- **All 2,943 existing end-to-end tests pass** with byte-identical generated output (excluding subquery formatting differences).
- **Internal unit tests rewritten** to use new types — same behavioral assertions, different fixture construction.
- **No old type references remain** in codebase (outside git history and docs).
- **No adapter/conversion layer** between pipeline stages.
- **Pipeline flows cleanly:** RawCallSite → BoundCallSite → TranslatedCallSite → QueryPlan → AssembledPlan → CarrierPlan → generated source.
- **Zero `UsageSiteInfo`, `PrebuiltChainInfo`, `ClauseInfo`, `CarrierClassInfo`** anywhere in the codebase.
