# Implementation Plan: 273-sql-dialect-config

## Background

Two coupled changes in one PR:

1. **Refactor:** Replace the flat `SqlDialect dialect` parameter that flows through the generator with an `internal sealed record SqlDialectConfig` carrier. Mirror its mode-flag fields as additive properties on `QuarryContextAttribute` so consumers declare them next to their context.
2. **Fix:** Use the new structure to emit MySQL-portable LIKE SQL — branch the emit on a new `MySqlBackslashEscapes` flag (default `true`, matching stock MySQL).

The carrier is generator-internal. `Quarry.Shared` (`SqlFormatting.cs`, parser), `Quarry`, and `Quarry.Migration` keep consuming the bare `SqlDialect` enum. Where generator code calls into shared formatting helpers, we extract `config.Dialect`. This preserves the runtime API.

This PR adds only one carrier flag (`MySqlBackslashEscapes`). The other axes named in the issue (`MySqlAnsiQuotes`, `MySqlOnlyFullGroupBy`, `PgStandardConformingStrings`, `SqlServerQuotedIdentifier`) get separate follow-up issues — they share the same shape and can be added incrementally.

## Key concepts

### `SqlDialectConfig` carrier shape

```csharp
namespace Quarry.Generators.Sql;

internal sealed record SqlDialectConfig(
    SqlDialect Dialect,
    bool MySqlBackslashEscapes = true)
{
    public static SqlDialectConfig Default(SqlDialect dialect) => new(dialect);

    public static SqlDialectConfig FromAttribute(AttributeData attr)
    {
        var dialect = SqlDialect.SQLite;
        var mysqlBackslashEscapes = true;

        foreach (var named in attr.NamedArguments)
        {
            switch (named.Key)
            {
                case "Dialect":
                    if (named.Value.Value is int dv) dialect = (SqlDialect)dv;
                    break;
                case "MySqlBackslashEscapes":
                    if (named.Value.Value is bool b) mysqlBackslashEscapes = b;
                    break;
            }
        }

        return new SqlDialectConfig(dialect, mysqlBackslashEscapes);
    }
}
```

`record` gives correct `Equals`/`GetHashCode` automatically — important for the IR cache (`ContextInfo`, `BoundCallSite`, `TranslatedCallSite` all participate in incremental generator caching).

### Where the carrier flows

The carrier replaces `SqlDialect` everywhere in `src/Quarry.Generator/**` that currently takes/holds a `SqlDialect` parameter, field, property, or local. Major sites:

- **IR carrier types** — `Models/ContextInfo`, `IR/BoundCallSite`, `IR/TranslatedCallSite` change their `Dialect` property type.
- **SQL assembly** — `IR/SqlAssembler` (~20 method overloads), `IR/SqlExprRenderer` (~20 methods).
- **CodeGen** — `CodeGen/TerminalBodyEmitter`, `CodeGen/ManifestEmitter`, `CodeGen/CarrierEmitter`, etc.
- **Parsing** — `Parsing/ContextParser` (where attribute is read), `Parsing/UsageSiteDiscovery`, `Parsing/ChainAnalyzer`.

Where the generator calls helpers in `Quarry.Shared` (e.g. `SqlFormatting.QuoteIdentifier(dialect, ...)`), we extract `config.Dialect` at the call site. Shared helpers stay on the bare enum.

Where `TerminalBodyEmitter` emits `SqlDialect.{value}` literals into generated source (e.g. `BatchInsertSqlBuilder.Build(..., SqlDialect.PostgreSQL, ...)`), we still emit `SqlDialect.{config.Dialect}` — the runtime helpers remain on the bare enum, the carrier doesn't leak into generated code.

### LIKE-emit fix mechanics

**Current ANSI emit** (works on PG, SQLite, SqlServer, and MySQL with `NO_BACKSLASH_ESCAPES`):

```sql
LIKE '%user\_name%' ESCAPE '\'
```

C# source emitting this: `sb.Append(" ESCAPE '\\'");`. The pattern literal `foo\_bar` is produced by `EscapeLikeMetaChars` and stored in `LikeExpr.Pattern` as a `LiteralExpr`, then rendered by `RenderLiteral` with single-quote wrapping.

**Default-mode MySQL parsing** of `'\'` consumes the closing quote as an escaped character — produces a 1064 syntax error.

