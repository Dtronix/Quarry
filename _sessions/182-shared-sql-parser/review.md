# Review: 182-shared-sql-parser

## Plan Compliance
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `IsKeywordUsableAsIdentifier` always returns `false` -- plan says keywords used as column/table names should be handled (quoted identifiers cover some cases but unquoted keywords-as-names like `FROM users` where `users` is not a keyword will work, while actual keyword identifiers like a column named `status` will not) | Medium | Unquoted keyword identifiers (`status`, `name`, `order`, `group`, etc.) that collide with `SqlTokenKind` keywords will be misclassified as keywords, causing parse failures instead of treating them as identifiers. The plan's Phase 5 explicitly calls for "identifiers that clash with keywords" to be handled. |
| Comma-separated implicit cross join in FROM not implemented or tested -- plan Phase 5 explicitly lists "Multiple tables in FROM (comma-separated implicit cross join)" as a required edge case | Low | `SELECT a FROM t1, t2` will parse `t1` as the table, then fail or misinterpret the comma. This is a stated plan deliverable. Low severity because the parser can still flag it via diagnostics rather than silently producing a wrong AST. |
| Plan says tokenizer should be a `ref struct` -- implementation is a `static class` with a `List<SqlToken>` return | Info | The plan described a `ref struct` tokenizer with `NextToken()`. The implementation materializes all tokens into a `List` upfront via a static `Tokenize` method. This is a reasonable deviation (plan itself noted the parser would store tokens in a list anyway), so no real concern. |
| `FIRST` keyword not recognized in `FETCH FIRST n ROWS ONLY` -- plan mentions `OFFSET n ROWS FETCH NEXT n ROWS ONLY` as the SQL Server pattern but comment in code says "could be FIRST" | Low | `FETCH FIRST 10 ROWS ONLY` (standard SQL, also used by DB2 and PostgreSQL) will fail because `FIRST` is tokenized as an Identifier and `Match(SqlTokenKind.Next)` will not consume it. The parser will then try `ParsePrimaryExpr` on the `FIRST` identifier, misinterpreting the structure. |

