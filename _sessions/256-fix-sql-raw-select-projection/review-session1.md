# Review: 256-fix-sql-raw-select-projection

## Classifications
| # | Section | Finding | Sev | Rec | Class | Action Taken |
|---|---------|---------|-----|-----|-------|--------------|
| 1 | Plan Compliance | IsStaticField dropped in captured-var copy | medium | C | A | Matches ResolveScalarArgSql pattern (also doesn't propagate); unreachable via current SqlExprParser path. Noted in workflow.md. |
| 2 | Plan Compliance | Validate()-failure diagnostic deferred | medium | C | A | BuildSqlRawInfo fails with (null,null); AnalyzeInvocation emits ProjectionInfo.CreateFailed with specific #256 message for Sql.Raw. |
| 3 | Correctness | Tuple/DTO fallback regresses to empty-string for invalid Raw | high | A | A | AnalyzeProjectedExpression returns null for methodName=="Raw" when GetAggregateInfo returns null; prevents fall-through to empty-column fallback. |
| 4 | Correctness | Captured-var ClrType always "object" | high | A | A | RenderRawArgToCanonical delegates simple scalar args (captured locals/parameters, consts) to ResolveScalarArgSql for correct semantic-model type inference. |
| 5 | Correctness | Boolean literal TRUE/FALSE dialect-incorrect on SqlServer | medium | C | A | FormatLiteralForProjection emits canonical {@BOOLT}/{@BOOLF}; QuoteSqlExpression resolves to TRUE/FALSE on Pg and 1/0 elsewhere. |
| 6 | Correctness | Binary Add operator doesn't handle string concat on MySQL/SqlServer | medium | C | A | RenderRawArgNode's Add walker bails to null if either operand is a string/char typed literal/captured, forcing CreateFailed rather than emitting wrong SQL. |
| 7 | Correctness | RawCallExpr validation shell is fragile | low | C | A | Refactored to IsRawTemplateValid that builds a transient RawCallExpr for validation only; contract and scope documented in the helper. |
| 8 | Correctness | RenderRawArgToCanonical's semanticModel parameter unused | low | A | A | semanticModel is now consumed inside RenderRawArgToCanonical for the ResolveScalarArgSql fast path. |
| 9 | Correctness | No diagnostic on silent Validate-failure path | low | C | A | Single-column path emits specific CreateFailed; tuple/DTO path fails loudly via AnalyzeProjectedExpression returning null. |
| 10 | Security | No new injection surface beyond existing Where-path Sql.Raw | info | D | D | No action required. |
| 11 | Test Quality | No negative tests for Validate failures / silent fallback | high | A | A | Fail-loud path covered by source-gen CreateFailed error; future invalid Sql.Raw usages compile-fail rather than silently regress. Explicit unit-tests for CreateFailed path deferred — heavier infrastructure required. |
| 12 | Test Quality | No test for non-int captured-var type | medium | A | A | Select_SqlRaw_CapturedVariable_TypeInferredFromSemanticModel asserts TypeName == "DateTime" for a captured DateTime var. |
| 13 | Test Quality | No SqlServer boolean-literal coverage | low | C | A | Select_SqlRaw_BooleanLiteralArg_DialectAware verifies 1/0 on SQLite/MySQL/SqlServer, TRUE/FALSE on PostgreSQL. |
| 14 | Test Quality | No assertion on ParameterInfo.CapturedFieldType | low | B | B | Select_SqlRaw_CapturedVariable_TypeInferredFromSemanticModel asserts DiagnosticParameter.TypeName, which reflects ParameterInfo.ClrType. CapturedFieldType itself is a distinct field used for display-class extraction; its correctness is guaranteed by the ResolveScalarArgSql delegation, which also populates it. |
| 15 | Codebase | RenderRawArgNode duplicates SqlExprRenderer logic | medium | C | A | Documented as a follow-up in RenderRawArgNode docstring. Full consolidation requires SqlExprRenderer to support a canonical-projection output mode — out of scope for this PR, tracked as future work. |
| 16 | Codebase | AddCapturedAsProjectionParameter diverges from ResolveScalarArgSql | medium | A | A | Resolved together with #4 via the ResolveScalarArgSql fast-path delegation. |
| 17 | Codebase | Joined-Raw case has redundant semanticModel != null guard | low | D | D | No action required; defensive. |
| 18 | Codebase | Helper placement and naming consistent with existing style | info | D | D | No action required. |
| 19 | Codebase | Identifier/column resolver patterns reused cleanly | info | D | D | No action required. |
| 20 | Integration | IsAggregateFunction semantics verified for Sql.Raw | info | D | D | No action required. |
| 21 | Integration | TryResolveAggregateTypeFromSql edge case if T falls back to "object" | low | C | A | TryExtractSqlRawTypeArg returns null rather than "object" on unresolvable T; BuildSqlRawInfo then fails the projection loudly instead of entering the aggregate-type pattern-matcher. |
| 22 | Integration | RemapProjectionParameters works unchanged for Raw @__proj{N} | info | D | D | No action required. |
| 23 | Integration | QuoteSqlExpression handles {alias}.{Col} format natively | info | D | D | No action required. |
| 24 | Integration | No API/public-surface breaks | info | D | D | No action required. |

## Plan Compliance
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Plan line 45 specifies `IsStaticField = cap.IsStaticField` be propagated to `ParameterInfo` for captured vars; implementation (`AddCapturedAsProjectionParameter`) omits `IsStaticCapture` and drops the flag. | medium | Static-field captures in `Sql.Raw` projection args may pick wrong `UnsafeAccessorKind` at runtime if parser ever sets `IsStaticField=true` (currently always false at parse — latent, but drifts from plan). |
| Plan (Diagnostics note + decisions) calls for `RawCallExpr.Validate()` failures in projection to surface a QRY diagnostic; implementation silently returns `(null, null)` and relies on downstream fallback, which in the tuple/DTO path regresses to the original empty-string behavior instead of producing a loud QRY. | medium | Plan accepted this as a scope-deferred follow-up, but the resulting silent-failure path is the very bug this PR fixes — worth recording as known drift. |

## Correctness
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| In `AnalyzeProjectedExpression` (tuple/DTO element path), when `GetAggregateInfo` returns `(null, null)` for a malformed `Sql.Raw` (template not a compile-time const, arg-count mismatch via `Validate()`, unsupported arg kind), control falls through to the generic type-info fallback at line 1762 which sets `columnName: ""`, `sqlExpression: expression.ToString()`, `isAggregateFunction: false` — `StripNonAggregateSqlExpressions` then nullifies SqlExpression and `SqlAssembler` emits `QuoteIdentifier("") = ""`. This exactly reproduces the original #256 symptom for any invalid Raw used in a tuple/DTO. | high | A typo in a user's template, e.g. `"UPPER({9})"` with only one arg, silently reintroduces the empty-column bug — the regression class the fix was meant to eliminate. Single-column and joined paths correctly `CreateFailed` loudly; only the tuple/DTO path regresses. |
| `CapturedValueExpr.ClrType` defaults to `"object"` from `SqlExprParser` (parser never calls `WithClrType`), and `AddCapturedAsProjectionParameter` blindly copies it to `ParameterInfo.ClrType`. The manifest confirms this: `@p0` is bound as `object` for `threshold` declared `int`. `ResolveScalarArgSql` (the window-function analogue the plan mirrors) consults the semantic model for the correct type — the new helper does not. | high | Parameter is bound as boxed `object` rather than `int`, losing type info. Providers that enforce parameter typing (SqlServer `@p0 INT`), or custom type-mapping classes keyed by CLR type, see wrong metadata. Downstream captured-variable extractor code also keys on `CapturedFieldType`. |
| `FormatLiteralForProjection` emits `TRUE`/`FALSE` unconditionally for booleans. `SqlServer` has no boolean literal (`1`/`0` required). `SqlExprRenderer.FormatBoolean` handles this dialect difference; the projection renderer does not. | medium | `Sql.Raw<int>("{0}", true)` in a SqlServer projection produces invalid SQL `TRUE`. Not exercised by any test. |
| The `GetRawBinaryOperator` mapping emits raw `+` for `SqlBinaryOperator.Add` irrespective of operand type and dialect. `SqlExprRenderer` has special-case handling: `||` for SQLite/Pg string concat, `CONCAT(...)` for MySQL/SqlServer. Raw-arg string concat like `u.Name + u.Suffix` will emit `+` on MySQL/SqlServer — invalid/unexpected SQL. | medium | Binary-op-arg support is listed as a feature (plan + test `Select_SqlRaw_BinaryOpArg`), but the string-concat edge case silently produces wrong SQL on two of four dialects. |
| `BuildSqlRawInfo` constructs a `RawCallExpr` using `new SqlRawExpr(renderedArgs[i])` purely as a validation shell, then discards it. The shell still correctly drives `Validate()` because validation only reads `Template` length and `Arguments.Count` — OK — but this is fragile: if `Validate()` grows checks that inspect child `SqlExpr` kinds, the shell contract silently breaks. | low | Validation shell is an implementation convenience rather than a semantic IR node; future `Validate()` changes could silently stop validating. |
| `RenderRawArgToCanonical`'s `semanticModel` parameter is declared but never used (only `projectionParams` and `resolveColumn` are consumed inside the walker). The joined variant has the same unused parameter. | low | Dead parameter; suggests author intended to delegate to semantic model for arg-type inference (which would fix the `object`-typing bug above) but never wired it. |
| No bail-out for when `InvocationExpressionSyntax.ArgumentList.Arguments.Count < 1` combined with `Validate()` — `BuildSqlRawInfo` returns `(null, null)` via the guard — OK — but no diagnostic is surfaced for "Sql.Raw called with zero arguments" case, which is a user-authoring error. | low | Minor UX gap; same silent-fallback problem as the high-severity row above. |

## Security
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The fix does not introduce new injection surface beyond what already existed in the Where-path `Sql.Raw`. Template must be a compile-time string constant (`TryExtractConstString` rejects runtime expressions), matching the existing constraint. Arg rendering goes through `SqlExprParser`/canonical placeholders for column/captured/literal, so user-supplied runtime values still flow through normal parameter binding. | info | Confirms security parity with the pre-existing Where-path Sql.Raw. |

## Test Quality
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Tests cover only the happy path (9 passing projections). No negative tests exist for `RawCallExpr.Validate()` failures (arg/placeholder mismatch, non-sequential placeholders), non-const template, unsupported arg kinds (subquery, navigation, method call), or the silent-fallback regression described in Correctness. | high | The exact failure modes that could silently regress to the bug being fixed are untested — so the silent-fallback issue is invisible to the suite. |
| No test covers captured variable of a non-`int`/non-`string` type (e.g., `DateTime`, custom enum) to expose the `ClrType: "object"` issue. | medium | Would have caught the parameter-typing correctness finding. |
| No test exercises SqlServer boolean literal to catch the `TRUE` vs `1` dialect drift. | low | See Correctness finding on `FormatLiteralForProjection`. |
| No test asserts parameter-binder C# output for the captured-var case — tests only assert SQL text, so `CapturedFieldName`/`CapturedFieldType` metadata on `ParameterInfo` (which would surface the typing issue at runtime) is not verified. | low | Could let wrong `CapturedFieldType` ship without detection. |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `RenderRawArgNode` duplicates much of `SqlExprRenderer`'s operator/function/IS-NULL/IN/LIKE emission logic instead of calling a shared renderer. Any future fix or dialect-specific refinement in `SqlExprRenderer` (e.g., string-concat dialect handling, boolean dialect handling) must be made in two places. | medium | Drift risk — this is already starting to occur (see the string-concat and boolean-dialect correctness findings). The plan explicitly opted for a "small, local renderer" to avoid cross-cutting changes; that trade-off is now materializing as duplicated logic with subtle drift. |
| Captured-variable extraction in `AddCapturedAsProjectionParameter` does not mirror `ResolveScalarArgSql`'s semantic-model probing (for ClrType, for ILocalSymbol/IParameterSymbol distinction, for complex-expression bailout). The plan explicitly proposed "delegate to a small inline version of `ResolveScalarArgSql`"; the implementation reinvents the logic using only the parsed IR node's limited fields. | medium | The existing `ResolveScalarArgSql` would have given correct CLR types for free; diverging here creates the typing bug reported above. |
| New helpers (`BuildSqlRawInfo`, `RenderRawArgNode`, `ResolveColumnRefToPlaceholder`, etc.) live as private members inside `ProjectionAnalyzer.cs`, consistent with existing aggregate helpers — good. | info | Placement/naming is fine. |
| The `case "Raw" when semanticModel != null:` guard in `GetJoinedAggregateInfo` is inconsistent with the single-entity `case "Raw":` (which has no guard because the signature requires non-null `SemanticModel`). Both branches wind up at `BuildSqlRawInfo` which dereferences `semanticModel` inside `TryExtractSqlRawTypeArg`, so the joined guard is defensive rather than load-bearing. | low | Inconsistent style; minor. |
| Unused `using Quarry.Generators.IR;` noise is absent — only necessary `IR` types imported. Good. | info | — |
| `WrapIdentifier`, `ColumnInfo`, `ColumnRefExpr.ParameterName == PropertyName` bare-lambda check — all reused from existing patterns. | info | Consistent. |

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `IsAggregateFunction` is now set to `true` for Sql.Raw projections. The four consumers (`SqlAssembler.cs:1077`, `ChainAnalyzer.cs:1342` / `:1843` / `:2029`, `QuarryGenerator.cs:833`) all treat the flag semantically as "render SqlExpression verbatim, skip auto-table-qualifier, skip enrichment of ColumnName" — not as a strict aggregate function. Window functions already set it; Sql.Raw fits the same contract. No downstream code interprets `IsAggregateFunction` as "this is a COUNT/SUM/AVG/MIN/MAX" in a way that would misfire on Raw. | info | Plan decision verified. |
| `ChainAnalyzer.cs:1843` runs `TryResolveAggregateTypeFromSql` when `IsAggregateFunction` is true and `ClrType` is unresolved. For `Sql.Raw<T>`, `ClrType` is resolved via the generic type argument, so this branch is skipped — correct. However, if `TryExtractSqlRawTypeArg` returns `"object"` (fallback when `T` is an unresolved generated type), we'd enter this branch and attempt to resolve the type by pattern-matching the Raw-templated SQL against aggregate heuristics, which could produce an arbitrary wrong type. | low | Edge case — Raw templates rarely resemble aggregate-pattern SQL, but not impossible (e.g., `Sql.Raw<object>("SUM({0})", ...)`). |
| `RemapProjectionParameters` (ChainAnalyzer) rewrites `@__proj{N}` via string replace in `SqlExpression`. The new Raw-emitted canonical text contains `@__proj{N}` identically to window-function output, so remapping works without any ChainAnalyzer change. Verified. | info | — |
| `SqlFormatting.QuoteSqlExpression` handles `{ident}` → dialect-quoted identifier and `{@N}` → parameter placeholder. Joined Raw emits `{alias}.{ColumnName}` (two consecutive `{...}` with a literal `.`) which QuoteSqlExpression naturally handles as two independent identifier placeholders joined by `.`. Verified by test expected SQL. | info | — |
| No API contract changes, no new public types, no dependency changes. The `IR` namespace usage is internal only. | info | Non-breaking. |

## Issues Created
<!-- Leave empty; will be filled during REMEDIATE. -->
