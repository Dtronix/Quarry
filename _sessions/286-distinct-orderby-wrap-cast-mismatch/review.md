# Review: TBD

## Classifications
| # | Section | Finding | Sev | Rec | Class | Action Taken |
|---|---------|---------|-----|-----|-------|--------------|
| 1 | Codebase Consistency | QRY019 `messageFormat` ends with `"{0}."` — relies on every callsite's `errorMessage` being a complete sentence with no trailing punctuation; no contract enforces this. | Low | C | C | Separate issue #291 created |

## Plan Compliance

No concerns.

The diff matches the revised plan precisely:
- `plan.md` Phase 1 → `SqlAssembler.cs:1357-1421` adds `forComparison: bool = false`, gates the CAST on `!forComparison`, and `RenderProjectionColumnRef` passes `forComparison: true`. Inner-projection emission at `SqlAssembler.cs:1626` is unchanged (default `false`). Matches plan.md lines 32-54.
- `plan.md` Phase 2 (revised, per workflow.md Decisions 2026-04-30) → `DiagnosticDescriptors.cs:349` `messageFormat` reduced to `"{0}. The original runtime method will be used instead."`. Matches plan.md line 77.
- Tests skipped per the 2026-04-30 test-skip decision (workflow.md line 25). Diff confirms zero test file changes (`git diff --stat src/*.Tests` is empty).
- Doc-comment guard on `AppendProjectionColumnSql.forComparison` (`SqlAssembler.cs:1361-1372`) explicitly mentions `forComparison`, references #286, references #274 emit-only wrap, and tells future maintainers to gate new emit-only wrappers on `!forComparison` — fulfills plan.md line 73's "durable guard" requirement.
- Two commits as planned (`abfa17d`, `b96375e`).

## Correctness

No concerns.

- Default of `forComparison = false` (`SqlAssembler.cs:1375`) preserves all existing call-site behavior — only the two `RenderProjectionColumnRef` call sites (`SqlAssembler.cs:1418`) opt in.
- Inner-projection emission inside the wrap renderer (`SqlAssembler.cs:1626`, `AppendProjectionColumnSql(sb, col, config, paramIndex)`) uses the 4-arg overload position; the new optional 5th parameter defaults `false`, so the CAST is preserved when the wrap genuinely fires. The `SqlServerWindowIntCastTests` "ProductionPath_RowNumber" assertions (file lines 59, 113, 161) confirm this path stays bit-identical.
- Gating expression at `SqlAssembler.cs:1384`: `!forComparison && col.RequiresSqlServerIntCast && dialect == SqlDialect.SqlServer` — short-circuits correctly, matches plan.md line 39.
- `RenderProjectionColumnRef` has only the 5-arg call now (`SqlAssembler.cs:1418`); both `NeedsDistinctOrderByWrap` (line 1475), `MayNeedDistinctOrderByWrap` (line 1501), and `projColIndexBySql` build (line 1578) all flow through `RenderProjectionColumnRef`, so the comparison-side fix lands at every detection site as plan.md line 12 promised.
- QRY019 `messageFormat` change preserves the `"The original runtime method will be used instead."` runtime-fallback hint (`DiagnosticDescriptors.cs:349`). Combined with `CallSiteTranslator.cs` `errorMessage` strings (lines 111, 130, 152, 254), the final rendered message becomes a clean single sentence rather than the doubled phrasing.
- The stale doc claim "byte-identical output" was correctly removed from `AppendProjectionColumnSql`'s summary (`SqlAssembler.cs:1360`) — under the new design the two paths are deliberately asymmetric.

## Security

No concerns.

The `forComparison` flag controls only whether a fixed-string SQL fragment (`CAST(... AS INT)`) is appended to already-rendered output. No new user-input flow into SQL, no new parameter handling, no escaping changes. The wrap suppression is dialect+column-flag gated and limited to a render-time string-equality lookup that does not affect emitted SQL when the wrap fires (only whether it fires unnecessarily). QRY019 message format change is a pure compile-time diagnostic with no runtime or SQL surface.

