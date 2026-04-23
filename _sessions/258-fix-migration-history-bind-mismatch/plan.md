# Plan: 258-fix-migration-history-bind-mismatch

## Summary

Fix the Npgsql-10 `08P01: bind message supplies 0 parameters, but prepared statement "" requires 8` failure in `MigrationRunner.InsertHistoryRowAsync` by making `SqlFormatting.GetParameterName` dialect-aware so the `DbParameter.ParameterName` we assign matches the `$N` / `@pN` placeholder emitted by `SqlFormatting.FormatParameter`. Upgrade the `Npgsql` test/tool package reference from `9.*` to `10.*` so the regression surface is actually exercised by CI.

## Key concepts

### Named vs positional parameter binding in ADO.NET
ADO.NET providers bind `DbParameter` objects to SQL placeholders in one of two ways:

- **Named** — the provider scans the SQL for a marker (e.g. `@p0`, `@name`) and looks up a `DbParameter` whose `ParameterName` equals that marker. SQLite (`Microsoft.Data.Sqlite`) and SQL Server work this way.
- **Positional** — the provider finds positional markers (e.g. `?` for MySql, `$1`…`$N` for PostgreSQL) and binds parameters in the order they were added to `DbCommand.Parameters`. `ParameterName` is, in theory, ignored.

Npgsql is a hybrid: it accepts `@name` markers (translating them to `$N` over the wire) *and* native `$N` markers. Historically (Npgsql 9) it was permissive — if the SQL used `$N` but parameters carried unrelated names like `@p0`, it silently bound them positionally. **Npgsql 10 tightened this**: when parameters have names that are not `$N` and the SQL uses `$N`, the parameters are not recognised and the Bind frame is sent with zero parameters. That produces the exact server-side error `08P01: bind message supplies 0 parameters, but prepared statement "" requires 8`.

### The mismatch in Quarry today
`src/Quarry.Shared/Sql/SqlFormatting.cs`:

```csharp
public static string FormatParameter(SqlDialect dialect, int index) => dialect switch
{
    SqlDialect.PostgreSQL => $"${index + 1}",   // $1, $2, ...
    SqlDialect.MySQL      => "?",
    _                     => $"@p{index}"        // @p0, @p1, ...
};

public static string GetParameterName(SqlDialect dialect, int index)
    => $"@p{index}";                             // ALWAYS @pN
```

For PostgreSQL these disagree (`$1` vs `@p0`). The generated runtime query code (`CarrierEmitter.EmitParamNameExpr`) already uses `"$N"` names for PostgreSQL via `Quarry.Internal.ParameterNames.Dollar`, so the generated query path is unaffected. The bug is confined to callers of `SqlFormatting.GetParameterName`, which are:

- `src/Quarry/Migration/MigrationRunner.cs:570` (`AddParameter`)
- `src/Quarry.Tool/Commands/MigrateCommands.cs:961` (`AddParameter`)

Both compose SQL from `FormatParameter` and then name their `DbParameter` via `GetParameterName` — on PostgreSQL the two do not match, which is exactly why every `InsertHistoryRowAsync`, `UpdateHistoryStatusAsync`, `DeleteHistoryRowAsync`, and `WarnLargeTablesAsync` call silently misbinds on Npgsql 10.

### The fix
Make `GetParameterName` dialect-aware and aligned with `FormatParameter`:

```csharp
public static string GetParameterName(SqlDialect dialect, int index) => dialect switch
{
    SqlDialect.PostgreSQL => $"${index + 1}",    // matches $N placeholder
    SqlDialect.MySQL      => $"@p{index}",        // placeholder is ? (positional); name is arbitrary, must be unique
    _                     => $"@p{index}"         // SQLite, SqlServer — matches @pN placeholder
};
```

