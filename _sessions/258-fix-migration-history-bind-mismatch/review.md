# Review: #261

## Classifications
| # | Section | Finding | Sev | Rec | Class | Action Taken |
|---|---------|---------|-----|-----|-------|--------------|
| 1 | Plan Compliance | Phase 1 & 2 complete and correct | Info | D | D |  |
| 2 | Plan Compliance | Post-REVIEW generator fixes correctly supersede understated plan audit | Info | D | D |  |
| 3 | Correctness | Core fix: GetParameterName dialect-aware for PostgreSQL | Info | D | D |  |
| 4 | Correctness | CarrierEmitter.EmitCarrierInsertTerminal routed through FormatParamName | Info | D | D |  |
| 5 | Correctness | TerminalBodyEmitter batch-insert switched on dialect for ParameterName | Info | D | D |  |
| 6 | Correctness | QuarryContext.RawSql* retains @pN contract with strengthened doc | Info | D | D |  |
| 7 | Correctness | BundleCommand template bumped to Npgsql 10.* | Info | D | D |  |
| 8 | Correctness | FormatParamName helper made internal and documented | Info | D | D |  |
| 9 | Correctness | EmitParamNameExpr helper made internal | Info | D | D |  |
| 10 | Test Quality | GetParameterName_MatchesFormatParameter_ForNamedDialects catches regression | Info | D | D |  |
| 11 | Test Quality | GetParameterName_IsUniquePerIndex_ForMySql verifies MySQL uniqueness | Info | D | D |  |
| 12 | Test Quality | Entity insert PG snapshot test verifies $N naming | Info | D | D |  |
| 13 | Test Quality | Entity insert SQLite snapshot test verifies @pN naming | Info | D | D |  |
| 14 | Test Quality | Batch insert PG snapshot test verifies ParameterNames.Dollar | Info | D | D |  |
| 15 | Test Quality | No live Npgsql 10 integration/bind-frame test; plan-accepted limitation | Medium | D | D |  |
| 16 | Security | No new surface; parameter metadata only | Info | D | D |  |
| 17 | Codebase Consistency | GetParameterName matches sibling helper style | Info | D | D |  |
| 18 | Codebase Consistency | Generator/runtime MySQL convention divergence (pre-existing) | Low | D | D |  |
| 19 | Codebase Consistency | Helper visibility changes accompanied by clear docstrings | Info | D | D |  |
| 20 | Integration | Npgsql 10 bump safe: no shipped packages depend on it directly | Info | D | D |  |
| 21 | Integration | Quarry.Tool Npgsql 10 API usage surface minimal and compatible | Info | D | D |  |
| 22 | Integration | BundleCommand template upgrade to Npgsql 10.* consistent with repo | Info | D | D |  |
| 23 | Integration | Test suite green: 3249/3249 (baseline 3242 + 7 new tests) | Info | D | D |  |

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| Phase 1 and Phase 2 both executed as specified in plan.md. Commit `0498e29` updates `SqlFormatting.GetParameterName` to a dialect-aware switch (PostgreSQL → `$N`, MySQL/SQLite/SqlServer → `@pN`). Commit `31c7577` upgrades `Npgsql` from `9.*` to `10.*` in `Quarry.Tests.csproj` and `Quarry.Tool.csproj`. `DialectTests` gains two new invariant tests (`GetParameterName_MatchesFormatParameter_ForNamedDialects` for SQLite/PostgreSQL/SqlServer, `GetParameterName_IsUniquePerIndex_ForMySql`), and PostgreSQL test cases flip from `@p0`/`@p5` to `$1`/`$6`. No scope creep, no files outside the declared set. | Info | The base implementation tracks the declared plan precisely. |
| The plan's original audit (lines 32, 106) claimed the generator was "already correct" via `CarrierEmitter`'s use of `ParameterNames.Dollar` for PostgreSQL. Post-REVIEW, this audit proved incomplete: the plan failed to identify `EmitCarrierInsertTerminal` (line 940) hard-coding `"@p{i}"` and `TerminalBodyEmitter` batch-insert (line 584) using `ParameterNames.AtP` unconditionally. Commit `06e25fe` (REMEDIATE phase) correctly fixed both sites and updated `workflow.md` with a "REVIEW correction" decision superseding the plan's audit scope. The superseding decision is explicitly recorded in `workflow.md` lines 36–46 and clearly marked as post-plan discovery. | Info | Session-1 REVIEW correctly identified and fixed a gap in the original plan's audit. Workflow documentation accurately reflects the correction. |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The new `SqlFormatting.GetParameterName` switches on dialect and returns `$N` (1-based) for PostgreSQL and `@pN` for all others (MySQL/SQLite/SqlServer). This exactly matches `SqlFormatting.FormatParameter`'s output on every dialect. The fix addresses the reported migration history `08P01: bind message supplies 0 parameters` error by ensuring `DbParameter.ParameterName` matches the SQL placeholder. Verified in place at `src/Quarry.Shared/Sql/SqlFormatting.cs:79–83`. | Info | Core fix is sound for the two originally scoped callers (`MigrationRunner.AddParameter`, `MigrateCommands.AddParameter`). |
| `CarrierEmitter.EmitCarrierInsertTerminal` at `src/Quarry.Generator/CodeGen/CarrierEmitter.cs:940` now routes through `FormatParamName(chain.Dialect, i)` instead of hard-coding `"@p{i}"`. The `FormatParamName` helper (lines 1273–1281) is dialect-aware (PostgreSQL → `$N+1`, others → `@pN`) and correctly matches the SQL placeholders rendered by `SqlAssembler.RenderInsertSql`. Generated entity inserts on PostgreSQL will now emit `ParameterName = "$1"`, `"$2"`, etc. | Info | Entity-insert path on Npgsql 10 + PostgreSQL is now unbroken. Generator snapshot test `CarrierGeneration_EntityInsert_EmitsDollarParameterNames_ForPostgreSQL` directly validates this output. |
| `TerminalBodyEmitter.EmitBatchInsert…` at `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs:576–585` now computes a dialect-aware `paramNameExpr` that selects `ParameterNames.Dollar(__paramIdx)` for PostgreSQL and `ParameterNames.AtP(__paramIdx)` otherwise. This matches the placeholder emitted by `BatchInsertSqlBuilder.Build` (which uses `SqlFormatting.FormatParameter`). Batch inserts on PostgreSQL will now bind correctly on Npgsql 10. Validated by snapshot test `CarrierGeneration_BatchInsert_UsesDollarParameterNames_ForPostgreSQL`. | Info | Batch-insert path on Npgsql 10 + PostgreSQL is now correct. |
| `QuarryContext.RawSql*` methods at lines 269, 332, 384, 419, 457 continue to hard-code `@pN` names. This is correct: the documented contract (XML docs lines 182–186 strengthened in REMEDIATE) tells callers to write `@p0, @p1, ...` in their SQL on every dialect, including PostgreSQL where Npgsql translates `@name` to positional internally. A new anchor comment at line 624 explains why switching to dialect-aware `$N` names would break existing code that follows the `@pN` contract. Decision recorded in `workflow.md` lines 40–41. | Info | RawSql contract is correctly preserved and newly documented to prevent future drift. |
| `BundleCommand` csproj template at line 470 now emits `Npgsql 10.*` instead of `9.*`. This aligns scaffolded bundles with the repo-wide Npgsql 10 upgrade. Generated bundles will install Npgsql 10 and leverage the fixed parameter-binding behavior. | Info | Consistency achieved: bundles now use the same Npgsql version as the repo that generated them. |
| `CarrierEmitter.FormatParamName` at line 1273 is now `internal` (previously `private`); the docstring at lines 1269–1271 correctly notes it must match `SqlFormatting.FormatParameter` output. `EmitParamNameExpr` at line 1287 is also `internal` (previously `private`). Both visibility changes are safe: the generator is an analyzer (compile-time only), and no external code references private generator internals. | Info | Visibility clarifications are appropriate for the promoted responsibilities. |

