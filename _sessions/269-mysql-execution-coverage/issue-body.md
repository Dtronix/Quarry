## Description

Quarry's generator threads a flat `SqlDialect` enum through every emit path. This works for branching SQL syntax by dialect family (PG/SQLite/MySQL/SqlServer) but provides no way to express **per-context dialect-mode flags** — the kind of thing a consumer's database server has configured that meaningfully changes which SQL is portable.

The immediate trigger is a real defect: Quarry-emitted `LIKE` SQL fails on default-mode MySQL. The longer-term issue is the generator's data model has no place to put this kind of configuration even when we know we need it.

This issue tracks two coupled changes:

1. **Refactor:** Replace the `SqlDialect dialect` parameter that flows through the generator with a `SqlDialectConfig` carrier that holds the dialect plus any per-context mode flags. Mirror those flags as additive properties on `QuarryContextAttribute` so consumers declare them next to their context.
2. **Fix:** Use the new structure to address the immediate bug — emit MySQL-portable `LIKE` SQL regardless of the consumer's `sql_mode` setting.

## Location

### Generator threading sites (representative — full audit needed in implementation)

- `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` — most fields and method parameters take `SqlDialect dialect`
- `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs`
- `src/Quarry.Generator/CodeGen/TerminalEmitHelpers.cs`
- `src/Quarry.Shared/Sql/SqlFormatting.cs` — dialect-aware emission
- Anywhere `[QuarryContext(Dialect = ...)]` is read out of the symbol model — currently only the `Dialect` property is consulted; the read path becomes `SqlDialectConfig.FromAttribute(...)`.

### Bug site

- `src/Quarry.Generator/CodeGen/...` — wherever the LIKE-pattern + ESCAPE clause is emitted for `Contains` / `StartsWith` / `EndsWith` calls. Currently emits identical SQL for all four dialects:

  ```sql
  LIKE '%foo\_bar%' ESCAPE '\'
  ```

### Consumer-facing attribute

- `src/Quarry/Context/QuarryContextAttribute.cs` (or wherever the attribute is declared in the runtime). New optional properties added; existing `Dialect` property unchanged.

## Diagnostics

### The bug, reproducer

```csharp
[QuarryContext(Dialect = SqlDialect.MySQL)]
public partial class MyDb : QuarryContext { ... }

// At runtime, against a stock MySQL 8 server (default sql_mode, no NO_BACKSLASH_ESCAPES):
await db.Users()
    .Where(u => u.UserName.Contains("user_name"))
    .Select(u => u.UserId)
    .ExecuteFetchAllAsync();
```

Throws:

```
MySqlConnector.MySqlException : You have an error in your SQL syntax;
check the manual that corresponds to your MySQL server version for the
right syntax to use near ''\'' at line 1
Server Error Code: 1064
SqlState: 42000
```

### Why

The emitted SQL is `LIKE '%user\_name%' ESCAPE '\'`. With default MySQL `sql_mode`, backslash is a string-literal escape character, so the `'\'` token is parsed as `open-quote → escaped-quote → string-still-open` and the parser consumes the next token looking for a closing quote, producing a 1064 syntax error. The same SQL works on:

- **PostgreSQL** (`standard_conforming_strings=on`, default since 9.1) — `'\'` is a literal backslash.
- **SQLite** — never treats backslash specially.
- **SqlServer** — never treats backslash specially.
- **MySQL with `NO_BACKSLASH_ESCAPES` in `sql_mode`** — `'\'` is a literal backslash.

So Quarry's MySQL path silently assumes consumer servers have `NO_BACKSLASH_ESCAPES` set, which is **not the default** and not common in practice.

### Coverage gap that hid this

PR #266 (issue #258) moved `Pg` from `MockDbConnection` to a real `NpgsqlConnection`, expanding execution coverage for PG. PR #271 (issue #269) extended the same pattern to MySQL. The MySQL execution coverage immediately surfaced this bug at:

- `CrossDialectStringOpTests::Where_Contains_LiteralWithMetaChars_InlinesWithEscape`
- `CrossDialectStringOpTests::Where_Contains_QualifiedConstField_WithMetaChars_EscapesPattern`

Both threw the 1064 above when executed against real MySQL.

## What Has Been Tried

PR #271 papered over the symptom by configuring the test container with `--sql-mode=...,NO_BACKSLASH_ESCAPES`:

```csharp
// MySqlTestContainer.cs (PR #271)
var container = new MySqlBuilder("mysql:8.4")
    .WithDatabase(BaselineDatabaseName)
    .WithCommand(
        "--character-set-server=utf8mb4",
        "--collation-server=utf8mb4_bin",
        "--sql-mode=ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION,NO_BACKSLASH_ESCAPES")
    .Build();
```

This is acknowledged in PR #271 as a temporary mitigation that masks the underlying generator bug. The test suite is green because the container is configured to make the generator's broken-on-default-MySQL SQL parse correctly. **Real consumers running default-mode MySQL would hit the 1064.**

## Gathered Information

### Why a flat enum is no longer sufficient

