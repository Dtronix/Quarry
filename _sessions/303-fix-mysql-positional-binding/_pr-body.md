## Summary
- Closes #303

MySQL (MySqlConnector) binds the Nth bare `?` placeholder to the Nth `cmd.Parameters.Add()`, but the generator bound parameters in chain-call (`GlobalIndex`) order while some renderers emit placeholders in a different SQL-text order — the DISTINCT + ORDER BY derived-table wrap hoists ORDER BY expressions textually before WHERE, silently swapping bound values (the reproducer returned 0 rows instead of 2). SQLite/SqlServer (`@pN` by name) and PostgreSQL (`$N` by index) carry slot identity in the placeholder and were unaffected.

The fix derives each chain's SQL-text bind order at **generation time**: MySQL variant rendering emits `{__Q{globalIndex}__}` markers in place of `?` (opt-in via `SqlDialectConfig.EmitMySqlBindMarkers`, set only by `SqlAssembler.Assemble`); a single-pass post-process in `PipelineOrchestrator` rewrites markers back to `?` (folding in collection tokenization), validates each variant's slot set against its mask's expected active parameters, merges per-variant orders into one per-chain ranking (`AssembledPlan.MySqlBindOrder`, null = identity), and `CarrierEmitter` emits its bind blocks in that ranking. Zero runtime cost: same SQL string constants, same straight-line bind code — just emitted in text order. Identity-order chains (the overwhelming majority) emit byte-identical code to before.

## Reason for Change

Issue #303: confirmed end-to-end reproducer (`.Where(o => o.Total > threshold).OrderBy(o => o.Total + bias).Distinct().Select(o => o.Total)` on MySQL 8.4 via Testcontainers) bound `bias` to the WHERE `?` and `threshold` to the ORDER BY `?`. The issue's audit list flagged additional suspect surfaces (window-function args, conditional masks, collection expansion, set ops, CTE rebasing); the marker mechanism fixes the entire class — order is read from the rendered text itself, so no renderer carries an ordering obligation, current or future.

## Impact

- **MySQL chains whose SQL-text order diverges from chain order now bind correctly** (wrap path, parameterized window-function args, pagination combinations). All other chains and all other dialects emit byte-identical code (proven by the full suite + exact-match manifests through the inert phase-1 commit).
- New warning **QRY048** fires when bind-order extraction/validation fails; the chain then falls back to chain-order binding (the pre-#303 behavior) instead of failing the build. Zero occurrences across the 600-chain test corpus.
- Cross-dialect fix (scope widened during review at user direction): pagination placeholders now carry the parameter's true global slot on **all** dialects. Previously, chains combining parameterized projection args (window functions) with parameterized `Limit`/`Offset` emitted colliding placeholder numbers — PostgreSQL silently bound a window arg's value to LIMIT; SQLite/SqlServer missed the bound name. Identity for all chains without projection params.

## Plan items implemented as specified

- Phase 1: marker emission (`SqlExprRenderer.AppendParameterPlaceholder`, `FormatMixedPagination`/`QuoteSqlExpression` optional formatter params), single-pass range-based rewrite + extraction with per-mask validation and cross-variant merge, `ReplaceNthOccurrence` deletion (and its literal-`?` miscount hazard), marker-free comparison renders for wrap detection.
- Phase 2: `EmitCarrierCommandBinding` iterates the ranking (mask-gated if-blocks handle permuted runs); pagination keeps bind-after-loop position.
- Phase 3: focused audit integration tests — window-function args, conditional mask × wrap (both variants executed), collection expansion × wrap — all passed first run against phases 1–2.
- Phase 4: generator `llm.md` internals section; corrected the wrap renderer's invariant comment.

## Deviations from plan implemented

(Recorded in `plan.md` "Implementation deviations" + workflow Decisions; all surfaced/ratified during the review pass.)

- INSERT VALUES markers descoped — column order == bind order by construction; params live in `InsertInfo`, not `ChainParameters`.
- `ReaderCodeGenerator` formatter wiring descoped — those strings ship into runtime/dynamic SQL where markers would leak.
- `MySqlBindOrder` is a nullable `IReadOnlyList<int>` excluded from `AssembledPlan.Equals` (derived from `SqlVariants`, which Equals already compares) instead of an equality-participating `EquatableArray<int>`.
- Validation failure emits **QRY048 warning** (with reason) + identity fallback, replacing the planned bare diagnostic sketch and an interim silent fallback.

## Gaps in original plan implemented

Found by the structured review (18 findings: 8 fixed-now, 4 gap-fixes, 6 no-action; see `review.md` classifications in the branch history):

- Post-process relocated from the interceptor output action into `PipelineOrchestrator.AnalyzeAndGroupTranslated` — both output consumers (interceptor emission, SQL manifest) now see final SQL with no dependence on Roslyn's cross-output execution order, and incremental equality compares post-processed plans consistently.
- Pagination markers/validation use `PaginationPlan.LimitParamIndex`/`OffsetParamIndex` (true slots) — fixes the window-param + parameterized-`Limit` shape on MySQL, then widened to all dialects (see Impact).
- Dead `ParameterName` guard removed; comments/llm.md corrected (MySQL parameter names are the constant `"?"` — the driver binds purely positionally).
- `TryMergeTextOrder` made internal with 8 direct unit tests (merge, insertion, contradiction-abort, determinism).

## Migration Steps

None. No public API changes; no schema changes; generated-code changes are confined to MySQL chains with divergent text order and the pagination-numbering fix.

## Performance Considerations

- Runtime: zero change — identical SQL strings, identical bind-code shape; reordering happens at generation time.
- Generator: one extra linear scan + rebuild per MySQL SQL variant (markers), plus an `O(variants × params²)` merge on chains with conditional masks — negligible against Roslyn costs; non-MySQL dialects pay nothing.
- Incremental caching: post-processing before file grouping restores equality between fresh and cached plans (previously, marker/token mutation inside the output action forced re-emission for parameterized MySQL chains).

## Security Considerations

No new dependencies; no user-input paths changed. Markers exist only inside the generator between rendering and post-processing; per-variant slot-set validation makes leakage loud (QRY048) rather than silent. A developer-authored string literal containing marker-shaped text (`{__Q5__}`) would be rewritten at generation time — exotic, developer-controlled, and now surfaced by QRY048.

## Breaking Changes

- Consumer-facing: none. New QRY048 warning can appear only on chains whose binding was already unreliable on MySQL.
- Internal: `SqlFormatting.FormatMixedPagination`/`QuoteSqlExpression` gained optional `parameterFormatter` parameters (internal class, source-shared — no binary consumers); `AssembledPlan` gained `MySqlBindOrder`; `SqlDialectConfig` gained `EmitMySqlBindMarkers`; SQL post-processing moved from `QuarryGenerator` into `PipelineOrchestrator`.

## Test Evidence

- Baseline before fix: 3234 passing + the new reproducer failing (0 rows).
- After: full suite **3269 + 201 (migration) + 146 (analyzers) green**, including 5 new MySQL integration tests (reproducer + 4 audit surfaces), a 4-dialect SQL-output + execution guard for pagination numbering, 11 marker unit tests, 8 merge unit tests, and 3 generation-level bind-order tests.