**3.B emit for MySQL+`MySqlBackslashEscapes=true`** doubles every backslash in the SQL output:

```sql
LIKE '%user\\_name%' ESCAPE '\\'
```

MySQL with default `sql_mode` parses `\\` inside a string literal as one backslash, so the runtime LIKE pattern is `%user\_name%` and the escape character is `\` — matches literally.

The escape pass at parse time (`SqlExprParser.CreateLikeExpr` and `SqlExprAnnotator.InlineLikePatternsRecursive`) is dialect-agnostic — it runs before context binding. We keep it producing ANSI form (single-backslash). The dialect-aware doubling happens **at render time** inside `RenderLikeExpr`, only for MySQL+default. Two emission points get the conditional:

1. **The literal pattern.** When `like.Pattern is LiteralExpr` and the rendered literal contains backslashes, double them in the rendered output.
2. **The ESCAPE clause.** Currently `sb.Append(" ESCAPE '\\'");`. Conditional on `config.Dialect == SqlDialect.MySQL && config.MySqlBackslashEscapes` to emit `" ESCAPE '\\\\'"` instead.

### Why we don't touch general string-literal emission

A defensible argument exists that ALL string-literal emission for MySQL+default should double-escape backslashes (consider `Where(u => u.Path == "C:\\foo")` where the SQL `'C:\foo'` is misparsed under default `sql_mode`). That is a separate bug from the LIKE-emit issue, with broader implications. **Out of scope** for this PR. If/when that bug is reported, the same `MySqlBackslashEscapes` flag is reused. Issue 273 is specifically scoped to LIKE.

## Phases

### Phase 1 — Introduce `SqlDialectConfig`, no behavior change

Pure structural refactor. Tests stay green throughout.

**Files added:**
- `src/Quarry.Generator/Sql/SqlDialectConfig.cs` — the new record (under same namespace as `SqlDialect`, `Quarry.Generators.Sql`). Holds only `Dialect` for now; no flags. `FromAttribute` reads only the `Dialect` named arg.

**Files modified — IR carrier types:**
- `src/Quarry.Generator/Models/ContextInfo.cs` — change property type `SqlDialect Dialect → SqlDialectConfig DialectConfig`. Update ctor parameter, `Equals`/`GetHashCode`. Add a convenience getter `public SqlDialect Dialect => DialectConfig.Dialect` to ease migration of callers.
- `src/Quarry.Generator/IR/BoundCallSite.cs` — same change. Convenience getter.
- `src/Quarry.Generator/IR/TranslatedCallSite.cs` — same change. Convenience getter.

**Files modified — generator-internal threading sites:**
- `src/Quarry.Generator/Parsing/ContextParser.cs` — replace inline dialect extraction loop (lines 90–105) with `var config = SqlDialectConfig.FromAttribute(attributeData);`. Pass `config` into `ContextInfo` ctor.
- `src/Quarry.Generator/IR/SqlAssembler.cs` — convert all `SqlDialect dialect` parameters in `RenderSelectSql`, `RenderDeleteSql`, `RenderUpdateSql`, `RenderInsertSql`, `RenderBatchInsertSql`, internal helpers — to `SqlDialectConfig config`. Replace `dialect == SqlDialect.X` with `config.Dialect == SqlDialect.X`. At call sites into `SqlFormatting.*` (Quarry.Shared), pass `config.Dialect`.
- `src/Quarry.Generator/IR/SqlExprRenderer.cs` — same conversion across all `Render*` methods. Replace `dialect` with `config`. Renders into shared formatting helpers via `config.Dialect`.
- `src/Quarry.Generator/IR/PipelineOrchestrator.cs` — adjust diagnostic checks (`site.Bound.Dialect != Sql.SqlDialect.PostgreSQL`) to use the convenience getter.
- `src/Quarry.Generator/IR/CallSiteBinder.cs`, `IR/AssembledPlan.cs`, `IR/FileOutputGroup.cs` — propagate config.
- `src/Quarry.Generator/CodeGen/CarrierEmitter.cs`, `CodeGen/TerminalBodyEmitter.cs`, `CodeGen/TerminalEmitHelpers.cs`, `CodeGen/CommandBehaviorSelector.cs`, `CodeGen/RawSqlColumnResolver.cs`, `CodeGen/ManifestEmitter.cs` — same.
- `src/Quarry.Generator/Generation/InterceptorCodeGenerator.cs`, `Generation/MigrateAsyncCodeGenerator.cs` — same.
- `src/Quarry.Generator/Projection/ProjectionAnalyzer.cs`, `Projection/ReaderCodeGenerator.cs` — same.
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs`, `Parsing/ChainAnalyzer.cs` — same.