The dialect identity is one axis of "what SQL should we emit." There are at least four orthogonal axes once you account for server-side configuration:

| Axis                          | MySQL                                  | PG                                    | SqlServer            | SQLite |
|-------------------------------|----------------------------------------|---------------------------------------|----------------------|--------|
| Identifier quoting            | `lower_case_table_names` (server init) | always case-sensitive                 | depends on collation | always |
| String escape handling        | `NO_BACKSLASH_ESCAPES` in `sql_mode`   | `standard_conforming_strings`         | always literal `'\'` | always |
| `"`-as-string-literal         | `ANSI_QUOTES` in `sql_mode`            | always identifier (per ANSI)          | always identifier    | identifier |
| `GROUP BY` strictness         | `ONLY_FULL_GROUP_BY` in `sql_mode`     | always full                           | always full          | lenient |

PR #271 hit the second axis (string escapes). The first and third were hand-pinned to "ANSI-like" via the test container config. The fourth is server-default-strict everywhere we care, but a consumer who runs MySQL 5.7 in legacy mode would expose Quarry's emitted SQL to lenient `GROUP BY`, which is its own correctness hazard.

A flat `SqlDialect` enum can't carry any of this information. Adding ad-hoc bool parameters for each axis would balloon every generator method signature and is exactly the kind of change a structured carrier exists to avoid.

### MySQL-LIKE escape design space

Three candidate fixes for the immediate bug, evaluated:

#### (A) Switch the default escape character generator-wide

Emit `LIKE '%foo|_bar%' ESCAPE '|'` for **all** dialects. PG / SQLite / SqlServer / MySQL all accept arbitrary single-character ESCAPE values. `|` is not a LIKE-pattern metacharacter (those are `%` and `_`).

| Pros | Cons |
|------|------|
| One-line generator change | Aesthetic divergence from hand-written SQL |
| Works under every MySQL `sql_mode` | If a consumer's data legitimately contains `\|` in indexed columns, escaping shows up — but the same is already true for `\`; no truly data-free char exists |
| No new config surface | Doesn't generalise to other `sql_mode` axes |

#### (B) Per-context attribute flag, mode-aware emission

Add `MySqlBackslashEscapes` to `QuarryContextAttribute`. Generator branches:

- `MySqlBackslashEscapes = true` (matches MySQL default): emit `'%foo\\_bar%' ESCAPE '\\'` — the literal parses to one backslash on default-mode MySQL.
- `MySqlBackslashEscapes = false`: emit ANSI form `'%foo\_bar%' ESCAPE '\'`.

| Pros | Cons |
|------|------|
| Emitted SQL matches what a consumer would write by hand | Consumer must declare server config in attribute, keep in sync |
| Generalises cleanly to other `sql_mode` axes | More config surface |
| Default value is a footgun if mis-set | |

#### (C) Combine

Default escape character changes (A), AND `QuarryContextAttribute` exposes the per-mode flag (B) for consumers who want hand-written-looking SQL.

| Pros | Cons |
|------|------|
| Safe by default | Two emit paths to test |
| Power-user opt-in | Most expensive to maintain |

### Recommendation

**For the LIKE-emission fix specifically: (A).** Lowest cost, no new config surface, removes the footgun. The aesthetic divergence is implementation detail consumers don't need to care about.

**For the structural problem: still introduce `SqlDialectConfig` as the carrier**, even though (A) doesn't immediately need it. Two reasons: (1) the other `sql_mode` axes (`ANSI_QUOTES`, `ONLY_FULL_GROUP_BY`) and PG's `standard_conforming_strings` will surface as bugs the same way the backslash issue did, and we'll want the structure already in place. (2) Threading a struct through the generator instead of a flat enum is a reversible, additive refactor today; it gets harder the longer we wait.

Note: this means landing the refactor and the immediate fix can be **separate commits in the same PR** — first the structural change with `SqlDialectConfig.Dialect` being the only field consulted (no behavior change), then the new field added and the LIKE emit branched on it (or the escape-char swap if we go with (A)).

## Suggested Approach

Phased landing in a single PR. Each phase is independently committable and runnable.

### Phase 1 — Introduce `SqlDialectConfig`, no behavior change

1. New file `src/Quarry.Generator/SqlDialectConfig.cs`:

   ```csharp
   internal sealed record SqlDialectConfig(SqlDialect Dialect)
   {
       public static SqlDialectConfig FromAttribute(AttributeData attr, ...)
       {
           var dialect = (SqlDialect)attr.NamedArguments
               .FirstOrDefault(a => a.Key == "Dialect").Value.Value!;
           return new SqlDialectConfig(dialect);
       }
   }
   ```

2. Audit every site in `src/Quarry.Generator/**` that takes `SqlDialect dialect` as a parameter, field, property, or local. Replace with `SqlDialectConfig config`. Replace `dialect == SqlDialect.X` with `config.Dialect == SqlDialect.X`.

3. Production runtime code (`Quarry`, `Quarry.Migration`, `Quarry.Shared`) keeps consuming bare `SqlDialect`. The carrier is generator-internal.

4. Tests: full suite passes unchanged. The refactor is purely structural.

