# Review (session 3): 204-cross-entity-set-ops

## Scope
HEAD `4c6aba6` vs `origin/master` (`eff00bc`). 14 commits, 16 production/test files touched (1813 diff lines). Focus areas per instructions: `IQueryBuilder.cs` (new XML `<remarks>` on 6 cross-entity overloads), `CallSiteBinder.cs` (per-context entity rebinding + namespace normalization), `SetOperationBodyEmitter.cs` (cross-entity arg type), `CrossDialectSetOperationTests.cs` (8 new tests), 4 manifest files. Prior review (`review.md`) covered plan compliance, correctness of `RawCallSite` / `SetOperationPlan` threading, and the initial SQLite-only test gap; all prior findings were addressed in session 2 (multi-dialect assertions, IntersectAll/ExceptAll coverage, whitespace, `CallSiteBinder` fix for per-context entity resolution).

## Plan Compliance
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All 6 plan phases remain implemented in order; session 2 remediation filled the cross-dialect gap from the first review, and session 3 added the docs follow-up discussed in `workflow.md` decisions | -- | Confirms plan intent preserved across both remediation cycles |
| Session 3 `<remarks>` docs on `IQueryBuilder.cs` match the workflow decision (2026-04-06 session 3): uniform block on all six cross-entity overloads documenting strict-TResult, explicit-projection escape hatch, and EF Core/LINQ to SQL parity | -- | Decision is faithfully executed |

