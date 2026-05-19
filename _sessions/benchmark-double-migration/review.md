# Review: benchmark-double-migration

## Classifications
| # | Class | Rec | Sev | Section | Finding | Action Taken |
|---|-------|-----|-----|---------|---------|--------------|
| 1 | D | D | info | Plan | Cross-dialect aggregate test deferred per recorded decision | Dismissed — intentional deferral, no action |
| 2 | D | D | info | Plan | Typed-marker landed at all 8 Sum/Avg sites; WIP msg says 6 (squash-merge will collapse) | Dismissed — WIP message is collapsed at squash |
| 3 | D | D | info | Plan | `Precision(18, 2)` removed correctly from migrated columns | Dismissed — confirmation only |
| 4 | D | D | info | Plan | Min/Max `"object"` defaults verified untouched per deferral decision | Dismissed — confirmation only |
| 5 | D | D | info | Correctness | Reordered Try 1/2/3 priority matches plan algorithm exactly | Dismissed — confirmation only |
| 6 | D | D | info | Correctness | The default flip is the actual fix; reorder/gate is future-proofing | Dismissed — confirmation only |
| 7 | D | D | info | Correctness | All 8 Sum/Avg sites migrated (no misses) | Dismissed — confirmation only |
| 8 | D | D | info | Correctness | DatabaseSetup seed literals are numerically equivalent to decimal originals | Dismissed — confirmation only |
| 9 | D | D | info | Test Q | Tests exercise the bug at the layer it lives | Dismissed — confirmation only |
| 10 | **B** | B | medium | Test Q | Joined / window / joined-window aggregate paths share the same fix but aren't unit-tested | Added 3 unit tests covering joined-aggregate, single-entity window-aggregate, and joined-window-aggregate paths. Writing them surfaced a deeper latent bug — `AnalyzeJoinedInvocation` was building the joined scalar aggregate `ProjectedColumn` without `TableAlias`, so Stage 4 enrichment couldn't reach it. Per-user-decision, fixed in-branch by mirroring the alias extraction from `ResolveJoinedAggregate`. All 3 new tests pass; full suite 3493/3493. See workflow.md Decisions dated 2026-05-19 ("Extend Stage 4 enrichment"). |
| 11 | D | D | low | Test Q | No `Col<float>` coverage | Dismissed — recorded plan scope, low value |
| 12 | D | D | info | Test Q | Test file pattern matches `CarrierGenerationTests.cs` | Dismissed — confirmation only |
| 13 | D | D | low | Test Q | `Sum_OverDecimalColumn_ResolvesToDecimal` is a positive sanity check (passes on master too) | Dismissed — cosmetic; the test still guards against future Roslyn-overload regressions |
| 14 | **A** | A | low | Codebase | `public const` on `internal static class` is unconventional (effectively internal) | Changed to `internal const string UnresolvedTypeMarker = "?"` to match the class's accessibility. |
| 15 | D | D | info | Codebase | XML-doc on `UnresolvedTypeMarker` is multi-paragraph (justified by content) | Dismissed — divergence justified by Stage 1→Stage 4 contract |
| 16 | D | D | info | Codebase | `GetDecimal` → `GetDouble` renames are mechanically consistent | Dismissed — confirmation only |
| 17 | D | D | info | Codebase | `DapperOrderLagDto` cleanly removed (only refs are session docs) | Dismissed — confirmation only |
| 18 | D | D | info | Codebase | Quarry.Benchmarks is `Exe` — schema change has no consumer to break | Dismissed — confirmation only |
| 19 | D | D | info | Integration | `Col<decimal>` still supported in ~30 other files | Dismissed — confirmation only |
| 20 | D | D | info | Integration | `UnresolvedTypeMarker` is internal-only — no public API change | Dismissed — confirmation only |
| 21 | D | D | info | Integration | Schema change is local to the benchmark `.exe` | Dismissed — confirmation only |
| 22 | D | D | info | Integration | Generator fix is strict bug fix; no behavior change for previously-correct code | Dismissed — confirmation only |


## Plan Compliance

