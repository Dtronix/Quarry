# Review: 185-migration-dapper

## Plan Compliance
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Roslyn version 4.14.0 used in Quarry.Migration and Quarry.Migration.Analyzers instead of plan-specified 5.0.0 | Low | Functionally correct (lower version is a stricter compatibility choice for netstandard2.0 analyzer packages), but deviates from plan. Existing Quarry.Analyzers uses 5.0.0. |
| `--apply` flag documented in CLI help but implementation prints "not yet implemented" | Medium | Plan Phase 8 specifies `--apply` to modify source files in place. This is stubbed but not functional, meaning the CLI convert command is read-only. |
| `--dry-run` flag from plan not implemented as an explicit option | Low | The default behavior is dry-run (report only), which matches plan intent. Just no explicit `--dry-run` flag. Acceptable since `--apply` being the opt-in action is the right UX. |
| Missing `ProjectReference` to `Quarry` in test project | Low | Plan Phase 1 specifies tests should reference `Quarry` for entity type references. Tests work without it by defining stub types inline, which is arguably better for isolation. |
| Code fix does not add `using` directives as plan Phase 7 specifies | Medium | Plan says "Add necessary using directives if missing." The code fix replaces syntax but never checks or adds using directives (e.g., `using Quarry;`). Generated chain code may not compile without manual `using` additions. |
| No tests for ConvertCommand or DapperConverter (Phase 8 test requirement) | Medium | Plan specifies testing the orchestration logic. The public `DapperConverter` facade and `ConvertCommand` have zero test coverage. |
| Missing test for NOT LIKE (Phase 6 test requirement) | Low | Plan lists `SELECT * FROM users WHERE name NOT LIKE '%test%'` as a required test case. Not present. The logic appears correct (parser wraps in SqlUnaryExpr(Not) which EmitUnary handles), but it is untested. |
| Missing test for unknown SQL function producing Sql.Raw fallback (Phase 6) | Low | Plan specifies testing SQL with an unknown function producing Sql.Raw fallback. Only CASE expression fallback is tested. |

## Correctness
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Dead code: qualified star branch in `EmitSelect` is unreachable | Medium | Lines 130-135 of `ChainEmitter.cs` check for `SqlStarColumn star && star.TableAlias != null`, but the preceding branch (line 123) already matches any `SqlStarColumn` and returns. A single `SELECT u.*` query will resolve to `_lambdaVars[0]` instead of the alias-resolved variable, which is wrong for multi-table queries where the alias refers to a non-primary table (e.g., `SELECT o.* FROM users u JOIN orders o ...` would emit `u` instead of `o`). |
| Analyzer hardcodes `SqlDialect.SQLite` for all parsing | Low | Both `DapperMigrationAnalyzer` and `DapperMigrationCodeFix` parse SQL with `SqlDialect.SQLite`. If the user's codebase targets PostgreSQL or SQL Server, dialect-specific syntax may fail to parse. The CLI command correctly accepts `--dialect` but the analyzer has no configuration mechanism. Acceptable for v1 since SQLite dialect is the most permissive parser. |
| `ChainEmitter` stores mutable state in instance fields | Low | `_tables`, `_lambdaVars`, and `_diagnostics` are instance-level. Each call to `Translate()` accumulates state, so a `ChainEmitter` instance cannot be reused across multiple translations. Code currently creates a new instance per translation, so this is not a bug, but the API is fragile. |
| Unused import `System.Collections.Concurrent` in `DapperMigrationAnalyzer.cs` | Trivial | Dead import, no functional impact. |

## Security
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No concerns. | | The tool operates on source code via Roslyn and does not handle user input, network I/O, or credentials. |

