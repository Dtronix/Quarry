# Review: 188-migration-converters

## Plan Compliance
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| EfCoreCallSite field name differs from plan: `ChainExpression` instead of `ChainRoot` | Low | The plan called for a `ChainRoot` field pointing at the DbSet access expression, but the implementation stores the full chain expression as `ChainExpression`. This is actually a better design for code-fix replacement, so the deviation is an improvement. No action needed. |
| Plan specified `EfCoreChainStep` as a nested type inside CallSite; implementation puts it in the same file but as a top-level internal class | Low | Functionally equivalent. Consistent with how SqlKataChainStep is also structured. |
| ADO.NET code fix does not emit the `// TODO: Remove DbCommand setup above` comment before the statement, only as leading trivia on the replacement expression | Low | Plan D5 says "adds a TODO comment before the replacement." The implementation attaches it as leading trivia which renders above the expression. Behavior matches intent; placement is slightly different from "before the statement" but functionally correct. |
| SqlKata detector walks forward from `new Query()` (plan Phase 6) | None | Correctly implemented as designed. |
| All nine diagnostic descriptors match planned IDs, severities, and categories | None | Full compliance with Phase 1 spec. |
| All three converters, analyzers, and code fixes delivered in scope | None | No scope creep. All 7 phases implemented. |

## Correctness
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| EF Core detector: `GroupJoin` is in `ChainMethods` but the converter has no `case "GroupJoin":` handler | Medium | A GroupJoin step will silently hit the `default` case and emit a generic "Unrecognized chain method 'GroupJoin' was skipped" warning. This is technically correct behavior (graceful degradation), but `GroupJoin` should either get an explicit case with a clear TODO message like `Join` does, or be moved to `UnsupportedEfCoreMethods` so the unsupported-method diagnostic path handles it. |
| EF Core `ReplaceParameter`: word-boundary check uses `!char.IsLetterOrDigit(body[i - 1]) && body[i - 1] != '_'` (line 234) | Medium | The replacement is string-based rather than syntax-aware. If the lambda parameter name is a common single letter like `u` and the body contains an identifier like `bus`, the word-boundary check prevents false replacement. However, the check does not account for the `@` verbatim identifier prefix. More importantly, if the entity accessor starts with the same letter as the original parameter (e.g., entity `Users` and param `u` both derive variable `u`), the code short-circuits correctly (`if (originalParam != lambdaVar)`). The string-based approach is fragile but acceptable for a migration tool that generates suggestions rather than compiled output. |
| EF Core detector: unsupported methods are recorded but their chain steps are NOT added to `steps` list | Low | When an unsupported method like `AsNoTracking()` is encountered, the method name is added to `unsupported` but no step is added to `steps`. The chain walk continues correctly. The converter receives `UnsupportedMethods` and emits warnings. This is correct per decision D6. |
| ADO.NET `FindCommandTextAssignment` only searches top-level statements in the enclosing block | Medium | The method iterates `block.Statements` directly, not `block.DescendantNodes()`. If `CommandText` is assigned inside a nested `if` block, `using` block, or `try` block, it will not be found. In contrast, `CollectParameterNames` uses `block.DescendantNodes()` and would find parameters inside nested blocks. This inconsistency could cause the detector to miss call sites where CommandText is assigned conditionally. |
| ADO.NET `FindCommandTextAssignment` returns the first matching assignment, not the last | Low | If `CommandText` is assigned multiple times (e.g., reassigned before Execute), only the first assignment is used. This could produce incorrect SQL for patterns like: `cmd.CommandText = "initial"; cmd.CommandText = "final"; cmd.Execute*()`. The last assignment before the Execute call should be used. |
| ADO.NET `CollectParameterNames` uses `DescendantNodes()` which scans the entire block | Low | This finds parameters added *after* the Execute call too. For a simple migration tool this is acceptable since the code being analyzed is presumably correct, but it could theoretically collect parameters from a different command setup later in the same block. |
| SqlKata detector `TryDetectSingle`: backward walk from invocation to `new Query()` does not check that the intermediate chain type remains `SqlKata.Query` | Low | The backward walk in `TryDetectSingle` traverses MemberAccess/Invocation chains syntactically until it finds an `ObjectCreationExpressionSyntax`. If another fluent builder happens to end with `new Query(...)` from a different type, it could produce a false match. The semantic check in `TryDetectFromCreation` will catch this, so the risk is mitigated. |
| SqlKata converter: several chain methods in detector `ChainMethods` have no converter `case` | Low | `WhereNot`, `OrWhereNull`, `OrWhereNotNull`, `WhereNotIn`, `OrWhereIn`, `OrWhereBetween`, `WhereTrue`, `WhereFalse` are all recognized by the detector but have no explicit case in the converter. They hit the `default` with a generic "Unrecognized chain method" warning. This is safe (graceful degradation) but the warning message is misleading -- these are recognized SqlKata methods, just not yet mapped. Better to emit specific messages. |
| EF Core detector returns null for nested terminals (line 103-109) | None | Correctly prevents double-detection of chains like `.Where(...).Count().ToString()`. The outer terminal owns the detection. Good edge-case handling. |
| SqlKata `EmitNullCheck`: silently produces no output when `columnArg` is null | Medium | If `WhereNull`/`WhereNotNull` is called without arguments (or with a non-literal argument), the method does nothing -- no `sb.Append`, no diagnostic. The chain code will silently omit the clause. Should at minimum emit a diagnostic or append a fallback `/* TODO */` comment. |

