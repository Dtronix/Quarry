# Review: 241-incremental-sql-mask-rendering

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Phase 1 (ORDER BY and post-union WHERE paramIndex fix) implemented as planned. All-terms iteration pattern applied to both sections, matching the existing main WHERE approach. | -- | Matches plan. |
| Phase 2 (Batch SELECT) implemented as planned. `RenderSelectSqlBatch` follows the segment-based architecture: shared prefix, pre-rendered WHERE/ORDER BY terms, shared middle, set operations handling, per-mask assembly. | -- | Matches plan. |
| Phase 3 (Batch DELETE) implemented as planned. `RenderDeleteSqlBatch` covers prefix + WHERE assembly per mask. | -- | Matches plan. |
| Phase 4 (Unit tests) implemented 9 tests in `SqlAssemblerBatchTests.cs`. Plan specified 6 test cases; #1 ("no conditional terms") was omitted because single-mask plans never enter the batch path (`PossibleMasks.Count > 1` guard). The remaining 5 planned cases are covered plus additional dialect and two-conditional-ORDER-BY tests. | Low | Test case #1 is unreachable by design, so omission is justified. However, a test that forcibly invokes `RenderSelectSqlBatch` with a single mask (mask=0, no conditional terms) via reflection would validate the batch method itself in this degenerate case. |
| Plan specified tests in `src/Quarry.Tests/Generation/SqlAssemblerTests.cs`; actual file is `src/Quarry.Tests/IR/SqlAssemblerBatchTests.cs`. | Low | Different path than planned but reasonable -- `IR/` matches the namespace of the code under test. Minor deviation from plan. |
| Integration guard uses `plan.PossibleMasks.Count > 1 && plan.Kind is QueryKind.Select or QueryKind.Delete` matching the plan's conditional dispatch pattern. | -- | Matches plan. |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `PreRenderWhereTerms` renders all non-trivial terms with global parameter offsets and `AssembleWhereClause` selects active terms per mask. This correctly mirrors the per-mask WHERE rendering in `RenderSelectSql`. | -- | Correct. |
| `PreRenderOrderByTerms` renders ALL order terms (unconditional + conditional) with global parameter offsets. `AssembleOrderByClause` filters to active terms per mask. This matches the Phase 1 fix in `RenderSelectSql`. | -- | Correct. |
| SQL Server `ORDER BY (SELECT NULL)` fallback: batch code passes `hasOrderBy: true` to `AppendPagination` to suppress the internal fallback, then handles it per-mask in the assembly loop via `needsSqlServerOrderByFallback`. This produces identical output to the per-mask path. | -- | Correct. |
| Set operations: batch code renders set operation operands with `RenderSelectSql(..., mask: 0, ...)` matching the per-mask code exactly. Post-union wrapping with `SELECT * FROM (...) AS "__set"` is handled correctly. | -- | Correct. |
| `finalParamCount = Math.Max(paramIndex, plan.Parameters.Count)` in batch mirrors `Math.Max(paramIndex, paramBaseOffset + plan.Parameters.Count)` in per-mask with `paramBaseOffset=0`. | -- | Correct. |
| Batch path skips `insertInfo` handling. This is safe because SELECT and DELETE don't use insertInfo (only Insert/BatchInsert do). | -- | Correct. |
| The batch method allocates a new `StringBuilder` and `List<string>` per mask in the assembly loop. For large mask counts (e.g., 2^N for N conditional terms), this creates O(2^N) temporary allocations. The per-mask path had the same allocation pattern, so this is not a regression, but an `ArrayPool`/`StringBuilder` pooling optimization was not explored. | Low | Not a regression but a missed further optimization opportunity. |
| `needsSqlServerOrderByFallback` is only set when `plan.Pagination != null`, correctly matching the per-mask behavior where the fallback is inside `AppendPagination` (which returns early when pagination is null). | -- | Correct. |

## Security