## Test Quality
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| 69 tests all pass, covering SchemaResolver, DapperDetector, ChainEmitter, and analyzer diagnostics | -- | Good baseline coverage. |
| No code fix tests | Medium | Plan Phase 7 specifies: "Code fix test: verify Dapper call replaced with correct Quarry chain" and "Code fix test: verify using directives added." Neither exists. The `DapperMigrationCodeFix` is entirely untested. |
| No integration test for the full pipeline (DapperConverter or ConvertCommand) | Medium | The public API surface (`DapperConverter.ConvertAll`) is untested. Tests only cover internal components in isolation. |
| ChainEmitter tests use `Does.Contain` rather than exact output matching | Low | Tests verify that output contains expected substrings rather than matching full output. This means subtle ordering or formatting regressions could slip through. Acceptable for a first version but should be tightened over time. |
| Analyzer tests duplicate the Dapper/Quarry stub code from DapperDetectorTests | Low | The `DapperStub` and `QuarryStub` strings are copy-pasted between test classes. Should be shared to reduce maintenance burden. |

## Codebase Consistency
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `DapperMigrationAnalyzer` and `DapperMigrationCodeFix` are `public sealed` while existing `QuarryQueryAnalyzer` is `internal sealed` | Low | The existing analyzer pattern uses `internal` visibility. The new analyzers use `public`. This is not technically wrong (Roslyn discovers analyzers by attribute, not visibility), but breaks consistency. |
| `Quarry.Migration.Analyzers.csproj` missing `BuildOutputTargetFolder` property | Medium | The existing `Quarry.Analyzers.csproj` sets `<BuildOutputTargetFolder>analyzers/dotnet/cs</BuildOutputTargetFolder>` which is required for proper NuGet analyzer packaging. The new project omits this, so the analyzer DLL will not be placed in the correct folder when packed as a NuGet. |
| Roslyn version mismatch: Migration projects use 4.14.0, rest of codebase uses 5.0.0 | Low | Could cause subtle API differences or type-forwarding issues. Not a build break today but a maintenance concern. |
| `SqlDialect` enum redefined in `Quarry.Migration/SqlDialect.cs` under `namespace Quarry` | Low | The file has a long explanatory comment about type conflicts. The approach works (exclude the shared one, define a local copy) but is fragile. If the shared `SqlDialect` enum gains new members, this copy must be manually updated. |

## Integration / Breaking Changes
| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `Quarry.Tool.csproj` gains a new `ProjectReference` to `Quarry.Migration` | Low | This adds the Migration assembly and its transitive Roslyn dependencies to the tool package. Increases package size. Not a breaking change, but should be noted for packaging. |
| New "convert" command added to Quarry.Tool CLI | -- | Non-breaking. Additive command. Help text updated. |
| No changes to existing public APIs or existing projects beyond `Quarry.sln` and `Quarry.Tool` | -- | Clean isolation. No risk of regression to existing functionality. |

## Classifications
| Finding | Section | Class | Action Taken |
|---------|---------|-------|--------------|
| Dead code in EmitSelect qualified star branch | Correctness | Bug | Flagged for fix |
| Missing BuildOutputTargetFolder in Analyzers csproj | Codebase Consistency | Config gap | Flagged for fix |
| Code fix does not add using directives | Plan Compliance | Missing feature | Flagged |
| No code fix tests | Test Quality | Coverage gap | Flagged |
| No DapperConverter/ConvertCommand tests | Test Quality | Coverage gap | Flagged |
| --apply not implemented | Plan Compliance | Stub | Flagged (acceptable if documented as future work) |
| Unused import in DapperMigrationAnalyzer | Correctness | Cleanup | Trivial |
| Analyzer uses public visibility | Codebase Consistency | Style drift | Flagged |
| Hardcoded SQLite dialect in analyzer | Correctness | Limitation | Acceptable for v1 |
| Missing NOT LIKE and unknown-function tests | Test Quality | Coverage gap | Low priority |

## Issues Created
None created. The two items recommended for immediate fix before merge:
1. Fix the unreachable qualified-star branch in `ChainEmitter.EmitSelect` (swap the order of the two `if` blocks so the more-specific qualified-star check comes first).
2. Add `<BuildOutputTargetFolder>analyzers/dotnet/cs</BuildOutputTargetFolder>` to `Quarry.Migration.Analyzers.csproj` for correct NuGet packaging.
