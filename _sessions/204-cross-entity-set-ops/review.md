# Review: 204-cross-entity-set-ops

## Plan Compliance
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All 6 phases implemented in order with matching commits | -- | Confirms plan was followed faithfully |
| Phase 6 tests only cover SQLite dialect, not all 4 dialects as specified in plan ("Each test verifies SQL output across all 4 dialects") | Medium | The plan explicitly requires cross-dialect verification (SQLite, PostgreSQL, MySQL, SQL Server). Existing same-entity tests in the same file use `AssertDialects` with all 4 contexts, but new cross-entity tests only call `Lite.*` and assert SQLite SQL. Product entities exist in all 4 dialect namespaces (Pg, My, Ss), so this is feasible to add. |
| Plan calls for 6 test cases; implementation has 6 test methods matching the described scenarios | -- | Full coverage of planned test cases |
| IntersectAll and ExceptAll cross-entity variants are not tested | Low | The plan only lists Union, UnionAll, Intersect, Except, plus parameter and OrderBy/Limit tests. However, the code path handles IntersectAll/ExceptAll identically. The discovery code correctly includes them in the `if` guard. Not a plan violation but a gap. |

## Correctness
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `SetOperationBodyEmitter` boundary guard (`setOpIndex < chain.Plan.SetOperations.Count`) is correct and consistent with the existing guard on line 60 | -- | Prevents index-out-of-range if setOpIndex is stale |
| `ResolveExecutionResultType` fallback when result type is null/empty produces `IQueryBuilder<operandEntityType>` (single type arg) | Low | This fallback path would only trigger for identity projections (no Select). A cross-entity union without a Select would produce different column sets, which QRY072 would catch. The fallback is safe but arguably dead code for valid cross-entity operations. |
| `RawCallSite.GetHashCode` does not include `OperandEntityTypeName` | Low | Consistent with existing design -- `GetHashCode` only uses 5 identity fields (UniqueId, MethodName, FilePath, Line, Column). The `Equals` method does include `OperandEntityTypeName`. This is correct because two call sites at the same file/line/column cannot differ only by operand entity type. No hash collision risk. |
| `UsageSiteDiscovery`: `TypeArguments[0]` extraction assumes the first type argument is TOther | Low | This matches the API design where `Union<TOther>(...)` has exactly one type parameter. If the API signature ever added more type params, this would need updating, but that is a future concern. |
| `global::` prefix stripping uses `StartsWith`/`Substring(8)` -- hardcoded length | Low | Works correctly for "global::" (8 chars). Standard pattern seen elsewhere in Roslyn tooling. |

## Security
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No concerns. | -- | The operand entity type name flows from Roslyn's semantic model (compiler-verified type resolution), not from user input. It is used only for code generation of interceptor method signatures. No injection vector exists. |

## Test Quality
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Cross-entity tests only verify SQLite dialect, not PostgreSQL/MySQL/SQL Server | Medium | Same-entity tests in the same file verify all 4 dialects via `AssertDialects`. Cross-entity tests should follow the same pattern to catch dialect-specific quoting or keyword differences. All 4 dialect contexts have `Products()` available. |
| No negative test for cross-entity column count mismatch (QRY072 with different projection widths across entities) | Low | QRY072 is not new to this branch, but a test confirming it still fires for cross-entity mismatches would validate the QRY073 removal didn't break the sibling diagnostic. |
| No test for IntersectAll or ExceptAll cross-entity | Low | These variants use the same code path. Same-entity tests already cover IntersectAll/ExceptAll. Risk is minimal but completeness would improve confidence. |
| SQLite execution assertions are well-designed | -- | Tests verify both SQL text and execution results with meaningful data (distinct tuples, parameter filtering, ordering). The Intersect test verifying 0 rows and Except verifying 3 rows confirm semantic correctness, not just "it runs". |
| Parameter remapping test (`CrossEntity_Union_WithParameters`) validates cross-entity parameter numbering | -- | Important scenario: ensures @p0 and @p1 are correctly assigned across entity boundaries. |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Extra blank line at PipelineOrchestrator.cs:214 after QRY073 removal | Low | The QRY072 block's closing brace at line 213 is followed by an empty line before the loop's closing brace at line 215. The original had this blank line as a separator before the QRY073 block. Minor whitespace inconsistency. |
| New `SetOperationPlan` constructor uses optional parameter (`string? operandEntityTypeName = null`) | -- | Consistent with existing optional parameters in `RawCallSite`. Avoids breaking existing callers. |
| `WithResultTypeName` copy method in `RawCallSite` correctly propagates `OperandEntityTypeName` | -- | All new fields are included in the copy, Equals, and constructor. |
| Test file follows existing test class structure (region grouping, async test pattern, harness usage) | -- | Matches the style of existing tests in `CrossDialectSetOperationTests.cs`. |
| Products table seeding in `QueryTestHarness` follows existing pattern (CREATE TABLE then INSERT) | -- | Consistent with Users, Orders, Warehouses seeding. |

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| QRY073 removal is a breaking change for users who suppress or filter by QRY073 | Low | Users who had `#pragma warning disable QRY073` or MSBuild NoWarn entries will see a compiler warning about unknown diagnostic ID. This is expected and documented in the decisions. The warning is harmless. |
| `SetOperationPlan` constructor signature change is source-compatible | -- | New parameter is optional with default null. No external consumers (internal class). |
| `RawCallSite` constructor signature change is source-compatible | -- | New parameter is optional with default null. No external consumers (internal class). |
| No changes to public API surface (IQueryBuilder interface) | -- | The `Union<TOther>`, `Intersect<TOther>`, `Except<TOther>` overloads already existed on IQueryBuilder. This change only wires the generator to support them. |