### Phase 2 — `QuarryContextAttribute` gains `MySqlBackslashEscapes`

1. `src/Quarry/QuarryContextAttribute.cs` (or wherever the attribute is declared): add property:

   ```csharp
   /// <summary>
   /// Set to <c>false</c> if the target MySQL server has
   /// <c>NO_BACKSLASH_ESCAPES</c> in its <c>sql_mode</c>. Default
   /// <c>true</c> matches stock MySQL. Ignored on non-MySQL dialects.
   /// </summary>
   public bool MySqlBackslashEscapes { get; set; } = true;
   ```

2. `SqlDialectConfig` carries the flag:

   ```csharp
   internal sealed record SqlDialectConfig(
       SqlDialect Dialect,
       bool MySqlBackslashEscapes);
   ```

3. `FromAttribute` reads the new property, defaulting to `true` when unset.

4. No emit changes yet — Phase 2 just wires the data through.

### Phase 3 — Branch the LIKE emit

Two sub-options to choose from at design-review time:

**3.A (recommended): switch the default escape character generator-wide.**

In the LIKE-pattern emit code, change the ESCAPE character from `\` to `|`. Update every cross-dialect SQL-shape assertion in `Quarry.Tests/SqlOutput/CrossDialect*Tests.cs` that hardcodes the SQL string. Drop the test-container `NO_BACKSLASH_ESCAPES` mitigation in `MySqlTestContainer.cs` (no longer needed). Add a regression test that runs `Where_Contains_LiteralWithMetaChars_InlinesWithEscape` on a default-mode MySQL container.

**3.B: keep `\` but emit MySQL-mode-aware double-escape.**

In LIKE-pattern emit:
- `if (config.Dialect == SqlDialect.MySQL && config.MySqlBackslashEscapes)`:
  emit `'%foo\\_bar%' ESCAPE '\\'` (each `\` doubled in the C# string literal of the emitted SQL).
- Otherwise: emit current ANSI form `'%foo\_bar%' ESCAPE '\'`.

Update SQL-shape assertions for the MySQL-default case. Drop the test-container mitigation only if all tests pass with default `sql_mode`; otherwise leave it and document the contexts where the mitigation is still needed.

### Phase 4 — Drop the test-container mitigation

In `src/Quarry.Tests/Integration/MySqlTestContainer.cs`, remove `NO_BACKSLASH_ESCAPES` from the `--sql-mode=` argument. The remaining flags (`STRICT_TRANS_TABLES`, `ONLY_FULL_GROUP_BY`, etc.) are MySQL 8.4 defaults and should be preserved verbatim. Run the suite against a default-`sql_mode` container to verify no regressions.

The `--collation-server=utf8mb4_bin` and `--character-set-server=utf8mb4` settings stay — those address case-sensitivity (a separate axis) and are correct in their own right.

### Test plan

- All existing tests stay green through every phase. Phase 1 is structural; Phase 2 adds a default-true property that doesn't affect emit; Phase 3 changes emit but the cross-dialect SQL-shape tests update in lockstep; Phase 4 removes the mitigation.
- New regression: a `[QuarryContext(Dialect = SqlDialect.MySQL, MySqlBackslashEscapes = false)]` context in a focused test, executing a `Contains(...)` query against a real MySQL container with `NO_BACKSLASH_ESCAPES` explicitly set in session — proves the opt-out path emits ANSI form correctly.
- New regression: same `Contains(...)` query against a default-mode MySQL container, with the default attribute (`MySqlBackslashEscapes = true`) — proves the default path emits parseable SQL.
- Manifest assertions: `quarry-manifest.mysql.md` regenerates with the new emission shape; review for any unintended drift.

### Future work this enables

Once `SqlDialectConfig` is in place, the same shape extends to:

- `MySqlAnsiQuotes` — when consumer sets `ANSI_QUOTES` in `sql_mode`, MySQL switches from backtick-only to backtick-and-double-quote identifier quoting. Quarry could emit double-quoted identifiers on MySQL too, matching PG / SQLite / SqlServer for true cross-dialect SQL portability.
- `MySqlOnlyFullGroupBy` — if false (legacy MySQL or consumer override), the generator can be more permissive about SELECT-without-GROUP-BY columns.
- `PgStandardConformingStrings` — for the small remaining set of PG consumers running pre-9.1 servers (or a legacy compatibility config).
- `SqlServerQuotedIdentifier` — `SET QUOTED_IDENTIFIER ON/OFF` analogue.

Each is additive — `SqlDialectConfig` gains a property, `QuarryContextAttribute` gains the same property, the generator branches where relevant, every existing consumer keeps working with defaults.

## Surfaced By

PR #271 (issue #269), Phase 4 cross-dialect mirror execution. The `Where_Contains_LiteralWithMetaChars_InlinesWithEscape` and `Where_Contains_QualifiedConstField_WithMetaChars_EscapesPattern` tests threw 1064 against real MySQL until the test container was hand-configured with `NO_BACKSLASH_ESCAPES`. PR #271 ships the test-only coverage; this issue tracks the proper generator fix.
