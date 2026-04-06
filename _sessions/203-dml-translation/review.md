# Review: 203-dml-translation (Pass 2)

## Plan Compliance
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All 8 phases from the original plan are present, plus the 4 REMEDIATE fixes from Pass 1 are committed in `268b2b5` | -- | Faithful execution of the plan |
| `TranslateInsert` REMEDIATE refactor correctly calls `RegisterPrimaryTable` and reads the entity via `_tables.Values.First().Entity` -- behavior is preserved (same warning text on missing table, same comment output on success) | -- | Pass-1 finding addressed correctly |
| New tests added in REMEDIATE for `Update_WithComputedExpression`, `Delete_WithTableAlias`, and three DML analyzer integration tests (`DeleteExecuteAsync_ReportsQRM001`, `UpdateExecuteAsync_ReportsQRM001`, `InsertExecuteAsync_ReportsQRM001`) | -- | Pass-1 gaps addressed |

## Correctness
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `DapperMigrationCodeFix.ConvertToQuarryAsync` calls `SyntaxFactory.ParseExpression(result.ChainCode)` on the INSERT translation output, which is a `// TODO:` comment, not a valid C# expression. Applying the code fix to a Dapper `ExecuteAsync("INSERT ...")` call will replace the invocation with an empty/invalid expression, producing broken source. INSERT is now reachable through the analyzer (the new `InsertExecuteAsync_ReportsQRM001` test confirms this), so a user clicking the fix-it lightbulb on an INSERT site will hit this | High | Real code-fix break for an end-to-end-supported flow. The INSERT comment-only output was tolerable while no analyzer routed INSERT through the emitter, but the new analyzer integration tests (and the existing `DapperMigrationCodeFix` registration on QRM001/QRM002) make this user-reachable |
| `DapperMigrationAnalyzer` reports any `ChainEmitter` warning as `QRM002` ("Dapper call converted with Sql.Raw fallback ... uses Sql.Raw for {N} expression(s)"). The new DELETE/UPDATE-without-WHERE warning ("DELETE without WHERE — .All() added") and the INSERT warning ("INSERT requires entity construction") get reported under that descriptor, even though neither path actually uses `Sql.Raw`. Users will see a misleading "uses Sql.Raw for 1 expression(s)" message on a perfectly translated `.All()` chain or an INSERT TODO | Medium | Diagnostic message wording is now incorrect for the new DML cases. QRM002 was designed for `Sql.Raw` fallbacks; conflating it with no-WHERE warnings and INSERT TODOs hides the real meaning from users |
| `TranslateUpdate` reads `var primaryVar = _lambdaVars[0];` and `_tables.Values.First().Entity.AccessorName` immediately after `RegisterPrimaryTable`. This works only because the emitter is constructed per-translation and `_tables`/`_lambdaVars` start empty. The coupling is implicit -- a future refactor that pre-registers anything (or reuses the emitter) would silently emit the wrong table. Same pattern in `TranslateSelect`/`TranslateDelete`/`TranslateInsert` after the REMEDIATE refactor | Low | Brittle implicit assumption introduced by the REMEDIATE refactor. A small `RegisterPrimaryTable` returning the `TableRef` (or out parameters for entity + variable) would make the data flow explicit |
| `InsertExecuteAsync_ReportsQRM001` asserts `Id == "QRM001" || Id == "QRM002"`. As implemented today INSERT always adds a warning, so it always reports QRM002. The "or QRM001" branch is dead -- the test does not actually pin down the expected diagnostic ID and would silently keep passing if the diagnostic flipped to either side | Low | Test does not lock the contract it claims to verify |
| `ParseTableSource` consults `ReadIdentifierName` for the implicit alias only if the next token is `Identifier`/`QuotedIdentifier`, so the new `Set`/`Into`/`Values` token kinds correctly do NOT get swallowed as table aliases in `UPDATE users SET ...` and `INSERT INTO users VALUES ...`. Verified by inspection | -- | Important edge case handled correctly by the existing parser design |
| UNION/INTERSECT/EXCEPT trailing-keyword check is correctly gated to `stmt is SqlSelectStatement`, preventing spurious unsupported-feature diagnostics on DML | -- | Correct |

## Security
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No concerns. The translator runs at compile time, never executes SQL, and treats parameter references as opaque names | -- | -- |