No concerns.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All 9 tests use a golden-comparison pattern: `AssertBatchMatchesPerMask` calls both `RenderSelectSqlBatch` and `RenderSelectSql` per mask via reflection and asserts SQL string equality and parameter count equality. This is a strong correctness guarantee. | -- | Good test design. |
| Tests use reflection to access private/internal methods (`BindingFlags.NonPublic`). This creates coupling to method signatures -- renaming or reordering parameters will silently break tests at runtime rather than at compile time. | Low | Accepted tradeoff for testing internal methods without exposing them publicly. NullReferenceException from `!` on `GetMethod` result will surface clearly if signatures change. |
| No tests for set operations (UNION/INTERSECT/EXCEPT) with conditional WHERE in the batch path. The plan did not specify this, but it's a complex code path (post-union wrapping, `postUnionWhereTerms`). | Medium | Set operations with conditional post-union WHERE terms are a realistic scenario. If the batch code has a bug in post-union assembly, existing integration tests may or may not catch it. A direct unit test would be more targeted. |
| No tests for GROUP BY / HAVING with conditional WHERE to verify `middleStr` is correctly placed between WHERE and ORDER BY. | Low | Covered implicitly by full test suite (3199 tests), but a direct batch test would add confidence. |
| No tests for CTE definitions in the batch path to verify `prefixStr` correctly includes WITH clause. | Low | CTEs are rendered identically in batch and per-mask (same code structure), and covered by full test suite. |
| No tests for JOINs (explicit or implicit) in the batch path. | Low | JOIN rendering is part of the shared prefix, identical between batch and per-mask. Covered by full suite. |
| Parameterized ORDER BY test (`BatchSelect_ParameterizedConditionalOrderBy_CorrectParamIndices`) specifically verifies that parameter indices don't shift when conditional terms are inactive -- this directly validates the Phase 1 correctness fix. | -- | Good targeted test. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Batch methods follow the same coding patterns as existing code: `StringBuilder` usage, `CountParameters`, `SqlExprRenderer.Render`, `RenderWhereCondition`, `AppendTableRef`, `SqlFormatting.QuoteIdentifier`. No new dependencies introduced. | -- | Consistent. |
| New helper methods (`PreRenderWhereTerms`, `PreRenderOrderByTerms`, `AssembleWhereClause`, `AssembleOrderByClause`) are placed in the `#region Helpers` section, consistent with existing code organization. | -- | Consistent. |
| Phase 1 changes (ORDER BY and post-union WHERE) modify `RenderSelectSql` to use the same all-terms iteration pattern already used for main WHERE. The code structure (HashSet for active set, accumulate paramOffset for all terms, filter active+non-trivial) is consistent across all three clause types. | -- | Consistent. |
| `Debug.Assert` message in batch CTE handling is slightly different from per-mask version (omits "Extend RenderSelectSql..." suggestion). | Low | Cosmetic difference. The batch version's message is still clear. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The batch path is gated by `plan.PossibleMasks.Count > 1`, so single-mask plans (no conditional terms) always use the original per-mask path. This means the optimization only activates when it provides benefit and avoids any risk for the common case. | -- | Good safeguard. |
| UPDATE queries are explicitly excluded from batch rendering (as specified in the plan decision). They continue to use the per-mask path. | -- | Correct. Insert and BatchInsert also continue using per-mask path. |
| No public API changes. `Assemble` signature unchanged. The batch methods are private static. | -- | No breaking changes. |
| The Phase 1 paramIndex fix (ORDER BY and post-union WHERE all-terms iteration) changes behavior for the per-mask path too: `paramIndex` is now advanced by ALL ORDER BY terms regardless of mask, not just active ones. For non-parameterized ORDER BY (the common case), `CountParameters` returns 0 so the behavior is identical. For parameterized conditional ORDER BY (theoretical case), the fix produces correct parameter indices. | Low | Behavioral change is intentional and correct. The previous code would have produced wrong parameter indices for parameterized conditional ORDER BY. Since this case doesn't exist in practice (no existing tests exercise it), the risk is near-zero. |
| The workflow.md in the diff shows `phases-complete: 3` and `phase: IMPLEMENT`, but the working copy's workflow.md shows `phases-complete: 4` and `phase: REVIEW`. This means the committed workflow.md is stale (from a mid-implementation commit). | Low | Session metadata only; no runtime impact. Should be updated before merge if session files are kept in the repo. |

## Classifications

| # | Section | Finding | Severity | Class | Action Taken |
|---|---------|---------|----------|-------|-------------|
| 1 | Plan Compliance | Test file path differs from plan | Low | D | Dismissed — reasonable placement |
| 2 | Plan Compliance | "No conditional terms" test omitted | Low | D | Dismissed — unreachable by design |
| 3 | Correctness | Per-mask allocation in assembly loop | Low | A | Fixed — reuse StringBuilder and List across masks |
| 4 | Test Quality | No set operations test in batch path | Medium | A | Fixed — added set-operation + post-union WHERE test |
| 5 | Test Quality | Reflection-based test coupling | Low | D | Dismissed — follows existing patterns |
| 6 | Test Quality | No GROUP BY/HAVING/CTE/JOIN batch tests | Low | D | Dismissed — covered by integration suite |
| 7 | Codebase Consistency | Debug.Assert message wording | Low | D | Dismissed — cosmetic |
| 8 | Integration | Workflow.md intermediate state | Low | D | Dismissed — updated during merge cleanup |

## Issues Created

None.
