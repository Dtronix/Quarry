# Code Review: #257 — Many<T>.Sum/Min/Max/Avg/Count in Select projection (Re-Review)

## Classifications

| # | Section | Finding | Sev | Rec | Class | Action Taken |
|---|---|---|---|---|---|---|
| 1 | Codebase Consistency | QRY073 ID reused — docs still mark it retired (release-notes-v0.3.0.md, analyzer-rules.md, llm.md) | Medium | A | A | Renamed descriptor to QRY074; code, tests, llm.md, analyzer-rules.md updated; v0.3.0 retired notes left intact. |
| 2 | Integration / Breaking Changes | Reusing retired QRY073 ID causes silent behavior change for users who kept `#pragma warning disable QRY073` pragmas | Medium | A | A | Resolved by #1 — QRY073 stays retired/skipped; lingering pragmas remain inert. |
| 3 | Correctness | `ResolveProjectionSubqueryColumn` swallows all `SqlExprBinder.Bind` exceptions via bare `catch` — loses root-cause signal | Low | C | A | Narrowed to `catch (Exception ex)`; unexpected binder throws now route to QRY900 with type+message preserved. |
| 4 | Correctness | Diagnostic `DiagnosticLocation` points at entire execution site, not the aggregate invocation — imprecise for multi-line Select | Low | C | A | Added `SubqueryInvocationLocation` on ProjectedColumn (captured from invocation syntax); diagnostic now points at the specific `.Sum(...)` / `.Max(...)` call. |
| 5 | Correctness | `TryParseNavigationAggregateColumn` silently returns null when parser yields non-SubqueryExpr; callers drop the column silently | Low | C | A | Added `TrySynthesizeUnresolvedSubquery` fallback so the unresolved IsResolved check in BuildProjection still fires QRY074. |
| 6 | Correctness | `ResolveSubqueryResultType` falls back to `"object"` for Min/Max when selector can't be resolved — reader will emit `GetValue` cast | Low | C | A | Threaded `selectorTypeIsKnown` out of the resolver; Min/Max with unresolved selector now emits QRY074 (retaining `object` for compilation safety). |
| 7 | Test Quality | No test for filtered navigation aggregates (`u.Orders.Where(...).Sum(...)`) — parser rejects them but no test locks this behavior | Low | D | D | Out of scope for #257 (follow-up feature). |
| 8 | Test Quality | `Select_Many_Sum_OnEmptyNavigation_ThrowsAtRead` asserts message contains "NULL" — brittle to provider wording changes | Low | C | A | Switched to exception-type assertion (QuarryQueryException / InvalidOperationException / InvalidCastException); no longer coupled to driver error wording. |
| 9 | Test Quality | QRY073 end-to-end test relies on an unregistered schema entity — functional but coupled to current registry semantics | Low | D | D | Acceptable coupling — renamed to QRY074 test. |
| 10 | Test Quality | No joined test covers the HasManyThrough + joined path (only HasMany on joined, HasManyThrough on single-entity) | Low | C | A | Added `Select_Joined_HasManyThrough_Max_OnLeftTable` to CrossDialectJoinTests covering all 4 dialects + data assertion. |
| 11 | Plan Compliance | Phase 5 negative diagnostic test WAS delivered (plan had permitted deferring it) — positive | Low | D | D | |
| 12 | Plan Compliance | Plan's "Open risk" on `useGenericParamFormat` was resolved cleanly by default `false` path matching existing aggregate handling | Low | D | D | |
| 13 | Codebase Consistency | New ProjectedColumn fields (`SubqueryExpression`, `OuterParameterName`) cleared after binding — prevents stale state; well-scoped | Low | D | D | |
| 14 | Codebase Consistency | `AttachLambdaParameterNames` allocates a fresh ProjectionInfo on every call — minor incremental-generator churn | Low | D | D | |
| 15 | Codebase Consistency | Pre-existing tuple-fixup at ProjectionAnalyzer.cs:493 drops CustomEntityReaderClass/JoinedEntityAlias — not introduced here, now partially masked | Low | D | D | |
| 16 | Security | No external input enters the subquery binder — SQL fragments come from compile-time syntax only | Low | D | D | |
| 17 | Integration / Breaking Changes | Public surface unchanged — new diagnostic is additive | Low | D | D | |
| 18 | Integration / Breaking Changes | Manifest output gained 9–10 rendered queries per dialect — expected and committed | Low | D | D | |

## Plan Compliance

