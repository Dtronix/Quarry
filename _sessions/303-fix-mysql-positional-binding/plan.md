# Plan: MySQL positional `?` binding alignment via generation-time marker-scan (#303)

## Problem statement

MySQL (MySqlConnector) binds the Nth `?` in SQL text to the Nth `cmd.Parameters.Add()` call. The generator assigns parameter `GlobalIndex` in chain-call order (`ChainAnalyzer`) and `CarrierEmitter.EmitCarrierCommandBinding` binds in that order, while SQL renderers emit placeholders in SQL-text order. For flat renders the two orders coincide; `SqlAssembler.RenderSelectSqlWithDistinctOrderByWrap` (SqlAssembler.cs:1531) hoists parameterized ORDER BY expressions textually before WHERE, so on MySQL values bind to the wrong slots. SQLite/SqlServer bind by `ParameterName`, PostgreSQL by explicit `$N` — only MySQL is affected.

Fix (per design decisions in workflow.md): keep bare `?` (no runtime cost), derive the SQL-text bind order at **generation time** by rendering MySQL placeholders as indexed marker tokens, scanning the assembled variants to extract the order, stripping markers back to `?`, and having `CarrierEmitter` emit its bind blocks in that order. Identity order (the overwhelmingly common case) produces byte-identical generated code.

## Key concepts

- **GlobalIndex** — per-chain parameter slot assigned in chain-call order by `ChainAnalyzer` (`paramGlobalIndex++` at ChainAnalyzer.cs:711/977/1430). Carrier `P{N}` fields and today's bind loop both follow it. Unchanged by this work.
- **Marker token** — `{__Q{globalIndex}__}` emitted *instead of* `?` by generator-side MySQL placeholder emission. Exists only inside the generator while variants are assembled; never reaches generated source, manifests, or runtime. Syntax is disjoint from the existing `{__COL_PN__}` / `{__PATCH_SET__}` tokens and from `QuoteSqlExpression`'s `{@N}` canonical placeholders (which are *resolved into* markers).
- **Bind order (ranking)** — a single per-chain permutation of `0..totalSlots-1` (chain params + limit/offset slots) in SQL-text order. One ranking serves all mask variants: every variant's text order is the ranking filtered to that variant's active params, because renderers traverse clauses in fixed structural order and masking only removes terms. Cross-variant consistency is asserted (see Phase 1 algorithm).
- **Runtime SQL paths (untouched)** — collection `__col{N}Parts` `"?"` strings (TerminalEmitHelpers.cs:1005), Patch SET runtime `__sb.Append('?')`, `BatchInsertSqlBuilder`, `MigrationRunner`. These build SQL at runtime where bind order already matches construction order.

## Phase 1 — Marker emission + strip/extract pass (no behavior change)

**Goal:** MySQL variants are rendered with markers, the post-pass restores `?` and records the ranking on `AssembledPlan`. Generated output and all SQL text are byte-identical to today; the ranking is computed but unconsumed.

1. New generator-internal helper `Quarry.Generator/IR/MySqlBindMarkers.cs`:
   ```csharp
   internal static class MySqlBindMarkers
   {
       internal static string Format(int globalIndex);            // "{__Q{n}__}"
       internal static void AppendTo(StringBuilder sb, int globalIndex);
       // Single pass: records marker ranges during the scan and rebuilds the SQL in one
       // StringBuilder pass from the inter-range segments (no string.Replace). Each
       // {__Q{n}__} becomes "?" — or "{__COL_P{n}__}" when isCollectionSlot(n) — and n is
       // appended to textOrder at its text position. Returns the original string instance
       // when no markers are present.
       internal static string RewriteAndExtract(string sql, Func<int, bool> isCollectionSlot, List<int> textOrder);
   }
   ```
   (Range-based single-pass rewrite per design refinement 2026-06-10 — no string.Replace; collection tokenization folds into the same pass.)
2. Marker emission sites (all generator-side; MySQL only, `genericParams: false` paths only):
   - `SqlExprRenderer.AppendParameterPlaceholder` (SqlExprRenderer.cs:275-278): MySQL branch appends `MySqlBindMarkers` token using `idx = paramBase + param.LocalIndex` (the global slot — same expression the PG branch uses).
   - `SqlAssembler.cs:833` (INSERT VALUES): MySQL emits marker for `plan.InsertColumns[i].ParameterIndex`.
   - `SqlAssembler.cs:1090` pagination: `SqlFormatting.FormatMixedPagination` gains an optional formatter so the generator can inject markers without affecting runtime/tool callers:
     `internal static string FormatMixedPagination(SqlDialect dialect, int? literalLimit, int? limitParamIndex, int? literalOffset, int? offsetParamIndex, Func<int, string>? parameterFormatter = null)`
   - `SqlFormatting.QuoteSqlExpression` `{@N}` resolution (SqlFormatting.cs:335): same optional-formatter approach:
     `public static string? QuoteSqlExpression(string? sqlExpression, SqlDialect dialect, int paramOffset = 0, Func<int, string>? parameterFormatter = null)` — generator callers (`SqlAssembler.cs:1391`, `ReaderCodeGenerator.cs:38/342`) pass the marker formatter when dialect is MySQL. This covers parameterized window-function projection args.
