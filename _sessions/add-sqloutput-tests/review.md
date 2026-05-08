# Review: add-sqloutput-tests

## Classifications

| # | Class | Rec | Sev | Section | Finding | Action Taken |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | B | B | Med | Plan Compliance | `Select_TwoLevel_NavSum_NestedNavCount` (3-level projection-side) replaced with sibling 1-level subqueries; deep projection-nesting gap not covered | Added `Select_ProjectionMixedNestingDepths_OrderTotalAndItemTotal` (2-level Sum/Sum). Discovered the generator's projection-type resolver mishandles nested int aggregates — kept the test at decimal and filed follow-up #294. |
| 2 | D | D | Low | Plan Compliance | `Update_ComputedColumnExcluded` absent — implicitly subsumed by QRY075 compile-time error | No-op (reasoned in 2026-05-06 decision). |
| 3 | B | B | Med | Plan Compliance | QRY075 has no positive generator-driver test asserting the diagnostic actually fires | Added `QRY075_UpdateSetAction_AssignToComputedColumn_Reports` and `QRY075_UpdateSetAction_AssignToWritableColumn_DoesNotReport`. |
| 4 | D | D | Low | Plan Compliance | `BatchInsert_ComputedColumnExcluded` renamed to `BatchInsert_ComputedColumnNotInColumnSelector_StillExcludedInValues` | No-op (cosmetic). |
| 5 | D | D | Low | Correctness | `StripQuoting` over-strip robustness — no active bug, current call sites only feed property names | No-op (defensive). |
| 6 | D | D | Low | Correctness | `case ClauseKind.Set` else-branch removal comment relies on translator invariant; previously dead code | No-op (defensive). |
| 7 | A | A | Med | Test Quality | `ToAsyncEnumerable_BreakEarly_StopsAfterFirstRow` only asserts `seen.Count == 1` — doesn't distinguish streaming from buffer-then-yield | Renamed to `ToAsyncEnumerable_BreakAfterFirst_YieldsOrderedFirstRow`; rewrote comment to admit it's a behavioral assertion, not a streaming-vs-buffering proof. |
| 8 | A | A | Low | Test Quality | `MultiContextPerFileTests` comment claims "carrier non-collision" but assertion only proves file separation | Tightened class docstring: now states the test only proves file separation, with carrier-name non-collision attributed to `file`-scoped accessibility. |
| 9 | B | B | Med | Test Quality | After 1abdd63, no test exercises QRY075's firing path (UpdateSetAction + computed column) — same gap as #3 | Same fix as #3 — see above. |
| 10 | D | D | Low | Test Quality | Deferred conditional-Having comment lacks issue/TODO ref | No-op (deferred follow-up referenced in 2026-05-03 decision). |
| 11 | D | D | Low | Codebase Consistency | `public class` vs `internal class` — local Generation/ convention is mixed | No-op (matches local convention). |
| 12 | D | D | Low | Codebase Consistency | `RunGenerator`/`CreateCompilation` helpers duplicated across two new test files | No-op (minor; refactor not justified). |
| 13 | D | D | Low | Codebase Consistency | Explicit `IQueryBuilder<User>` typing in streaming tests — justified by reassignment in `if(true)` blocks | No-op (justified). |
| 14 | D | D | Low | Integration | `InterceptorKind.UpdateSet` removal — verified internal-only, no external API impact | No-op (verified). |
| 15 | D | D | Low | Integration | Manifest .md regeneration — mechanical, expected per workflow | No-op (expected). |
| 16 | D | D | Low | Integration | `TagSchema` additive — DDL/seed parity correctly maintained across four dialects | No-op (verified). |

## Issues Created

- #294: Generator: nested int-aggregate projections resolve as decimal (CS9144)
- #296: Audit — are `MaxConditionalBits=8` / `MaxIfNestingDepth=2` justified by real usage?

## Plan Compliance

