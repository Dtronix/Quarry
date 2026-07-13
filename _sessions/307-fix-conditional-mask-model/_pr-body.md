# Summary

- Closes #307

Fixes both verified critical defects in the conditional clause bitmask model, adds two defense-in-depth layers, and remediates 14 findings from the structured review (1 High among them).

# Reason for Change

The conditional bitmask model produced silent wrong results or runtime crashes on documented usage patterns with zero compile-time signal:

- **Defect 1:** conditional `.Limit()` / `.Offset()` / `.Distinct()` were silently applied unconditionally — silent truncation when the branch wasn't taken; a runtime-valued limit defaulted to 0 and returned zero rows; `ToDiagnostics` reported the clause inactive while the executed SQL contained it.
- **Defect 2:** `else if` chains and multi-clause branches produced unenumerated masks — branch groups were keyed by condition text, so reachable mask values dispatched `null` SQL into the provider at runtime.

# Impact

- **Conditional modifiers honored (defect 1):** `Limit`/`Offset`/`Distinct` under `if` now render per-mask (`LIMIT`/`OFFSET`/`DISTINCT` gated per variant, including the SQL Server `ORDER BY (SELECT NULL)` injection and the DISTINCT ORDER-BY wrap), set their mask bit at runtime, bind pagination parameters only when active, and report consistently in `ToDiagnostics`/manifest. MySQL positional `?` bind-order extraction handles the per-variant pagination slots. `WithTimeout` no longer consumes a bit — its `TimeSpan?` carrier field with `DefaultTimeout` fallback is already runtime-correct, so a bit only doubled the variant table.
- **Structural cascade grouping (defect 2):** branch groups are keyed by cascade identity (syntax ancestry of the whole `if/else-if/else` chain or ternary) with per-arm enumeration — all of an arm's bits enumerate together, arms are mutually exclusive, and a no-arm mask is enumerated when the cascade lacks a final else, has arms without chain sites, or is itself nested inside another conditional arm. `else if` chains of any arm count, multi-clause branches, and ternary reassignment (`q = flag ? q.Where(x) : q`) are now fully supported.
- **Bit identity:** `ConditionalTerm` carries `SiteUniqueId`; all site→bit correlation is by identity, fixing a latent positional misassignment (chains partially inside an `if` had baseline-depth sites stealing bits, producing swapped predicates).
- **Defense in depth:** generated dispatch guards unenumerated masks (bounds + null check → actionable `InvalidOperationException` via `Quarry.Internal.ThrowHelper` instead of a provider null-CommandText error), and a generation-time brute-force validator asserts reachable ⊆ enumerated masks, demoting to QRY032 on violation — deliberately a separate walk from the enumerator.
- **Offset-without-LIMIT fixed** (surfaced by review): SQLite/MySQL reject bare `OFFSET n`; `SqlFormatting` now emits the dialect no-limit idiom (`LIMIT -1` / `LIMIT 18446744073709551615`). Covers both plain offset-only chains (pre-existing bug) and the limit-inactive variants manufactured by a conditional Limit.
- **Fail-loud demotions for unrepresentable shapes** (review): a chain site inside an `else if (...)` condition expression, or a clause in a different arm than its terminal within the same cascade, now demotes to QRY032 instead of silently baking wrong SQL.
- Docs updated: root `llm.md` (participating methods + example), generator `llm.md` (cascade model internals), `docs/articles/querying.md`.

Tests: 3363 + 201 + 146 green across all four dialects (Docker-based MySQL/PostgreSQL/SQL Server included), up from 3281 at baseline — ~80 new tests covering every arm of the new model.

# Plan items implemented as specified

All 7 plan steps: (1) `SiteUniqueId` bit identity, (2) runtime dispatch guard on both dispatch paths, (3) WithTimeout bit removal + `IsKnownBuilderMethod` fix for its reassignment form, (4) full conditional Limit/Offset/Distinct gating, (5) structural cascade grouping with per-arm enumeration, (6) generation-time reachability validator with unit-tested pure core, (7) documentation across all three surfaces.

# Deviations from plan implemented

- Step 4's MySQL handling was implemented via per-variant marker validation and trailing-slot handling in `RewriteMySqlBindMarkers` rather than extending `BuildParamConditionalMap` as the plan worded — pagination virtual slots never enter the conditional map. Intent achieved; the plan's missing test shape (conditional runtime-valued Limit + parameterized Where) was added during review remediation (F8).
- The no-arm mask option enumerates FIRST (not last) so the base variant keeps its lead position in diagnostics and manifest output — discovered via a #301 gap-pin test that asserts on the lead variant.

# Gaps in original plan implemented

Review pass (16 findings: 1H/5M/10L → classified 7A/7B/2D) drove these beyond the plan:

- **F3 (High):** a fully-represented `if/else` nested inside an outer conditional arm enumerated masks `{1,2}` while runtime mask 0 was reachable (outer branch not taken) — and the validator was blind to it by construction. Fixed by forcing the no-arm option for cascades at relative depth > 1, in both the enumerator and the validator's `ZeroAllowed` derivation; pinned at generation and execution level including the dangling-else form.
- **F4/F6:** QRY032 demotions for else-if-condition chain sites and sibling-arm clauses (silent wrong SQL previously).
- **F5:** the offset-without-LIMIT dialect idiom fix described above.
- **F7–F10:** test gaps closed — nested-cascade shapes, MySQL bind-order with conditional runtime pagination, behavioral throw-path coverage (`ThrowHelperTests`), collection-path dispatch guard assertion, and `ToDiagnostics` consistency for conditional Offset/Distinct/no-arm/multi-clause shapes.
- **F12/F13:** manifest variant labels now dedupe by arm identity and mark final-else arms as `else(<cond>)`; pagination diagnostic parameters carry conditional-bit metadata inside `ClauseDiagnostic`.

# Performance Considerations

Enumeration and validation are generation-time only (validator is brute force over ≤256 masks). Generated dispatch gains one bounds+null branch per multi-variant terminal execution; single-variant chains are unchanged. Variant tables shrink for conditional-WithTimeout chains (bit removed).

# Security Considerations

Reviewed — no concerns. No user source text flows unescaped into generated code or SQL; the new emitted constructs use only integer bit indices; `GroupKey` is generator-internal.

# Breaking Changes

## Consumer-facing (behavioral, all intended fixes — release notes should call these out)

1. **Ternary reassignment** (`q = flag ? q.Where(x) : q`) previously baked the predicate in unconditionally; query results change when `flag` is false. This shape was never an error before.
2. **Conditional `Limit`/`Offset`/`Distinct`** previously applied always; untaken-branch executions now return the full/undeduplicated row set (this is defect 1's fix — the old behavior was silent truncation).
3. **Previously-rejected shapes now compile** (flat 4+-arm else-if chains, reassigning conditional `WithTimeout`) and **previously-compiling degenerate shapes now demote to QRY032** (chain site inside an else-if condition; clause in a different arm than its terminal).
4. Offset-only pagination now emits valid SQL on SQLite/MySQL (previously a runtime syntax error).

## Internal

- `ToDiagnostics`/manifest surfaces: `BranchKind` is derived structurally (a lone `if` inside an else block now reports `Independent`); conditional `WithTimeout` clause entries report `IsConditional = false`; manifest variant labels changed format. Snapshot-style assertions on these surfaces may need updates.
- Generated code now references `Quarry.Internal.ThrowHelper` (new public runtime API; generator ships inside the Quarry package, so versions always match).
