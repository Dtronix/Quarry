# Review: 203-dml-translation (Pass 3)

## Plan Compliance
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All 8 phases of the original plan are still present, plus the 4 Pass-1 fixes (commit `268b2b5`) and the 7 Pass-2 fixes (commit `2755311`). The diff against `origin/master` is internally consistent | -- | Faithful execution of the plan plus two remediation rounds |
| INSERT continues to be emitted as a `// TODO:` comment (per the original Decision in `workflow.md`), and is now correctly classified as `IsSuggestionOnly` so the IDE code fix never substitutes the comment text for the invocation | -- | Pass-2 High fix verified |

## Correctness
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `DapperConverter` (the public facade in `src/Quarry.Migration/DapperConverter.cs`, used by `Quarry.Tool`'s `convert` command) does NOT propagate `ConversionResult.IsSuggestionOnly`. `DapperConversionEntry` exposes only `ChainCode`, `IsConvertible => ChainCode != null`, and `HasWarnings`. For an INSERT, `IsConvertible` is therefore `true`, `HasWarnings` is `true`, and `ConvertCommand` prints `=> // TODO: Construct ... entity and use: // db.Users().Insert(entity)... (with warnings)` and counts the call under "Converted with warnings". A user reading the tool output sees the comment block flattened onto one line as if it were chain code, then a final hint to "use the IDE code fix (QRM001)" — but the analyzer routes INSERT to QRM003, so the IDE code fix is *not* registered for it. The CLI consumer of `ChainEmitter` was missed by both prior passes — they only audited the analyzer + code fix path | High | Same root cause as the Pass-2 INSERT High fix, but on the parallel CLI consumer. The Pass-2 fix only patched the analyzer/code-fix branch; the `quarry convert` command still treats INSERT as a successful "with warnings" conversion and shows comment text to the user. The fix is to surface `IsSuggestionOnly` on `DapperConversionEntry` (or change `IsConvertible` to require `!IsSuggestionOnly`) and have `ConvertCommand` print INSERT as a manual-conversion suggestion in its own bucket |
| `SqlNodeWalker.Walk` has no cases for `SqlDeleteStatement`, `SqlUpdateStatement`, `SqlInsertStatement`, or `SqlAssignment`. `Walk(deleteStmt, visitor)` visits only the root and never descends into `Table`, `Where`, `Assignments`, or `ValueRows`. `FindAll<SqlParameter>(deleteStmt)` returns an empty list. Today the only callers (`SqlParserReviewTests`) walk `SelectStatement` instances, so nothing breaks at runtime, but any future feature that walks DML AST trees (parameter extraction, column extraction, refactoring tools) will silently get nothing back | Low | Latent gap. The DML AST nodes are now part of the public-ish parser surface but the walker is incomplete. Inconsistent with the rest of Phase 1's promise that DML statements are first-class AST citizens |
| `DapperMigrationAnalyzer.AnalyzeInvocation` branch ordering (null → QRM003, IsSuggestionOnly → QRM003, warnings → QRM002, else → QRM001) is correct. The IsSuggestionOnly branch is placed before the warnings branch, so INSERT (which has both a warning and `IsSuggestionOnly = true`) goes to QRM003 as intended | -- | Pass-2 High fix verified |
| `DapperMigrationCodeFix.ConvertToQuarryAsync` has the `if (result.IsSuggestionOnly) return document;` guard placed *after* the `result.ChainCode == null` check, so a comment-only suggestion never reaches `SyntaxFactory.ParseExpression(result.ChainCode)`. Belt-and-suspenders: even if QRM002 were ever raised on an `IsSuggestionOnly` result by mistake, the code fix would still no-op | -- | Pass-2 High fix verified |
| `MigrationDiagnosticDescriptors.DapperQueryWithRawFallback` (QRM002) `messageFormat` is now `"This Dapper call was converted with {0} warning(s): {1}"` and the analyzer's only call site passes `(warnings.Count, firstMessage)`. There is exactly one producer (`DapperMigrationAnalyzer.AnalyzeInvocation` line 87-91), so no other caller is at risk of passing a single arg and rendering `{1}` literally or throwing | -- | Pass-2 Medium fix verified |
| `RegisterPrimaryTable` now returns `TableRef?` and all four `Translate*` methods (`TranslateSelect`/`TranslateDelete`/`TranslateUpdate`/`TranslateInsert`) read `primaryTable.Entity.AccessorName` and `primaryTable.Variable` directly. No `Translate*` method still uses `_tables.Values.First()` or `_lambdaVars[0]` — the remaining `_lambdaVars[0]` references are confined to `EmitSelect`, `ResolveColumnAccess`, `ResolveTableVariable`, and `BuildLambdaParams`, which are SELECT-side helpers and pre-existing | -- | Pass-2 Low fix verified |
| `ConvertCommand.cs` summary prose updated from "With Sql.Raw fallback" to "Converted with warnings", but the line below (`"To apply conversions, use the IDE code fix (QRM001) or install Quarry.Migration as an analyzer."`) still mentions only QRM001. Now that QRM003 is the user-visible channel for INSERT manual-conversion suggestions, the hint should mention QRM003 too (or be reworded) so users know an INSERT TODO will not be auto-applied | Low | Misleading user instruction. Minor copy fix |

## Security
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No concerns. The translator runs at compile time, never executes SQL, and treats parameter references as opaque names | -- | -- |

## Test Quality
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `Update_WithQualifiedColumn_AndAlias` correctly exercises `UPDATE users u SET u.is_active = 0 WHERE u.user_id = @id` and asserts `.Set(u => { u.IsActive = 0; })` and `.Where(u => u.UserId == id)`. The aliased column resolves through the `_tables["u"]` entry rather than a fallback, locking down the contract that `primaryTable.Variable` (not `_lambdaVars[0]`) is threaded through | -- | Pass-2 Low fix verified |
| `InsertExecuteAsync_ReportsQRM003` (renamed from `InsertExecuteAsync_ReportsQRM001`) asserts `Any(d => d.Id == "QRM003")` AND `Any(d => d.Id == "QRM001" || d.Id == "QRM002")` is `false`. The previously loose "QRM001 OR QRM002" assertion has been tightened to a strict QRM003 contract with an explicit explanatory comment | -- | Pass-2 Low fix verified |
| `Delete_MissingFrom_HasDiagnostic`, `Update_MissingSet_HasDiagnostic`, and `Insert_MissingValues_HasDiagnostic` now assert `result.Success == false` AND that the diagnostic message contains the specific token name (`Expected 'FROM'`, `Expected 'SET'`, `Expected 'VALUES'`). I traced these through `Expect(SqlTokenKind.X)` → `AddDiagnostic($"Expected '{FormatTokenKind(kind)}', got '{FormatToken(Current)}'")` → `FormatTokenKind(From)` returns `"FROM"`, etc. The tests will pass against the current parser and will fail loudly if the wording drifts | -- | Pass-2 Low fix verified |
| There is no end-to-end test that runs `DapperConverter.ConvertAll` over an INSERT and asserts the entry is treated as a manual-conversion suggestion (the existing `DapperConverterTests` only cover SELECT). This is the test that would have caught the High Correctness finding above | Low | Coverage gap on the public CLI consumer of `ChainEmitter` |
| The new `Update_WithQualifiedColumn_AndAlias` test pins `.Set(u => { u.IsActive = 0; })` but does not also assert that the *fallback* path is gone — i.e., that no `x.IsActive` (default-variable) substring leaks. Mostly fine, but a `Does.Not.Contain("x.")` would harden the regression-prevention | -- | Minor — current assertion is sufficient in practice |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All four `Translate*` methods follow the same pattern: `var primaryTable = RegisterPrimaryTable(...); if (primaryTable == null) return ...;` then `db.{primaryTable.Entity.AccessorName}()`. Symmetric and easy to reason about | -- | Consistent |
| `MigrationDiagnosticDescriptors` field name `DapperQueryWithRawFallback` is now misleading after the Pass-2 wording change — the descriptor's title is "Dapper call converted with warnings" but the field name still says "RawFallback". A future reader scanning declarations could think the field is unrelated to the new `.All()`/INSERT warnings. Renaming the field (and the corresponding `SupportedDiagnostics` entry) would close the gap. Pure cosmetic — does not affect emitted diagnostics | Low | Internal naming inconsistency. Not user-visible |
| `ConversionResult.Diagnostics` xmldoc still says `"warnings for Sql.Raw fallbacks, etc."` after the Pass-2 wording change. Same cosmetic drift as above | -- | Comment-only |
| `DapperMigrationAnalyzer.cs` line 82 inline comment is `"// Converted with warnings (Sql.Raw fallback, no-WHERE DML, etc.)"` — accurately reflects the broader scope after Pass-2 | -- | Consistent |
| `SqlNodeWalker` is missing the new statement cases (see Correctness finding) — the rest of the parser-side files have been updated for DML, so this is the only inconsistency | -- | Same as Correctness finding above |

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `SqlParseResult.Statement` was widened from `SqlSelectStatement?` to `SqlStatement?` in Phase 1; the convenience `SelectStatement` accessor (`Statement as SqlSelectStatement`) is in place and used by the two non-Migration consumers (`RawSqlMigrationAnalyzer.cs` and `RawSqlColumnResolver.cs`), each correctly short-circuiting on null | -- | Internal types only, smooth migration |
| `DapperConverter` is a `public` API class and `DapperConversionEntry` is a `public` result type. Adding `IsSuggestionOnly` to `DapperConversionEntry` to fix the High Correctness finding above is an additive (non-breaking) public API change. Changing `IsConvertible` to `ChainCode != null && !IsSuggestionOnly` would technically be a behavior change for any external caller that already special-cases comment-shaped output, but no such consumer exists in this repo | Medium | The fix surface is in a public API. Worth noting for the FINALIZE step |
| End-to-end DML routing through the analyzer is exercised by `DeleteExecuteAsync_ReportsQRM001`, `UpdateExecuteAsync_ReportsQRM001`, and `InsertExecuteAsync_ReportsQRM003`. The corresponding CLI path through `DapperConverter` is not exercised for DML at all | Low | See Test Quality finding |

## Classifications
| Finding | Section | Class | Action Taken |
|---------|---------|-------|--------------|
| `DapperConverter` doesn't propagate `IsSuggestionOnly` — INSERT shows as "converted with warnings" in CLI output | Correctness (High) | (B) Gap | Add `IsSuggestionOnly` to `DapperConversionEntry` (additive public API), propagate in `ConvertAll`, update `ConvertCommand` to print as manual-suggestion bucket |
| Public API change to `DapperConversionEntry` (additive) | Integration (Medium) | (B) Gap | Same fix — additive constructor parameter and property |
| `SqlNodeWalker.Walk` missing DML statement cases | Correctness (Low) | (B) Gap | Add cases for `SqlDeleteStatement`, `SqlUpdateStatement`, `SqlInsertStatement`, `SqlAssignment` |
| `ConvertCommand` "use the IDE code fix (QRM001)" prose ignores QRM003 | Correctness (Low) | (B) Gap | Update prose to mention QRM003 / manual conversion |
| No `DapperConverter` end-to-end test for INSERT | Test Quality (Low) | (B) Gap | Add test asserting INSERT entry surfaces `IsSuggestionOnly` |
| `DapperQueryWithRawFallback` field name misleading after Pass-2 wording change | Codebase Consistency (Low) | (B) Gap | Rename to `DapperQueryWithWarnings` |
| `ConversionResult.Diagnostics` xmldoc still says "Sql.Raw fallbacks" | Codebase Consistency (--) | (B) Gap | Update xmldoc text |

## Issues Created
None — all findings addressed in Pass-3 REMEDIATE.