3. Rewrite/extract pass in `QuarryGenerator` at the existing post-process point (QuarryGenerator.cs:659, where `TokenizeCollectionParameters` runs):
   - For MySQL, `RewriteAndExtract` runs for **all** assembled plans (markers must be stripped even for non-carrier-eligible chains — manifests/SQL-output read variant SQL), with collection-token emission gated on the same carrier-eligibility predicate that gates tokenization today. The `TokenizeCollectionParameters` MySQL branch and `ReplaceNthOccurrence(sql, '?', ...)` (QuarryGenerator.cs:961-968, latent literal-`?` miscount hazard) are **deleted** — folded into the single pass. Non-MySQL dialects keep the existing tokenize path.
   - The same pass yields each variant's text order (collection slots contribute at their text position).
   - Per-variant validation: extracted slot set must equal the variant's expected active param set (+ limit/offset slots when present). Mismatch ⇒ generator diagnostic (reuse QRY900 path) — this check alone would have caught #303 at compile time.
   - Cross-variant merge into one chain ranking: insertion-merge each variant's sequence into a master order; if two slots co-occur in any two variants with contradictory relative order ⇒ assert/diagnostic (design says this cannot happen; never-co-occurring slots — mutually exclusive branch groups — get GlobalIndex tiebreak, their relative bind order is immaterial).
   - Store on `AssembledPlan` as `EquatableArray<int> MySqlBindOrder` (empty ⇒ identity; only stored when ≠ identity, keeping incremental-cache churn nil for unaffected chains). Apply via the same pending-updates pattern `TokenizeCollectionParameters` already uses for `SqlVariants`.
4. Patch chains: `{__PATCH_SET__}` contributes no compile-time slots; assert ranking is identity for Patch chains (UPDATE has no hoisting; SET-first runtime binding stays as is).

**Tests (Phase 1):**
- New `Quarry.Tests/IR/MySqlBindMarkersTests.cs` — `StripAndExtract` unit tests: plain, interleaved with `{__COL_PN__}`, `?` inside string literals untouched, marker-free SQL passthrough.
- `DialectTests` — `FormatMixedPagination`/`QuoteSqlExpression` formatter-override cases.
- SQL-output regression: full existing suite must pass unchanged (proves markers never leak: manifests, `CrossDialect*` SQL assertions are exact-match).
- A generation-level test asserting the wrap chain's `AssembledPlan.MySqlBindOrder` is the hoisted order (e.g. `[1, 0]` for the reproducer shape) and a flat chain's is empty/identity.

## Phase 2 — CarrierEmitter consumes the ranking (fix goes live)

**Goal:** the reproducer passes; all other generated code byte-identical.

1. `EmitCarrierCommandBinding` (CarrierEmitter.cs:688): iterate `for k in 0..paramCount-1 { i = order[k]; ... }` where `order` is `chain.MySqlBindOrder` when non-empty else identity. The existing conditional-block open/close logic already handles non-contiguous same-bit runs (it keys on consecutive `BitIndex` changes); permuted order just yields more, smaller `if` blocks.
2. Pagination: assert limit/offset slots rank last for MySQL (LIMIT/OFFSET is textually last in every MySQL statement Quarry emits); keep the existing bind-after-loop structure.
3. `__bindShift` semantics: shift accumulation order now follows text order, which is exactly what makes the *collection* `?` positions line up too; MySQL `ParameterName`s (`@pN`, driver-ignored) remain unique regardless of order.
4. Diagnostics path (`ToDiagnostics` parameter lists) stays in GlobalIndex order — cosmetic; recorded under Risks.

**Tests (Phase 2):**
- `CarrierGenerationTests`: for a wrap-shaped MySQL chain, generated interceptor source adds the ORDER-BY-hoisted parameter's bind block before the WHERE parameter's (string-order assertion on emitted code); flat chain emission byte-identical to a pre-change snapshot.
- Integration: `DistinctOrderByWrap_ParameterizedWhereAndOrderBy_OnMySQL_PreservesBindingAlignment` (the reproducer) goes green.
- Full suite green.

## Phase 3 — Focused audit integration tests

Per design decision: 2–3 MySQL end-to-end tests on the riskiest remaining divergence surfaces, in `MySqlIntegrationTests`:

1. **Window-function projection param** — `Select(o => (o.Total, Sql.Sum(o.Total, over => over.PartitionBy(o.Status).OrderBy(o.OrderId))))`-style chain with a captured non-column OVER arg and a parameterized `Where`; projection params render before WHERE. Assert row values, non-overlapping value ranges.
2. **Conditional mask × wrap** — wrap-path chain with a conditional `Where` (if-gated) plus parameterized OrderBy; execute both mask variants in one test, assert each variant's results (per-mask coverage of the shared ranking).
3. **Collection expansion × wrap** — `Where(o => ids.Contains(o.OrderId))` + parameterized `OrderBy` + `Distinct`; collection `?`s and hoisted OrderBy `?` interleave.

