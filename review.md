# Implementation Plan Review: impl-plan-compiler-finishes.md

## Software Architect Findings

### Modularity and Separation of Concerns

**SA-1: Clean phase boundaries.** The three-phase decomposition (Phase 4: Pipeline Restructure, Phase 5: Chain Analysis + SQL Assembly + Carrier Redesign, Phase 6B: Codegen Completion) has well-defined inputs and outputs at each boundary. Phase 4 produces `TranslatedCallSite[]`, Phase 5 produces `AssembledPlan[]` + `CarrierPlan[]`, Phase 6B consumes them for final emission. Each phase is independently testable. *(Sections 3, 6, 7, 8)*

**SA-2: Layered composition pattern is sound but property access is deep.** The `RawCallSite -> BoundCallSite -> TranslatedCallSite` layered composition avoids field duplication and keeps each layer focused on its concern. However, the resulting access patterns are verbose: `site.Bound.Raw.EntityTypeName`, `site.Bound.Raw.Kind`, `site.Bound.Raw.BuilderKind`. Phase 6B (Sections 8.5-8.8) documents dozens of these deep-access mappings. While this works, it creates a readability burden across every emitter method. Consider adding convenience properties (or extension methods) on `TranslatedCallSite` for the most frequently accessed fields from inner layers. *(Sections 8.4, 8.5, 8.6, 8.7, 8.8)*

**SA-3: CallSiteBinder and CallSiteTranslator have well-defined responsibilities.** `CallSiteBinder` handles entity resolution and metadata enrichment; `CallSiteTranslator` handles SqlExpr pipeline execution (bind, extract params, render). The separation is clean: binder needs `EntityRegistry`, translator does not. This allows future binding strategies (e.g., multi-database routing) without touching translation. *(Steps 4.2, 4.3)*

**SA-4: SqlAssembler as a clean-room replacement is architecturally clean.** By consuming `QueryPlan` and delegating to `SqlExprRenderer`, `SqlAssembler` avoids inheriting the `SqlFragmentTemplate` indirection. The per-mask iteration algorithm (Section 7, Step 5.2) is well-specified and deterministic. The separation of dialect-specific formatting into `SqlFormatting` utility methods keeps the assembler dialect-agnostic in its core logic. *(Step 5.2)*

**SA-5: Phase 4 temporary adapter for ChainAnalyzer is a reasonable compromise.** The plan explicitly acknowledges that `ChainAnalyzer` still expects `UsageSiteInfo` during Phase 4 and introduces a temporary `UsageSiteInfo.FromTranslatedCallSite()` adapter. This keeps Phase 4's scope manageable. The adapter is deleted in Phase 5 when ChainAnalyzer is rewritten. This is sound phased delivery. *(Step 4.4, paragraph "Adapting PipelineOrchestrator")*

### Extensibility Assessment

**SA-6: Adding new clause types is well-supported.** The `ClauseKind` enum drives clause dispatch through the pipeline. Adding a new clause type requires: (1) new enum value, (2) handling in `CallSiteTranslator.Translate`, (3) new term type on `QueryPlan`, (4) rendering in `SqlAssembler`, (5) emitter method. Each step is localized. *(Steps 4.3, 5.1, 5.2)*

**SA-7: Adding new dialects is well-supported.** Dialect differences are centralized in `SqlFormatting.FormatParameter`, `SqlFormatting.QuoteIdentifier`, and the pagination syntax dispatch in `SqlAssembler`. Adding a new dialect requires updating these helpers plus dialect-specific rendering tests. No changes to the core pipeline or IR types. *(Step 5.2, "Dialect-specific formatting")*

**SA-8: Adding new optimization strategies may be constrained.** The `OptimizationTier` enum (Tier 1/2/3) is baked into `ChainAnalyzer` and `CarrierAnalyzer` with hardcoded gate logic. A future Tier 0 (e.g., fully constant queries) or Tier 4 (e.g., partial runtime assembly) would require changes across `ChainAnalyzer`, `SqlAssembler`, `CarrierAnalyzer`, and emitters. A strategy/visitor pattern could decouple tier classification from SQL assembly, but the current approach is acceptable given three well-defined tiers. *(Steps 5.1, 5.3)*