## Correctness
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `ParseLimitOffset` ROWS/ROW fallback logic is incorrect -- `Match(SqlTokenKind.Rows)` then `if (!Check(SqlTokenKind.Rows)) Match(SqlTokenKind.Row)` will always attempt to match ROW on the NEXT token after ROWS was consumed | Medium | If the input is `OFFSET 20 ROWS FETCH...`, after consuming ROWS the parser tries `Match(Row)` against `FETCH`. It happens to fail gracefully (no match on FETCH), but the logic is semantically wrong and will fail on pathological input. The correct pattern is `if (!Match(SqlTokenKind.Rows)) Match(SqlTokenKind.Row)`. The same bug appears in the FETCH clause at lines 428-430. |
| Backslash escape in `ReadStringLiteral` applies to ALL dialects, not just MySQL | Medium | The plan specifies backslash escaping for MySQL only (`\'`), but the implementation skips `\` + next-char for every dialect. For SQLite/PostgreSQL/SqlServer, the string `'C:\path\to\file'` would be incorrectly tokenized (the `\p`, `\t`, `\f` pairs are consumed as escape sequences, corrupting the token boundaries). |
| `Expect` does not advance the cursor on failure | Medium | When an expected token is missing, the parser records a diagnostic but stays at the same position. In most paths this is safe because subsequent checks break out of loops, but if `Expect` is called in a loop body (e.g., `Expect(SqlTokenKind.On)` in `ParseJoins`), a malformed query could trigger repeated diagnostics at the same position without progress. Consider advancing on failure or adding a recovery mechanism. |
| `IEquatable` implementations on most AST nodes are shallow -- they ignore child nodes | Low | `SqlSelectStatement.Equals` only compares `IsDistinct`. `SqlBinaryExpr.Equals` only compares `Operator`. `SqlInExpr.Equals` only compares `IsNegated`. Two structurally different ASTs could compare as equal. If these equality implementations are ever used in collections or deduplication, they will produce incorrect results. If equality is not needed now, the `IEquatable` implementations are misleading. |
| Window function `SqlUnsupported` text extraction in `ParsePrimaryExpr` (identifier branch) uses a fragile offset calculation | Low | `overStart - name.Length - 1` assumes the function name is immediately before `overStart` with exactly one character gap. For `COUNT(*) OVER(...)`, the gap includes the parenthesized args. The substring bounds could be wrong, potentially producing incorrect raw text or an `ArgumentOutOfRangeException`. |

## Security
No concerns. This is a parser producing an internal AST -- it does not execute SQL, interpolate user input, or produce output sent to a database.

## Test Quality
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No test for comma-separated FROM (implicit cross join) | Medium | Plan Phase 5 explicitly requires this edge case. Whether it should parse successfully or produce a diagnostic, there should be a test documenting the expected behavior. |
| No test for backslash in string literals on non-MySQL dialects | Medium | The backslash-for-all-dialects bug (see Correctness) is untested. A test like `Tokenize("'C:\\path'", SQLite)` would expose the issue. |
| No test for `FETCH FIRST n ROWS ONLY` syntax | Low | The SQL Server OFFSET/FETCH tests only use `NEXT`. There is no test covering `FIRST` which is the other valid keyword in the standard. |
| No test for malformed JOIN (e.g., `INNER t2 ON ...` missing JOIN keyword) | Low | Error recovery paths in `TryParseJoinKind` / `Expect(SqlTokenKind.Join)` are untested. These paths are reachable and could produce confusing diagnostics or stall. |
| No test for unterminated string literal or quoted identifier | Low | Edge cases like `SELECT 'unterminated FROM t` exercise the tokenizer's EOF-during-literal handling. The tokenizer handles this (loops until end), but there's no test verifying the behavior. |
| No test for nested block comments | Low | `/* outer /* inner */ still in comment */` is not tested. The tokenizer does not support nested block comments (it stops at the first `*/`), which is correct for most SQL dialects, but the behavior should be documented with a test. |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All four new parser files follow the existing `#if QUARRY_GENERATOR` dual-namespace pattern | OK | Consistent with `SqlFormatting.cs`, `SqlDialect.cs`, and `SqlClauseJoining.cs`. |
| `Compile Remove` in `Quarry.csproj` follows the same pattern as Migration and Scaffold exclusions | OK | Consistent with existing exclusion entries. |
| `internal sealed class` with `IEquatable<T>` used throughout, no records | OK | Matches the decision to target netstandard2.0 (no records). |
| Tests use `Quarry.Generators.Sql.Parser` namespace via Generator assembly, with `GenDialect` alias pattern | OK | Follows the existing test project conventions for accessing generator internals. |

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No existing code consumes the parser yet -- pure additive change | Info | The parser is not wired into any generator, analyzer, or migration code path. There is zero risk of breaking existing behavior. This also means the parser cannot be integration-tested against real consumer scenarios yet. |
| `SqlUnsupported` extends `SqlExpr` -- can appear anywhere an expression is expected | Info | This design means AST walkers must handle `SqlUnsupported` in every expression position. Not a bug, but consumers need to be aware. The `HasUnsupported` flag on `SqlParseResult` provides a fast bailout. |
| No changes to `Quarry.Generator.csproj` or `Quarry.Analyzers.csproj` | OK | As noted in the plan, these already include `Sql/**/*.cs` via shared projitems wildcard, so the new `Sql/Parser/` files will be automatically compiled into both assemblies. |

## Classifications
| Finding | Section | Class | Action Taken |
|---------|---------|-------|--------------|
| `IsKeywordUsableAsIdentifier` always returns false | Plan Compliance | A | Fix: allow common keywords as identifiers in name-position contexts |
| Comma-separated FROM not implemented | Plan Compliance | A | Fix: implement comma-separated FROM as implicit cross join |
| ROWS/ROW fallback logic bug | Correctness | A | Fix: `if (!Match(Rows)) Match(Row)` |
| Backslash escape applies to all dialects | Correctness | A | Fix: gate on `dialect == MySQL` |
| `Expect` not advancing on failure | Correctness | A | Fix: advance cursor on failure |
| Shallow `IEquatable` implementations | Correctness | D | Ignore -- pattern consistency only, equality not used structurally |
| Window function unsupported text offset math | Correctness | A | Fix: simplify unsupported text capture |
| Missing FIRST keyword token | Plan Compliance | B | Add: FIRST to SqlTokenKind and keyword classifier |
| Missing test: comma-separated FROM | Test Quality | B | Add test |
| Missing test: backslash in non-MySQL string | Test Quality | B | Add test |
| Missing test: FETCH FIRST | Test Quality | B | Add test |
| Missing test: malformed JOIN | Test Quality | B | Add test |
| Missing test: unterminated literals | Test Quality | B | Add test |

## Issues Created
None -- issues to be created after review discussion.