## Security
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No concerns | -- | This is a code analysis/migration tool operating on Roslyn syntax trees at compile time. No user-facing input surfaces, no network calls, no file I/O beyond what Roslyn already does. SQL strings are extracted from source code literals, not external input. |

## Test Quality
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No code fix tests (no `CodeFixVerifier` / `CodeFixTest` infrastructure used) | High | The plan specifies code fix tests for all three converters: "Code fix: replaces chain correctly, handles await, adds usings." None of the test files exercise the code fix providers. The code fixes (`EfCoreMigrationCodeFix`, `AdoNetMigrationCodeFix`, `SqlKataMigrationCodeFix`) are untested. These are the most complex pieces -- they manipulate syntax trees, handle await expressions, and add using directives. |
| EF Core stub is duplicated across 3 test files (DetectorTests, ConverterTests, AnalyzerTests) | Low | The ~55-line stub is copy-pasted. A single shared helper class would reduce maintenance burden and risk of drift. Same for the ADO.NET stub (3 copies) and SqlKata stub (3 copies). Follows existing Dapper test patterns though, so this is consistent with codebase conventions. |
| No edge case test for EF Core chains with multiple unsupported methods in sequence | Low | Tests cover single unsupported methods (AsNoTracking), but not chains like `.AsNoTracking().Include(x => x.Orders).Where(...)` which would exercise the unsupported method chain walk continuation. |
| No test for ADO.NET string concatenation SQL | Low | The detector's `ExtractStringValue` handles `BinaryExpressionSyntax` for string concatenation, but no test verifies this works with constant concatenation like `"SELECT * " + "FROM users"`. |
| No test for ADO.NET multiple CommandText assignments | Low | The `FindCommandTextAssignment` first-wins behavior is untested. |
| No test for SqlKata `ForPage` mapping | Low | `EmitForPage` converts page/perPage to Offset/Limit arithmetic, but no test exercises this conversion. |
| No test for SqlKata aggregate terminals (`AsSum`, `AsAvg`, `AsMin`, `AsMax`) | Low | Only `AsCount` is tested. The `AsSum` and other aggregate mappings have non-trivial column resolution logic that is untested. |
| Analyzer tests verify correct diagnostic ID emission for all 3 states per converter | None | Good coverage of the QRM0x1/0x2/0x3 classification logic. |
| Detector tests cover both positive detection and negative rejection (non-EF, non-SqlKata, non-DbCommand types) | None | Good boundary testing. |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| EF Core converter resolves entities via `TryResolveEntity` (iterating `schemaMap.Entities`) while SqlKata uses `schemaMap.TryGetEntity` and ADO.NET uses the `ChainEmitter` pipeline | Medium | Three different schema resolution paths for the same conceptual operation. The EF Core converter matches by class name (`UserSchema` or `User`) via case-insensitive string comparison on `entity.ClassName`. SqlKata and ADO.NET/Dapper resolve by table name via the dictionary lookup in `SchemaMap.TryGetEntity`. This divergence means EF Core resolution could match different entities than the other converters for the same underlying table. The EF Core path should ideally resolve via table name too, or at least use a shared method. |
| Analyzer per-node allocation: `new XxxDetector()` on every `AnalyzeInvocation` call | Low | All four analyzers (Dapper, EF Core, ADO.NET, SqlKata) allocate a new detector instance per syntax node. The detectors are stateless and lightweight, so GC pressure is minimal. Consistent with established Dapper pattern. |
| `EnsureUsing` helper is duplicated in all three code fix files | Low | Identical 6-line method copy-pasted. Could be extracted to a shared base class or utility. Consistent with Dapper code fix though. |
| Visibility: All new detectors, call sites, and chain steps are `internal`. Converters and their result types are `public`. Analyzers and code fixes are `internal`. | None | Matches Dapper pattern exactly. |
| Naming conventions follow established patterns: `XxxDetector`, `XxxCallSite`, `XxxConverter`, `XxxConversionEntry`, `XxxMigrationAnalyzer`, `XxxMigrationCodeFix` | None | Consistent throughout. |
| All analyzers follow the same `RegisterCompilationStartAction` -> type check -> `RegisterSyntaxNodeAction` pattern | None | SqlKata registers on `ObjectCreationExpression` instead of `InvocationExpression`, which is the correct node type for its detection entry point (`new Query()`). |

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Six new public types added to `Quarry.Migration` | Medium | `EfCoreConverter`, `EfCoreConversionEntry`, `EfCoreConversionDiagnostic`, `AdoNetConverter`, `AdoNetConversionEntry`, `AdoNetConversionDiagnostic`, `SqlKataConverter`, `SqlKataConversionEntry`, `SqlKataConversionDiagnostic` (9 total). These become part of the public API surface. The entry/diagnostic types are simple DTOs without interfaces, which limits future refactoring flexibility. Consider whether a shared `IConversionEntry` interface or a common base would reduce duplication and make the API more uniform. |
| No new NuGet package dependencies | None | EF Core, ADO.NET, and SqlKata are detected via Roslyn semantic model using type metadata names. No actual references to those packages are needed. This is the correct approach -- avoids pulling in framework dependencies into the analyzer package. |
| Diagnostic IDs QRM011-QRM033 are now registered | Low | These are new analyzer diagnostics. Consumers who filter or suppress by ID prefix (`QRM0*`) will automatically include these. No breaking change, but downstream documentation should be updated. |
| `MigrationDiagnosticDescriptors` adds 9 new fields | None | Purely additive. Existing Dapper descriptors (QRM001-003) unchanged. |
| ADO.NET converter takes a dependency on `DapperCallSite` and `DapperMigrationAnalyzer.TryParseWithFallback` | Low | The ADO.NET converter creates adapter `DapperCallSite` instances to reuse the `ChainEmitter` pipeline. This couples ADO.NET conversion to the Dapper infrastructure. If Dapper types change, ADO.NET conversion breaks. A shared interface or adapter pattern would be more resilient, but for now the internal coupling is acceptable since both live in the same assembly. |