Expected: all pass already (Phase 2 fixed them); these pin the behavior. Manifest `quarry-manifest.mysql.md` gains the new chains (regenerated, committed).

## Phase 4 — Docs and cleanup

- `src/Quarry.Generator/llm.md`: short subsection under SQL assembly describing marker-scan bind-order derivation and the `MySqlBindOrder` contract (generator internals doc per repo convention).
- Update the stale invariant comment at SqlAssembler.cs:1566-1574 to reference the marker mechanism instead of implying bind order follows GlobalIndex.
- Confirm no `Ignore`/TODO remnants; final full-suite run.

## Dependencies

Phase 2 depends on 1; Phase 3 on 2; Phase 4 independent of 3 but last for doc accuracy. Each phase is independently committable with green tests (reproducer failure is the recorded pre-existing baseline item until Phase 2).

## API / signature changes (headers only)

- `+ Quarry.Generator/IR/MySqlBindMarkers.cs` (internal static class; see Phase 1)
- `~ SqlFormatting.FormatMixedPagination(..., Func<int, string>? parameterFormatter = null)`
- `~ SqlFormatting.QuoteSqlExpression(string?, SqlDialect, int paramOffset = 0, Func<int, string>? parameterFormatter = null)`
- `~ AssembledPlan`: `+ EquatableArray<int> MySqlBindOrder` (empty = identity)
- `~ SqlExprRenderer.AppendParameterPlaceholder` — MySQL branch body only
- `~ QuarryGenerator.TokenizeCollectionParameters` — MySQL branch replaced; `- ReplaceNthOccurrence` (if no other callers)

## Risks & rollback

- **Marker leakage** into generated SQL/manifests — mitigated by exact-match SQL-output tests + per-variant slot-set validation (loud generator diagnostic, not silent corruption).
- **Cross-variant order contradiction** (design says impossible) — surfaces as generator diagnostic rather than wrong SQL; fallback would be per-mask bind switches (not planned).
- **ToDiagnostics / parameter logging** lists params in GlobalIndex order while MySQL text order differs — cosmetic only (names `@pN` still map to carrier fields); documented, not changed.
- **Incremental caching**: `MySqlBindOrder` participates in `AssembledPlan` equality (EquatableArray) — empty-when-identity keeps unaffected chains' cache keys stable.
- Rollback: revert is clean per phase; Phase 1 alone is inert (computed, unconsumed).

## Implementation deviations (recorded 2026-06-10, post-review)

- **INSERT VALUES markers descoped** (Phase 1.2 listed SqlAssembler.cs:833): INSERT binds in column order by construction (VALUES order == bind order, no other clauses) and its parameters live in `InsertInfo`, not `ChainParameters` — markers would only add validation noise. INSERT renders bare `?` via `FormatParameter`.
- **ReaderCodeGenerator formatter wiring descoped** (Phase 1.2 listed ReaderCodeGenerator.cs:38/342): those `QuoteSqlExpression` calls produce runtime/dynamic SQL strings (reader column arrays) where markers would leak into shipped code; they keep default placeholder formatting.
- **`MySqlBindOrder` shape**: landed as a nullable settable `IReadOnlyList<int>` excluded from `AssembledPlan.Equals` instead of an equality-participating `EquatableArray<int>` — the ranking is derived deterministically from `SqlVariants`, which Equals already compares, so including it would be redundant; null (not empty) means identity.
- **Comparison-render isolation**: implemented as marker-free `with`-copies of the config (`cmpConfig`) at the wrap-detection sites plus the `forComparison` gate in `AppendProjectionColumnSql`, rather than relying on `forComparison` alone.
- **Validation failure handling** (post-review remediation of findings #1/#6): extraction/validation failure now emits warning **QRY048** at the chain's terminal (with a reason) in addition to the identity fallback; the bare `Debug.Assert` was removed.
- **Post-process location** (post-review remediation of findings #8/#17): `RewriteMySqlBindMarkers` + `TokenizeCollectionParameters` moved from the interceptor output action into `PipelineOrchestrator.AnalyzeAndGroupTranslated` (before file grouping), removing the dependence on cross-output execution order and restoring incremental-cache equality for post-processed plans.
- **Pagination marker slots** (post-review remediation of finding #7): pagination markers carry `PaginationPlan.LimitParamIndex`/`OffsetParamIndex` (true global slots) instead of the clause-level running index, which lags when projection params exist; the extraction validates against the same plan slots. Non-MySQL dialects keep the running index — their pre-existing numbering on projection-param + pagination chains is a separate issue.
- **No ParameterName changes for reordered chains** (post-review, finding #9): on MySQL every name path already emits the constant `"?"`; the driver binds purely positionally, so permutation requires no naming work.

## Out of scope

- Npgsql named-mode migration; any dialect other than MySQL.
- Reordering diagnostics/logging parameter lists.
- The `CarrierEmitter.cs` `__colShift`/CS0219 change and `llm.md` restructure sitting uncommitted in master's working tree (user's separate work).