## Classifications
User direction (2026-04-06): "Fix all" — address every finding in this PR.

| Finding | Section | Class | Action Taken |
|---------|---------|-------|--------------|
| Cross-entity tests missing multi-dialect assertions | Test Quality / Plan Compliance | (A) | Fixed pre-existing source generator bug in `CallSiteBinder.Bind` (entity type resolution for per-context generated entities when a user-written partial exists in a different namespace). All cross-entity test methods now assert across all 4 dialects via `AssertDialects`. |
| Extra blank line in PipelineOrchestrator after QRY073 removal | Codebase Consistency | (A) | Removed the blank line at line 214. |
| No IntersectAll/ExceptAll cross-entity tests | Test Quality | (A) | Added `CrossEntity_IntersectAll_TupleProjection` and `CrossEntity_ExceptAll_TupleProjection` tests (Pg-only — these set operators are PostgreSQL-only via existing QRY070/QRY071). |
| No negative test for QRY072 with cross-entity mismatch | Test Quality | (D)→(A) | **Reclassified 2026-04-06 (session 3)** after deeper investigation. The original (D) classification rested on the claim "TResult pins both sides to identical column counts." This is true for tuples and required-init records but **NOT** for DTOs with object initializers — both sides can share `TResult=MyDto` while assigning a different number of properties (e.g., `Select(u => new MyDto { A = u.X, B = u.Y })` vs `Select(u => new MyDto { A = u.X })`). `ProjectionAnalyzer.AnalyzeInitializerExpressions` counts one column per assignment expression, so the IR ends up with mismatched `Columns.Count`. **Action:** added `PipelineOrchestratorTests.CollectPostAnalysisDiagnostics_SetOperationColumnCountMismatch_EmitsQRY072` (and a matching no-op test for matched columns) which constructs the `AssembledPlan` directly via reflection on the private orchestrator method, bypassing the source-generator harness limitations with default interface methods. Also corrected the misleading comment in `GeneratorTests.cs`. |
| QRY073 removal may warn users with existing suppressions | Integration | (D) | Acknowledged — documented in decisions and PR description. |
| Orphaned `<summary>` block above `WithOperandEntityTypeName` (RawCallSite.cs:217-224) — `WithResultTypeName` doc was stranded when the new copy method was inserted | Correctness (session 3) | (A) | Moved the original `<summary>` block back down above `WithResultTypeName` at line 346. Pure doc fix; no behavior change. |
| `CrossEntity_Union_WithParameters` asserted only count, not row values — would still pass if products were silently dropped and users duplicated | Test Quality (session 3) | (A) | Replaced count-only assertion with `OrderBy(r => r.UserName)` + tuple-sequence `Is.EqualTo(new[] { (2,"Bob"), (3,"Charlie"), (3,"Doohickey"), (1,"Widget") })`, matching the row-value strengthening already applied to `CrossEntity_Union_TupleProjection` in commit `12bc318`. |
| `CallSiteBinder.cs:40-52` rebind loop used unconditional `break` — functionally correct but structurally fragile if context-name uniqueness ever changes | Correctness (session 3) | (A) | Replaced the unconditional `break` with `if (rebound != null) break;` so the outer loop continues searching if the first matching context has no entity match. |

## Issues Created
None — the multi-dialect coverage gap turned into an in-PR generator fix.
