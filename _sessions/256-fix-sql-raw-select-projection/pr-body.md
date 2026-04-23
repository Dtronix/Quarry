## Summary
- Closes #256

## Reason for Change
`Sql.Raw<T>(...)` used inside a `Select` tuple/DTO projection silently rendered as an empty string literal in the generated SQL (e.g., `SELECT "OrderId", "" FROM "orders"`). No QRY diagnostic, no build warning, no runtime error — just wrong SQL. The IR-based Where/Having path already handled `RawCallExpr` correctly via `SqlExprRenderer`; the projection path never built one because projection analysis doesn't route through the IR.

## Impact
`Sql.Raw<T>` is now fully supported in Select projections across all four dialects (SQLite, PostgreSQL, MySQL, SqlServer) for:
- Single-entity tuple/DTO/object-initializer projections (`.Select(u => (u.Id, X: Sql.Raw<T>(...)))`).
- Joined projections (`.Select((a, b) => (a.Id, b.Foo, Sql.Raw<T>(...)))`).
- Single-column projections (`.Select(u => Sql.Raw<T>(...))`).
Supported template args: column references (`u.Xxx`), compile-time constants, captured runtime locals/parameters, and IR expressions like `u.Price * 2`.

Invalid `Sql.Raw` usage now fails loudly in two forms:
- **Template errors** (arg/placeholder count mismatch, non-sequential placeholders, non-const template) — emit **QRY029** compile-time diagnostic at the Select call location, matching the Where-path behavior.
- **Unsupported argument kinds** (C# ternary, unknown method invocation, string-typed column concatenation on MySQL/SqlServer, unresolvable `T`) — the walker bails, the projection fails analysis, and the chain degrades to a runtime-build path instead of emitting wrong SQL.

## Plan items implemented as specified
- **Phase 1** — Single-entity `Raw` case in `GetAggregateInfo`; new helpers `BuildSqlRawInfo`, `RenderRawArgToCanonical`, `RenderRawArgNode`, `ResolveColumnRefToPlaceholder`, `FormatLiteralForProjection`, `AddCapturedAsProjectionParameter`, `GetRawTemplateValidationError`, `TryExtractConstString`, `TryExtractSqlRawTypeArg`, `SubstituteTemplatePlaceholders`. Uses `SqlExprParser` to parse each arg; walks the SqlExpr tree and emits canonical `{ColumnName}` / `@__proj{N}` / inline-literal placeholders.
- **Phase 2** — Joined-projection support in `GetJoinedAggregateInfo` with `{alias}.{ColumnName}` resolution via a column-resolver delegate so the walker serves both single-entity and joined paths. Single-column (both single-entity and joined) works automatically through the existing aggregate routing.
- **Phase 3** — Cross-dialect tests in `CrossDialectMiscTests.cs` covering column ref, multiple column refs, captured variable, literal parameter, no placeholders, DTO initializer, single-column, joined tuple, joined multi-arg, joined captured-var, binary-op arg, dialect-aware bool literal.

## Deviations from plan implemented
- **Canonical placeholder for booleans** (`{@BOOLT}` / `{@BOOLF}`) — the plan originally suggested dialect-threading for literal rendering. After integration, we discovered projection analysis runs at a fixed discovery dialect (PostgreSQL) so the emitted SqlExpression must be truly dialect-agnostic. Booleans now emit canonical placeholders that `SqlFormatting.QuoteSqlExpression` resolves per-dialect (TRUE/FALSE on PostgreSQL, 1/0 elsewhere). This is a minimal, additive extension to the placeholder scheme used for identifiers and parameters.

## Gaps in original plan implemented
### Session 1 review (all classified A/B)
1. **Fail-loud fallback** — `AnalyzeProjectedExpression` no longer falls through to the generic type-info fallback when `Sql.Raw` returns `null`; returns `null` explicitly and `AnalyzeInvocation` emits a specific CreateFailed message.
2. **Captured-var CLR typing** — fast-path delegates simple scalar args (captured locals/parameters and compile-time constants) to `ResolveScalarArgSql`, which consults the semantic model for authoritative types.
3. **String-concat guard** — `Add` operator on string-typed operands bails to null rather than emitting `"a + b"` which is invalid on MySQL/SqlServer.
4. **T=`object` guard** — `TryExtractSqlRawTypeArg` returns `null` when `T` is unresolvable, and `BuildSqlRawInfo` fails the projection rather than falling through to aggregate-type heuristics.
5. **Validation refactor** — Replaced the throwaway `RawCallExpr` shell used for placeholder validation with a dedicated helper with documented scope.

### Session 2 review (8 A / 4 B classifications)
6. **Walker fail-loud on `SqlRawExpr`** — the walker used to emit the parser's fallthrough text verbatim, which leaked C# source (e.g., `/* unsupported: C# ternary expression */` or `u.Foo(x)`) into SQL for any unhandled syntax. Walker now returns null and the projection fails loudly.
7. **String-column concat guard extended** — the Add-operator string-concat bail now also catches string-typed `ColumnRefExpr` operands via a new `isStringColumn` delegate, covering cases like `Sql.Raw<string>("{0}", u.FirstName + u.LastName)` on MySQL/SqlServer.
8. **Captured-var metadata propagation** — `AddCapturedAsProjectionParameter` now sets `IsStaticCapture` and `ExpressionPath`, matching the canonical `SqlExprClauseTranslator.ExtractParametersCore` pattern so deep-path captures via the walker land correctly in the parameter binder.
9. **Bool-literal detection tightened** — `FormatLiteralForProjection` no longer carries dead `"true"`/`"1"` branches that the parser never emits; the operator table throws `ArgumentOutOfRangeException` on unknown operators rather than emitting the MySQL `?` placeholder.
10. **QRY029 for projection-path template errors** — `GetRawTemplateValidationError` captures the `RawCallExpr.Validate()` message; a thread-static `_pendingSqlRawErrors` accumulator is drained at `ProjectionAnalyzer` entry points and attached to `ProjectionInfo.SqlRawValidationErrors`; `PipelineOrchestrator` emits QRY029 per entry scoped to the Select call location. New `ProjectionFailureReason.SqlRawValidationError`. Three new UsageSiteDiscoveryTests lock in the behavior (too-many-args, too-few-args, non-sequential placeholders).
11. **Test coverage expansion** — captured-var test now asserts `Name`/`Value`/`IsCollection`/`IsEnum` and runs the query end-to-end to verify the parameter binder round-trips the captured value. New `Select_SqlRaw_Joined_MultipleArgs` and `Select_SqlRaw_Joined_WithCapturedVariable` tests exercise joined-context multi-arg and captured-var paths.
12. **Operator table consolidation** — `SqlExprRenderer.GetSqlOperator` is now `internal` and shared with the Sql.Raw projection walker, removing one source of drift. The renderer's fallback was also tightened to throw rather than emit `"?"`. Remaining IN/LIKE/IS NULL/function-call/unary duplication is documented as a tracked follow-up — full consolidation would require the renderer to grow a canonical-projection output mode.

## Migration Steps
None. This is a pure bug fix and additive feature — no API changes, no schema changes, no consumer-facing breaks.

## Performance Considerations
Negligible. The projection analyzer already walks `Select` lambdas via `SqlExprParser`; adding `Sql.Raw` handling is a constant-factor addition per raw call site. `SqlFormatting.QuoteSqlExpression` gains two new placeholder checks (`@BOOLT`, `@BOOLF`) which are O(1) string comparisons on the single-pass placeholder scan. The thread-static QRY029 accumulator is allocated lazily and is only touched for projections containing malformed `Sql.Raw` templates.

## Security Considerations
No new injection surface. The template must still be a compile-time string constant (via `TryExtractConstString`) — matching the existing constraint for `Sql.Raw` in Where-path. User-supplied runtime values continue to flow through normal parameter binding (`@__proj{N}` → `{@globalIdx}` → dialect-specific parameter placeholder). Captured variables bind with the CLR type inferred from the semantic model, not `object`, which preserves provider-enforced parameter typing (e.g., SqlServer `INT` binding).

## Breaking Changes
### Consumer-facing
- None. `Sql.Raw<T>` in Select projections previously emitted silently-wrong SQL and now either produces correct SQL or a loud failure (QRY029 for template errors, runtime-build degradation for unsupported arg kinds). Users who had broken `Sql.Raw<T>` in Select before should now see either their intended SQL or an actionable diagnostic.

### Internal
- New placeholders `{@BOOLT}` and `{@BOOLF}` in canonical SqlExpression format, resolved by `SqlFormatting.QuoteSqlExpression`. Additive only; no change to existing identifier/parameter placeholder handling.
- New `IReadOnlyList<string>? SqlRawValidationErrors` init-only property on `ProjectionInfo`. New `ProjectionFailureReason.SqlRawValidationError` enum value. Both consumed by `PipelineOrchestrator.CollectTranslatedDiagnostics`.
- `SqlExprRenderer.GetSqlOperator` visibility changed from `private` to `internal` so the Sql.Raw projection walker can share the operator table; fallback now throws on unknown operators rather than emitting `"?"`.
- `ProjectedColumn.IsAggregateFunction` is set `true` for Sql.Raw projection columns. The four downstream consumers (`SqlAssembler.cs`, `ChainAnalyzer.cs`, `QuarryGenerator.cs`) all treat the flag as "render SqlExpression verbatim; skip auto-table-qualifier; skip ColumnName enrichment", matching the semantic already used by window functions.
