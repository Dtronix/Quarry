# Review: 224-refactor-sql-expression

## Plan Compliance
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Plan specifies 5 phases / 5 commits; implementation delivers 4 commits by merging Phase 2 (switch to canonical format) and Phase 3 (simplify `ExtractColumnNameFromAggregateSql`) into one commit (`a13cfdc`). | Low | Acceptable consolidation -- Phase 3 depended on Phase 2 and both touched `ChainAnalyzer.cs`. No behavioral difference. |
| All 6 files listed in the plan were modified. All render sites (`SqlAssembler`, `ReaderCodeGenerator` x2) are wired. `ReQuoteSqlExpression` is deleted. `quotedName` dead code is removed. `dialect` parameter removed from 15 internal helper methods. `TranslateStringMethodToSql` / `TranslateSubstringToSql` correctly retain `dialect`. | None | Full plan compliance. |
| `ChainAnalyzer.BuildProjection` retains `var col = rawCol;` instead of inlining `rawCol` directly. | Low | Harmless indirection -- `col` is used extensively in the subsequent block. A future cleanup could inline it, but it does not affect correctness. |

## Correctness
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `QuoteSqlExpression`: a lone `{` with no matching `}` is handled -- falls through to `sb.Append(sqlExpression[i])`. Empty `{}` (close == i+1) also falls through correctly, appending both chars verbatim. No off-by-one risk. | None | Correct edge-case handling. |
| `ExtractColumnNameFromAggregateSql`: uses `LastIndexOf('{')` to find the rightmost placeholder (the column, not the table alias). Then `IndexOf('}', lastOpen + 1)` finds the closing brace. Returns `null` on no match or empty content. Correct for all documented patterns (`SUM({Total})`, `MIN({t0}.{Total})`, `LAG({Total}) OVER ...`). | None | Sound logic. |
| `ReaderCodeGenerator.GenerateColumnNamesArray` line 329: `resolved!` null-forgiving operator is safe because the guard on line 324 ensures `column.SqlExpression` is non-null/non-empty, and `QuoteSqlExpression` only returns null for null input. | None | No null-deref risk. |
| Stale comments in `ChainAnalyzer.cs` at lines 2137 and 2145-2146 still reference old quoting styles (`SUM("Total")`, `MIN(t0."Total")`, `SUM(\`Total\`)`, `MIN([Total])`). The method they describe (`TryResolveAggregateTypeFromSql`) now receives `{identifier}` format expressions. | Low | Misleading documentation, but no runtime impact. Should be updated for clarity. |

## Security
No concerns. The refactoring does not introduce new external inputs, user-facing surfaces, or dependencies. `QuoteSqlExpression` processes only internally generated placeholder strings, not user input.

## Test Quality
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| New unit tests in `DialectTests.cs` cover: null input, no-placeholder passthrough, single placeholder (4 dialects), multiple placeholders (3 dialects), OVER clause (3 dialects), and legacy pre-quoted passthrough (2 dialects). | None | Good coverage of the new `QuoteSqlExpression` method. |
| No dedicated unit test for `ExtractColumnNameFromAggregateSql` with the new `{identifier}` format. | Low | The method is private and exercised by integration tests (the 2862 existing Quarry.Tests cover aggregate/window function SQL generation end-to-end). Acceptable, but a targeted test would add resilience against future regressions. |
| No test for edge cases like lone `{` without `}`, or empty `{}`. | Low | These inputs cannot be produced by `WrapIdentifier` in normal flow, but a defensive test would document the contract. |
| Missing test: `QuoteSqlExpression` with PostgreSQL dialect for OVER clause variant. Only SQLite, MySQL, SqlServer tested for OVER. | Low | PostgreSQL and SQLite use the same double-quote style, so functionally covered, but the omission is a minor gap. |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `WrapIdentifier` follows the same private-static-helper pattern as the old `QuoteIdentifier` it replaces. Method naming is clear. | None | Consistent with codebase style. |
| New `QuoteSqlExpression` follows the same public-static pattern as existing `SqlFormatting` methods (`QuoteIdentifier`, `EscapeStringLiteral`). | None | Consistent API surface. |
| Commit messages are concise, reference `#224`, and describe the "why". | None | Follows repo convention. |

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `ReQuoteSqlExpression` was a `public` method on `SqlFormatting`. Its removal is a breaking change for any external consumer calling it directly. | Medium | If `SqlFormatting` is part of the public API (it is `internal static partial class`), this is safe. Verified: the class is `internal`, so no external consumers are affected. |
| The canonical `{identifier}` format is now stored in `ProjectedColumn.SqlExpression`. Any code path that reads `SqlExpression` and assumes dialect-quoted identifiers will break. | Low | All known consumers in the codebase have been audited. Non-rendering usages (null checks, equality comparisons, debug traces in `FileEmitter`, `CarrierEmitter`, `QuarryGenerator`) do not parse the quoting and are unaffected. The debug trace at `ChainAnalyzer.cs:2822` logs raw `{identifier}` format, which is actually more readable. |
| No new NuGet dependencies. No API contract changes to consumer-facing types. | None | Clean refactoring. |

## Classifications
| # | Section | Finding | Severity | Class | Action Taken |
|---|---------|---------|----------|-------|--------------|
| 1 | Plan Compliance | Phases 2+3 merged | Low | D | Intentional consolidation |
| 2 | Plan Compliance | `var col = rawCol` indirection | Low | D | Harmless |
| 3 | Correctness | Stale comments referencing old quoting | Low | A | Updated to `{identifier}` format |
| 4 | Test Quality | No dedicated ExtractColumnNameFromAggregateSql test | Low | A | Added tests via TryResolveAggregateTypeFromSqlPublic |
| 5 | Test Quality | No edge-case tests for malformed placeholders | Low | D | Cannot be produced by WrapIdentifier |
| 6 | Test Quality | Missing PostgreSQL OVER clause test | Low | A | Added PostgreSQL test case |
| 7 | Integration | ReQuoteSqlExpression removal | Medium | A | Verified: class is internal, grep confirms zero remaining callers |
| 8 | Integration | {identifier} format in SqlExpression | Low | A | All 13 consumer sites audited and verified safe |

## Issues Created
(none)
