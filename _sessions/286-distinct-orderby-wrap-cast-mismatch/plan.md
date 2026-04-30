# Plan: 286-distinct-orderby-wrap-cast-mismatch

## Background

`SqlAssembler.NeedsDistinctOrderByWrap` and `MayNeedDistinctOrderByWrap` decide whether `Distinct() + ORDER BY` needs a derived-table wrap by comparing two SQL fragments via string equality:

1. **Projection-column reference** rendered through `RenderProjectionColumnRef` → `AppendProjectionColumnSql`. On Ss, this method wraps window-function int projections with `CAST(... AS INT)` (per #274/#283).
2. **ORDER BY term expression** rendered through `SqlExprRenderer.Render`. This path does NOT apply the CAST — the cast metadata lives on `ProjectedColumn`, not `SqlExpr`.

When a chain selects a window-function int (e.g., `RowNum: Sql.RowNumber(...)`) AND orders by the same window expression, the rendered strings differ on Ss only: the projection side has `CAST(ROW_NUMBER() OVER (...) AS INT)` while the ORDER BY side has `ROW_NUMBER() OVER (...)`. The lookup misses, and the wrap fires unnecessarily. Output is functionally correct (the inner SELECT still emits the CAST via `AppendProjectionColumnSql` directly), just less efficient — an extra `_o0` column is added, the same window function is evaluated twice inside DISTINCT, and the outer ORDER BY references the alias instead of the projection.

The wrap-detection comparison code at `SqlAssembler.cs:1559-1561` (in `RenderSelectSqlWithDistinctOrderByWrap`) ALSO uses `RenderProjectionColumnRef`, so the wrap-renderer's "is this ORDER BY term in the projection?" lookup hits the same Ss-only false negative. The fix below addresses both call sites because they share the helper.

## Fix shape (decided)

Add a `forComparison: bool = false` parameter to `AppendProjectionColumnSql`. When `true`, skip the `CAST(... AS INT)` wrap in the aggregate-function branch. `RenderProjectionColumnRef` passes `forComparison: true`. Direct callers (the inner-projection emission inside the wrap renderer) leave it at the default `false` and continue to emit the CAST.

This produces an asymmetry by design: `RenderProjectionColumnRef` (comparison-only) renders un-wrapped output that matches `SqlExprRenderer.Render`'s ORDER BY rendering, while inner-emission via `AppendProjectionColumnSql` keeps the CAST wrap (because when the wrap genuinely fires, the inner SELECT must still apply the cast for the production reader). The doc on `AppendProjectionColumnSql` is updated to record this.

## Scope

- One file changed in production code: `src/Quarry.Generator/IR/SqlAssembler.cs`.
- Tests added to: `src/Quarry.Tests/SqlOutput/CrossDialectDistinctOrderByTests.cs`.
- No `SqlExpr` / `SqlExprRenderer` / `ProjectionAnalyzer` changes.
- No CTE, batch, or pagination changes — those code paths already feed through the same helper.

## Phases

### Phase 1 — Fix the comparison-side render

**Change `AppendProjectionColumnSql`:**

Add a `bool forComparison = false` parameter. Inside the aggregate-function branch, gate the cast wrap on `!forComparison`:

```csharp
if (col.IsAggregateFunction && !string.IsNullOrEmpty(col.SqlExpression))
{
    var rendered = SqlFormatting.QuoteSqlExpression(col.SqlExpression!, dialect, paramOffset);
    if (!forComparison && col.RequiresSqlServerIntCast && dialect == SqlDialect.SqlServer)
        rendered = $"CAST({rendered} AS INT)";
    sb.Append(rendered);
    return;
}
```

Update the XML doc on `AppendProjectionColumnSql` to mention `forComparison`: when `true`, the emit-only `CAST(... AS INT)` wrap (per #274) is suppressed so the rendered string matches `SqlExprRenderer.Render`'s un-wrapped ORDER BY form for use in `Distinct()` wrap-detection comparisons. The non-aggregate branch (`"alias"."col"`) is already symmetric with ORDER BY render and needs no change.

**Change `RenderProjectionColumnRef`:**

Pass `forComparison: true` to `AppendProjectionColumnSql`. Update its XML doc (already mentions wrap detection — extend it to note that the cast wrap is suppressed because ORDER BY render does not apply it).

**Direct callers of `AppendProjectionColumnSql` are unchanged:**

The inner-projection emission at `SqlAssembler.cs:1609` continues to call with the default `forComparison: false` — when the wrap genuinely fires, the inner SELECT still emits the CAST.

**No behavior change on PG/My/Lite:** the cast branch is gated on `dialect == SqlServer`, so non-Ss dialects render identically to today regardless of the new flag.

**No behavior change on Ss for non-window-int projections:** the cast branch is also gated on `col.RequiresSqlServerIntCast`, which only the window-function int projections set.

**Tests for this phase:** none yet — green tests come after Phase 2's coverage is added. Existing `CrossDialectDistinctOrderByTests` and `SqlServerWindowIntCastTests` must still pass.

**Commit:** `Fix: DISTINCT wrap detection compares CAST-wrapped projection ref against unwrapped ORDER BY on Ss (#286)`

### Phase 2 — Bundle one-line QRY019 message-format fix

**Discovery during planning:** The original test plan required reproducing the bug through a chain like `.OrderBy(o => Sql.RowNumber(...))`. A probe showed this construct currently triggers `QRY019: ClauseNotTranslatable` (window functions are recognized only in `ProjectionAnalyzer`, not in `SqlExprBinder` for general expression contexts) and falls back to runtime LINQ — the SQL emitted has no `ORDER BY` clause at all. Therefore the cross-path string mismatch is **unreachable through the public chain API today**. The Phase 1 fix is preventive: it preserves wrap-detection symmetry for any future emit-only wrapper added to `AppendProjectionColumnSql`, or if `SqlExprBinder` ever gains window-function support.

**No test phase.** The fix is preventive; existing tests already cover all reachable paths and would fail if Phase 1 broke emit-side behavior:
- `SqlServerWindowIntCastTests` (production CAST-emit path).
- `CrossDialectDistinctOrderByTests` (DISTINCT wrap path).
- `CrossDialectWindowFunctionTests` (window-function emission across dialects).

A white-box unit test of `RenderProjectionColumnRef` was considered and rejected — it would pin a private implementation detail without representing a real reachable bug. The doc-comment update on `AppendProjectionColumnSql.forComparison` (in Phase 1) is the durable guard: future maintainers adding emit-only wrappers to that method are explicitly told to gate them on `!forComparison`.

**Bundled glitch fix:** the QRY019 diagnostic surfaced during the probe with doubled phrasing — `"OrderBy clause contains an expression that cannot be translated to SQL clause could not be translated to SQL at compile time."` This is because `CallSiteTranslator` already substitutes a verbose ErrorMessage like `"OrderBy clause contains an expression that cannot be translated to SQL"` into `messageFormat: "{0} clause could not be translated to SQL at compile time. The original runtime method will be used instead."`, producing the duplication.

**Edit:** `src/Quarry.Generator/DiagnosticDescriptors.cs` — change `messageFormat` to `"{0}. The original runtime method will be used instead."`. The `{0}` substitution from `CallSiteTranslator.ErrorMessage` already contains the meaningful clause-kind context. Result: `"OrderBy clause contains an expression that cannot be translated to SQL. The original runtime method will be used instead."` (no duplication).

**Tests for this phase:** none — the change is purely cosmetic. No tests assert on QRY019 message text in the existing suite (verified via grep). If any hidden test does fail, that would be a finding to update.

**Commit:** `Fix: QRY019 diagnostic message duplicated 'translated to SQL' phrase (#286)`

### Phase 3 — Verify no regressions across the existing suite

Run the full test suite (`dotnet test --nologo`). All 3423 baseline tests must remain green. New tests from Phase 2 must pass.

If any existing test moves (e.g., one that previously expected the unnecessary wrap on Ss for a window-function projection), examine it carefully — it should be updated to expect the new no-wrap shape. Such a test should fail BEFORE Phase 1 lands (which it doesn't here — see Diagnostics in the issue: "No automated test currently covers `Distinct() + window-function projection + ORDER BY (window function)` on Ss"), so I expect zero existing-test movement. If any existing test does move, that's a finding to flag in the PR description.

Phase 3 is verification-only — no commit unless an existing test legitimately needs an update, in which case it's bundled with a brief note.

## Dependencies between phases

- Phase 2 depends on Phase 1 (Phase 2 tests will fail on master without Phase 1's fix).
- Phase 3 depends on Phases 1 & 2 (full-suite verification after both land).

## Risks / non-risks

- **Non-risk: emit path.** The CAST wrap on the SELECT side is preserved bit-for-bit because direct `AppendProjectionColumnSql` callers don't pass `forComparison`. `SqlServerWindowIntCastTests` (production-path regression for #274/#283) will continue to pass without modification.
- **Non-risk: non-Ss dialects.** The cast branch is dialect-gated; non-Ss codepaths are unchanged.
- **Risk: future emit-only wrappers.** If a future change adds another `[dialect]-only emit wrapper` to `AppendProjectionColumnSql`, it must also be gated on `!forComparison` or the comparison-side will drift again. Doc-comment update in Phase 1 should call this out so the next maintainer doesn't miss it.
- **Risk: hidden QRY019 message asserts.** Phase 2 changes a diagnostic format string. If any test pins the exact pre-fix wording, it will fail. Grep showed no such tests in the current suite, but the full-suite run in Phase 3 will surface anything missed.