## Test Quality
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `Update_QualifiedColumn` (parser) and `Delete_WithTableAlias` (emitter) verify the parser side of qualified columns and the emitter side of an aliased DELETE, but no emitter test exercises an UPDATE SET with a *qualified* column reference (`UPDATE users u SET u.is_active = 0`). The emitter path through `ResolveColumnAccess` for `colRef.TableAlias != null` in an UPDATE SET assignment is therefore untested | Low | Small but real coverage gap for a path the parser specifically supports |
| `Insert_EmitsComment` checks that the comment contains `UserName` and `Email`, and the analyzer test `InsertExecuteAsync_ReportsQRM001` accepts either `QRM001` or `QRM002`. Neither test verifies the actual end-user outcome (broken code-fix output) or the incorrect diagnostic message wording. This is what allowed the High-severity code-fix bug above to slip past two review passes | Low | Tests cover "the comment was emitted" but not "the comment is usable downstream" |
| `Delete_MissingFrom_HasDiagnostic`, `Update_MissingSet_HasDiagnostic`, `Insert_MissingValues_HasDiagnostic` only assert `Diagnostics.Count > 0`. They do not check the diagnostic message text or that `Statement` is null/non-null, so they would still pass if the parser produced an unrelated diagnostic for the same input | Low | Weak error-case assertions |
| `Update_WithComputedExpression` test (added in REMEDIATE) correctly exercises `salary = salary + 1000` and asserts the emitter produces `u.Salary = u.Salary + 1000`. The test does what it claims | -- | REMEDIATE Finding #1 fix verified |
| `Delete_WithTableAlias` test (added in REMEDIATE) correctly exercises `DELETE FROM users u WHERE u.user_id = @id` and asserts the alias-qualified WHERE resolves through the schema. The test does what it claims | -- | REMEDIATE Finding #2 fix verified |
| The three DML analyzer integration tests (added in REMEDIATE) compile a small Dapper-using snippet through the full `DapperMigrationAnalyzer` pipeline and assert a QRM diagnostic is reported. They do what they claim for DELETE and UPDATE; the INSERT test is loosened (see Correctness finding above) | -- | REMEDIATE Finding #3 fix verified for two of three statement types |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| New AST node classes (`SqlDeleteStatement`, `SqlUpdateStatement`, `SqlInsertStatement`, `SqlAssignment`) follow the established sealed-class + `NodeKind` override pattern | -- | Consistent |
| Mechanical `result.Statement!.` → `result.SelectStatement!.` rename applied uniformly across `SqlParserTests.cs`, `SqlParserEdgeCaseTests.cs`, `SqlParserReviewTests.cs` | -- | Clean |
| `RegisterPrimaryTable` is now reused by all four `Translate*` methods after the REMEDIATE refactor -- no remaining duplicated table-lookup logic | -- | REMEDIATE Finding #4 fix verified |
| Emitter section comment style (`// ─── DELETE translation ─── `) matches the existing SELECT-translation separators | -- | Consistent |
| Tokenizer keyword entries inserted in alphabetical order within each length bucket | -- | Follows existing convention |

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `SqlParseResult.Statement` widened from `SqlSelectStatement?` to `SqlStatement?`. All internal callers updated. Convenience `SelectStatement` accessor smooths the migration | -- | Internal types only, no external impact |
| `RawSqlMigrationAnalyzer` and `RawSqlColumnResolver` (in `Quarry.Generator/CodeGen`) updated to use `SelectStatement` and short-circuit on non-SELECT inputs -- behavior unchanged for SELECT, gracefully degrades for DML | -- | Correct |
| End-to-end DML routing through `DapperMigrationAnalyzer` is now exercised by the new integration tests for DELETE/UPDATE; INSERT is reachable but produces a code-fix that breaks (see Correctness High finding) | High | Same root cause as the High Correctness finding -- INSERT now reaches the code-fix path, where the comment-shaped output is invalid as a C# expression replacement |

## Classifications
| Finding | Section | Class | Action Taken |
|---------|---------|-------|--------------|
| `DapperMigrationCodeFix` produces broken C# when applied to INSERT (parses comment as expression) | Correctness (High) | (B) Gap | Add `ConversionResult.IsSuggestionOnly` flag; route to QRM003 in analyzer; guard in code fix |
| End-to-end INSERT routing through analyzer breaks code fix | Integration (High) | (B) Gap | Same root-cause fix as above |
| `QRM002` message wording wrong for new DML warnings (claims "Sql.Raw fallback" for `.All()` and INSERT TODOs) | Correctness (Medium) | (B) Gap | Update QRM002 messageFormat to be generic over warning kinds; update ConvertCommand.cs prose |
| `TranslateUpdate` brittle implicit coupling (`_lambdaVars[0]`/`_tables.Values.First()` after `RegisterPrimaryTable`) | Correctness (Low) | (B) Gap | Refactor `RegisterPrimaryTable` to return `TableRef?`; update all 4 callers |
| `InsertExecuteAsync_ReportsQRM001` accepts QRM001 OR QRM002 (loose) | Test Quality (Low) | (B) Gap | After High fix, INSERT goes to QRM003 — rename test and tighten assertion |
| Missing UPDATE qualified-column emitter test | Test Quality (Low) | (B) Gap | Add `Update_QualifiedColumn_WithAlias` test |
| Parser error-case assertions only check `Diagnostics.Count > 0` | Test Quality (Low) | (B) Gap | Tighten `Delete_MissingFrom_*`, `Update_MissingSet_*`, `Insert_MissingValues_*` to also check Statement is null and message text |

## Issues Created
None — all findings addressed in REMEDIATE.