| Finding | Severity | Why It Matters |
|---|---|---|
| Cross-dialect aggregate test was deferred per the 2026-05-18 "Test coverage" decision and the deferral is explicitly recorded in workflow.md plus plan.md "Known follow-ups" #3. The implementation matches the recorded scope. | info | Confirms scope discipline; the absence is intentional, not a miss. |
| Typed-marker rename landed at all 8 Sum/Avg call sites (4 regular: `GetSqlAggregateInfo` Sum/Avg + `GetJoinedAggregateInfo` Sum/Avg; 4 window: `GetWindowFunctionInfo` Sum/Avg + `GetJoinedWindowFunctionInfo` Sum/Avg). Matches the 2026-05-19 "typed sentinel" decision exactly. Note that the WIP commit message `892312d` says "6 Sum/Avg call sites" — the actual touch count is 8; this is a stale message-only inconsistency, no code impact. | info | Verifies the decision was implemented at the right scope; documents the message/reality mismatch for the squashed-commit message. |
| `Precision(18, 2)` was correctly removed from `OrderSchema.Total` and `OrderItemSchema.UnitPrice`. Master had no `Precision` on `LineTotal`, so nothing to remove there. Precision is semantically meaningless on IEEE-754 `double` so this is correct. | info | Confirms the migration didn't leave dangling decimal-only modifiers on double columns. |
| Min/Max defaults verified NOT touched per the 2026-05-19 decision deferring them to follow-up #1: 8 `"object"` defaults remain at Min/Max sites (lines 1838, 1848, 2586, 2595, 2786, 2789, 2848, 2851). | info | Scope held; Min/Max migration genuinely deferred. |

## Correctness

| Finding | Severity | Why It Matters |
|---|---|---|
| Reordered priority in `ResolveAggregateClrType` matches plan.md exactly: Try 1 column lookup, Try 2 SemanticModel argument type, Try 3 (gated) invocation return type, then default. The `argResolved` gate on Try 3 is computed once and reused — correct. | info | The reorder + gate is the future-proofing layer; verified to match the documented algorithm. |
| The actual fix that makes the bug go away is the `"decimal"` → `UnresolvedTypeMarker` default flip at the 8 Sum/Avg call sites; the reorder is functionally inert in Stage 1 single-entity discovery (empty `columnLookup`, Error-typed semantic model). This is acknowledged explicitly in workflow.md and handoff.md and is intentional future-proofing. | info | Useful context for the squash-commit reviewer — the two changes target different failure modes. |
| All 8 Sum/Avg call sites are migrated (verified by grep at lines 1818, 1828, 2568, 2577, 2780, 2783, 2842, 2845). No misses. | info | Confirms completeness of the call-site migration. |
| `DatabaseSetup.cs`'s `Math.Round(10.0 + (i * 1.5), 2)` for i in [1..100] and `Math.Round(5.0 + (itemId % 20) * 2.5, 2)` for itemId % 20 in [0..19] are numerically equivalent to the decimal originals. 1.5, 2.5, 5.0, 10.0 and integer factors all have exact IEEE-754 representations within this domain; max magnitude ≈ 160 well under double precision; `Math.Round(..., 2)` normalizes any sub-cent dust. | info | The seed data is bit-identical after conversion; no test parity risk from the literal change. |

## Security

No concerns.

## Test Quality