**TerminalBodyEmitter literal emission** (lines 518, 519, 556, 587):

Strings of the form `SqlDialect.{chain.Dialect}` go into generated source code that runtime helpers consume. Change to `SqlDialect.{chain.DialectConfig.Dialect}` (or use the convenience getter `chain.Dialect`). The emitted output stays identical — `SqlDialect.PostgreSQL` etc.

**Tests added/modified:** None. The full suite of 3,364 tests must pass unchanged.

**Commit:** `refactor: introduce SqlDialectConfig carrier (generator-internal) (#273)`

### Phase 2 — Add `MySqlBackslashEscapes` to attribute and carrier

Wires the flag through the data model. Default `true` (matches stock MySQL). No emit change yet.

**Files modified:**

- `src/Quarry/Context/QuarryContextAttribute.cs` — add property:

  ```csharp
  /// <summary>
  /// Set to <c>false</c> if the target MySQL server has
  /// <c>NO_BACKSLASH_ESCAPES</c> in its <c>sql_mode</c>. Default
  /// <c>true</c> matches stock MySQL. Ignored on non-MySQL dialects.
  /// </summary>
  public bool MySqlBackslashEscapes { get; set; } = true;
  ```

- `src/Quarry.Generator/Sql/SqlDialectConfig.cs` — add field:

  ```csharp
  internal sealed record SqlDialectConfig(
      SqlDialect Dialect,
      bool MySqlBackslashEscapes = true)
  ```

  Update `FromAttribute` to read the new named arg with default `true`.

**Tests added:**

- `src/Quarry.Tests/QuarryContextAttributeTests.cs` (or wherever attribute tests live — discover during impl) — assert `MySqlBackslashEscapes` defaults to `true`.
- `src/Quarry.Tests/Generation/ContextParserTests.cs` (if exists, else add a minimal test) — assert `SqlDialectConfig.FromAttribute` reads the flag, defaults to `true` when absent.

**Commit:** `feat: add MySqlBackslashEscapes flag to QuarryContextAttribute and SqlDialectConfig (#273)`

### Phase 3 — Branch LIKE emit on `MySqlBackslashEscapes`

The emit fix proper. Renderer produces dialect-aware LIKE SQL.

**Files modified:**