## Security

No concerns.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `DialectTests.GetParameterName_MatchesFormatParameter_ForNamedDialects` is an invariant test that verifies for 10 indices (0–9) on SQLite, PostgreSQL, and SqlServer that `GetParameterName(d, i) == FormatParameter(d, i)`. This is precisely the regression guard that would have caught the original bug and will prevent the two helpers from drifting in the future. | Info | Strong protection for the shared-helper fix. |
| `DialectTests.GetParameterName_IsUniquePerIndex_ForMySql` verifies that MySQL parameter names are unique across 10 indices. This correctly enforces the positional-binding contract where uniqueness is required even though the placeholder is positional `?`. | Info | MySQL-specific invariant is well-formed. |
| The existing `GetParameterName_ReturnsNameForDbParameter` PostgreSQL test cases pin output to `$1` and `$6` exactly. Combined with the new invariant test, helper-level coverage is comprehensive. | Info | Pinning the exact value prevents silent regressions. |
| Three new generator snapshot tests in `CarrierGenerationTests` directly validate generated `ParameterName` assignments: `CarrierGeneration_EntityInsert_EmitsDollarParameterNames_ForPostgreSQL` asserts generated PG entity-insert code contains `__p0.ParameterName = "$1"` and `__p1.ParameterName = "$2"` and does not contain `@pN`. `CarrierGeneration_EntityInsert_EmitsAtParameterNames_ForSQLite` validates SQLite entity inserts emit `@p0` / `@p1`. `CarrierGeneration_BatchInsert_UsesDollarParameterNames_ForPostgreSQL` verifies batch inserts on PG use `ParameterNames.Dollar(__paramIdx)` and not `ParameterNames.AtP`. | Info | Generator code paths are now protected by explicit snapshot tests. |
| No live Npgsql integration test against a PostgreSQL instance is added. The plan explicitly forbids Docker dependency in CI. The helper-level invariant `GetParameterName == FormatParameter` and the generator snapshot tests are a reasonable substitute for the specific fixes committed, but they do NOT defend against future developers writing new parameter-binding code outside these paths. Given the plan's explicit no-integration-test decision, this is accepted; the documented risk remains. | Medium | Test coverage is solid for committed code but limited for preventing new copies of the pattern outside guarded paths. Future refactors consolidating all `ParameterName` assignments through `SqlFormatting.GetParameterName` would extend the invariant's reach. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| The new switch expression in `SqlFormatting.GetParameterName` (lines 79–83) matches the style of sibling helpers in the same file (`FormatParameter`, `GetIdentifierQuoteChars`, `FormatBoolean`, `FormatBinaryLiteral`). `[MethodImpl(MethodImplOptions.AggressiveInlining)]` is preserved. Comments explain the dialect-aware logic clearly. | Info | Stylistically consistent with the codebase. |
| The generator has `CarrierEmitter.FormatParamName` at line 1273 which was already dialect-aware (PostgreSQL → `$N+1`, MySQL → `"?"`, else `@pN`) and is now `internal`. The runtime's `SqlFormatting.GetParameterName` is also dialect-aware but returns `@pN` for MySQL (not the literal `?` returned by the generator). This creates two slightly incompatible conventions for MySQL: the generator emits `"?"` as a compile-time constant (for `CommandText`), while the runtime helper returns `@pN` for `DbParameter.ParameterName` uniqueness. Both are correct (the generator path does not go through `SqlFormatting.GetParameterName`), but the dual conventions are noted. Pre-existing, not a regression. | Low | Pre-existing stylistic inconsistency amplified by this PR's explicit decision to keep `@pN` for MySQL runtime names. Worth a future alignment but not blocking. |
| Helper visibility changes (`FormatParamName` and `EmitParamNameExpr` from `private` to `internal`) are accompanied by clear docstrings explaining the constraint (must match `SqlFormatting.FormatParameter`). Naming and exception-handling conventions are unchanged. | Info | Consistency maintained. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `Npgsql` is upgraded only in `Quarry.Tests` and `Quarry.Tool` — both are non-shipped, development-time packages. The shipping packages (`Quarry`, `Quarry.Migration`, `Quarry.Shared`, `Quarry.Analyzers`, `Quarry.Generator`) do not take a direct `Npgsql` dependency, so no version constraint is imposed on consumers. Users on Npgsql 9 or 10 both continue to work. | Info | Integration is safe; no consumer breakage. |
| `Quarry.Tool` ships as a global CLI tool. It uses Npgsql minimally (`NpgsqlConnectionStringBuilder` and `NpgsqlConnection` constructors, both preserved across 9→10). The upgrade introduces no breaking Npgsql API changes for the tool. Build succeeds clean. | Info | No tool regression. |
| `BundleCommand` template upgrade from `Npgsql 9.*` to `10.*` means newly scaffolded bundles will install Npgsql 10. This is consistent with the repo's upgrade and allows bundles to run on modern Npgsql + PostgreSQL. | Info | Consistency achieved; no downside. |
| Test suite: 3249/3249 tests passing (Quarry.Tests 2945, Quarry.Migration.Tests 201, Quarry.Analyzers.Tests 103). +7 vs. the baseline (3242), attributable to the two `GetParameterName` test-case flips for PostgreSQL (running as separate cases), two new invariant tests, and three new generator snapshot tests. No regressions. Build is clean; only pre-existing NU1903 warnings on `System.Security.Cryptography.Xml` 9.0.0 persist. | Info | Integration is verified green. |

## Issues Created

None. All REVIEW findings in session 1 were addressed in the REMEDIATE phase (commit `06e25fe`). This session-2 second-opinion sweep finds no new actionable items.