| Finding | Severity | Why It Matters |
|---|---|---|
| The 5 tests exercise the bug at the exact layer it lives (Stage 1 aggregate CLR-type resolution emitted into the carrier interface). Per the workflow record, all 5 fail on master and pass after the fix. Assertions check both the positive (`Contain("IQueryBuilder<Order, double>")`) and the regression-blocker (`Not.Contain("IQueryBuilder<Order, decimal>")`) for the non-decimal cases. Strong test design. | info | Confirms the tests cover the actual bug, not a coincident surface. |
| Coverage gaps relative to the 8 migrated call sites: tests only cover single-entity `GetSqlAggregateInfo` Sum/Avg (2 of 8 paths). Joined-aggregate (`GetJoinedAggregateInfo`), window-aggregate (`GetWindowFunctionInfo`), and joined-window (`GetJoinedWindowFunctionInfo`) paths share the same defaultType-change but are not regression-tested at the unit level. A future maintainer touching one of those paths could revert it without test failure. | medium | The bug's mechanism is identical across all 4 sites; risk is symmetric. The Sum-over-decimal positive test passes on both master and the fix, so it's a baseline check rather than a regression guard. |
| No `Col<float>` coverage. The 2026-05-18 "test coverage" decision listed double/int/long; float wasn't called out, so this is consistent with the recorded plan, but float is the most likely next-encountered non-decimal numeric and would round out the priority-lock-down. | low | Minor gap; not a deviation from the recorded plan. |
| Test file pattern (compilation construction, reference set, `RunGeneratorsAndUpdateCompilation`, `.Interceptors..g.cs` lookup) matches `Generation/CarrierGenerationTests.cs` precisely. Code style and helper layout consistent. | info | Good consistency. |
| The `Sum_OverDecimalColumn_ResolvesToDecimal` test would have passed on master too (master's bug was to return decimal-as-default for any column type — coincidentally correct for decimal). It serves as a positive sanity check rather than a regression test, but the test name and asserts don't make that distinction explicit. | low | Cosmetic; doesn't affect the test's value as a permanent guard against future Roslyn-overload regressions. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---|---|---|
| `TypeClassification` is `internal static`; declaring `public const string UnresolvedTypeMarker` on an internal class is effectively internal access. Conventional style on an internal class would be `internal const`. Matches the task-guidance hint as "unconventional but harmless". | low | Cosmetic. If `TypeClassification` is ever promoted to `public`, the constant unintentionally becomes part of the public surface area. |
| XML-doc on `UnresolvedTypeMarker` is multi-paragraph (uses blank line for paragraph break inside a `<summary>`). Surrounding XML-doc style in `TypeClassification.cs` is single-paragraph terse summaries. The new doc is more thorough than the file's local convention but reads cleanly and the extra depth is justified by the Stage 1 → Stage 4 contract being non-obvious. | info | Minor style divergence justified by content; no action implied. |
| `GetDecimal` → `GetDouble` renames in the 7+1 reader benchmark files are mechanically consistent and match the pattern used by other numeric reader calls (`GetInt32`, `GetString`) in the same files. No drift. | info | Clean migration. |
| `DapperOrderLagDto` removal: only references remaining anywhere in the repo are in the session docs (`plan.md`, `handoff.md`); no orphan code references. | info | Clean removal. |
| `Quarry.Benchmarks.csproj` is `OutputType=Exe` with no `<PackageId>`/`<IsPackable>` — the schema files are not a public API surface, so the `Col<decimal>` → `Col<double>` change has no downstream consumer to break. | info | Confirms the breaking-shape change is internally scoped. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---|---|---|
| `Col<decimal>` remains supported and is used in ~30 other files across Samples, Quarry.Tests, Quarry.Migration.Tests, Quarry.Analyzers.Tests, the GeneratorHarness corpus, and the `llm.md`/`docs/articles/schema-definition.md` documentation. The migration is local to Quarry.Benchmarks; no library-level shape change. | info | Confirms decimal is still a first-class column type; users migrating their own aggregate-over-double schemas now get correct codegen as a side benefit of the generator fix. |
| `TypeClassification.UnresolvedTypeMarker` is a new symbol on an `internal static` class — not a publicly visible breaking change to package consumers. | info | No public-API impact. |
| Schema breaking change (`Col<decimal> Total` → `Col<double> Total`) is entirely internal to the benchmark `.exe`. No `PackageId`, no `ProjectReference` from any consumer outside the solution. | info | No external consumers. |
| Phase 1 generator fix is a strict bug fix — it only changes behavior when the previous behavior was demonstrably wrong (Stage 1 aggregate over non-decimal column). Schemas that aggregate over `Col<decimal>` continue to resolve to `decimal` via the enrichment pass. No silent behavior change for working code. | info | The generator change is safe to land as a non-breaking fix for downstream users. |

## Issues Created

_(empty for now)_
