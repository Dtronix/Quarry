# Review: 258-fix-migration-history-bind-mismatch

## Classifications
| # | Section | Finding | Sev | Rec | Class | Action Taken |
|---|---------|---------|-----|-----|-------|--------------|
| 1 | Plan Compliance | Phase 1 & 2 landed as described | Info | D | D |  |
| 2 | Plan Compliance | Plan audit understates blast radius — generator has same bug class | Medium | B | B | workflow.md decisions updated with post-REVIEW correction superseding the original audit |
| 3 | Plan Compliance | BundleCommand csproj template still pins Npgsql 9.* | Low | C | A | Template bumped to Npgsql 10.* in BundleCommand.cs:470 |
| 4 | Correctness | Core fix is correct for the two in-scope callers | Info | D | D | — |
| 5 | Correctness | CarrierEmitter.cs:940 hard-codes `"@p{i}"` — entity insert will fail on Npgsql 10 | High | A | A | Route through FormatParamName(chain.Dialect, i); helper made internal |
| 6 | Correctness | TerminalBodyEmitter.cs:584 uses ParameterNames.AtP — batch insert will fail on Npgsql 10 | High | A | A | Switched on chain.Dialect: Dollar for PG, AtP otherwise |
| 7 | Correctness | QuarryContext.RawSql* hard-codes `@p{i}` — users writing native `$N` will trip on PG | Low | C | A | Kept `@pN` (matches documented contract on all dialects); doc + anchor comment strengthened |
| 8 | Correctness | PostgreSqlIntrospector uses bare `"schema"` names — relies on Npgsql normalisation | Low | D | D |  |
| 9 | Correctness | MySQL name divergence between SqlFormatting (`@pN`) and generator (`"?"`) — pre-existing | Low | D | D |  |
| 10 | Test Quality | Invariant test catches regression at the helper level | Info | D | D |  |
| 11 | Test Quality | MySQL uniqueness test does not pin `@p` shape | Info | D | D |  |
| 12 | Test Quality | Existing PG TestCase pins `$1`/`$6` exactly | Info | D | D |  |
| 13 | Test Quality | No invariant covers generator ParameterName sites | Medium | B | B | Added 3 generator snapshot tests in CarrierGenerationTests (PG entity-insert, SQLite entity-insert, PG batch-insert) |
| 14 | Test Quality | Npgsql 10 bump adds no runtime binding test | Medium | D | D |  |
| 15 | Codebase Consistency | Style consistent with sibling helpers | Info | D | D |  |
| 16 | Codebase Consistency | Generator has duplicate `FormatParamName` — pre-existing | Low | D | D |  |
| 17 | Codebase Consistency | Naming/errors consistent | Info | D | D |  |
| 18 | Integration | Shipping packages do not take Npgsql directly | Info | D | D |  |
| 19 | Integration | Quarry.Tool uses only minimal Npgsql APIs | Info | D | D |  |
| 20 | Integration | BundleCommand pins Npgsql 9 — duplicate of #3 | Low | C | A |  |
| 21 | Integration | Full suite green (3246/3246) | Info | D | D |  |


## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Phase 1 and Phase 2 both landed as described in `plan.md`. Commit `0498e29` updates `SqlFormatting.GetParameterName` to the dialect-aware form; commit `31c7577` bumps `Npgsql` from `9.*` to `10.*` in `Quarry.Tests.csproj` and `Quarry.Tool.csproj`. `DialectTests` gains the two invariant tests specified in plan.md §Phase 1 (`GetParameterName_MatchesFormatParameter_ForNamedDialects`, `GetParameterName_IsUniquePerIndex_ForMySql`) and the PostgreSQL TestCase values flip from `@p0`/`@p5` to `$1`/`$6` as specified. No scope creep, no files outside the declared set. | Info | Confirms the branch faithfully executes the recorded plan. |
| The plan (lines 32 and 106) asserts that the generated-code path is "already correct" because `CarrierEmitter` uses `ParameterNames.Dollar` for PostgreSQL. This audit is incomplete — see Correctness below. The plan understates the blast radius of the Npgsql-10 strict-bind change and does not call out the additional generator sites that hard-code `"@p{i}"`. | Medium | If the premise in the plan is wrong, the fix may be advertised as complete when in fact other PostgreSQL code paths are still broken on Npgsql 10. |
| The `BundleCommand` csproj template still emits `<PackageReference Include="Npgsql" Version="9.*" />` (`src/Quarry.Tool/Commands/BundleCommand.cs:470`) even though the tool's own csproj was bumped to 10.*. Not explicitly listed in plan.md. | Low | Generated bundles continue to pin Npgsql 9 for consumers. Not a correctness bug — the bundle calls the fixed `MigrationRunner.AddParameter`, so it works on either version — but it is inconsistent with the plan's intent to "exercise the strict Npgsql 10 binding behavior". |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The new `SqlFormatting.GetParameterName` now matches `SqlFormatting.FormatParameter` byte-for-byte on every named-binding dialect (PostgreSQL → `$N`, SQLite/SqlServer → `@pN`), which precisely closes the reported `08P01` failure. MySQL keeps `@pN` so names remain unique; since the placeholder is positional `?`, name collisions would not matter anyway, but uniqueness is a harmless strengthening. The patch is correct for the two callers in scope (`MigrationRunner.AddParameter` at `src/Quarry/Migration/MigrationRunner.cs:567` and `MigrateCommands.AddParameter` at `src/Quarry.Tool/Commands/MigrateCommands.cs:958`). | Info | Core fix is verified correct for the reported scenario. |
| `Quarry.Generator.CodeGen.CarrierEmitter.EmitCarrierInsertTerminal` (`src/Quarry.Generator/CodeGen/CarrierEmitter.cs:940`) emits `__p{i}.ParameterName = "@p{i}"` unconditionally, with no dialect switch. The SQL that path runs against is produced by `SqlAssembler.RenderInsertSql` (`src/Quarry.Generator/IR/SqlAssembler.cs:890`), which on PostgreSQL renders `$1, $2, ...` via `SqlFormatting.FormatParameter`. This is structurally the same bug as the migration issue, for every generated entity `Insert(...).ExecuteNonQueryAsync()` / `ExecuteScalarAsync<T>()` on PostgreSQL — and it is NOT fixed by this PR. Plan.md line 106 claimed the generator was already correct; this is a counterexample. | High | Entity inserts on Npgsql 10 will hit the same `08P01: bind message supplies 0 parameters` error. If Quarry is used to insert entities into PostgreSQL via the generated fast path, this is a production break that the fix does not close. |
| `Quarry.Generator.CodeGen.TerminalBodyEmitter.EmitBatchInsert…` (`src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs:584`) emits `__p.ParameterName = Quarry.Internal.ParameterNames.AtP(__paramIdx)` unconditionally. The runtime `BatchInsertSqlBuilder.Build` (`src/Quarry/Internal/BatchInsertSqlBuilder.cs:62`) uses `SqlFormatting.FormatParameter` which on PostgreSQL produces `$N`. Same class of bug as above, for batch inserts. Not covered by this PR. | High | Batch inserts on Npgsql 10 will fail with `08P01`. |
| `src/Quarry/Context/QuarryContext.cs` hard-codes `param.ParameterName = $"@p{i}"` at lines 269, 332, 384, 419, 457 for the `RawSql*` methods. These APIs accept user-supplied SQL so the placeholder form is the caller's choice; the documented contract (XML docs around line 200) tells users to write `@p0, @p1, ...`. On Npgsql 9 the `@pN` name in ParameterName matched the user's `@pN` placeholder; on Npgsql 10 with strict binding, user SQL containing `@pN` still works because Npgsql still translates `@name` markers, but if a user supplied `$1` placeholders (common for experienced PG users) they would hit the same mismatch. Out of scope per plan, but worth flagging. | Low | Documented convention keeps this path safe by default, but users who write native `$N` SQL will be surprised on Npgsql 10. Consider a follow-up. |
| `PostgreSqlIntrospector` binds parameters with `AddParameter(cmd, "schema", schema)` (no `@` and no `$`) while the SQL uses `@schema` markers (`src/Quarry.Shared/Scaffold/PostgreSqlIntrospector.cs:22`). This relies on Npgsql's name-normalisation tolerance, which historically stripped `@` and compared. Npgsql 10 release notes (checked via the project's current behaviour; the introspector tests do not hit a live PG) retain this compatibility, but the invariant is not enforced by any test. | Low | Undocumented reliance on provider-specific name-normalisation. Not a regression of this PR, but a latent risk now that the test project binds Npgsql 10. |
| MySQL parameter naming divergence between `SqlFormatting.GetParameterName` (post-fix → `@p{i}`) and the generator's `CarrierEmitter.FormatParamName`/`EmitParamNameExpr` which emit the literal string `"?"` for every MySQL parameter (`src/Quarry.Generator/CodeGen/CarrierEmitter.cs:1276`, `1288`). Since MySqlConnector binds positionally, neither is broken, but the codebase now has two incompatible conventions for "MySQL parameter name". | Low | Pre-existing inconsistency amplified by this PR's explicit decision to keep `@pN` for MySQL. Worth a future alignment but not blocking. |

