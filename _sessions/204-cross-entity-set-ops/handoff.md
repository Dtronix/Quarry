# Work Handoff: 204-cross-entity-set-ops

## Key Components
- `src/Quarry.Generator/IR/RawCallSite.cs` — Added `OperandEntityTypeName` field
- `src/Quarry.Generator/IR/QueryPlan.cs` — Added `OperandEntityTypeName` to `SetOperationPlan`
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — Extracts TOther generic type arg during discovery
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — Propagates operand entity type to SetOperationPlan
- `src/Quarry.Generator/CodeGen/SetOperationBodyEmitter.cs` — Generates correct arg type for cross-entity
- `src/Quarry.Generator/IR/PipelineOrchestrator.cs` — QRY073 diagnostic removed
- `src/Quarry.Generator/DiagnosticDescriptors.cs` — QRY073 descriptor removed
- `src/Quarry.Tests/SqlOutput/CrossDialectSetOperationTests.cs` — 6 new cross-entity tests (Lite only)
- `src/Quarry.Tests/QueryTestHarness.cs` — Added products table schema and seed data

## Completions (This Session)
1. **Phase 1**: Added `OperandEntityTypeName` field to `RawCallSite` (constructor, property, Equals, `WithResultTypeName` copy method) and `SetOperationPlan` (constructor, property, Equals, GetHashCode). Commit: `f50b1ce`
2. **Phase 2**: Extracted TOther type argument during discovery using `semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol` + `TypeArguments[0].ToDisplayString(FullyQualifiedFormat)` + global:: prefix stripping. Commit: `ea3f70a`
3. **Phase 3**: Propagated `OperandEntityTypeName` from `RawCallSite` through `ChainAnalyzer` to `SetOperationPlan` constructor. Commit: `e862a3a`
4. **Phase 4**: Updated `SetOperationBodyEmitter.EmitSetOperation` to generate the correct `argType` when the operand has a different entity type. Uses `ResolveExecutionResultType` to get the shared result type and constructs `IQueryBuilder<{operandEntity}, {resultType}>`. Commit: `d42d0e1`
5. **Phase 5**: Removed QRY073 `CrossEntitySetOperationNotSupported` diagnostic from `PipelineOrchestrator.CollectPostAnalysisDiagnostics`, removed the descriptor from `DiagnosticDescriptors.cs`, and updated `GeneratorTests.DiagnosticDescriptors_SetOperation_IdsAreUnique` test. Commit: `b2f3364`
6. **Phase 6**: Added 6 cross-entity tests (Union, UnionAll, Intersect, Except, WithParameters, WithPostUnionOrderByLimit) + products table to `QueryTestHarness`. Tests use only the Lite (SQLite) context due to pre-existing interceptor bug with Products() in Pg/My/Ss contexts. Commits: `4e625bd`, `46bb068`
7. **Review analysis**: Agent-produced `review.md` with 5 findings (2 medium, 3 low). Medium findings both relate to multi-dialect test coverage gap.

## Previous Session Completions
- Session 1 (suspended mid-REVIEW): Completed all 6 implementation phases. Wrote `review.md` analysis pass. Suspended before presenting classifications to user.
- Session 2 (suspended after REMEDIATE→REVIEW phase reset): All "Fix all" remediation work done, PR #210 created with green CI. Pre-existing CallSiteBinder per-context entity bug fixed. Phase reset back to REVIEW so user could add review items from a Union TResult discussion.

## Session 3 Completions
- Resumed from suspend. Baseline tests 2965 green.
- User chose to add Union TResult docs only (declining the QRY072 retest path).
- Added a uniform `<remarks>` block to all six cross-entity overloads on `IQueryBuilder` (`Union<TOther>`, `UnionAll<TOther>`, `Intersect<TOther>`, `IntersectAll<TOther>`, `Except<TOther>`, `ExceptAll<TOther>`). The block explains the strict `TResult` constraint enforced by C# generics, the explicit-`Select` escape hatch for structurally different entities, and the parity with EF Core / LINQ to SQL conventions.
- Updated PR #210 description: added a "Design Notes" section between "Gaps in original plan implemented" and "Migration Steps" covering the strict-TResult constraint, the SQL UNION permissiveness tradeoff, the escape hatch, EF Core / LINQ to SQL parity, and QRY072's defensive retention.
- Tests still 2965 green after the doc-only changes.

