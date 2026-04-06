# Review: 203-dml-translation

## Plan Compliance
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All 8 phases implemented in order across 4 commits (AST infrastructure, tokenizer keywords, parser dispatch + DELETE/UPDATE/INSERT, emitter for all three) | -- | Implementation matches the planned phase structure faithfully |
| `SqlStatement` base class introduced; `SqlSelectStatement`, `SqlDeleteStatement`, `SqlUpdateStatement`, `SqlInsertStatement` all inherit from it as specified | -- | Matches plan Phase 1 exactly |
| `SqlParseResult.Statement` changed from `SqlSelectStatement?` to `SqlStatement?`, with convenience `SelectStatement` accessor added | -- | Clean approach to the planned breaking change, easier migration for callers |
| UPDATE `.Set()` uses single block-body lambda as planned: `.Set(u => { u.Col1 = val1; u.Col2 = val2; })` | -- | Matches the design decision |
| INSERT emitted as comment with TODO and diagnostic warning, exactly as specified | -- | Matches the design decision |
| DELETE/UPDATE without WHERE emit `.All()` plus diagnostic warning, as specified | -- | Matches the design decision |
| No integration-level analyzer test for DML (Phase 8 item) -- the `RawSqlMigrationAnalyzer` only handles SELECT via `parseResult.SelectStatement` and silently ignores DML | Low | The plan's Phase 8 mentioned verifying that `ExecuteAsync("DELETE ...")` routes through the full pipeline, but no analyzer currently handles `ExecuteAsync`. The `ChainEmitter` is ready, but no analyzer invokes it for DML. This appears intentional (no DML analyzer exists yet) but is worth noting as a gap vs. the plan's Phase 8 aspirations |

## Correctness
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No concerns with parser dispatch logic -- `ParseRoot()` correctly dispatches by first keyword token and falls through to diagnostic for unknown statements | -- | Clean and correct |
| `ParseTableSource()` correctly avoids consuming `SET` keyword as table alias because `SET` is now its own `SqlTokenKind`, not `Identifier` -- the implicit alias check only triggers for `Identifier`/`QuotedIdentifier` tokens | -- | Important edge case handled correctly by the existing parser design |
| UPDATE `ParseExpression()` for assignment values correctly stops at commas and WHERE, since expression parsing bottoms out before those tokens | -- | The `do { ... } while (Match(Comma))` loop in `ParseUpdateStatement` works correctly because `ParseExpression` doesn't consume commas |
| `TranslateInsert` bypasses `RegisterPrimaryTable()` and does its own `TryGetEntity` lookup | Low | This is intentional since INSERT only emits a comment and doesn't need lambda variables or table registration, but it means the error diagnostic message is duplicated. Not a bug, just minor code duplication |
| UNION/INTERSECT/EXCEPT check correctly gated to SELECT statements only (line 626: `stmt is SqlSelectStatement`) | -- | Prevents false unsupported-feature warnings on DML statements |

## Security
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No concerns. The code is a source-to-source translator operating at compile time. No runtime SQL execution, no user input handling. Parameter references are preserved by name without evaluation. | -- | -- |

## Test Quality
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| 30 new tests added across 3 files: 19 parser DML tests, 12 tokenizer keyword tests (6 keywords x upper+lower), and ~11 ChainEmitter DML tests. All 2987 tests pass. | -- | Good coverage |
| Parser tests cover: simple cases, WHERE clauses, parameters, complex WHERE, trailing semicolons, qualified columns, multi-row INSERT, missing-keyword error cases | -- | Solid edge case coverage |
| Emitter tests cover: DELETE with/without WHERE, complex WHERE, unknown table, UPDATE single/multi column, with/without WHERE, unknown table, INSERT comment output and diagnostic | -- | Covers all the key emission paths |
| Missing test: UPDATE with expression values (e.g., `SET count = count + 1`) rather than just literals/parameters | Low | Computed-value SET assignments are common in real SQL. The parser would handle these (since `ParseExpression` supports binary ops), and the emitter would emit them via `EmitExpression`, but there is no explicit test verifying this path end-to-end |
| Missing test: INSERT without INTO (some dialects support `INSERT table ...`) | Low | The parser requires INTO and would correctly produce a diagnostic, but there is no test for this specific error case |
| Missing test: DELETE with table alias (`DELETE FROM users u WHERE u.id = @id`) | Low | The parser handles this via `ParseTableSource` alias support, but no test verifies alias resolution in DELETE emission |
| The existing test `Parse_NonSelectStatement_HasDiagnostics` was correctly updated to `Parse_NonSelectStatement_NowSupported` and a new `Parse_UnknownStatement_HasDiagnostics` was added for TRUNCATE | -- | Good maintenance of existing tests |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All existing test files updated from `result.Statement!.` to `result.SelectStatement!.` -- consistent mechanical change across SqlParserTests.cs, SqlParserEdgeCaseTests.cs, SqlParserReviewTests.cs | -- | Clean and consistent |
| New AST node classes follow the same pattern as existing ones: sealed class, constructor with properties, `NodeKind` override | -- | Matches established patterns |
| New parser methods follow the same structure as `ParseSelectStatement()`: consume keyword, parse components, return statement | -- | Consistent with codebase style |
| `RegisterPrimaryTable()` extracted from `TranslateSelect` and reused in `TranslateDelete`/`TranslateUpdate` -- good refactoring to avoid duplication | -- | Reduces code duplication |
| Section comment style (`// --- DELETE statement ---`) matches existing section separators in the file | -- | Consistent formatting |
| Keywords added to `ClassifyKeyword()` are inserted in alphabetical order within their length bucket | -- | Follows existing convention |

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `SqlParseResult.Statement` type changed from `SqlSelectStatement?` to `SqlStatement?` -- this is an internal API (all types are `internal`), not a public breaking change | Low | All internal callers updated. The convenience `SelectStatement` property provides a smooth migration path. Since these are `internal` types, there is no external consumer impact |
| `RawSqlMigrationAnalyzer` updated to use `parseResult.SelectStatement` -- correctly continues to handle only SELECT for its use case | -- | No behavioral change for the existing analyzer |
| `RawSqlColumnResolver` updated with null check for `SelectStatement` returning fallback for non-SELECT -- handles the new possibility of DML parse results gracefully | -- | Prevents NullReferenceException if a non-SELECT statement reaches this code path |
| No analyzer integration for DML statements yet -- `ChainEmitter` can translate DELETE/UPDATE/INSERT but no analyzer calls it for `ExecuteAsync` Dapper calls | Medium | The emitter infrastructure is in place but there is no end-to-end integration. Users running the migration analyzer will not get suggestions for DML statements. This is likely a deliberate next-step item, but the plan's Phase 8 implied this would be verified |

## Classifications
| Finding | Section | Class | Action Taken |
|---------|---------|-------|--------------|
| Missing test for UPDATE with computed expression values (`count = count + 1`) | Test Quality | Gap | Noted for follow-up |
| Missing test for DELETE with table alias | Test Quality | Gap | Noted for follow-up |
| No DML analyzer integration (ExecuteAsync not routed through ChainEmitter) | Integration | Gap | Noted as future work -- emitter is ready, analyzer needs extension |
| Duplicated table-lookup logic between `TranslateInsert` and `RegisterPrimaryTable` | Correctness | Nit | Minor duplication, acceptable given INSERT's unique comment-only output |

## Issues Created
None -- all findings are low severity or intentional gaps for future work. The implementation is solid, well-tested, and faithfully follows the plan.