| Finding | Severity | Why It Matters |
|---|---|---|
| Phase 5 negative diagnostic test was delivered (plan permitted deferring it); QRY073 now has both descriptor-shape and end-to-end emission tests in `GeneratorTests.cs:1391` and `GeneratorTests.cs:1406`. | Low | Positive observation: remediation addressed the optional Phase 5 gap; reduces the risk of a silent future regression. |
| "Open risk" (plan §Open risks) about `useGenericParamFormat` in `SqlExprRenderer.Render` was resolved by relying on the default `false`, matching the existing aggregate-column path downstream in `SqlAssembler.AppendSelectColumns:1077-1080` (which invokes `QuoteSqlExpression` as a pass-through when no `{...}` placeholders are present — `SqlFormatting.cs:289-291`). | Low | Positive observation: the risk was verified, not merely assumed away. |
| Plan §Open risks noted possible alias collision when multiple aggregates share `sq0`. Manifest output (e.g., `quarry-manifest.sqlite.md` for `Select_Many_MultipleAggregates_InTuple_Repro`) confirms each sibling subquery is independently parenthesized; duplicate `sq0` across siblings is legal SQL and tests pass cross-dialect. | Low | Positive observation: concern verified with concrete rendered SQL. |

## Correctness

| Finding | Severity | Why It Matters |
|---|---|---|
| `ResolveProjectionSubqueryColumn` at `ChainAnalyzer.cs:1798-1815` wraps the whole `SqlExprBinder.Bind` call in a bare `catch { ... }` that emits QRY073 with no inner-exception info. If `Bind` ever throws for a reason unrelated to navigation resolution (e.g., dialect mismatch, future refactor, NullReferenceException), the user sees a misleading "navigation could not be resolved" message and no stack trace. Consider narrowing the catch or falling back to QRY900 with `ex.Message`. | Low | Debuggability: the intent is defensive, but catching `Exception` without surfacing real bugs is the classic recipe for silent regressions — exactly the failure mode #257 was filed to prevent. |
| Diagnostic location at `ChainAnalyzer.cs:2066` and `2110` uses `executionSite.Location` — the outer call site, not the specific navigation-aggregate invocation. For a multi-line tuple Select with several aggregates, the error squiggle points at `.Select(...)` rather than the offending `u.Badnav.Sum(...)`. | Low | User experience: acceptable for a first pass; the simpler `SqlExprParser.Parse` API used in `TryParseNavigationAggregateColumn` does not preserve source spans. Non-blocking. |
| `TryParseNavigationAggregateColumn` (`ProjectionAnalyzer.cs:644-668`) returns `null` when `SqlExprParser.Parse` yields something other than a `SubqueryExpr`. The caller sites at `ProjectionAnalyzer.cs:341`, `617`, `687`, `702` drop back to `return null`, causing the overall projection to fall through to generic "unsupported" paths — no diagnostic is emitted. | Low | Silent fallthrough: rare in practice (method name matches aggregate but parser produces non-subquery IR), but exactly the silent-failure pattern this PR was meant to eliminate. Worth a TODO. |
| `ResolveSubqueryResultType` (`ChainAnalyzer.cs:1875-1888`) falls back to `"object"` for Min/Max when selector can't be resolved. Downstream `TypeClassification.GetReaderMethod("object")` emits `GetValue`, returning `object?` in the projected tuple. Matches existing Sql.* aggregate fallback, so not a regression — but the failure mode is invisible to the user. | Low | Quiet type degradation: the post-bind `TryResolveAggregateTypeFromSql` retry at `ChainAnalyzer.cs:2088` won't re-enter because the rendered subquery SQL has its own column aliases. |

## Security

No concerns. The navigation-aggregate path consumes only compile-time `InvocationExpressionSyntax`; rendered SQL uses existing dialect-formatted identifier quoting via `SqlFormatting.QuoteIdentifier` (unchanged). No runtime user input flows into the new SQL construction path.

## Test Quality