| Finding | Severity | Why It Matters |
| --- | --- | --- |
| Plan's `Select_TwoLevel_NavSum_NestedNavCount` (1-level Sum + 3-level nested Sum/Count over `Orders.Items.Tags`) was replaced with `Select_TwoSiblingProjectionSubqueries_AliasReusesPerColumn` at `src/Quarry.Tests/SqlOutput/CrossDialectNestedSubqueryTests.cs:182`, which uses two sibling 1-level subqueries (`Orders.Sum` + `Orders.Count`) — the deep projection-side nesting case the plan called out is not covered, and the substitution is not recorded in workflow.md Decisions. | Medium | Phase 3's stated goal was 3+ level nesting coverage; the projection-subquery case silently dropped from 3-level to 1-level leaves the deep-projection path that was explicitly identified as a gap unverified, contradicting the plan without a documented rationale. |
| Plan's `Update_ComputedColumnExcluded` (Phase 4 / `CrossDialectSchemaTests.cs:119`) is absent from the branch — it was implicitly subsumed by the QRY075 diagnostic decision (Update().Set on computed columns is now a compile error, so a runtime exclusion test is meaningless), but no Decisions entry confirms the dropped test. | Low | Tracking this in Decisions would prevent a future reviewer from re-adding the test or wondering why the symmetric Insert/BatchInsert pair has no Update sibling. |
| QRY075 has no positive generator-driver test asserting the diagnostic actually fires; only the negative POCO test (`ComputedColumnDiagnosticTests.QRY075_UpdateSetPoco_DoesNotReport_BecauseInsertInfoFiltersComputed` at `src/Quarry.Tests/Generation/ComputedColumnDiagnosticTests.cs:91`) remains after 1abdd63. | Medium | QRY075 is the headline new diagnostic of Phase 4 yet the test fixture only proves it stays silent on the POCO path; if a future change regresses the Action-lambda or column-expression hooks, no test will catch it. |
| `BatchInsert_ComputedColumnExcluded` test was renamed to `BatchInsert_ComputedColumnNotInColumnSelector_StillExcludedInValues` at `src/Quarry.Tests/SqlOutput/CrossDialectSchemaTests.cs:153` — equivalent coverage but plan-vs-code naming mismatch. | Low | Cosmetic; harder for someone reading the plan to grep for the implementation. |

## Correctness

| Finding | Severity | Why It Matters |
| --- | --- | --- |
| `ChainAnalyzer.StripQuoting` at `src/Quarry.Generator/Parsing/ChainAnalyzer.cs:1938` splits on the last `.` and then `Trim('"', '`', '[', ']')` — if a column expression's `ColumnSql` legitimately contains dialect chars in unusual orderings (e.g., a value contained inside the column reference) the trim could over-strip; in practice `ColumnSql` is built by `CallSiteTranslator` as a quoted bare property name so this is fine, but the helper has no asserts/guard against multi-segment dotted refs deeper than 2 segments. | Low | Robustness rather than active bug — current call sites only feed property-name-shaped strings to the helper. |
| `ChainAnalyzer.cs:1059`'s removal of the `else` for `ClauseKind.Set` with null `SetAssignments` is correct given current call sites, but the safety claim in the comment ("only UpdateSetAction/UpdateSetPoco produce Set clauses") relies on `CallSiteTranslator` always populating `SetAssignments`; if a future translator path emits a `ClauseKind.Set` clause with null assignments this would silently drop the SET term instead of throwing. | Low | The branch was previously dead-code regardless, so this is a regression-resistance concern, not a real fault. |

## Security

No concerns.

## Test Quality