### Highest Architectural Risk

**SA-9: Phase 5 (ChainAnalyzer rewrite) carries the highest architectural risk.** The current `ChainAnalyzer` relies on Roslyn `SemanticModel` dataflow analysis, syntax tree walking for variable tracking, and `IfStatementSyntax` nesting detection. The plan moves conditional detection and disqualifier detection to Phase 4 per-site binding (Section 7, Step 5.1: "disqualifying conditions are detected earlier during the Phase 4 per-site binding pass and recorded on `RawCallSite` as flags (`IsInsideLoop`, `IsInsideTryCatch`, `IsCapturedInLambda`)"). However, these fields (`IsInsideLoop`, `IsInsideTryCatch`, `IsCapturedInLambda`, `ConditionalInfo`, `ChainId`) do not currently exist on `RawCallSite` or `BoundCallSite` in the codebase. They are referenced in Phase 5's description but their creation is not specified in Phase 4's "Files Changed" table or step descriptions. This is a significant gap -- Phase 5 depends on data that Phase 4 does not explicitly create. *(Steps 4.6, 5.1)*

---

## Performance Engineer Findings

### The .Collect() Bottleneck

**PE-1: Phase 4 correctly solves the .Collect() bottleneck.** The current pipeline calls `.Collect()` on raw usage sites and then performs all enrichment, translation, and analysis in the collected transform. Phase 4 moves binding and translation to individually-cached `Select`/`SelectMany` stages before `.Collect()`. The plan correctly identifies that editing one `.Where()` lambda will only re-execute that site's binding and translation; all other sites return cached results. The `.Collect()` call still exists but now operates on pre-translated sites. *(Section 6, "New Pipeline Architecture")*