| Finding | Severity | Why It Matters |
|---|---|---|
| `Select_Many_Sum_OnEmptyNavigation_ThrowsAtRead` (`CrossDialectSubqueryTests.cs:1216-1219`) asserts `caught.Message + InnerException.Message` contains "NULL". If the `Microsoft.Data.Sqlite` provider changes its null-read message wording (e.g., to "SqliteDataReader returned null"), the test breaks without a Quarry code change. | Low | Brittle to upstream. The test's intent — document the non-nullable-empty-set caveat — is valuable; matching exception *type* (`QuarryQueryException`) would be more robust. |
| No test exercises `u.Orders.Where(...).Sum(...)` or `u.Orders.Take(5).Count()`. The `IsNavigationAggregateCall` check at `ProjectionAnalyzer.cs:603-611` rejects these (requires bare `MemberAccessExpressionSyntax` receiver), and they silently fall through to generic "unsupported projection". | Low | Scope boundary is correct per plan, but a pinning test would alert a future contributor before they accidentally extend coverage incorrectly. |
| `Generator_WithUnresolvableNavigationAggregateInSelect_ReportsQRY073` (`GeneratorTests.cs:1406`) declares `VariantSchema` but omits `.Variants()` from the context, relying on `EntityRegistry.ByEntityName` having no "Variant" entry. Correct intent, but coupled to current registry semantics — if the registry ever auto-discovers schemas, the test passes for the wrong reason. | Low | The test still detects QRY073 emission; the coupling risk is latent. |
| HasManyThrough + joined-Select path is not covered: there are HasManyThrough tests on single-entity Select (`CrossDialectHasManyThroughTests.Select_HasManyThrough_Max_InTuple`) and HasMany on joined Select (`CrossDialectJoinTests.Select_Joined_Many_Sum_OnLeftTable`), but no test combines the two. | Low | Gap in the matrix; the joined-context branch in `BuildProjection` passes through the same `ResolveSubqueryTargetEntity` code, so a bug would likely surface elsewhere, but full coverage would reduce false confidence. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---|---|---|
| QRY073 is reused, but `docs/articles/releases/release-notes-v0.3.0.md:47, 338, 360`, `docs/articles/analyzer-rules.md:130`, `llm.md:545`, and `src/Quarry.Generator/llm.md` all state QRY073 was retired in v0.3.0 (already released 2026-04-21). The PR reintroduces the ID with an entirely different meaning ("projection subquery unresolved" vs the retired "set-op column mismatch"). Either update those documents in this PR or — simpler — switch the new descriptor to QRY074 (QRY074–079 are all unused per `DiagnosticDescriptors.cs`). | Medium | The diagnostic-ID register is user-visible via `#pragma` suppression. If docs stay out of sync users are misled; if the docs update but the ID is reused, any lingering `#pragma warning disable QRY073` from prior code silently suppresses a new Error-severity diagnostic — the exact silent-failure mode this PR was filed to fix. |
| `ProjectedColumn.SubqueryExpression` (IR node) and `OuterParameterName` (string) are init-only fields stored on a data-plane model, mixing IR and metadata layers. The plan explicitly chose this design and the fields are cleared after binding (`ChainAnalyzer.cs:1843-1844`), preventing downstream leakage. | Low | Positive observation with caveat: acceptable given the clear clearing discipline. |
| `AttachLambdaParameterNames` (`ProjectionAnalyzer.cs:570-580`) allocates a fresh `ProjectionInfo` on every call. Incremental-generator cache churn is a theoretical concern; unmeasured. | Low | Minor: `ProjectionInfo` is a class (not a record), so `with` is not available; the allocation cost is likely negligible. |
| Pre-existing bug at `ProjectionAnalyzer.cs:493` (tuple-fixup reconstructs `ProjectionInfo` without `CustomEntityReaderClass`, `JoinedEntityAlias`, `ProjectionParameters`) is partially masked now because the subsequent `AttachLambdaParameterNames` call propagates `ProjectionParameters`. Not introduced by this PR. | Low | Noting so a future reviewer doesn't flag the masked behavior as a regression. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---|---|---|
| Reusing the retired QRY073 ID is a semantic breaking change for suppression pragmas. The v0.3.0 release notes (`release-notes-v0.3.0.md:47, 338`) told users to remove `#pragma warning disable QRY073` directives; users who ignored that advice will now silently suppress the new Error-severity diagnostic — allowing exactly the silent regression this PR is meant to prevent. | Medium | Severity=Error diagnostic becoming pragma-suppressable-by-accident is the core risk. Mitigation: use QRY074 (all of QRY074–079 are unused). |
| Manifest test outputs gained 9–10 rendered queries per dialect (`quarry-manifest.{sqlite,mysql,postgresql,sqlserver}.md`). Totals in the Metric tables updated accordingly. | Low | Expected and correct. |
| Public surface unchanged — `ProjectedColumn` and `ProjectionInfo` changes are `internal sealed`; new `DiagnosticDescriptors.ProjectionSubqueryUnresolved` is additive. | Low | Positive: fix is entirely inside the generator boundary. |