For MySQL the placeholder is `?` and parameters bind positionally, so the name is never used for lookup; we keep `@pN` so each parameter has a stable, unique identifier (matches today's behaviour).

## Phases

### Phase 1 — Fix `SqlFormatting.GetParameterName` and update existing dialect tests
**Files:**
- `src/Quarry.Shared/Sql/SqlFormatting.cs` — update `GetParameterName` to the dialect-aware form above.
- `src/Quarry.Tests/DialectTests.cs` — update `GetParameterName_ReturnsNameForDbParameter` cases. The current cases assert PostgreSQL returns `@p0`/`@p5`; after the fix they should read:
  ```csharp
  [TestCase(SqlDialect.PostgreSQL, 0, "$1")]
  [TestCase(SqlDialect.PostgreSQL, 5, "$6")]
  ```
  SQLite/SqlServer/MySQL cases stay the same (`@pN`). Also add an invariant test:
  ```csharp
  [TestCase(SqlDialect.SQLite)]
  [TestCase(SqlDialect.PostgreSQL)]
  [TestCase(SqlDialect.SqlServer)]
  public void GetParameterName_MatchesFormatParameter_ForNamedDialects(SqlDialect dialectType)
  {
      var dialect = SqlDialectFactory.GetDialect(dialectType);
      for (int i = 0; i < 10; i++)
          Assert.That(SqlFormatting.GetParameterName(dialect, i),
                      Is.EqualTo(SqlFormatting.FormatParameter(dialect, i)),
                      $"ParameterName must equal the SQL placeholder for named-binding dialects (index {i})");
  }

  [Test]
  public void GetParameterName_IsUniquePerIndex_ForMySql()
  {
      var dialect = SqlDialectFactory.GetDialect(SqlDialect.MySQL);
      var names = Enumerable.Range(0, 10)
                            .Select(i => SqlFormatting.GetParameterName(dialect, i))
                            .ToArray();
      Assert.That(names.Distinct().Count(), Is.EqualTo(names.Length),
                  "Each MySQL parameter must have a unique ParameterName even though the ? placeholder is positional");
  }
  ```
  These two invariants would have caught the bug and will prevent the two helpers from drifting apart again.

**Tests expected to change:** existing `GetParameterName_ReturnsNameForDbParameter` PostgreSQL cases flip from `@p0`/`@p5` to `$1`/`$6`. Two new invariant tests added.

**Commit:** `Fix MigrationRunner parameter binding on Npgsql 10 (GH-258)`

### Phase 2 — Upgrade Npgsql to 10.* in test/tool projects
**Files:**
- `src/Quarry.Tests/Quarry.Tests.csproj` — `<PackageReference Include="Npgsql" Version="9.*" />` → `Version="10.*"`.
- `src/Quarry.Tool/Quarry.Tool.csproj` — same.

**Dependency:** must be after Phase 1 — otherwise `dotnet test` on the upgraded Npgsql would run against the still-broken helper and fail the suite on any path that actually talks to Npgsql (there are none today, but the dependency ordering is still the right discipline). Realistically the test projects don't exercise live PostgreSQL at all, so the upgrade is a plain metadata bump; still, we commit Phase 1 first so history reads sensibly (fix → upgrade).

**Tests expected to change:** none — this is a package version bump. If the build or test run produces any new warning / error from Npgsql 10 API changes, address in this phase.

**Commit:** `Upgrade Npgsql to 10.* in test and tool projects`

## Risks / things to watch
- Any other place in the repo that sets `DbParameter.ParameterName` directly to an `@pN`-style string for PostgreSQL would have the same bug. I have already audited the non-generated code (only two sites, both in scope) and the generated code (`CarrierEmitter`, which uses `ParameterNames.Dollar` for PostgreSQL and is already correct).
- Upgrading Npgsql 9 → 10 may surface API changes in `Quarry.Tool` or `Quarry.Tests`. Neither project uses Npgsql APIs directly today — both reference the package transitively for `Quarry.Shared.Scaffold.PostgreSqlIntrospector`-style scenarios — but we verify with a clean `dotnet build` and full `dotnet test` in Phase 2.
- No PostgreSQL integration test is added (per user decision). The fix is instead protected by the invariant test pairing `GetParameterName` with `FormatParameter`, which is what would have caught this bug in the first place.