| Finding | Severity | Why It Matters |
| --- | --- | --- |
| `CrossDialectStreamingTests.ToAsyncEnumerable_BreakEarly_StopsAfterFirstRow` at `src/Quarry.Tests/SqlOutput/CrossDialectStreamingTests.cs:60` only asserts `seen.Count == 1`; this passes whether the implementation streams lazily or buffers all rows then stops on `break`, so the test does not actually prove short-circuit / lazy enumeration as the comment ("the underlying reader is short-circuited rather than buffered") claims. | Medium | The test name and intent are about streaming semantics, but the assertion doesn't distinguish streaming from buffer-then-yield; a regression to eager materialization wouldn't be caught. |
| `MultiContextPerFileTests.TwoContextsInOneFile_EmitTwoIndependentInterceptorFiles` at `src/Quarry.Tests/Generation/MultiContextPerFileTests.cs:61` asserts two files emit and each declares a `file sealed class Chain_` carrier, but never inspects the carrier names for non-collision — both files could declare `Chain_0` and the assertion still passes (which is exactly what `file` accessibility allows). The test description in the workflow says it proves "carrier non-collision" but the assertion only proves file separation. | Low | The workflow already records that `file`-scoped carriers make `Chain_0` non-colliding by language semantics, so a name-collision test would be redundant; nonetheless the comment overstates what the assertion proves. |
| `ComputedColumnDiagnosticTests` at `src/Quarry.Tests/Generation/ComputedColumnDiagnosticTests.cs:91` contains only one test method. After 1abdd63 removed both the failing typed-lambda test and the redundant non-computed-column test, no test exercises the diagnostic firing path that QRY075 was added to protect (`UpdateSetAction` lambda + computed column via column-expression / SetAssignment paths). | Medium | A diagnostic with one negative test and zero positive tests can silently break without notice. |
| `CrossDialectConditionalMaskTests` at `src/Quarry.Tests/SqlOutput/CrossDialectConditionalMaskTests.cs:386` retains a 5-line comment-block flagging the deferred conditional-Having case, which is intentional per the 2026-05-03 decision, but the comment doesn't reference an issue number / follow-up tag. | Low | Plain-text follow-ups in test files tend to drift; a `// TODO(QRY-####):` or session reference would aid future cleanup. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
| --- | --- | --- |
| `ComputedColumnDiagnosticTests` at `src/Quarry.Tests/Generation/ComputedColumnDiagnosticTests.cs:21` is declared `public class`, while `MultiContextPerFileTests` (also new in this branch, same directory) at `src/Quarry.Tests/Generation/MultiContextPerFileTests.cs:19` is `public class` as well — but most test fixtures under `src/Quarry.Tests/SqlOutput/` are `internal class`. Within `src/Quarry.Tests/Generation/` the existing convention is mixed (`ConditionalCarrierTests` is `public`), so this is consistent locally but inconsistent across the project. | Low | Cosmetic; doesn't affect correctness or test discovery. |
| The `RunGenerator` helper and `CreateCompilation` helper are duplicated almost verbatim across `ComputedColumnDiagnosticTests.cs:48-88` and `MultiContextPerFileTests.cs:24-58` — both new files. | Low | Future maintainers will need to update assembly references in two places; a shared `GeneratorDriverHarness` would have been the DRY approach. |
| `CrossDialectStreamingTests` at `src/Quarry.Tests/SqlOutput/CrossDialectStreamingTests.cs:101-104` declares `IQueryBuilder<User> lt = ...` etc., explicitly typed; most existing tests in the same directory use `var`. | Low | The explicit typing was needed to allow reassignment inside `if (true) { ... }` blocks (matches the pattern in `CrossDialectConditionalMaskTests`), so this is justified by the conditional-mask requirement. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
| --- | --- | --- |
| `InterceptorKind.UpdateSet` enum value removed at `src/Quarry.Generator/Models/InterceptorKind.cs` — verified `internal enum InterceptorKind` is internal-only, so the public Quarry API surface is unchanged. No remaining references in `src/Quarry.Generator/` or `src/Quarry.Tests/` (only `ClauseRole.UpdateSet` and `OptimizationTier.UpdateSet`, which are unrelated types). | Low | Confirms the removal is internal-only and doesn't break external consumers. Listed for the record. |
| Manifest .md files at `src/Quarry.Tests/ManifestOutput/quarry-manifest.{mysql,postgresql,sqlite,sqlserver}.md` regenerate to add the `tags` table and the new query shapes from streaming + nested subquery + conditional mask tests — mechanical pipeline output, expected per the workflow. | Low | Already documented as expected; only listed here so a reviewer skimming the diff isn't surprised by the +3000-line .md additions. |
| New schema (`TagSchema`) and `Many<TagSchema> Tags` navigation on `OrderItemSchema` are additive — existing tests are unaffected and DDL/seed are added consistently across all four dialect containers (`QueryTestHarness.cs`, `Integration/{PostgresTestContainer,MySqlTestContainer,MsSqlTestContainer}.cs`). PostgreSQL seed sequence advance (`tags`/`TagId`) is added at `Integration/PostgresTestContainer.cs:398`. | Low | The cross-dialect DDL/seed parity is correctly maintained; flagging only because schema additions are the kind of change that often desyncs across dialects. |
