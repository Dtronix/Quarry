# Work Handoff: 181-set-operations

## Key Components
- `src/Quarry/Query/IQueryBuilder.cs` — Union/UnionAll/Intersect/IntersectAll/Except/ExceptAll methods
- `src/Quarry.Generator/IR/QueryPlan.cs` — SetOperatorKind, SetOperationPlan, PostUnionWhereTerms/GroupByExprs/HavingExprs
- `src/Quarry.Generator/Models/InterceptorKind.cs` — 6 new InterceptorKind values
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — Set operation discovery, OperandChainId extraction, OperandArgEndLine/Column
- `src/Quarry.Generator/IR/RawCallSite.cs` — OperandChainId, OperandArgEndLine, OperandArgEndColumn
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — Inline operand splitting with boundary detection, AnalyzeOperandChain, seenSetOperation flag for post-union clause routing
- `src/Quarry.Generator/IR/SqlAssembler.cs` — Set operation SQL rendering, subquery wrapping for post-union clauses, paramBaseOffset for operand parameter indices
- `src/Quarry.Generator/CodeGen/SetOperationBodyEmitter.cs` — Per-index set operation interceptor emission with operand carrier class casting
- `src/Quarry.Generator/CodeGen/FileEmitter.cs` — Operand chain carrier emission, operandCarrierNames mapping, QueryPlanReferenceComparer
- `src/Quarry.Generator/CodeGen/InterceptorRouter.cs` — EmitterCategory.SetOperation routing
- `src/Quarry.Generator/IR/AssembledPlan.cs` — IsOperandChain flag propagation
- `src/Quarry.Generator/DiagnosticDescriptors.cs` — QRY070 (IntersectAllNotSupported), QRY071 (ExceptAllNotSupported)
- `src/Quarry.Generator/IR/PipelineOrchestrator.cs` — QRY070/QRY071 emission in CollectTranslatedDiagnostics
- `src/Quarry.Tests/SqlOutput/CrossDialectSetOperationTests.cs` — 11 tests (+ 3 skipped diagnostic tests in GeneratorTests.cs)

## Completions (This Session)
- Phase 3-4: Chain analysis, SQL assembly, dual-carrier code generation
- Phase 5: Post-union WHERE/GroupBy/Having with subquery wrapping
- Phase 6: QRY070/QRY071 diagnostics for INTERSECT ALL/EXCEPT ALL
- Remediation round 1: Chained set ops fix, PostUnionWhereTerms Equals, GroupBy/Having redirect, PipelineErrorBag
- Remediation round 2: Operand param indices fix (@p0 collision), diagnostic ID collision renumbering (QRY041->QRY070), PostUnionGroupBy/HavingExprs Equals
- C-tier items: Cross-dialect param tests, GroupBy test, diagnostic scaffolding

## Previous Session Completions
- Phase 1: Foundation types + runtime API
- Phase 2: Discovery + chain linking

## Progress
- All 6 implementation phases complete
- Two review passes completed with full remediation
- User requested a THIRD review pass before merge
- PR #201 is open, branch pushed and rebased on master

## Current State
Feature-complete for same-entity set operations. All 2870 tests pass (3 diagnostic tests skipped). Branch rebased on origin/master. PR #201 open.

Next session: run fresh REVIEW analysis, classify findings, remediate, then FINALIZE (squash merge).

## Known Issues / Bugs
None in implemented scope.

## Dependencies / Blockers
None. Ready for review and merge.

## Architecture Decisions
- **Dual-carrier approach**: Each operand gets its own carrier class. Union interceptor copies parameter fields at correct offsets.
- **Operand boundary detection**: OperandArgEndLine/Column from Roslyn syntax spans prevents post-union clauses from being absorbed into the operand during inline splitting.
- **Post-union subquery wrapping**: WHERE/GroupBy/Having after set operations get `SELECT * FROM (...) AS "__set"`. ORDER BY/LIMIT apply directly.
- **Diagnostic IDs**: QRY070/QRY071 (QRY041/042 already taken by RawSqlUnresolvableColumn).

## Open Questions
- User wants another review pass — unclear what specific concerns remain.
- Cross-entity unions deferred (API exists, discovery/codegen don't handle cross-entity type params yet).

## Next Work (Priority Order)
1. Run third REVIEW analysis pass (fresh review.md)
2. Classify and remediate any findings
3. FINALIZE — delete session artifacts, squash merge PR #201, clean up worktree
