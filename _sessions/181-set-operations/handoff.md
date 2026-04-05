# Work Handoff: 181-set-operations

## Key Components
- `src/Quarry/Query/IQueryBuilder.cs` — Union/UnionAll/Intersect/IntersectAll/Except/ExceptAll methods on both IQueryBuilder<T> and IQueryBuilder<TEntity, TResult> (including cross-entity generic overloads)
- `src/Quarry.Generator/IR/QueryPlan.cs` — SetOperatorKind enum, SetOperationPlan class, QueryPlan.SetOperations property
- `src/Quarry.Generator/Models/InterceptorKind.cs` — 6 new InterceptorKind values
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — InterceptableMethods dict entries, ExtractSetOperationOperandChainId helper
- `src/Quarry.Generator/IR/RawCallSite.cs` — OperandChainId property
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — Inline operand splitting, AnalyzeOperandChain, set operation plan building, IsSetOperationKind/MapToSetOperatorKind helpers
- `src/Quarry.Generator/IR/SqlAssembler.cs` — Set operation SQL rendering (UNION/INTERSECT/EXCEPT between SELECT statements), GetSetOperatorKeyword helper
- `src/Quarry.Generator/CodeGen/SetOperationBodyEmitter.cs` — Carrier interceptor for set operation methods
- `src/Quarry.Generator/CodeGen/FileEmitter.cs` — Dispatch for set operation InterceptorKinds
- `src/Quarry.Generator/CodeGen/InterceptorRouter.cs` — EmitterCategory.SetOperation routing
- `src/Quarry.Tests/SqlOutput/CrossDialectSetOperationTests.cs` — 7 cross-dialect tests (currently failing)

## Completions (This Session)
- Phase 1: Foundation types + runtime API (committed af0d050)
- Phase 2: Discovery + chain linking (committed 4dfa517)
- Phase 3 partial: Chain analysis inline operand splitting, SQL assembly, SetOperationBodyEmitter (WIP commit 3e175b9)

## Previous Session Completions
(none — first session)

## Progress
- Phases 1-2 fully complete and committed
- Phase 3-4 merged — SQL assembly works for set operations. The QueryPlan → SQL rendering correctly generates `SELECT ... UNION SELECT ...` with ORDER BY/LIMIT on combined result
- Code generation partially works — SetOperationBodyEmitter generates the Union method interceptor but the operand chain's sites don't get interceptors

## Current State
The blocking issue is that the operand chain (right-hand side of Union/Intersect/Except) shares the same ChainId as the main chain. Both chains are rooted at the same db context variable in the same method. The inline operand splitting in ChainAnalyzer correctly identifies operand sites and builds their QueryPlan. The SQL is correctly assembled. But the operand's runtime method calls (db.Users(), .Where()) need interceptors to create and configure the operand carrier at runtime.

### Failed approach: Skipping operand chains
Tried skipping operand chains in the analysis loop entirely. Result: operand's Users() call throws NotSupportedException because no interceptor is generated.

### Failed approach: isOperandChain flag with synthetic terminal
Tried using ChainRoot as synthetic execution site for operand chains. Result: build succeeds but the generated interceptor for Union has wrong signature (tries to use Users() signature for Union method). Fixed the signature issue but the operand's own ChainRoot and Where interceptors are still not generated because the sites are extracted during splitting.

## Known Issues / Bugs
- 7 new tests in CrossDialectSetOperationTests fail with NotSupportedException
- The AnalyzeOperandChain method is unused in the current approach (inline splitting produces plans directly)

## Dependencies / Blockers
- The operand carrier problem blocks all Phase 3-6 work

## Architecture Decisions
- Set operations are modeled as `IReadOnlyList<SetOperationPlan>` on QueryPlan (each with Kind + operand QueryPlan + parameter offset)
- SQL rendering: left SELECT rendered normally, then each set operation appends `UNION/INTERSECT/EXCEPT + operand SELECT`, then ORDER BY/LIMIT apply to combined result
- Inline operand splitting: when both chains share ChainId, ChainAnalyzer identifies operand sites by position (sites between set-op call and next terminal that start with a new ChainRoot)
- SetOperationBodyEmitter: pass-through interceptor that accepts the operand builder argument, copies operand parameters to main carrier fields, returns the carrier

## Open Questions
- Which of the 3 solution approaches for operand carrier generation is best? (See workflow.md Suspend State for descriptions)
- Should the operand chain share the main carrier (approach 2) or get its own carrier (approach 1)?
- How should parameter copying work at runtime between operand and main carriers?

## Next Work (Priority Order)
1. **Solve operand chain carrier generation** — This is the critical blocker. The operand's ChainRoot and clause sites need interceptors. Recommended approach: dual carrier (approach 1) — generate a separate carrier for the extracted operand sites. The operand carrier is created by its ChainRoot interceptor and clause methods configure it. The Union interceptor on the main carrier receives the operand carrier, casts it, and copies parameter field values. This requires:
   a. After inline splitting, create an AnalyzedChain for the operand sites (with ChainRoot as synthetic execution site)
   b. Run the operand AnalyzedChain through SqlAssembler and CarrierAnalyzer
   c. FileEmitter generates a separate carrier class for the operand
   d. SetOperationBodyEmitter's Union interceptor casts `other` to the operand carrier class and copies P0, P1, etc.

2. **Get the 7 CrossDialectSetOperationTests passing** — Once the operand carrier works, verify SQL output and execution results

3. **Commit phases 3-4** — Once tests pass

4. **Phase 5: Cross-entity support + post-union WHERE** — Subquery wrapping for post-union filtering

5. **Phase 6: Diagnostics** — Compile-time errors for unsupported dialect combinations