## Classifications
| # | Section | Finding | Severity | Class | Action Taken |
|---|---------|---------|----------|-------|-------------|
| 1 | Plan Compliance | ChainExpression name differs from plan | Low | D | Ignored — improvement |
| 2 | Plan Compliance | ChainStep as top-level class | Low | D | Ignored — consistent |
| 3 | Plan Compliance | ADO.NET TODO as leading trivia | Low | D | Ignored — functionally correct |
| 4 | Correctness | GroupJoin in ChainMethods no converter case | Medium | A | Moved to UnsupportedEfCoreMethods |
| 5 | Correctness | EF Core ReplaceParameter string-based | Medium | D | Acceptable for migration tool |
| 6 | Correctness | ADO.NET FindCommandTextAssignment top-level only | Medium | A | Changed to DescendantNodes |
| 7 | Correctness | ADO.NET returns first not last CommandText | Low | C | Tracked as #244 |
| 8 | Correctness | ADO.NET CollectParameterNames scans full block | Low | D | Acceptable |
| 9 | Correctness | SqlKata TryDetectSingle backward walk | Low | D | Mitigated by semantic check |
| 10 | Correctness | SqlKata converter missing Where variant cases | Low | A | Added explicit cases with diagnostics |
| 11 | Correctness | SqlKata EmitNullCheck silent no-op | Medium | A | Added fallback comment |
| 12 | Test Quality | No code fix tests | High | C | Tracked as #245 |
| 13 | Test Quality | Stub duplication | Low | D | Follows Dapper convention |
| 14 | Test Quality | No multi-unsupported EF Core chain test | Low | D | Single unsupported covered |
| 15 | Test Quality | No ADO.NET concatenation SQL test | Low | D | Covered by Dapper tests |
| 16 | Test Quality | No ADO.NET multi-CommandText test | Low | D | Edge case |
| 17 | Test Quality | No SqlKata ForPage test | Low | B | Added test |
| 18 | Test Quality | No SqlKata aggregate terminal tests | Low | B | Added AsSum and AsMax tests |
| 19 | Codebase | EF Core different schema resolution path | Medium | A | Centralized in SchemaMap.TryGetEntityByTypeName |
| 20 | Codebase | Analyzer detector allocation per node | Low | D | Follows Dapper pattern |
| 21 | Codebase | EnsureUsing duplicated | Low | D | Follows Dapper pattern |
| 22 | Integration | 9 new public types without shared interface | Medium | C | Tracked as #246 |
| 23 | Integration | ADO.NET depends on DapperCallSite adapter | Low | D | Internal coupling acceptable |

## Issues Created
- #244: ADO.NET converter: use last CommandText assignment before Execute call
- #245: Add code fix tests for EF Core, ADO.NET, and SqlKata converters
- #246: Add shared interface for migration converter result types