- `src/Quarry.Generator/IR/SqlExprRenderer.cs` — in `RenderLikeExpr`:

  At line 411 (the ESCAPE clause):
  ```csharp
  if (like.NeedsEscape)
  {
      var escapeClause = (config.Dialect == SqlDialect.MySQL && config.MySqlBackslashEscapes)
          ? " ESCAPE '\\\\'"   // SQL: ESCAPE '\\' → MySQL parsed: \
          : " ESCAPE '\\'";    // SQL: ESCAPE '\' → ANSI: \
      sb.Append(escapeClause);
  }
  ```

  At pattern emission (around lines 380–406), when `like.Pattern is LiteralExpr` and dialect is MySQL+default, double backslashes in the rendered string. Approach: render the pattern into a scratch StringBuilder via `RenderExpr` (current behavior), then if `config.Dialect == SqlDialect.MySQL && config.MySqlBackslashEscapes && like.Pattern is LiteralExpr`, post-process the scratch to double every `\` before appending. Non-literal patterns (parameter-bound, column refs) need no transformation — parameters bypass string-literal parsing entirely.

  Helper introduced:
  ```csharp
  private static string DoubleBackslashes(string s) =>
      s.IndexOf('\\') < 0 ? s : s.Replace("\\", "\\\\");
  ```

- `src/Quarry.Generator/Translation/SqlLikeHelpers.cs` — update the doc comment on `EscapeLikeMetaChars` (line 14) to reflect that the escape backslash is dialect-aware at render time, not at this layer. No behavior change to the function itself.

- `src/Quarry.Generator/IR/SqlExprNodes.cs` — update doc comment on `LikeExpr.NeedsEscape` (line 380) similarly.

**Tests added/modified:**

The cross-dialect SQL-shape tests in `src/Quarry.Tests/SqlOutput/CrossDialect*Tests.cs` that assert exact LIKE SQL on MySQL need updates for the new doubled-backslash form on MySQL with `MySqlBackslashEscapes` defaulted to `true`. Update assertions for:
- `Where_Contains_LiteralWithMetaChars_InlinesWithEscape`
- `Where_Contains_QualifiedConstField_WithMetaChars_EscapesPattern`

The PG / SQLite / SqlServer assertions in those tests do not change.

Add focused test that exercises both modes:
- `src/Quarry.Tests/SqlOutput/CrossDialectLikeEscapeModeTests.cs` (new file) — declare two `[QuarryContext]` test contexts with `Dialect = SqlDialect.MySQL`, one with default `MySqlBackslashEscapes = true`, one with `MySqlBackslashEscapes = false`. Assert the emitted SQL shape for `Contains` with metacharacter literals matches the expected ANSI vs doubled form for each.

**Commit:** `fix(generator): emit MySQL-portable LIKE SQL on default sql_mode (#273)`

### Phase 4 — Add focused default-mode-MySQL execution test

Proves the generator fix works against a real default-mode MySQL container.

**Files added:**

- `src/Quarry.Tests/Integration/MySqlDefaultModeTestContainer.cs` — sibling helper to `MySqlTestContainer.cs`. Same shape (lazy boot, Docker-unavailable detection, baseline DB) but the `--sql-mode=` argument **omits** `NO_BACKSLASH_ESCAPES` (uses the MySQL 8.4 stock default `sql_mode`). Different container name to avoid collision.

- `src/Quarry.Tests/Integration/MySqlBackslashEscapesTests.cs` — new focused test class:
  - `[Category("MySqlIntegration")]` (so it runs under the same gating as other MySQL integration tests).
  - Boots the default-mode container.
  - Declares a test context with `[QuarryContext(Dialect = SqlDialect.MySQL)]` (default `MySqlBackslashEscapes = true`).
  - Executes `Contains("user_name")`, `Contains("foo%bar")`, `Contains("a\\b")` queries against the live container — proves no 1064 and correct row matches.
  - Also declares a context with `[QuarryContext(Dialect = SqlDialect.MySQL, MySqlBackslashEscapes = false)]`. Sets `SET sql_mode = ...,NO_BACKSLASH_ESCAPES` in the connection's session before running the same queries — proves the opt-out path emits parseable SQL.

**Files NOT modified:** `src/Quarry.Tests/Integration/MySqlTestContainer.cs` keeps the `NO_BACKSLASH_ESCAPES` mitigation intact. The main test suite stays green throughout.

**Commit:** `test: add MySqlBackslashEscapes regression coverage on default-mode MySQL (#273)`

## Phase dependencies

- Phase 1 → Phase 2 (carrier shape must exist before adding fields to it)
- Phase 2 → Phase 3 (carrier must carry `MySqlBackslashEscapes` before renderer can branch on it)
- Phase 3 → Phase 4 (emit fix must exist before regression test can prove it)

Each phase is independently committable. After every phase: full test suite must be green (all 3,364 baseline tests, plus any new tests added in that phase).

## Test strategy summary

| Phase | Tests added | Tests modified | Expected result |
|-------|-------------|----------------|-----------------|
| 1 | none | none | All 3,364 pass unchanged |
| 2 | 2 unit tests (attribute default + FromAttribute parse) | none | All pass |
| 3 | 1 cross-dialect SQL-shape test class | 2 existing test methods (MySQL assertions only) | All pass |
| 4 | 1 integration test class + new container helper | none | All pass; new tests boot Docker container |

## Issues to file in REMEDIATE

Per the design decision to defer other `sql_mode` axes — file follow-up issues for:

- `MySqlAnsiQuotes` — when consumer sets `ANSI_QUOTES`, MySQL switches identifier quoting from backtick-only to backtick-and-double-quote. Quarry could emit double-quoted identifiers on MySQL too.
- `MySqlOnlyFullGroupBy` — if false, generator can be more permissive about SELECT-without-GROUP-BY columns.
- `PgStandardConformingStrings` — for legacy PG consumers running pre-9.1 servers.
- `SqlServerQuotedIdentifier` — `SET QUOTED_IDENTIFIER ON/OFF` analogue.

Each is additive to `SqlDialectConfig` and `QuarryContextAttribute` and follows the same shape established here.