## Correctness
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `RawCallSite.cs:217-224` has two stacked `<summary>` blocks before `WithOperandEntityTypeName`. The original `"Creates a copy with a different ResultTypeName. Used by PipelineOrchestrator to patch unresolved tuple types..."` summary was left in place when the new `WithOperandEntityTypeName` XML doc was inserted above the next method, so now `WithOperandEntityTypeName` is preceded by two summaries and `WithResultTypeName` at line 346 has NO summary at all. C# compiler XML doc extraction typically emits CS1587/CS1591 and attaches only the last summary, meaning the `ResultTypeName` documentation is effectively lost from IntelliSense and any generated docs | Low | Pure documentation regression — behavior is unaffected, but the prior (correct) doc for `WithResultTypeName` has been orphaned. Easy fix: move the first `<summary>` block to sit directly above `WithResultTypeName` at line 346 |
| `CallSiteBinder.cs:40-52` rebind loop: the outer `foreach (var ctx in registry.AllContexts)` has `continue` for non-matching contexts and an unconditional `break` at line 51 after the first matching context's inner loop completes. This is functionally correct because context `ClassName` values are unique, but the `break` is structurally fragile — if a future refactor allows duplicate context names (e.g., nested contexts in different namespaces), the code would silently skip the later one. A clearer form would be `if (rebound != null) break;` after the inner loop, or using `FirstOrDefault` | Low | Defensive style nit. No current bug |
| `CallSiteBinder.cs:37` guard only triggers when `entry.Context.ClassName != raw.ContextClassName`. If `raw.ContextClassName` is null (site without a resolved context), the rebind is skipped. That is consistent with the null-safety checks elsewhere, but worth noting — cross-entity set ops discovered on a site that failed to capture `ContextClassName` would still use the foreign-context `entry` | -- | Edge case appears unreachable in practice because the set-operation path sets `ContextClassName` during discovery, but the branch is untested |
| `SetOperationBodyEmitter.cs:29-47` cross-entity arg-type construction: the fallback branch `resultType != null ? "IQueryBuilder<{op}, {res}>" : "IQueryBuilder<{op}>"` still exists. As noted in the prior review, the single-type-arg fallback is effectively dead for cross-entity ops (TResult must be non-null for C# generic inference to succeed). Not a bug, just confirmed unchanged | -- | No new concern |
| `UsageSiteDiscovery.cs:731-737` `TypeArguments[0]` extraction assumes the method binding succeeded and `methodSymbol.TypeArguments.Length > 0`. For candidate-only / errored symbol info (e.g., partial code in IDE), `GetSymbolInfo(...).Symbol` would be null and the extraction is skipped. That gracefully degrades to same-entity behavior (operand entity name null) — which is the right fallback, but there is no telemetry/diagnostic path when `SymbolInfo.CandidateSymbols` contains a single method with type args. Downstream behavior is a harmless same-entity emit that will then fail with QRY073's successor paths or a compile error | Low | Edge case for in-progress editing. Not new to session 3; not flagged in `review.md` |

## Security
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No concerns. | -- | TOther flows from Roslyn semantic model only; no user-input surface reached. |

## Test Quality
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `CrossDialectSetOperationTests.cs:676-704` — `CrossEntity_IntersectAll_TupleProjection` and `CrossEntity_ExceptAll_TupleProjection` are `async Task` but contain no `await` on the assertion path after `await using var t = ...`. The initial `QueryTestHarness.CreateAsync()` makes `async` mandatory, so this is fine, but neither test actually executes the query on PostgreSQL (SQLite execution is skipped because the operators are PG-only). Coverage is therefore SQL-text-only; semantic/row-level validation is missing for IntersectAll/ExceptAll cross-entity | Low | The same gap exists for same-entity IntersectAll/ExceptAll in the same file, so this is consistent. Worth noting because the in-PR decision was specifically to not defer these variants |
| `CrossEntity_Union_WithPostUnionOrderByLimit` uses `.OrderBy(u => u.UserName)` on the post-union carrier. The field name resolves against the first operand's projection (`Users.Select(u => (u.UserId, u.UserName))`), and the assertion data (Alice/Bob/Charlie first) matches because Users sort earlier than Products alphabetically. The test is semantically correct, but it would be slightly more robust against accidental "only users flowed through" bugs if the assertion included a row where a Product sorts into the top-3 (e.g., LIMIT 6 with Doohickey at index 3) | -- | Informational — similar spot-check to the row-value verification already added in `CrossEntity_Union_TupleProjection` |
| `CrossEntity_Union_WithParameters` verifies count-only (`Has.Count.EqualTo(4)`), not the specific row values. Unlike `CrossEntity_Union_TupleProjection` (which was strengthened in commit `12bc318` to verify row values after a review comment), this test would pass even if the cross-entity operand rows did not flow through correctly — for example if products were silently dropped and users duplicated to match the count. The fix would be to `OrderBy` + assert the tuple sequence, matching the other tests | Low | Partial regression against the "verify by value, not count" principle applied elsewhere in this PR |
| `GeneratorTests.cs:1409-1419` — the replacement QRY072 commentary explains why the cross-entity column-count mismatch cannot be triggered from C# source. That is accurate and sufficient. No in-code test for it was added | -- | Consistent with D-classification from the prior review |
| The 8 new cross-entity tests cover Union, UnionAll, Intersect, Except (all-4-dialects + SQLite execution), IntersectAll, ExceptAll (PG-only SQL text), WithParameters (4-dialect + SQLite count), WithPostUnionOrderByLimit (4-dialect + SQLite row check) | -- | Good breadth; matches the session 2 remediation commitments |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `RawCallSite.WithEntityTypeName` and `RawCallSite.WithOperandEntityTypeName` duplicate ~55 lines of constructor-call boilerplate each, which together with the existing `WithResultTypeName` means 3 near-identical copies. If a future field is added to `RawCallSite`, all three must be updated or copies will silently drop the new field | Low | Same pattern as existing `WithResultTypeName`, so consistent with existing codebase; noted only as an accumulating maintenance cost. Consider a private `CopyWith(...)` helper in a follow-up |
| `CallSiteBinder.cs:95-112` operand normalization hand-strips `"global::"` with `Substring(8)` and manually extracts the simple name with `LastIndexOf('.') + Substring(+1)`. `UsageSiteDiscovery.cs:734` does the same `"global::"` strip. Both match the existing codebase pattern (Roslyn tooling convention), and the prior review already accepted this | -- | Not new |
| `IQueryBuilder.cs` new `<remarks>` blocks are byte-identical across all 6 overloads (good consistency), use `<typeparamref name="TResult"/>` correctly (TResult is in scope from the interface declaration), and use `<c>Select</c>` for inline code. `<typeparamref name="TOther"/>` is NOT referenced in the prose — only in the method signature/constraint. Minor asymmetry (TResult gets a typeparamref, TOther does not) but the remarks prose does not call out TOther so it's defensible | -- | Docs are well-formed |
| `GeneratorTests.cs:1413-1419` comment on QRY072 references `DiagnosticDescriptors_SetOperation_IdsAreUnique` as the descriptor-level coverage point; that test name exists earlier in the same file per the diff context | -- | Coherent rationale |
| No doc comment was added on the same-entity Union/UnionAll/Intersect/IntersectAll/Except/ExceptAll overloads at `IQueryBuilder.cs:281-285, 301-305, 321-325, 341-345, 361-365, 381-385`. The cross-entity overloads now have rich `<remarks>`, while the simpler same-entity overloads remain at single-sentence summaries. Mildly asymmetric but arguably correct since the same-entity overloads do not need the TResult-strictness explanation | -- | Informational |

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| QRY073 removal remains a breaking change for users with `#pragma warning disable QRY073` — unchanged from prior review. Documented in PR #210 Design Notes | Low | Prior review finding; not re-flagging |
| `RawCallSite` constructor gained a new optional parameter `operandEntityTypeName`. All existing callers use named arguments, so this is source-compatible. Internal type; no external impact | -- | No concern |
| Manifest count deltas (sqlite `+16/+0` rendered? — actually `+20` per pg manifest: 290→306 discovered, 54→60 deduped, 236→246 rendered) are consistent with adding 8 new top-level query chains per dialect (+ operand sub-chains). Expected churn from new tests | -- | Sanity-checked |
| Cross-entity manifest entries appear in all 4 dialect manifests including MySQL for `INTERSECT`/`EXCEPT` — MySQL 8.0.31+ is required for these operators but the library already emits them for same-entity cases, so this is not a new compatibility cliff | -- | Pre-existing behavior |

## Notes vs prior review
Intentionally NOT re-flagged (already addressed in session 2 per `review.md` classifications):
- Cross-entity tests missing multi-dialect coverage (A — fixed via `CallSiteBinder` rebind + `AssertDialects` in all but IntersectAll/ExceptAll)
- Extra blank line after QRY073 removal in `PipelineOrchestrator.cs` (A — removed in commit `7ec073d`)
- IntersectAll/ExceptAll cross-entity tests missing (A — added as PG-only SQL-text tests)
- QRY072 negative test (D — C# type system blocks the scenario; descriptor-level coverage kept)
- QRY073 suppression warning for existing users (D — documented)

New findings in session 3 are limited to: (1) the orphaned `<summary>` in `RawCallSite.cs` introduced by the `WithOperandEntityTypeName` insertion (Low, documentation-only), (2) `CrossEntity_Union_WithParameters` verifies count-only rather than row values unlike its siblings (Low, test strength), and (3) the `CallSiteBinder` rebind loop's structural fragility (Low, style). All other changes in session 3 (the six `<remarks>` blocks) are clean additions.

## Classifications (session 4 — "Fix all")
| Finding | Class | Action Taken |
|---------|-------|--------------|
| Orphaned `<summary>` above `WithOperandEntityTypeName` (RawCallSite.cs) | (A) | Moved the orphaned block back above `WithResultTypeName` at line 346. |
| `CrossEntity_Union_WithParameters` count-only assertion | (A) | Strengthened with `OrderBy` + tuple-sequence assertion `[(2,"Bob"), (3,"Charlie"), (3,"Doohickey"), (1,"Widget")]`. |
| `CallSiteBinder` rebind loop unconditional `break` | (A) | Replaced with `if (rebound != null) break;` so the outer loop keeps searching if the first matching context has no entity match. |