## Session 2 Completions
- Resumed session, user chose "Fix all" classification.
- Investigated and fixed pre-existing source generator bug: per-context entity type resolution in `CallSiteBinder.Bind`. The chain root and builder method discovery had been recording the schema-namespace user-written class instead of the per-context generated class, causing CS9144 interceptor signature mismatches for any non-default context (PgDb/MyDb/SsDb) using an entity that has a partial declaration in user source. Fix rewrites `RawCallSite.EntityTypeName` and `OperandEntityTypeName` to `global::{contextNamespace}.{entityName}` only when the discovery's namespace differs from the context namespace. Includes a foreign-context rebind fallback when registry resolution returns the wrong context entry.
- Added `WithEntityTypeName` and `WithOperandEntityTypeName` copy helpers on `RawCallSite`.
- Removed leftover blank line in `PipelineOrchestrator.cs` (Class A finding).
- Extended cross-entity tests to all 4 dialects via `AssertDialects` (Union, UnionAll, Intersect, Except, WithParameters, WithPostUnionOrderByLimit).
- Added `CrossEntity_IntersectAll_TupleProjection` and `CrossEntity_ExceptAll_TupleProjection` tests (Pg-only).
- Strengthened Union/UnionAll/Except assertions with exact row-value checks (not just count) to prove product rows flow through the positional reader despite the C# tuple element labels coming from the receiver projection.
- Refreshed Pg/My/Ss manifest snapshots.
- Decided NOT to add a runtime QRY072 negative test (the C# type system rejects column-count mismatches before the generator runs); coverage stays at the descriptor level.
- Created PR #210 with comprehensive description, CI passed (1m35s).
- Open Union TResult discussion: explored why same-`TResult` is enforced by C# generic constraints, what SQL would allow, and whether to relax. Conclusion: keep strict — users can explicitly project to a common type if needed; matches EF Core / LINQ to SQL conventions; QRY072 stays as a defensive guard for future projection-flattening shapes.

## Progress
- Phases 1-6 complete and committed ✓
- Pre-existing per-context entity resolution bug fixed ✓
- All 2965 tests passing (79 migration + 103 analyzer + 2783 main) ✓
- Review analysis complete ✓
- Review classifications recorded ✓
- Remediation complete ✓
- PR #210 created and CI green ✓
- Merge NOT yet done ✗ (intentionally paused — user moved phase back to REVIEW)

## Current State
Phase reset back to REVIEW from REMEDIATE at user request, with all work preserved. PR #210 is open with green CI. The reset is for the user to add additional review items or revisit existing classifications — possibly informed by the Union TResult / SQL UNION permissiveness discussion. No code or commits were reverted.

## Known Issues / Bugs
**Pre-existing bug discovered during Phase 6 (not caused by this branch):**
- When `Products()` (and likely any entity accessor not previously used in cross-dialect set operation test files) is called on Pg/My/Ss contexts in a set operation context, the source generator produces interceptor signatures that don't match the call site, causing CS9144 errors.
- Example error: `Cannot intercept method 'PgDb.Products()' with interceptor '...Products_...(PgDb)' because the signatures do not match.`
- This forced the cross-entity tests to use only the Lite (TestDbContext/SQLite) context.
- The Lite context correctly generates `IQueryBuilder<Quarry.Tests.Samples.Product, (int UserId, string UserName)> other` for the Union interceptor, proving the cross-entity feature works end-to-end.
- The Pg/My/Ss context Union interceptor generates `IQueryBuilder<User, ...>` (wrong) instead of `IQueryBuilder<Product, ...>`, suggesting `GetSymbolInfo` on the Union call in these contexts doesn't resolve `TypeArguments` correctly, OR the discovery semantic model for those contexts doesn't fully resolve `Pg.Product`/`My.Product`/`Ss.Product` generic type arguments.

## Dependencies / Blockers
- **Blocking multi-dialect test coverage**: The pre-existing Pg/My/Ss interceptor bug. Needs a separate investigation and fix before multi-dialect cross-entity tests can be added. Recommended to file as a separate issue during REMEDIATE.

## Architecture Decisions
- **Discovery-based operand entity type extraction** (vs chain-based): Used `semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol` to extract `TypeArguments[0]` during discovery. This is direct and doesn't require looking up entity types from operand chains. Alternative would have been to derive it from the operand chain's ChainRoot entity type during chain analysis.
- **Optional constructor parameters**: Added `operandEntityTypeName` as optional parameters (`= null`) on both `RawCallSite` and `SetOperationPlan` constructors to preserve source compatibility.
- **Fall-through to same-entity behavior**: When `OperandEntityTypeName` is null, the emitter falls back to the existing `argType = receiverType` behavior, ensuring zero impact on same-entity set operations.
- **QRY073 full removal** (vs deprecation): Decided to delete the descriptor entirely rather than marking it obsolete. Users with existing suppressions will see an "unknown diagnostic" warning but that's harmless.
- **Lite-only cross-entity tests**: Due to the pre-existing Pg/My/Ss bug, tests only verify the Lite context. The feature itself is dialect-agnostic (the SetOperationBodyEmitter doesn't emit any dialect-specific code), so this is acceptable validation of the feature. The multi-dialect gap should be tracked separately.

## Open Questions
- Should the pre-existing Pg/My/Ss Products() interceptor bug be fixed in this PR or deferred as a separate issue? Current recommendation: **defer as separate issue** since it's pre-existing and unrelated to cross-entity set operations.
- Is the "unknown diagnostic" warning for QRY073 suppressions acceptable? User-facing impact is minimal but should be documented in the PR description.

## Next Work (Priority Order)
1. **Resume REVIEW classification**: Present findings to user via AskUserQuestion. Recommended classifications:
   - (A) Fix extra blank line in `PipelineOrchestrator.cs:214`
   - (C) Create issue for multi-dialect cross-entity test gap (blocked by pre-existing Pg/My/Ss interceptor bug)
   - (D) Ignore no IntersectAll/ExceptAll cross-entity tests
   - (D) Ignore no QRY072 negative test for cross-entity
   - (D) Ignore QRY073 suppression warnings
2. **REMEDIATE phase**:
   - Fix blank line in `PipelineOrchestrator.cs:214`
   - Create GitHub issue for Pg/My/Ss interceptor bug via `gh issue create`
   - Commit the fix
   - Rebase on origin/master
   - Run tests (must all pass)
3. **Create PR**: Read all `_sessions/` artifacts, build comprehensive PR body covering phases, decisions, deviations (Lite-only tests), and the follow-up issue. Use `gh pr create`.
4. **Wait for CI**: Verify CI passes before proposing merge.
5. **FINALIZE**: Squash merge after user approval.