**PE-2: The collected stage still re-executes fully when any site changes.** The plan states: "If any single `TranslatedCallSite` in the array differs, this stage re-executes" (Stage 5 description). This means chain analysis, diagnostic collection, and file grouping all re-run whenever any query site changes. While these operations are "lightweight" (the plan's word), chain analysis over hundreds of sites with conditional mask computation is not trivially cheap. For projects with many query sites, this is still O(N) on every edit. The improvement is real (expensive translation is cached), but the plan should quantify the expected cost of the remaining collected stage. *(Section 6, Stage 5)*

### Equality/GetHashCode Efficiency

**PE-3 (BUG): `RawCallSite.Equals` omits `InitializedPropertyNames` and `NonAnalyzableReason`.** The `Equals` method on `RawCallSite` (lines 104-127 of `RawCallSite.cs`) does not compare `InitializedPropertyNames` (a `HashSet<string>`) or `NonAnalyzableReason` (a `string`). If two call sites differ only in initialized properties (e.g., different insert column sets) or non-analyzable reasons, the incremental driver will treat them as equal, returning stale cached results. `InitializedPropertyNames` is especially dangerous because it directly affects `InsertInfo` construction during binding. *(Step 4.2, algorithm point 4)*

**PE-4 (BUG): `RawCallSite.Equals` omits `JoinedEntityTypeName`.** The `JoinedEntityTypeName` field (line 89 of `RawCallSite.cs`) is not compared in `Equals`. Two join sites targeting different joined entities would be treated as identical by the incremental cache, producing incorrect `BoundCallSite` values. This field drives entity resolution in `CallSiteBinder.Bind` (Step 4.2, algorithm point 6). *(Step 4.2)*

**PE-5: `HashSet<string>` on `RawCallSite` is problematic for equality.** `HashSet<string>` does not implement value equality. `RawCallSite.Equals` currently does not compare it at all (PE-3), but even if it did, `Object.Equals(HashSet, HashSet)` would use reference equality. The field should be converted to `ImmutableSortedSet<string>` or `IReadOnlyList<string>` (sorted) with a proper sequence comparison for deterministic equality. This is a prerequisite for correct incremental caching. *(RawCallSite.cs, line 34)*

**PE-6 (BUG): `QueryPlan.Equals` omits `GroupByExprs`, `HavingExprs`, `PossibleMasks`, and `UnmatchedMethodNames`.** The `Equals` method on `QueryPlan` (lines 77-95 of `QueryPlan.cs`) compares `Joins`, `WhereTerms`, `OrderTerms`, `SetTerms`, `InsertColumns`, `ConditionalTerms`, and `Parameters` but does not compare `GroupByExprs`, `HavingExprs`, `PossibleMasks`, or `UnmatchedMethodNames`. Two query plans differing only in GROUP BY or HAVING clauses would be considered equal. This would cause incorrect SQL assembly results when plans are cached by the incremental driver. *(QueryPlan.cs)*

**PE-7 (BUG): `AssembledPlan.Equals` omits `SqlVariants`, `ExecutionSite`, `ClauseSites`, and `EntitySchemaNamespace`.** The `Equals` method (lines 50-59 of `AssembledPlan.cs`) only compares `Plan`, `EntityTypeName`, `ResultTypeName`, `Dialect`, `MaxParameterCount`, and `ReaderDelegateCode`. It does not compare `SqlVariants` (the actual SQL strings), `ExecutionSite`, `ClauseSites`, or `EntitySchemaNamespace`. While `Plan.Equals` may serve as a proxy for SQL content equality, if `SqlAssembler` is non-deterministic or produces different SQL for equal `QueryPlan` inputs (e.g., due to floating-point rendering), `AssembledPlan` equality would fail to detect the difference. More critically, `ClauseSites` contains `TranslatedCallSite` references that the emitters need -- if those differ but the plan is "equal," the emitters receive stale site references. *(AssembledPlan.cs)*

### SelectMany for Navigation Joins

**PE-8: Navigation join SelectMany fan-out is bounded.** The plan describes two SelectMany operations: one in Stage 2 (discovery, Approach A) where navigation joins emit additional `RawCallSite` entries, and one in Stage 3 (binding). With Approach A (Section 6, Step 4.6), the fan-out at binding is 1:1 (binder returns one `BoundCallSite` per raw site). The discovery-time fan-out is bounded by the number of downstream fluent chain calls after a navigation join, typically 1-5 (Where, Select, Execute, etc.). This is acceptable. *(Step 4.6)*

### SqlAssembler Rendering

**PE-9: Per-mask SQL generation calls SqlExprRenderer repeatedly for shared sub-expressions.** For a Tier 1 chain with 4 conditional bits (16 mask variants), `SqlAssembler` renders SQL 16 times. Unconditional terms (joins, GROUP BY, HAVING, projection) are re-rendered in every variant even though their SQL is identical. The plan does not mention caching rendered fragments across mask variants. For chains with many unconditional terms, this is O(masks * unconditional_terms) redundant work. A pre-rendering pass for unconditional terms would reduce this to O(masks * conditional_terms + unconditional_terms). *(Step 5.2, "Algorithm -- per-mask SQL generation")*

### Memory

**PE-10: Layered composition memory overhead is modest.** Each `TranslatedCallSite` holds a reference to its `BoundCallSite`, which holds a reference to its `RawCallSite`. This is three heap objects per site. For a project with 500 call sites, this is 1,500 objects -- negligible. The `SqlExpr` trees are the heavier allocation, but they exist in the current pipeline too (within `PendingClauseInfo`). No regression expected. *(Section 6, pipeline architecture)*

**PE-11: No SyntaxNode reference leak -- well-guarded.** The plan explicitly prohibits `SyntaxNode` references in cached pipeline values (Section 4, Constraints; Step 4.1, "SyntaxNode references"). `RawCallSite` stores only value-type location data. This is critical for memory and is correctly addressed. *(Step 4.1)*

### Missing Benchmarks

**PE-12: No quantitative performance targets or benchmarks.** The plan describes qualitative improvements ("editing one `.Where()` lambda will no longer re-translate every query site") but sets no measurable targets. Suggested additions: (a) target latency for incremental re-generation after a single-site edit in a 500-site project, (b) memory ceiling for cached pipeline state, (c) benchmark comparing old vs. new pipeline on the project's own test suite. The "Performance validation" section (Section 6, Validation) describes how to verify caching behavior but not how to measure throughput. *(Section 6, Validation)*

---

## End User (Developer Consumer) Findings

### Diagnostic Messages

**EU-1: QRY019 "skip" diagnostic should include actionable guidance.** The plan states that untranslated clause sites produce a QRY019 diagnostic "with the clause kind name (e.g., 'Where', 'OrderBy')" (Step 4.3, "Error handling -- skip and diagnose"). This tells the developer *what* was skipped but not *why* or *what to do about it*. The diagnostic message should include: (a) which expression pattern was unsupported, (b) that the runtime fallback is being used (no correctness loss), and (c) a link or reference to supported expression patterns. *(Step 4.3)*

### Byte-Identical Output Constraint

**EU-2: "Byte-identical output" is realistic for non-subquery cases if SqlExprRenderer is bug-free.** The plan correctly scopes the byte-identical constraint to "non-subquery cases" (Section 5, "Generated output must be byte-identical for non-subquery cases"). The existing snapshot comparison tests (`InterceptorIntegrationTests`, 126 tests) enforce this. However, the constraint depends on `SqlExprRenderer` producing character-identical SQL to what `ClauseTranslator` + `CompileTimeSqlBuilder` currently produce. Any difference in whitespace, parenthesization, or operator precedence handling would break the constraint. The plan acknowledges this risk implicitly ("Any divergence indicates a translation regression in the SqlExpr pipeline") but does not describe a strategy for debugging divergences. Recommendation: add a side-by-side diff tool or diagnostic mode that runs both old and new translators during development and reports divergences before committing. *(Section 5, Section 6 Validation)*

**EU-3: Subquery case exclusion from byte-identical constraint is not well-defined.** The plan mentions "non-subquery cases" but does not define which tests or patterns constitute "subquery cases" or what output changes are expected for those. If subquery output changes, the baseline files must be updated. The plan should list the specific test cases or patterns excluded from the byte-identical constraint. *(Section 5)*

### Test Builder Ergonomics

**EU-4: TestCallSiteBuilder fluent API is well-designed.** The `TestCallSiteBuilder` (Section 8.10) hides the three-layer IR construction behind a fluent API. The factory methods (`WhereClause`, `OrderByClause`, `SelectClause`, `RawSql`, `Terminal`, `Transition`) cover common patterns. The `Build()` method constructs all three layers with sensible defaults. This significantly reduces test boilerplate compared to manually constructing `RawCallSite -> BoundCallSite -> TranslatedCallSite`. *(Step 6B.8)*

**EU-5: TestPlanBuilder lacks carrier plan construction.** `TestPlanBuilder` produces `AssembledPlan` but there is no corresponding builder for `CarrierPlan`. Tests for carrier-path emitters (TransitionBodyEmitter, ClauseBodyEmitter, TerminalBodyEmitter) need `CarrierPlan` instances. Either `TestPlanBuilder` should have a `.WithCarrierPlan()` chain or a separate `TestCarrierPlanBuilder` should be provided. *(Step 6B.8)*

### Documentation and Contributor Onboarding

**EU-6: The plan is detailed enough for implementation.** Each step specifies: the file to create/modify, the method signatures, the algorithm with numbered substeps, what moves from which old file/line, and what remains unchanged. The Decisions Log (Section 2) explains the "why" behind each choice. A new contributor could implement a phase by following the step-by-step instructions. *(All sections)*

**EU-7: The plan lacks a visual pipeline diagram.** The five-stage pipeline (Section 6) is described textually but has no diagram. A simple flow diagram showing `RawCallSite -> (Combine EntityRegistry) -> BoundCallSite -> TranslatedCallSite -> (Collect) -> QueryPlan -> AssembledPlan -> CarrierPlan -> Generated Source` would significantly aid comprehension. *(Section 6)*

### Learning Curve

**EU-8: The new pipeline is more learnable than the old one.** The old pipeline has entangled concerns: `UsageSiteInfo` (25+ nullable properties serving multiple stages), `PendingClauseInfo` wrapping `SqlExpr`, dual translators (`ExpressionSyntaxTranslator` and `ClauseTranslator`), and `EnrichUsageSiteWithEntityInfo` doing binding + translation + enrichment in one method. The new pipeline has clear type boundaries: discovery produces `RawCallSite`, binding produces `BoundCallSite`, translation produces `TranslatedCallSite`, chain analysis produces `QueryPlan`, SQL assembly produces `AssembledPlan`. Each type's purpose is self-documenting from its name and stage. *(Section 1)*

---

## Devil's Advocate Findings

### Old Translator Deletion Risk

**DA-1: Deleting old translators is aggressive but justified given the test coverage.** The plan deletes 3,240 lines of translator code (Step 4.5) with no fallback path. If `SqlExpr` has undiscovered gaps (expressions the old translators handled but `SqlExprParser` does not parse), those expressions will silently fall through to QRY019 "skip" diagnostics. However, the 49 `CompileTimeRuntimeEquivalenceTests` and 126 `InterceptorIntegrationTests` provide strong coverage. The risk is limited to expression patterns not covered by any test. Recommendation: before deletion, run both old and new translators on the full test suite and confirm zero divergence. *(Step 4.5)*

**DA-2: The "SqlExpr handles 100% of expression translation" claim (Section 2, Decisions Log) is untested against production codebases.** The 2,929 tests exercise the generator's own test patterns. Real-world user code may contain expression patterns (ternary operators inside Where lambdas, method group conversions, nested null-coalescing) that the test suite does not exercise. The plan should include a validation step against at least one external real-world project using Quarry before deleting the old translators. *(Section 2, Decision "Old translator fate")*

### ChainAnalyzer Rewrite Risk

**DA-3: ChainAnalyzer rewrite depends on fields that do not exist yet.** The plan's Step 5.1 describes the rewritten ChainAnalyzer reading `ConditionalInfo`, `IsInsideLoop`, `IsInsideTryCatch`, `IsCapturedInLambda`, and `ChainId` from `RawCallSite`. A codebase search confirms these fields do not exist on `RawCallSite`, `BoundCallSite`, or `TranslatedCallSite`. They are not listed in Phase 4's "Files Changed" table (Section 6, "Files Changed"). Phase 5 depends on Phase 4 creating these fields, but Phase 4 does not specify their creation. This is a gap in the plan. *(Steps 4.6, 5.1)*

**DA-4: Tier classification logic has subtle edge cases.** The current `ChainAnalyzer` has `MaxIfNestingDepth = 2` and handles forked chains, loop assignments, try/catch blocks, lambda captures, and opaque assignments. The plan says "disqualifying conditions are detected earlier during the Phase 4 per-site binding pass" (Step 5.1), but the binding pass operates on a single `(RawCallSite, EntityRegistry)` pair without access to the method body or other sites in the same method. Detecting whether a call site is inside a loop, try/catch, or lambda requires syntax tree context (walking parent nodes), which is available during discovery (Stage 2) but not during binding (Stage 3). The plan should clarify that disqualifier detection happens in discovery, not binding. *(Steps 4.1, 5.1)*

### Byte-Identical Output and SqlExprRenderer

**DA-5: SqlExprRenderer may produce different formatting than ClauseTranslator.** The plan claims byte-identical output (Section 5) but the two rendering paths are architecturally different. `ClauseTranslator` builds SQL strings incrementally via `StringBuilder` with manual formatting. `SqlExprRenderer` walks an `SqlExpr` tree recursively. Differences could arise in: (a) parenthesization (the renderer may add/omit parens around binary expressions), (b) whitespace around operators, (c) NULL check formatting (`IS NULL` vs. `= NULL`), (d) LIKE escape clause formatting. The existing 40 IR pipeline stress tests (mentioned in Section 5) verify SQL round-tripping but do not explicitly verify character-identical output against the old translator's output. *(Section 5, Step 4.3)*

### Navigation Join Circular Dependencies

**DA-6: Navigation joins via SelectMany cannot create circular dependencies.** The plan's Approach A (Step 4.6) walks the syntax tree *upward* from the join `InvocationExpressionSyntax` to find parent fluent chain calls. Since C# fluent chains are syntactically acyclic (each `.Method()` call wraps its receiver), the tree walk cannot loop. Self-referential navigation properties (entity A has navigation to entity A) would produce a valid chain with the same entity on both sides but would not cause infinite recursion. This concern is unfounded. *(Step 4.6)*

**DA-7: Navigation join SelectMany may produce duplicate sites.** The plan states: "The `CreateSyntaxProvider` already deduplicates by invocation syntax node -- each `InvocationExpressionSyntax` is visited exactly once" (Step 4.6). However, if two different navigation join sites in the same fluent chain both walk upward and discover the same parent `.Select()` call, two `RawCallSite` entries would be emitted for that Select. The plan should specify a deduplication strategy (e.g., by `UniqueId` or `(FilePath, Line, Column)`) in the SelectMany flattening step. *(Step 4.6)*

### CarrierPlan vs CarrierClassInfo

**DA-8: CarrierPlan replacing CarrierClassInfo is justified, not over-engineering.** `CarrierClassInfo` requires the emitter to consult `PrebuiltChainInfo` separately for parameter extraction details, SQL map access, and tier information. `CarrierPlan` is self-contained: it carries fields, parameters, mask metadata, eligibility status, and base class name. This eliminates a class of bugs where emitter code accesses `CarrierClassInfo` and `PrebuiltChainInfo` with mismatched chain indices. The plan's `CarrierAnalyzer.Analyze(assembled, chainIndex)` signature (Step 5.3) makes the relationship explicit. *(Step 5.3)*

### Phase 5 Risk Assessment

**DA-9: Phase 5 is at least as risky as Phase 4, contrary to the plan's emphasis.** The plan labels Phase 4 as the "critical path" (Section 1). However, Phase 5 rewrites three major subsystems simultaneously: ChainAnalyzer (1,159 lines), CompileTimeSqlBuilder (976 lines), and CarrierClassBuilder (200 lines). Phase 4 is primarily a pipeline restructure with adapter patterns for backward compatibility. Phase 5 replaces core algorithms (chain grouping, mask computation, SQL rendering, carrier eligibility) with no adapter fallback. If Phase 5's SqlAssembler produces even slightly different SQL for any mask variant, every snapshot test fails. *(Section 7)*

### Rollback Strategy

**DA-10: No rollback strategy is documented.** The plan describes each phase as a series of green commits. If a phase is partially complete and a blocking issue is discovered (e.g., SqlExprRenderer cannot handle a specific expression pattern), the only option is to revert all commits in that phase. The plan should specify: (a) whether each phase is behind a feature flag or compiler switch, (b) whether the old pipeline can coexist temporarily with the new pipeline for A/B comparison, (c) at what point the old translators are deleted (currently Step 4.5, before Phase 4 is fully validated). Deleting old translators before Phase 5 validation means Phase 5 issues cannot be debugged by comparison with the old path. *(Steps 4.5, Section 7)*

### Implicit Phase Dependencies

**DA-11: Phase 4 Step 4.6 (Navigation Join Resolution) has an implicit dependency on Phase 5's ChainId concept.** Step 5.1 describes grouping sites by `ChainId` ("All sites sharing the same `ChainId` within a method scope belong to the same chain"). Step 4.6 describes emitting additional `RawCallSite` entries for navigation join chain members during discovery. But `ChainId` is not assigned during discovery -- it is described as assigned "during the per-site binding" (Step 5.1). For Phase 4's temporary adapter to work with the existing `ChainAnalyzer`, these additional sites must be linkable to their chains. The adapter's chain linking strategy is not specified. *(Steps 4.4, 4.6, 5.1)*

---

## Cross-Agent Discussion

### Agreement: Phase 5 is the highest-risk phase

The Software Architect (SA-9), Performance Engineer (PE-2), and Devil's Advocate (DA-9) all converge on Phase 5 as the highest-risk phase. SA-9 identifies the missing field definitions (`ConditionalInfo`, `ChainId`, etc.) as a gap. DA-9 notes the simultaneous rewrite of three subsystems with no adapter fallback. PE-2 notes the collected stage still being O(N). The plan's emphasis on Phase 4 as the "critical path" should be revised to give Phase 5 equal or greater attention.

### Agreement: Equality bugs are critical blockers

PE-3, PE-4, PE-5, PE-6, and PE-7 identify concrete equality bugs in existing IR types. The Software Architect concurs that these are critical because the entire incremental caching strategy depends on correct `Equals`/`GetHashCode`. The Devil's Advocate adds that these bugs could cause silent correctness regressions (stale cached translations being reused when inputs have changed) that would be extremely difficult to diagnose. These must be fixed before Phase 4 is implemented.

### Disagreement: Aggressive old translator deletion timing

SA-5 considers the temporary adapter pattern reasonable. DA-1 and DA-10 argue that deleting old translators in Phase 4 (Step 4.5) before Phase 5 validation removes the ability to A/B compare old and new SQL output. EU-2 notes the byte-identical constraint depends on SqlExprRenderer correctness. Resolution: the old translators should be deleted in Phase 4 as planned (they are no longer called), but the deleted files should remain accessible in git history, and a diagnostic script comparing old vs. new translator output should be run before the deletion commit.

### Complementary: Missing fields (SA-9/DA-3) and disqualifier timing (DA-4)

SA-9 and DA-3 both identify the missing `ConditionalInfo`, `IsInsideLoop`, `ChainId` fields. DA-4 adds that these fields cannot be populated during binding (Stage 3) because the binder lacks syntax tree context. The Performance Engineer concurs that if these fields are populated during discovery (Stage 2), they must participate in `RawCallSite.Equals` to ensure correct caching. This means the `RawCallSite` type needs field additions, Equals/GetHashCode updates, and IEquatable implementation fixes -- all before Phase 4 begins.

### Complementary: Test coverage and real-world validation

DA-2 notes that the "100% coverage" claim for SqlExpr is tested only against the project's own test suite. EU-2 notes the byte-identical constraint's dependence on renderer correctness. Together, these suggest a pre-deletion validation step: run the generator against at least one real-world Quarry consumer project and verify no regressions.

### Agreement: TestCallSiteBuilder is well-designed, but incomplete

EU-4 praises the test builder API. EU-5 identifies the missing `CarrierPlan` builder. DA-8 confirms `CarrierPlan` is needed (not over-engineering). A `TestCarrierPlanBuilder` should be added to the plan.

---

## Recommended Changes

### Critical (must fix before implementation)

1. **Fix `RawCallSite.Equals` to include all fields affecting downstream behavior.** Add comparisons for `InitializedPropertyNames` (converted to a sorted `IReadOnlyList<string>` or `ImmutableSortedSet<string>`), `NonAnalyzableReason`, and `JoinedEntityTypeName`. Without this fix, the incremental cache will produce stale results for insert sites with different column sets, sites with different non-analyzable reasons, and join sites targeting different entities. *(PE-3, PE-4, PE-5)*

2. **Fix `QueryPlan.Equals` to include `GroupByExprs`, `HavingExprs`, `PossibleMasks`, and `UnmatchedMethodNames`.** Without these comparisons, two query plans differing in GROUP BY, HAVING, mask enumeration, or unmatched methods would be treated as equal, causing incorrect SQL assembly results or incorrect tier classification when plans are cached. *(PE-6)*

3. **Fix `AssembledPlan.Equals` to include `SqlVariants` and `EntitySchemaNamespace`.** While `Plan.Equals` may cover most cases, `SqlVariants` contains the actual SQL strings consumed by emitters. If `SqlAssembler` ever produces different SQL for structurally equal `QueryPlan` inputs, the missing comparison would cause emitters to use stale SQL. At minimum, `SqlVariants.Count` should be compared; ideally, a dictionary equality check should be implemented. *(PE-7)*

4. **Specify creation of `ConditionalInfo`, `ChainId`, `IsInsideLoop`, `IsInsideTryCatch`, `IsCapturedInLambda` fields.** These fields are referenced in Phase 5 (Step 5.1) but not defined in Phase 4's step descriptions or "Files Changed" table. Add explicit steps to Phase 4 (or a pre-Phase-5 step) that: (a) define these fields on `RawCallSite`, (b) populate them during discovery (Stage 2) where syntax tree context is available, (c) include them in `RawCallSite.Equals` and `GetHashCode`. *(SA-9, DA-3, DA-4)*

5. **Convert `RawCallSite.InitializedPropertyNames` from `HashSet<string>` to an immutable sorted collection.** `HashSet<string>` does not support value equality, making it incompatible with the incremental caching requirements stated in Section 4 Constraints ("Every type flowing through the pipeline must have correct, deterministic `Equals()` and `GetHashCode()`"). Use `ImmutableArray<string>` (sorted at construction time) or `ImmutableSortedSet<string>`. *(PE-5)*

### Important (should fix, risk if ignored)

6. **Clarify that disqualifier detection (loop, try/catch, lambda capture) happens during discovery (Stage 2), not binding (Stage 3).** Step 5.1 says "detected earlier during the Phase 4 per-site binding pass" but the binding pass has no syntax tree access. The discovery transform has the `SemanticModel` and can walk parent syntax nodes to detect containing loops, try/catch blocks, and lambda scopes. Update the plan to place this detection in Step 4.1 (discovery) rather than Step 4.2 (binding). *(DA-4)*

7. **Add a deduplication strategy for navigation join chain member sites.** If two navigation joins in the same fluent chain discover the same downstream site, the current plan would emit duplicate `RawCallSite` entries. Add a `HashSet<(string FilePath, int Line, int Column)>` deduplication check in the discovery-time chain member emission loop (Step 4.6, Approach A, point 2). *(DA-7)*

8. **Cache unconditional SQL fragments across mask variants in SqlAssembler.** For Tier 1 chains with many unconditional terms, pre-render the unconditional portions once and reuse the rendered strings across all 2^N mask variants. This avoids O(masks * unconditional_terms) redundant `SqlExprRenderer.Render` calls. *(PE-9)*

9. **Add a rollback/comparison strategy for old translator deletion.** Before deleting translators in Step 4.5, run a comparison script that executes both old and new translation paths on the full test suite and confirms zero divergence. Document that git history preserves the deleted files and describe how to temporarily restore them for debugging if Phase 5 issues arise. *(DA-10)*

10. **Document quantitative performance targets.** Add measurable targets to the Validation sections: e.g., "Incremental re-generation after a single-site edit in a 500-site project should complete in under 200ms" and "Peak pipeline memory usage should not exceed 50MB for a 1000-site project." Without targets, performance improvements cannot be verified. *(PE-12)*

11. **Add QRY019 diagnostic guidance.** Include the unsupported expression pattern and a note that the runtime fallback is used (no correctness loss) in the diagnostic message. Example: `QRY019: Could not translate Where expression 'x.Foo.Bar?.Baz' at compile time (unsupported null-conditional access). The runtime SQL builder will handle this expression.` *(EU-1)*

### Nice-to-Have (improve quality)

12. **Add convenience properties on `TranslatedCallSite` for frequently accessed inner fields.** Properties like `UniqueId`, `Kind`, `EntityTypeName`, `Dialect`, `FilePath`, `Line`, `Column` accessed via `site.Bound.Raw.X` are used dozens of times across all emitters. Adding forwarding properties (e.g., `public string UniqueId => Bound.Raw.UniqueId`) would reduce noise in emitter code. *(SA-2)*

13. **Add a visual pipeline diagram.** A simple ASCII or Mermaid diagram showing the five stages, their input/output types, and where `.Collect()` sits would aid contributor onboarding. *(EU-7)*

14. **Add `TestCarrierPlanBuilder`.** The test builder section (Step 6B.8) provides `TestCallSiteBuilder` and `TestPlanBuilder` but no builder for `CarrierPlan`. Carrier-path emitter tests need `CarrierPlan` instances. *(EU-5)*

15. **List specific subquery test cases excluded from byte-identical constraint.** The plan excludes "subquery cases" from the byte-identical output guarantee but does not enumerate which tests or patterns this covers. Listing them prevents confusion during test baseline reviews. *(EU-3)*

16. **Consider extracting conditional detection into a dedicated `ConditionalAnalyzer` pass.** The plan distributes conditional detection between discovery (syntax tree context) and chain analysis (chain-level grouping). A dedicated `ConditionalAnalyzer` operating in Stage 2 would centralize this logic and make it independently testable. *(SA-8, DA-4)*
