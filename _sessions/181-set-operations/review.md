# Review: #201

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| QRY043 (SetOperationProjectionMismatch) and QRY044 (PostUnionColumnNotInProjection) from plan Phase 6 are not implemented. Plan specified 4 diagnostics; only 2 (QRY070/QRY071) were built. | Medium | Missing compile-time safety net -- users get no diagnostic when operand projections differ in column count or when post-union WHERE references columns not in the projection. These are helpful guardrails, but omission is acceptable for an initial release if documented. |
| Cross-entity `Union<TOther>()` overloads defined in `IQueryBuilder<TEntity, TResult>` (IQueryBuilder.cs lines 282-363) are not wired in discovery or code generation. Phase 5 of the plan called for full cross-entity support. workflow.md notes this is deferred. | Low | API surface advertises capability that will throw `InvalidOperationException` at runtime. Acceptable tradeoff if documented as not-yet-supported. The default-throw pattern matches other deferred features in the codebase. |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `GetSetOperatorKeyword` (SqlAssembler.cs) returns `"UNION"` for the default case instead of throwing. | Low | Silent fallback to UNION for an unexpected enum value would mask bugs rather than failing fast. All 6 enum cases are covered so the default is unreachable in practice. |
| Post-union GROUP BY rendering (SqlAssembler.cs) does not advance `paramIndex` after each GROUP BY expression. | Low | GROUP BY typically references column names, not parameters, so impact is unlikely in practice. But it breaks the invariant established by every other clause rendering loop in the assembler. |
| `RawCallSite.Equals()` includes `OperandChainId` but omits `OperandArgEndLine` and `OperandArgEndColumn`. | Low | Two RawCallSites identical except for operand argument end position would compare as equal. Harmless in practice. |
| Stale comment in PipelineOrchestrator.cs says "QRY041/QRY042" but the actual descriptors are QRY070/QRY071. | Low | Misleading for future maintainers. Code behavior is correct. |

## Security

No concerns.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| QRY070/QRY071 diagnostic tests all `[Ignore]`d with reason "Requires full generator pipeline." | Medium | Zero automated test coverage for diagnostic code paths. |
| No post-union HAVING test. | Low | The HAVING rendering code path is not directly tested. |
| No negative tests for cross-entity set operations. | Low | Low priority since cross-entity is documented as deferred. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `AnalyzeOperandChain` (~215 lines) duplicates significant logic from `AnalyzeChainGroup`. | Medium | Changes to projections or parameter remapping need mirroring. |
| Duplicate XML doc comment in UsageSiteDiscovery.cs. | Low | `DetectPreparedQueryEscape` lost its documentation. |

## Integration / Breaking Changes

No concerns. All changes are additive with safe defaults.

## Classifications

| # | Finding | Section | Class | Action Taken |
|---|---------|---------|-------|-------------|
| 1 | QRY043/QRY044 not implemented | Plan Compliance | A | Added QRY072 (projection mismatch). QRY044 not needed — type system prevents invalid column references. |
| 2 | Cross-entity unions not wired | Plan Compliance | A | Added QRY073 diagnostic for unsupported cross-entity set operations. |
| 3 | GetSetOperatorKeyword default → "UNION" | Correctness | A | Changed to throw InvalidOperationException. |
| 4 | GROUP BY paramIndex not advanced | Correctness | A | Fixed both regular and post-union GROUP BY rendering. |
| 5 | RawCallSite.Equals omits EndLine/Column | Correctness | A | Added OperandArgEndLine and OperandArgEndColumn to Equals. |
| 6 | Stale QRY041/042 comment | Correctness | A | Updated to QRY070/071. |
| 7 | QRY070/071 tests [Ignore]d | Test Quality | A | Replaced with descriptor validation tests (unique IDs, severity). 0 skipped tests. |
| 8 | No post-union HAVING test | Test Quality | A | Added Union_WithPostUnionGroupByAndHaving test. |
| 9 | Duplicate XML doc | Codebase Consistency | A | Fixed — restored DetectPreparedQueryEscape doc, removed duplicate. |
| 10 | AnalyzeOperandChain duplication | Codebase Consistency | A | Extracted EnrichIdentityProjectionWithEntityColumns shared helper. |

## Issues Created
(none — all items addressed in this session)
