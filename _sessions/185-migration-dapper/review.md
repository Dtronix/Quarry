# Review: 185-migration-dapper (Re-review)

Reviewer ran all 76 migration tests (pass), builds clean (0 warnings) for both Quarry.Migration and Quarry.Migration.Analyzers.

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| All 8 plan phases implemented: project scaffolding, SchemaResolver, DapperDetector, ChainEmitter core, JOINs/aggregates/ORDER BY/LIMIT, parameters/edge cases, Roslyn analyzer+code fix, CLI convert command. | N/A (pass) | Full plan coverage. |
| Plan specifies `Microsoft.CodeAnalysis.CSharp` 5.0.0 for Quarry.Migration; actual is 4.14.0. | Low | The csproj uses 4.14.0 instead of 5.0.0. This builds and works fine but diverges from the plan. See Codebase Consistency. |
| Plan specifies `QUARRY_GENERATOR` conditional; actual uses `QUARRY_MIGRATION`. | Low | Different define name is fine -- it avoids colliding with the generator define. Reasonable deviation. |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `--apply` flag in `ConvertCommand` is a no-op: it reads the file, iterates entries, logs "Applied" messages, but **never writes the modified content back**. Lines 128-156 read the file, split lines, set a `modified` flag, but never call `File.WriteAllText` or equivalent. | High | Users running `quarry convert --from dapper --apply` will see "Applied" and "Done" messages but their files will be unchanged. This is misleading. Either implement the write or remove the `--apply` flag and document that only the IDE code fix (QRM001) can apply changes. |
| `DapperDetector.ExtractSqlString` has a dead code path at lines 149-151. The first `if` (line 145) already catches all `StringLiteralExpression` nodes. The second `if` (line 149) re-checks the same variable with the same condition and additionally checks `Utf8StringLiteralExpression`, but `verbatim` is the same `sqlArg` cast that already matched above. | Low | Dead code; the Utf8 string literal branch is unreachable because the first branch already returns. Not a bug (Dapper won't accept Utf8 string literals for SQL), but confusing. |
| `DapperMigrationAnalyzer.AnalyzeInvocation` hardcodes `SqlDialect.SQLite` (line 55). If the user's project targets PostgreSQL or SQL Server, dialect-specific syntax may fail to parse. | Medium | The analyzer cannot detect the correct dialect from context. The CLI `ConvertCommand` accepts `--dialect` but the analyzer has no equivalent configuration. This should at minimum be documented, or the analyzer should try multiple dialects on parse failure. |
| `DapperMigrationCodeFix` also hardcodes `SqlDialect.SQLite` (line 62). Same issue as above. | Medium | Code fix will produce incorrect conversions if the user's SQL uses dialect-specific syntax (e.g., SQL Server `TOP` instead of `LIMIT`). |
| `DapperMigrationCodeFix.ConvertToQuarryAsync` calls `detector.Detect(semanticModel, invocation)` passing the single `invocation` node as the root (line 52). This works because `Detect` walks `DescendantNodes()` and the invocation itself is a descendant of itself. However, it could also match nested invocations within the arguments. | Low | Unlikely to cause issues in practice since Dapper calls don't typically nest, but `TryDetectSingle(model, invocation)` would be more precise here. |
| `SELECT DISTINCT` is silently ignored -- `IsDistinct` flag is never checked. | Low | Not in scope per the plan (plan doesn't mention DISTINCT). Acceptable for v1, but a diagnostic warning when encountering DISTINCT would improve UX. |
| `ChainEmitter.DeriveVariable` collision logic: if the one-letter variable collides and the two-letter candidate also collides, digit appending starts at 2 (e.g., `u2`, `u3`). Skipping `u1` is a minor aesthetic issue. | Low | The pattern `u`, `us`, `u2`, `u3` is slightly inconsistent. Not a bug. |

## Security

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No concerns. | N/A | The tool operates on source code only; it does not execute SQL or connect to databases. String escaping in `EscapeString` handles `\` and `"` correctly for C# string literals. |

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| 76 tests covering SchemaResolver (17), DapperDetector (13), ChainEmitter (28), DapperConverter (5), DapperMigrationAnalyzer (3). Good breadth. | N/A (pass) | Solid coverage of the core pipeline. |
| No test for `RIGHT JOIN`, `CROSS JOIN`, or `FULL OUTER JOIN` emission even though `EmitJoin` handles them. | Medium | These join kinds are implemented in ChainEmitter (lines 360-365) but untested. A regression could go unnoticed. |
| No test for `QuerySingleOrDefaultAsync` terminal mapping despite it being listed in `DapperMethods`. | Low | The `TerminalMapping_AllVariants` test covers `QuerySingleOrDefaultAsync` indirectly via `MapTerminal` but not through the full pipeline. Minor gap. |
| No test for multiple qualified star columns in SELECT (e.g., `SELECT u.*, o.*`). | Low | The `EmitSelect` logic handles `SqlStarColumn` in the multi-column branch (lines 145-149) but this path is untested. |
| `ChainEmitterTests.FakeCallSite` passes `null!` for `invocationSyntax` (line 71). This is fine for unit tests but means `InvocationSyntax` is never null-checked in production code paths that receive these test call sites. | Low | Not a real issue since `InvocationSyntax` is only used by the code fix, which always has a real syntax node. |
| No test for the `DapperMigrationCodeFix` end-to-end (applying the code fix to a document). | Medium | Analyzer tests verify diagnostic reporting but not the code fix replacement. A bug in `ConvertToQuarryAsync` (e.g., the `await` expression handling, `using` directive insertion) would not be caught. |
| `ConvertCommand` (`--apply`, `--dry-run`) has no automated tests. | Medium | Plan Phase 8 mentions testing the orchestration logic. The `DapperConverterTests` cover the public API but not the CLI command itself. The no-op `--apply` bug (see Correctness) would have been caught with a test. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Roslyn package version mismatch: `Quarry.Migration` and `Quarry.Migration.Analyzers` use `Microsoft.CodeAnalysis.CSharp` 4.14.0, while the rest of the codebase (`Quarry.Analyzers`, `Quarry.Generator`, `Quarry.Analyzers.CodeFixes`) all use 5.0.0. | Medium | Inconsistent Roslyn versions across analyzer packages in the same solution. Could cause confusing version conflicts when both analyzer packages are loaded in the same IDE session. Should align to 5.0.0 for consistency. |
| `Quarry.Migration.Analyzers` does not set `SuppressDependenciesWhenPacking` like `Quarry.Migration` and the existing `Quarry.Analyzers` do. | Low | When packed as a NuGet, dependencies would be included, potentially causing version conflicts for consumers. |
| `DapperMigrationAnalyzer` is `internal sealed` while the existing `QuarryQueryAnalyzer` is also `internal sealed`. Consistent. | N/A (pass) | Good. |
| `DapperMigrationCodeFix` is `internal sealed` while `RawSqlMigrationCodeFix` in the existing codebase is `internal sealed`. Consistent. | N/A (pass) | Good. |
| File-scoped namespaces used throughout -- consistent with existing codebase style. | N/A (pass) | Good. |
| `DapperConverter` is the only `public` class in `Quarry.Migration`; all others are `internal`. Clean API surface. | N/A (pass) | Good design. |
| NUnit test style matches existing tests (`[TestFixture]`, `[Test]`, `Assert.That` with constraint model). | N/A (pass) | Consistent. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `Quarry.Tool.csproj` gains a `ProjectReference` to `Quarry.Migration`. Since `Quarry.Migration` imports `Quarry.Shared.projitems` (shared SQL parser), and `Quarry.Tool` also imports `Quarry.Shared.projitems`, there could be duplicate compilation of shared types. The Migration csproj correctly excludes `SqlDialect.cs` from Shared and defines its own internal copy to avoid type conflicts. The Tool csproj excludes `Sql/**` entirely. | Low | The type isolation is correct. The Tool references Migration via `ProjectReference`, so it uses Migration's compiled types. No conflict. This is well-handled. |
| The `SqlDialect.cs` in `Quarry.Migration` defines `internal enum SqlDialect` in `namespace Quarry;`. This is visible to `Quarry.Migration.Analyzers` via `InternalsVisibleTo` but hidden from `Quarry.Tool` (which lacks IVT). The Tool uses the public `DapperConverter` API which accepts a `string?` dialect parameter. | N/A (pass) | Clean separation. The public API uses strings, internal API uses the enum. |
| New solution entries for 3 projects. All configured with Debug/Release for all platform targets (Any CPU, x64, x86). | N/A (pass) | Standard configuration. |
| No changes to existing source files except `Quarry.sln` (new project entries), `Quarry.Tool/Program.cs` (new command dispatch), and `Quarry.Tool/Quarry.Tool.csproj` (new project reference). | N/A (pass) | Minimal footprint on existing code. |

## Classifications

| Finding | Section | Class | Action Taken |
|---------|---------|-------|--------------|
| `--apply` flag is a no-op | Correctness | Bug | Logged as High severity finding |
| Hardcoded SQLite dialect in analyzer/code fix | Correctness | Design limitation | Logged as Medium severity finding |
| Dead code in `ExtractSqlString` | Correctness | Dead code | Logged as Low |
| Missing RIGHT/CROSS/FULL OUTER JOIN tests | Test Quality | Coverage gap | Logged as Medium |
| Missing code fix end-to-end test | Test Quality | Coverage gap | Logged as Medium |
| Missing ConvertCommand tests | Test Quality | Coverage gap | Logged as Medium |
| Roslyn version mismatch (4.14.0 vs 5.0.0) | Codebase Consistency | Inconsistency | Logged as Medium |
| Missing `SuppressDependenciesWhenPacking` in Analyzers csproj | Codebase Consistency | Packaging | Logged as Low |
| SELECT DISTINCT silently ignored | Correctness | Design limitation | Logged as Low |
| `detector.Detect` used instead of `TryDetectSingle` in code fix | Correctness | Minor imprecision | Logged as Low |

## Issues Created

None yet. The `--apply` no-op bug (High) should be addressed before merge -- either implement the file-write logic or remove the flag and document that only the IDE code fix applies changes. The remaining Medium findings are acceptable for a v1 but should be tracked for follow-up.
