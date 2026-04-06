# Review: #201 (Pass 4)

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No new concerns. | - | All plan phases are implemented or explicitly deferred with diagnostics (QRY073 for cross-entity, QRY044 not needed). Previous review findings on plan gaps have all been addressed. |

Positive notes: The six-phase plan is well-followed. Subquery wrapping for post-union WHERE/HAVING/GroupBy, direct ORDER BY/LIMIT application, parameter remapping, and dialect-specific diagnostics are all implemented as specified. Design decisions in workflow.md (dual-carrier approach, auto subquery wrapping, full re-chain API) are faithfully reflected in the code.

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `AssembledPlan.Equals()` and `GetHashCode()` do not include `IsOperandChain`. | Low | In the Roslyn incremental source generator pipeline, `AssembledPlan.Equals` is used for caching. Two plans differing only in `IsOperandChain` would compare as equal, potentially skipping re-generation. In practice this is very unlikely because operand chains have different `SqlVariants` (they have no execution SQL variants) and different `Plan` contents than non-operand chains. Still, the Equals contract is technically incomplete. File: `src/Quarry.Generator/IR/AssembledPlan.cs`, lines 153-164. |
| `QueryPlan.GetHashCode()` does not include `SetOperations.Count` or any post-union fields. | Low | Pre-existing pattern: the existing `GetHashCode` only uses `Kind`, `Tier`, `PrimaryTable`, `WhereTerms.Count`, `Parameters.Count`. The new fields (`SetOperations`, `PostUnionWhereTerms`, `PostUnionGroupByExprs`, `PostUnionHavingExprs`) are covered in `Equals` but not in `GetHashCode`. This follows the existing convention (many fields omitted from GetHashCode for simplicity). Hash collisions are resolved by `Equals`, so correctness is preserved, but dictionary performance could degrade for large collections of plans that differ only by set operation content. |

All correctness issues from the previous 3 reviews have been confirmed fixed: `GetSetOperatorKeyword` now throws on unknown kind, `RawCallSite.Equals` includes all three new fields, GROUP BY `paramIndex` is correctly advanced, stale comments are updated.

## Security

No concerns. Set operations compose existing parameterized SQL rendering with the same parameter binding infrastructure. No raw string interpolation of user input, no new external dependencies, no auth changes.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No test for set operation with parameterized post-union WHERE (parameter remapping across all three layers: left params + operand params + post-union params). | Low | The current post-union WHERE test uses a constant literal (`Where(u => u.UserId <= 2)`), not a captured variable. A test with a captured variable in the post-union WHERE would exercise the full three-layer parameter index chain. The parameterized cross-operand test already validates two-layer remapping, and the subquery wrapping test validates the SQL structure, so this is a gap in explicit coverage but not a likely bug vector. |
| No test for post-union ORDER BY with parameterized expressions. | Low | ORDER BY expressions typically reference columns, not parameters, so this is a narrow gap. The existing ORDER BY tests with set operations verify the SQL structure correctly. |

Positive notes: Test coverage is strong overall. 2873 tests pass, 0 skipped. The suite includes: all 6 set operators with cross-dialect SQL verification, SQLite execution with result count and value assertions, chained set operations (Union then Except), post-union WHERE with subquery wrapping across 4 dialects, post-union GroupBy, post-union GroupBy+Having, parameterized set operations with correct parameter indices across all 4 dialects. Diagnostic descriptor tests verify unique IDs and severity. The QRY070/QRY071 diagnostic descriptor validation tests replaced the previous [Ignore]d tests.

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No new concerns. | - | After the session 3 refactoring that extracted `EnrichIdentityProjectionWithEntityColumns`, the remaining duplication between `AnalyzeOperandChain` and `AnalyzeChainGroup` is acknowledged and acceptable (documented in workflow.md as known). |

Positive notes: The new code follows established patterns consistently: `Unsafe.As` for carrier casting, `PipelineErrorBag.Report` for error surfacing (replacing silent catch blocks), `SqlFormatting.QuoteIdentifier` for identifier quoting, standard `IEquatable<T>` implementations with `Equals`/`GetHashCode`. `SetOperationBodyEmitter` follows the same structure as `ClauseBodyEmitter` and `TransitionBodyEmitter`. The `QueryPlanReferenceComparer` in `FileEmitter` is a clean, focused utility.

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `IQueryBuilder<T>` and `IQueryBuilder<TEntity, TResult>` add 6 and 12 new default interface methods respectively. | Low | These are additive-only changes with default `throw` implementations. Existing implementations do not break. The cross-entity `Union<TOther>()` overloads on `IQueryBuilder<TEntity, TResult>` are defined but not wired in discovery/codegen (guarded by QRY073 diagnostic). Users who call these will get either a compile-time diagnostic (if the generator detects it) or a runtime `InvalidOperationException` (the default throw). This matches the pattern used for other not-yet-intercepted methods in the codebase. |
| `AssembledPlan` constructor gains a new `isOperandChain` parameter (default: `false`). | Low | Backward compatible due to default value. All existing call sites continue to work unchanged. |
| `RawCallSite` constructor gains 3 new parameters (all nullable with defaults). | Low | Backward compatible due to defaults. The `Clone()` method correctly propagates the new fields. |

No breaking changes detected. All new public API surface uses default interface implementations. All internal changes use optional parameters with defaults.

## Classifications

| # | Finding | Section | Class | Action Taken |
|---|---------|---------|-------|-------------|
| 1 | AssembledPlan.Equals omits IsOperandChain | Correctness | D | Ignored — unreachable in practice. |
| 2 | QueryPlan.GetHashCode omits set operation fields | Correctness | D | Ignored — follows existing convention. |
| 3 | No parameterized post-union WHERE test | Test Quality | A | Added test — exposed and fixed a real bug in ResolveSiteParams (post-union param offset). |
| 4 | No parameterized post-union ORDER BY test | Test Quality | A | Added test — passes correctly. |

## Issues Created
(none)
