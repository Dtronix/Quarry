## Summary
- Closes #258
- Aligns `DbParameter.ParameterName` with the SQL placeholder emitted by `SqlFormatting.FormatParameter` on every dialect, so Npgsql 10's strict name-based binding accepts the command.
- Upgrades `Npgsql` to `10.*` in the test and tool projects (and the `BundleCommand` csproj template) so the regression surface is actually exercised by CI and by newly scaffolded bundles.

## Reason for Change
On Npgsql 10 + PostgreSQL 17, `Quarry.Migration.MigrationRunner.InsertHistoryRowAsync` fails with:

```
08P01: bind message supplies 0 parameters, but prepared statement "" requires 8
```

Root cause: `SqlFormatting.GetParameterName` always returned `@p{index}`, but `SqlFormatting.FormatParameter` renders `${index+1}` for PostgreSQL. Npgsql 9 was lax and still bound the parameters positionally; Npgsql 10 strictly matches parameters by name, so `@p0..@p7` names do not match `$1..$8` placeholders and the Bind frame is sent with zero parameters.

The same class of mismatch existed in three other places:
- `CarrierEmitter.EmitCarrierInsertTerminal` hard-coded `"@p{i}"` for entity-insert parameters → every generated `Insert(entity).Execute*Async()` on PostgreSQL would have failed the same way.
- `TerminalBodyEmitter` batch-insert terminal used `ParameterNames.AtP(__paramIdx)` unconditionally → every `InsertBatch(...).Values(...).Execute*Async()` on PostgreSQL would have failed the same way.
- `BundleCommand` csproj template pinned `Npgsql 9.*`, so newly scaffolded bundles would not have matched the repo-wide upgrade.

## Impact
- `MigrationRunner` history writes (and `MigrateCommands` equivalents) now succeed on Npgsql 10.
- Generated entity-insert and batch-insert code paths now emit `ParameterName = "$N"` on PostgreSQL and the existing `@pN` on SQLite/SQL Server; MySQL keeps positional `?` / unique names.
- Freshly scaffolded bundles (`quarry bundle`) install `Npgsql 10.*`.

## Plan items implemented as specified
- **Phase 1** — `SqlFormatting.GetParameterName` switched on dialect: PostgreSQL → `$N+1`, SQLite/SqlServer → `@pN`, MySQL → `@pN` (unique per index even though the placeholder is positional `?`). `DialectTests.GetParameterName_ReturnsNameForDbParameter` PostgreSQL TestCases flip from `@p0`/`@p5` to `$1`/`$6`. Two new invariants added: `GetParameterName_MatchesFormatParameter_ForNamedDialects` and `GetParameterName_IsUniquePerIndex_ForMySql`.
- **Phase 2** — `Npgsql` bumped from `9.*` to `10.*` in `Quarry.Tests.csproj` and `Quarry.Tool.csproj`.

## Deviations from plan implemented
None — plan phases landed as written.

## Gaps in original plan implemented
REVIEW discovered the plan's audit of the generator ("already correct") was incomplete. Post-review fixes:
- `CarrierEmitter.cs` — `EmitCarrierInsertTerminal` now routes through `FormatParamName(chain.Dialect, i)` (helper promoted to `internal`) instead of hard-coding `"@p{i}"`.
- `TerminalBodyEmitter.cs` — batch-insert parameter naming switches on `chain.Dialect`: `ParameterNames.Dollar(__paramIdx)` for PostgreSQL, `ParameterNames.AtP(__paramIdx)` otherwise.
- `BundleCommand.cs` — csproj template now emits `Npgsql 10.*`.
- `QuarryContext.cs` — `RawSqlAsync<T>` XML doc tightened and anchor comment added explaining why `@pN` is the correct name on every dialect (including PostgreSQL, where Npgsql translates `@name` internally) and why a dialect-aware `$N` name here would break the documented `@pN` SQL convention.
- Added three generator snapshot tests in `CarrierGenerationTests` covering PG entity-insert (`$1`/`$2`), SQLite entity-insert (`@p0`/`@p1`), and PG batch-insert (`ParameterNames.Dollar(__paramIdx)`). These cover the generator paths that the SqlFormatting-level invariant cannot reach.

## Migration Steps
None. Consumers do not need to change anything. Callers on Npgsql 9 continue to work; callers on Npgsql 10 now work too.

## Performance Considerations
No hot-path changes. The new PostgreSQL `ParameterName` value is a short string switch identical in cost to the old one; the generator emits exactly the same number of assignments and uses the same `ParameterNames.*` lookup table.

## Security Considerations
No new surface. All changes operate on `DbParameter.ParameterName` (metadata only); no SQL text construction changed; no user-supplied data newly flows into the parameter layer.

## Breaking Changes
- **Consumer-facing:** None. The `ParameterName` assigned by the runtime is an implementation detail; user SQL and user-supplied parameter values are unchanged. Users who were already following the documented `RawSql` `@pN` convention continue to work on every dialect.
- **Internal:** `CarrierEmitter.FormatParamName` and `CarrierEmitter.EmitParamNameExpr` are now `internal static` (were `private static`). No external consumers reference the generator's internals, so this is not a surface break.

## Test Results
3249/3249 tests passing (Quarry.Tests 2945, Quarry.Migration.Tests 201, Quarry.Analyzers.Tests 103). The +7 versus baseline are the two PG-flipped `GetParameterName_ReturnsNameForDbParameter` cases, two new `DialectTests` invariants, and three new `CarrierGenerationTests` snapshot tests.