## Test Quality

No concerns.

The test-skip decision is defensible:
- The bug is unreachable through the public chain API (workflow.md line 25; window functions in `OrderBy` hit `QRY019` and runtime LINQ fallback, so no `ORDER BY` clause is rendered for the comparison to mismatch on).
- Existing reachable coverage is preserved: `SqlServerWindowIntCastTests` (file lines 59, 113, 161) pins the production CAST emit path. A grep of `CrossDialectDistinctOrderByTests` confirmed it has no window-function cases (so nothing there could have caught the false-positive wrap before, and nothing there can break after the fix). `RequiresSqlServerIntCast` flow is untouched, so `CrossDialectWindowFunctionTests` remains unaffected.
- The doc-comment guard on `forComparison` is the durable contract a unit test would otherwise pin; preferring the doc over a private-helper assertion follows the repo's existing convention (the file has many similar prescriptive `<remarks>` blocks).
- 3423/0 baseline maintained per workflow.md line 33; no test movement to investigate.

The only marginal concern is that no test exercises the QRY019 messageFormat output post-fix, so a future regression of the format string into a new doubled-phrase shape would not be caught — but pinning diagnostic wording is generally avoided in this repo (no QRY019 wording was pinned previously either), so consistency favors the current decision.

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| QRY019 `messageFormat` is now `"{0}. The original runtime method will be used instead."` and depends on every `CallSiteTranslator` failure path producing an `errorMessage` that is a complete clause without trailing punctuation. The four current callsites (`CallSiteTranslator.cs:111, 130, 152, 254`) all comply, but no test or contract enforces that future callsites will. | Low | A new contributor adding a 5th failure path who appends `"."` to their `errorMessage` would produce `"... .. The original runtime method will be used instead."`. Worth a one-line comment near the descriptor or above each `errorMessage:` literal. Not blocking; tracked in Classifications. |

Other consistency checks pass:
- `forComparison: bool = false` follows the repo's defaulted-bool naming convention (compare `useGenericParamFormat`, `inBooleanContext`, etc. seen throughout `SqlExprBinder`/`SqlExprRenderer` calls).
- Doc-comment style matches the surrounding `<param>` blocks in `SqlAssembler.cs` — multi-paragraph `<remarks>`-style prose with concrete issue numbers (`#274`, `#286`) — same shape as the file's other doc comments.
- The QRY019 descriptor still follows the `id`/`title`/`messageFormat`/`category`/`defaultSeverity`/`isEnabledByDefault`/`description` shape used by every other descriptor in the file.

## Integration / Breaking Changes

No concerns.

- `AppendProjectionColumnSql` is `private static` (`SqlAssembler.cs:1374`) — adding an optional trailing parameter is source- and binary-compatible at the only call sites, all internal to this file.
- `RenderProjectionColumnRef` is `private static` (`SqlAssembler.cs:1415`) — signature unchanged.
- `RequiresSqlServerIntCast` flow is unchanged — the column flag is set the same way and read at the same spot; only the gate evaluating it gained a `!forComparison` conjunction. Production emit (where `forComparison == false`) sees the identical condition.
- No public/internal API surface changed. No generated-code shape change for any reachable chain (probe in workflow.md line 25 confirms the buggy chain shape is currently unreachable, so production SQL output is byte-identical for all 3423 tests).
- QRY019 wording change: no test in the suite pins the wording (verified by grep on `"could not be translated to SQL"` and `"translated to SQL at compile"` — only matches are session docs and `Sql.cs` runtime exception strings unrelated to QRY019). External consumers parsing diagnostic message text would see the new shorter form, but consumers should key off `id == "QRY019"`, not message content.

## Issues Created
- #291: QRY019 messageFormat depends on CallSiteTranslator errorMessage punctuation discipline