## Security

No concerns.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `GetParameterName_MatchesFormatParameter_ForNamedDialects` is exactly the invariant that would have caught the original bug, and it covers SQLite, PostgreSQL, and SqlServer for indices 0–9. It will correctly fail if anyone edits either helper without updating the other. Strong regression guard. | Info | Solid protection for the specific fix. |
| `GetParameterName_IsUniquePerIndex_ForMySql` enforces uniqueness but does not pin the `@pN` shape. A future refactor that returns, e.g., `"p"+i` would still satisfy the invariant even though it would change observable behaviour. Minor. | Info | Consider strengthening to also assert the name starts with `@p` or equal the existing `GetParameterName` contract. |
| The existing `GetParameterName_ReturnsNameForDbParameter` TestCase pair `[TestCase(SqlDialect.PostgreSQL, 0, "$1")]` / `[TestCase(SqlDialect.PostgreSQL, 5, "$6")]` pins the PG output exactly. Good coverage overlap with the new invariant. | Info | — |
| No live Npgsql integration test is added. The plan explicitly documents this decision ("no Docker dependency in CI"). The unit-test invariant `GetParameterName == FormatParameter` is a reasonable substitute because it protects against the specific regression, but it does NOT protect against future callers that set `ParameterName` to a hard-coded `"@p{i}"` literal without going through `SqlFormatting.GetParameterName`. Per the Correctness section, at least two such callers already exist (`EmitCarrierInsertTerminal`, `BatchInsert` terminal) and are unprotected by these invariants. | Medium | A future refactor that consolidates all ParameterName sites through `SqlFormatting.GetParameterName` would extend the invariant's coverage. As written, the new tests only protect the one helper being fixed. |
| The Npgsql 10 package bump does not add any test that actually exercises Npgsql 10 binding behaviour. There are no live PostgreSQL tests in `Quarry.Tests` or `Quarry.Migration.Tests` (confirmed by searching for `Testcontainer`, `NpgsqlConnection`, `PostgreSql` test harnesses — only string-match BundleCommand tests exist). So "Phase 2 ensures CI exercises the regression surface" in the plan is partly aspirational: the upgrade catches Npgsql 10 compile-time API changes but cannot catch runtime bind-frame regressions. | Medium | Without a live-PG test (or a well-placed mock verifying that `cmd.Parameters[i].ParameterName == SqlFormatting.FormatParameter(dialect, i)` during an actual `InsertHistoryRowAsync` call), a future reintroduction of the bug — for instance by someone writing a new `AddParameter` helper that bypasses `SqlFormatting.GetParameterName` — will not be caught. Not blocking given the plan's explicit choice, but worth stating. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The new switch expression in `GetParameterName` matches the style of sibling helpers in `SqlFormatting.cs` (see `FormatParameter`, `GetIdentifierQuoteChars`, `FormatBoolean`, `FormatBinaryLiteral`). The `[MethodImpl(MethodImplOptions.AggressiveInlining)]` attribute is preserved. Comments are explanatory and in-place. | Info | Cleanly consistent. |
| The generator has a near-duplicate `FormatParamName(SqlDialect, int)` at `src/Quarry.Generator/CodeGen/CarrierEmitter.cs:1271` that has always been dialect-aware (PostgreSQL → `$N+1`, MySQL → `"?"`, else `@pN`). This is close to but not the same as the new `SqlFormatting.GetParameterName`: the generator version returns `"?"` for MySQL, while the runtime returns `@p{i}`. Two codebases, two conventions. | Low | Pre-existing but now more visibly inconsistent. A future "share the helper between runtime and generator via the `QUARRY_GENERATOR` conditional" refactor would be cleaner. |
| Naming and error-handling conventions in the patch are consistent with the surrounding file — no new exceptions thrown, no new allocations introduced. The inline-switch form matches sibling helpers. | Info | — |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `Npgsql` is upgraded only in `Quarry.Tests` and `Quarry.Tool` — both are non-shipped projects (test suite and CLI tool). The shipping packages (`Quarry`, `Quarry.Migration`, `Quarry.Shared`, `Quarry.Analyzers`, `Quarry.Generator`) do not take a direct `Npgsql` dependency, so there is no public API surface change and no forced consumer upgrade. | Info | Safe to land; consumers on Npgsql 9 or 10 both continue to work. |
| `Quarry.Tool` ships as a global tool (`Quarry.Tool` package per csproj). Upgrading its Npgsql from 9 to 10 means the CLI will bind the Npgsql 10 runtime when the `scaffold --provider postgres` or `migrate --provider postgres` commands run against a live database. Npgsql 10's breaking changes (strict binding, removed deprecated members, `NpgsqlConnection` lifecycle changes per the 10.0 release notes) could surface if the tool uses advanced Npgsql APIs. Grepping `Quarry.Tool` for `Npgsql` shows only two usages: `new Npgsql.NpgsqlConnectionStringBuilder` (ScaffoldCommand:332) and `new Npgsql.NpgsqlConnection(connectionString)` (MigrateCommands:883). Both are minimal surface APIs preserved across 9 → 10. Build succeeds clean. | Info | No break observed. |
| `Quarry.Tool.BundleCommand` emits a csproj that pins `Npgsql` Version=`9.*` (`src/Quarry.Tool/Commands/BundleCommand.cs:470`). The emitted bundle still uses the fixed `MigrationRunner`, so it works on either Npgsql 9 or 10. But the pin is inconsistent with the rest of the upgrade: a bundle scaffolded today will install Npgsql 9 by default, despite the broader project moving to 10. Arguably an oversight rather than a break. | Low | Worth considering a companion update of the bundle template to `10.*` (or leaving it unpinned) so users do not get a stale Npgsql in new bundles. |
| Full test suite runs green after the change (3246 total passing: Quarry.Tests 2942, Quarry.Migration.Tests 201, Quarry.Analyzers.Tests 103 — +4 vs. baseline which is the two new invariant tests plus the two refactored `TestCase` rows running as separate cases). No transitive package conflicts observed during build; the only warnings are the pre-existing NU1903 on `System.Security.Cryptography.Xml` 9.0.0 flagged in the workflow baseline. | Info | Integration is clean. |
