# Review: scope-trest-tuple-projection

## Classifications

| # | Section | Finding (one line) | Sev | Rec | Class | Action Taken |
|---|---|---|---|---|---|---|
| 1 | Plan Compliance | All three plan phases delivered; no scope creep | Info | D | D |  |
| 2 | Plan Compliance | plan.md cites `:1445` for anon-rejection; actual is `:1446` | Low | D | D |  |
| 3 | Plan Compliance | Verdict column filled honestly; CTE marked Untested | Info | D | D |  |
| 4 | Plan Compliance | Plan's three-option (a/b/c) recommendation collapsed to (a) | Info | D | D |  |
| 5 | Correctness | All scope.md audit-table line citations verified | Info | D | D |  |
| 6 | Correctness | Ordinal-to-Rest mappings in 16-elem comments correct | Info | D | D |  |
| 7 | Correctness | scope.md self-contradictory phrase "Rest.Item4 (well, Rest.Item3)" | Low | A | A | Reworded the 10-elem narration in scope.md; `Priority` is now correctly stated at `Rest.Item2` (ordinal 8) and `OrderDate` at `Rest.Item3` (ordinal 9) |
| 8 | Correctness | 8-element comment ValueTuple<T8> framing accurate | Info | D | D |  |
| 9 | Correctness | 16-elem seed assertions match `QueryTestHarness:608` | Info | D | D |  |
| 10 | Correctness | `IsValidTupleTypeName` audit-candidate verdict well-founded | Info | D | D |  |
| 11 | Security | No concerns | — | D | D |  |
| 12 | Test Quality | Cross-dialect parity + execution structure correct | Info | D | D |  |
| 13 | Test Quality | `Tuple_7Elements_FlatLast` asserts 4 fields on Lite, 2 on Pg/My/Ss | Low | A | A | Pg/My/Ss assertions tightened to match Lite (UserId, UserName, OrderId, Total) |
| 14 | Test Quality | No NULL coverage at the TRest boundary (no nullable inside `Rest`) | Medium | A | A | Added `Tuple_NullableInsideRest` (9 elements with `Order.Notes` at ordinal 8 / Rest.Item2; asserts both non-null and NULL row materialize correctly through the IsDBNull-guarded reader path) |
| 15 | Test Quality | Enum + DateTime coverage at `Rest.Item2/3` present | Info | D | D |  |
| 16 | Test Quality | 16-elem skips middle Rest ordinals (4/5) — coverage edge-only | Low | A | A | Added `OrderDate` (Rest.Item4, ordinal 10) and `OrderItemId` (Rest.Item5, ordinal 11) assertions to `Tuple_16Elements_DeepDoubleNested` |
| 17 | Test Quality | No CTE wide-tuple coverage; highest unverified risk in audit | Medium | A | A | Added `Tuple_PostCteWideProjection` (8 elements rooted on `FromCte<Order>()`). Surfaced 3 pre-existing generator bugs: Bug A fixed in this PR (`SqlAssembler.cs:1359` empty-alias handling); Bugs B and C deferred (#280, #281) — test was scoped to use a non-FK 8th element (`Echo: o.OrderId`) and client-side sorting to avoid entanglement |
| 18 | Codebase Consistency | Conventions match `CrossDialect*Tests.cs` exactly | Info | D | D |  |
| 19 | Codebase Consistency | Type-level XML doc more verbose than peers (acceptable) | Info | D | D |  |
| 20 | Codebase Consistency | Manifest regeneration mechanical, content-only | Info | D | D |  |
| 21 | Integration | Branch purely additive; no public-surface or behavior change | Info | D | D |  |
| 22 | Integration | TupleElements-flat claim verified against `:1600` | Info | D | D |  |
| 23 | Integration | Recommendation (b) follow-up routing is API-friction-free | Info | D | D |  |
| 24 | Integration | "No arity guard" recommendation rests on still-untested CTE path | Low | A | A | scope.md updated: CTE row now reads "OK after Bug A fix"; the "no arity guard" recommendation still stands but rests on tested ground (Bug A fixed; FK projection through CTE deferred to #280 with the test scoped accordingly) |

## Plan Compliance
| Finding | Severity | Why It Matters |
|---|---|---|
| Plan's three phases (wide-tuple tests, scope.md, single commit) all delivered. Diff is exactly what plan.md describes: 1 new test file + scope/plan/workflow + auto-regenerated manifests. No generator code touched, matching the "Scope-only deliverable, no generator changes" decision. | Info | — |
| Plan referenced `ProjectionAnalyzer.cs:237/580/1445` for the anonymous-type rejection sites; actual line numbers are 235/578/1446. scope.md updates the citation to `AnalyzeJoinedExpression:578` — accurate. The `:1445` → actual `1446` is a 1-line drift but the surrounding context makes it self-correcting. | Low | Citation drift can mislead future readers chasing the audit; not load-bearing because scope.md narrows it to one verified site. |
| Plan's Phase-1 audit table column "Verdict (after Phase 1)" is filled in scope.md as planned. CTE re-projection is honestly marked `Untested by this workflow` (scope creep avoided — recommended as follow-up rather than added here). | Info | — |
| Recommendation (b) "ship with arity guard" from plan.md is dropped from scope.md in favor of (a) "ship without guard." Justified by the runtime evidence; not a deviation, but worth noting that the plan's three-option list collapsed to one recommendation. | Info | — |

## Correctness
| Finding | Severity | Why It Matters |
|---|---|---|
| All audit-table line citations verified against the worktree: `ReaderCodeGenerator.GenerateTupleReader:149`, `TypeClassification.BuildTupleTypeName:303`, `ProjectionAnalyzer.BuildTupleTypeNameFromSymbol:1598`, `IsValidTupleTypeName:1650` (`var parts = inner.Split(',');` is exactly there), `ChainAnalyzer.cs:2258` and `:2292` (both call `TypeClassification.BuildTupleTypeName(columns, fallbackToObject: false)`). All claims grounded. | Info | — |
| Ordinal-to-Rest mappings in the 16-element test comments are correct. Ordinal 7 (8th element, `o.Total`) → `Rest.Item1`. Ordinal 13 (14th, `oi.Quantity`) → `Rest.Item7`. Ordinal 14 (15th, `oi.UnitPrice`) → `Rest.Rest.Item1`. Ordinal 15 (16th, `oi.LineTotal`) → `Rest.Rest.Item2`. Matches C# `ValueTuple<T1..T7, ValueTuple<T1..T7, ValueTuple<T1, T2>>>` rewrite. | Info | — |
| Scope.md narration of the 10-element test contains a self-corrected slip: "Includes a `DateTime` at `Rest.Item4` (well, ordinal 9 → `Rest.Item3`)." The parenthetical correction is right; the leading claim is wrong. Cosmetic but the document is the artifact of record. | Low | Reader of scope.md may briefly mistrust the audit table when they hit the inline contradiction; trivial to clean up. |
| 8-element test comment says `ValueTuple<T1..T7, ValueTuple<T8>>`. Strictly accurate — `ValueTuple<T>` is a real 1-arity overload and the C# compiler does emit `Rest = new ValueTuple<T8>(...)` for an 8-tuple. Verified against scope.md's framing. | Info | — |
| Seed data backing the 16-element assertions confirmed in `QueryTestHarness.cs:608` (`OrderItemId=1, ProductName='Widget', Quantity=2, UnitPrice=125.00, LineTotal=250.00`). Assertions in `Tuple_16Elements_DeepDoubleNested` align exactly. | Info | — |
| The `IsValidTupleTypeName` "audit candidate" verdict is well-founded: the function uses `inner.Split(',')` while a depth-aware splitter (`SplitTupleElements`) lives in the same generator project at `TypeClassification.cs:276`. The two functions agree on flat-only inputs but diverge as soon as a tuple element is itself a generic with a comma (e.g., `Dictionary<int, string>`). Latent bug correctly identified. | Info | — |

## Security
| Finding | Severity | Why It Matters |
|---|---|---|
| No concerns. | — | — |

## Test Quality
| Finding | Severity | Why It Matters |
|---|---|---|
| Cross-dialect SQL parity is asserted via `QueryTestHarness.AssertDialects` for all four arities, mirroring the surrounding `CrossDialect*Tests.cs` pattern. Execution is run on Lite/Pg/My/Ss with `ExecuteFetchAllAsync`. Fixture/namespace/usings/`internal class` annotation match the convention exactly. | Info | — |
| `Tuple_7Elements_FlatLast` asserts named access on Lite for 4 fields but only 2 (`UserName`, `Total`) on Pg/My/Ss. That's enough to detect a per-dialect reader regression but not enough to detect a per-dialect ordinal-misalignment that happens to swap two same-typed columns. Tightening the non-Lite assertions to cover at least one column from each end of the tuple would harden the regression net. | Low | The 8/10/16 tests progressively widen the cross-dialect assertions, so the gap is most visible at arity 7 — the boundary the test is named after. |
| No NULL-coverage at the TRest boundary. `Order.Notes` is nullable and was deliberately excluded by the plan to keep materialization clean. The 10-element test was the natural place to stage one nullable column inside `Rest`; the audit-table verdict for `BuildTupleTypeNameFromSymbol` (which has explicit nullability handling at lines 1618–1623) goes un-exercised across the `TRest` boundary as a result. | Medium | The upcoming anon→tuple rewrite will routinely produce wide projections with nullable columns past ordinal 7. The runtime "OK" verdict is weaker than it could be without a single nullable-inside-Rest assertion. |
| `Tuple_10Elements_DeeperNested` does cover an enum (`OrderPriority`) at Rest.Item2 and a `DateTime` at Rest.Item3 — meaningful for the enum-cast and `DateTime` reader-call paths. Good coverage of the two reader-emission specializations most likely to break under nesting. | Info | — |
| `Tuple_16Elements_DeepDoubleNested` does not assert `OrderDate` (Rest.Item5) or `OrderItemId` (Rest.Item5) — Rest.Item4–5 are skipped. Coverage is dense at depth 0/1/2 boundaries (the Rest-edges) and sparse in the middle. Probably fine, since a regression that breaks only mid-`Rest` ordinals is implausible, but the test is named "DeepDoubleNested" and could close the loop with one more middle assertion. | Low | Cosmetic completeness; not a regression risk. |
| All four tests run end-to-end on real PG/My/Ss connections. That is the right level of evidence for a "TRest works at runtime today" verdict — symbol-only tests would not have caught a reader-emission bug. | Info | — |
| No CTE coverage for wide tuples. scope.md flags this honestly under "Untested by this workflow" with a recommendation for a separate PR. The audit table marks `FromCte<T>` risk as Medium, which is the highest unverified risk in the document. | Medium | The "ship without arity guard" recommendation rests partly on a Medium-risk path (CTE re-projection) that has zero test coverage. The recommendation acknowledges this with follow-up (c), but a CI-gated workflow would prefer the test land in the same change. |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---|---|---|
| File header `using Quarry; using Quarry.Tests.Samples; using Pg = …; using My = …; using Ss = …;` matches `CrossDialectCompositionTests.cs`. Namespace `Quarry.Tests.SqlOutput` matches. `[TestFixture] internal class` matches. `await using var t = await QueryTestHarness.CreateAsync(); var (Lite, Pg, My, Ss) = t;` per-test setup matches. `Prepare()` + `ToDiagnostics()` + `AssertDialects` + `ExecuteFetchAllAsync` flow matches. | Info | — |
| File contains an XML doc-comment at the type level explaining `TRest`. This is more documentation than the surrounding `CrossDialect*Tests.cs` files typically carry (most just have a one-line `[TestFixture]` or no doc at all; `CrossDialectCompositionTests` has a 4-line `<summary>`). Slight stylistic divergence in the direction of more documentation, which is appropriate for a guard-rail test file with non-obvious intent. | Info | — |
| Manifest regeneration is mechanical: 4 dialect manifests gain three `Users().Join(...).OrderBy(...).Select(...)` entries plus one `Users().Join(...).Join(...).OrderBy(...).Select(...)` entry per dialect. Diffs are content-only (no formatting drift). Same shape as prior cross-dialect feature additions. | Info | — |

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---|---|---|
| Branch is purely additive (1 new test file + 4 manifest regenerations + 3 session docs). No public surface, no generator behavior changes, no breaking changes. Tests guard a runtime invariant that existing callers already depend on for arities 1–6. | Info | — |
| Audit-table claim that `Roslyn's INamedTypeSymbol.TupleElements returns the full element list flat regardless of TRest nesting` is correct — verified against `BuildTupleTypeNameFromSymbol:1600` which iterates `tupleType.TupleElements` directly with no Rest-awareness, and the 16-element test passes through this path. | Info | — |
| Recommendation (b) "fix `IsValidTupleTypeName:1650`" is correctly framed as an independent follow-up. The fix candidate (`TypeClassification.SplitTupleElements:276`) is `internal` to the `Quarry.Generator` assembly and `IsValidTupleTypeName` is in the same assembly, so the routing is straightforward — no API friction. | Info | — |
| The upcoming anon→tuple code-fix branch is not in this worktree. No file-level merge conflict surface here (the new test file is in a fresh path; the manifest deltas are append-only inside dialect-specific files). The only conceptual coupling is that scope.md's recommendation (a) commits the anon→tuple branch to ship without an arity guard — that decision should be re-validated when the rewrite lands, especially against the still-untested CTE wide-tuple path. | Low | The integration risk is in the *recommendation*, not the code: a future reviewer of the rewrite branch needs to see CTE coverage before the "no arity guard" stance is reconfirmed. |

## Issues Created

- #280: FK `.Id` projection in CTE post-Select emits empty column name + unfilled cast type (Bug B; review item #17 surfaced during implementation)
- #281: Post-CTE chain-continuation methods (OrderBy/Limit/Offset on IEntityAccessor) emit malformed interceptor (Bug C; surfaced during review item #17 implementation)
