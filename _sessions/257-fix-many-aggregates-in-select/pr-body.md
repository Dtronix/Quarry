## Summary
- Closes #257

## Reason for Change
`Many<T>.Sum/Min/Max/Avg/Average/Count` worked in `Where` but silently emitted broken
SQL and uncompilable C# (`(?)r.GetValue(N)`, `IQueryBuilder<T, (object, object, ...)>`,
empty SELECT columns) when used in `Select` projection. Cause: the generator had two
disjoint pipelines for `SubqueryExpr` — `Where` used the parser/binder/renderer chain,
`Select` used a plain-text path that only recognized static `Sql.*` aggregates.

## Impact
- All five navigation aggregates now work in tuple, DTO, and joined-context Select
  projections across SQLite / PostgreSQL / MySQL / SqlServer.
- HasManyThrough projections also covered, including in joined-select contexts.
- Failure mode for unresolvable navigations is no longer silent — the generator
  emits diagnostic **QRY074** at the offending aggregate invocation (not the
  enclosing `.Select(...)` call). Unexpected binder exceptions route to **QRY900**
  with the underlying type and message preserved.

## Plan items implemented as specified
- Phase 1: `ProjectedColumn.SubqueryExpression` + `OuterParameterName` fields and
  `ProjectionInfo.LambdaParameterNames` for downstream binding.
- Phase 2: `ProjectionAnalyzer` detects
  `<param>.<NavProp>.{Sum|Min|Max|Avg|Average|Count}(...)` via `SqlExprParser`,
  stores the unbound `SubqueryExpr` on a placeholder column.
- Phase 3: `ChainAnalyzer.BuildProjection` binds (`SqlExprBinder.Bind`) and renders
  (`SqlExprRenderer.Render`) the `SubqueryExpr`, resolves the aggregate's CLR type
  from the selector column, falls through to a diagnostic + placeholder column on
  bind failure.
- Phase 4: Cross-dialect `Select_Many_{Count,Sum,Min,Max,Average}_InTuple` tests,
  the issue's exact multi-aggregate repro, DTO initializer, joined-context Count + Sum.
- Phase 5: Descriptor sanity test + end-to-end emission test (covers the deferred-
  descriptor wiring).

## Deviations from plan implemented
None — all design decisions were honored. The plan's "open risks" section noted
two follow-up concerns (renderer `paramBase` for captured-value selectors, chained
`.Where(...).Sum(...)` shape); both confirmed out of scope for this issue.

## Gaps in original plan implemented
Caught across two review passes:

**Pass 1 (initial review)**
- **Deferred-descriptor registration.** The diagnostic was emitted from
  `BuildProjection` but never registered in `QuarryGenerator.s_deferredDescriptors`,
  so the deferred-reporting loops silently dropped it. Registration added; end-to-end
  emission test added.
- **Equality bookkeeping.** `ProjectedColumn.Equals` now compares `SubqueryExpression`
  structurally instead of by reference; `ProjectionInfo.Equals` / `GetHashCode` now
  include `LambdaParameterNames`. Protects Roslyn incremental-generator caching.
- **Coverage breadth.** Added `Select_Joined_Many_Sum_OnLeftTable` (joined-context
  selector type resolution + outer-correlation alias), `Select_HasManyThrough_Max_InTuple`
  (ThroughNavigation branch in `ResolveSubqueryTargetEntity`), and
  `Select_Many_Sum_OnEmptyNavigation_ThrowsAtRead` (locks in the NULL-on-empty-set
  contract).

**Pass 2 (re-review)**
- **QRY073 ID collision with v0.3.0 retirement.** `QRY073` was introduced and retired
  in v0.3.0 (released 2026-04-21); release notes explicitly instructed users to
  remove `#pragma warning disable QRY073` pragmas. Reusing that ID would silently
  suppress the new Error-severity diagnostic for anyone who left the pragmas in.
  Renamed to **QRY074** (next unused slot). Existing v0.3.0 retirement docs are
  intact and now note that QRY073 is intentionally skipped so lingering pragmas
  stay inert.
- **Bare `catch` in `ResolveProjectionSubqueryColumn`.** Narrowed: unexpected binder
  exceptions now emit QRY900 with `ex.GetType().Name + ex.Message` preserved,
  instead of masquerading as a nav-resolution failure.
- **Diagnostic location precision.** Added `ProjectedColumn.SubqueryInvocationLocation`
  (captured from the `InvocationExpressionSyntax`); `BuildProjection` prefers it over
  the enclosing chain site so the squiggle lands on the specific `.Sum(...)`/`.Max(...)`
  call.
- **Parser-fallthrough silent drop.** `TryParseNavigationAggregateColumn` no longer
  returns null when the parser yields a non-`SubqueryExpr` for an aggregate-shaped
  invocation. A synthesized unresolved `SubqueryExpr` carries enough info through so
  `BuildProjection` still emits QRY074.
- **Min/Max unresolved-selector degradation.** `ResolveSubqueryResultType` threads
  `selectorTypeIsKnown` out; Min/Max with an unresolved selector emits QRY074 rather
  than silently degrading to `object` / `GetValue`.
- **Brittle message assertion.** `Select_Many_Sum_OnEmptyNavigation_ThrowsAtRead` now
  asserts on exception *type* (`QuarryQueryException` / `InvalidOperationException` /
  `InvalidCastException`) instead of matching a "NULL" substring — no longer coupled
  to driver wording.
- **Matrix gap.** Added `Select_Joined_HasManyThrough_Max_OnLeftTable` covering the
  joined + through-nav + Max combination.

## Migration Steps
None. Existing queries are untouched — single-entity Select without navigation
aggregates short-circuits before any new code runs.

## Performance Considerations
The new branch in `BuildProjection` is gated by `hasSubqueryColumn` (one quick scan
of `projInfo.Columns`). Non-subquery projections pay no overhead. Per-subquery cost
is one `SqlExprBinder.Bind` + one `SqlExprRenderer.Render`, comparable to existing
`Where`-clause subquery binding.

## Security Considerations
None. Subquery SQL is built entirely from parsed `SqlExpr` IR (column / property
names from the source code's lambda) and dialect-specific `QuoteIdentifier` calls —
no string interpolation of user values, no `Sql.Raw`-style escape hatches.

## Breaking Changes
- Consumer-facing: **none**. Code that previously failed to compile (CS0246 cascade)
  now compiles and produces correct SQL. New diagnostic ID **QRY074** is additive.
  Existing `#pragma warning disable QRY073` directives from the v0.3.0 migration
  remain inert — the ID stays retired.
- Internal: `ProjectedColumn` and `ProjectionInfo` constructors gained optional
  parameters at the end (additive). All call sites are within the generator.
