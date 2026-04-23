# Quarry v0.3.1

_Released 2026-04-23_

**Three silently-wrong projections fixed, an Npgsql 10 compatibility fix, and clearer authoring errors.** This release closes three silent-failure modes that emitted broken SQL with no diagnostic (`Many<T>` aggregates in `Select`, `Sql.Raw<T>` in `Select`, PostgreSQL entity inserts on Npgsql 10), adds loud compile-time diagnostics for common first-project friction points (QRY043 for non-materializable `RawSqlAsync` row types, QRY044 for missing `InterceptorsNamespaces` entries), and auto-registers `Quarry.Generated` via the shipped `.targets` so consumers no longer hit CS9137 for a Quarry-internal namespace. 4 PRs merged since v0.3.0.

---

## Highlights

- **`Many<T>` aggregates now work in `Select` projections** — `Sum`, `Min`, `Max`, `Avg`/`Average`, `Count` on nav properties now render correctly in tuple, DTO, and joined-context `Select` projections across all four dialects. Previously emitted broken SQL and uncompilable C# silently. `QRY074` (error) surfaces unresolvable cases.
- **`Sql.Raw<T>` now works in `Select` projections** — single-entity, joined, and single-column projections all render correctly. Template errors now emit `QRY029` at the `Select` call site instead of silently producing `SELECT "Col", "" FROM …`.
- **Npgsql 10 compatibility** — `MigrationRunner` history writes and generated entity/batch inserts now align `DbParameter.ParameterName` with the `$N` placeholder PostgreSQL emits, unblocking Npgsql 10's strict name-based binding.
- **QRY043: non-materializable `RawSqlAsync<T>` row types surfaced at authoring time** — positional records, init-only properties, abstract classes, and interfaces now fail with a named Quarry diagnostic instead of CS0144 / CS7036 against generated code.
- **QRY044: missing `InterceptorsNamespaces` entry surfaced at authoring time** — every `[QuarryContext]` class whose namespace isn't opted in gets a warning with the exact csproj line to paste, instead of an untargeted CS9137.
- **`Quarry.Generated` auto-registered** — the shipped `build/Quarry.targets` now adds `Quarry.Generated` to `InterceptorsNamespaces` automatically; consumers only list their own namespaces.
- **Nested row types supported in `RawSqlAsync<T>`** — row records/classes declared inside an enclosing class no longer hit CS0138; the generator emits fully-qualified names in the interceptor.

---

## New Diagnostics

- **QRY043** (error) — `RawSqlAsync<T>` / `RawSqlScalarAsync<T>` row entity type `T` is not materializable (positional record, init-only property, abstract class, or interface). Generator diagnostic. Workaround: project on a chain query with `Select(x => new Dto { ... })` — the immutability comes from the projection, not from the row type.
- **QRY044** (warning) — `[QuarryContext]` class's namespace is not listed in the MSBuild `<InterceptorsNamespaces>` property. Analyzer diagnostic (ships in `Quarry.Analyzers`), diagnostic-only (no code fix — the target is the `.csproj`, not a source document). The message includes the exact csproj line to paste.
- **QRY074** (error) — Navigation aggregate (`Sum` / `Min` / `Max` / `Avg` / `Average` / `Count`) in a `Select` projection could not be resolved — the nav property does not exist on the outer entity, or its target entity is not registered on the context. Generator diagnostic; location points at the offending `.Sum(...)` / `.Max(...)` invocation rather than the enclosing `.Select(...)` call.
- **QRY029 extended** — placeholder-count and non-sequential-placeholder errors in `Sql.Raw<T>` now also fire from the projection (`Select`) path, not just `Where` / `Having`.

---

## Bug Fixes

### SQL Correctness

- **`Many<T>.Sum/Min/Max/Avg/Count` in `Select` projections** (#263, closes #257). Previously silently emitted broken SQL and uncompilable readers when used inside `.Select(...)` tuples, DTOs, or joined contexts — the generator had two disjoint pipelines for `SubqueryExpr` and the projection path only recognized static `Sql.*` aggregates. Now routes through the shared parser / binder / renderer chain and resolves the aggregate's CLR type from the selector column. `HasManyThrough` projections are covered, including in joined-select contexts. Failure mode is no longer silent — `QRY074` points at the specific aggregate invocation.
  ```csharp
  // Now works — previously emitted (?)r.GetValue(N) and IQueryBuilder<T, (object, object, ...)>
  db.Users().Select(u => (
      u.UserName,
      OrderCount: u.Orders.Count(),
      Total: u.Orders.Sum(o => o.Total),
      PeakOrder: u.Orders.Max(o => o.Total)));
  ```
- **`Sql.Raw<T>` in `Select` projections** (#262, closes #256). Previously rendered as an empty string literal in generated SQL (`SELECT "OrderId", "" FROM "orders"`) with no QRY diagnostic, no build warning, no runtime error. Now supported for single-entity tuples / DTOs / object-initializer projections, joined projections (`.Select((a, b) => ...)`), and single-column projections. Supported argument kinds: column references (`u.Xxx`), compile-time constants, captured runtime locals / parameters, and IR expressions like `u.Price * 2`. Booleans emit canonical placeholders so each dialect renders `TRUE` / `FALSE` or `1` / `0` correctly. Invalid templates now fail loudly: arg / placeholder count mismatches emit `QRY029` at the `Select` call location; unsupported argument kinds (ternary, unknown methods, string-column concat on MySQL / SqlServer, unresolvable `T`) fail projection analysis and degrade to a runtime-build path instead of emitting wrong SQL.
- **Npgsql 10 bind-parameter mismatch** (#261, closes #258). On Npgsql 10 + PostgreSQL 17, `MigrationRunner.InsertHistoryRowAsync` failed with `08P01: bind message supplies 0 parameters, but prepared statement requires 8`. `SqlFormatting.GetParameterName` always returned `@p{index}`, but PostgreSQL renders placeholders as `${index+1}` — Npgsql 9 was lax and bound positionally, Npgsql 10 strictly matches by name. `GetParameterName` now switches on dialect: PostgreSQL → `$N+1`, SQLite / SqlServer → `@pN`, MySQL → `@pN` (unique per index even though the placeholder is positional `?`). The same mismatch existed silently in two generated code paths — `CarrierEmitter.EmitCarrierInsertTerminal` hard-coded `"@p{i}"`, and the batch-insert terminal used `ParameterNames.AtP(...)` unconditionally — both fixed. `Npgsql` bumped to `10.*` in `Quarry.Tests.csproj`, `Quarry.Tool.csproj`, and the `quarry bundle` csproj template. Consumer `@pN` SQL text is unchanged on every dialect.

### Code Generation

- **Nested row types in `RawSqlAsync<T>`** (#260). Row records / classes declared inside an enclosing class no longer fail with CS0138. `RawSqlTypeInfo.IsNestedType` flags nested sites and `FileEmitter` skips the bad `using <EnclosingType>;` directive, emitting `global::`-prefixed FQNs in the interceptor instead.

---

## Tooling

- **Package `.targets` auto-registers `Quarry.Generated`** (#260). The shipped `build/Quarry.targets` adds `Quarry.Generated` to `<InterceptorsNamespaces>`, so authors only list their own context namespaces. Consumers whose `.csproj` already lists it continue to work (semicolon-separated lists deduplicate semantically).

---

## Documentation & Tooling

- Removed stale `// Note: Many<T> ... not yet in Select projections` from `llm.md`; added a working Select-projection example.
- Replaced "Sql.Raw in Select silently renders empty" warning in `docs/articles/querying.md` with a working Select-projection example and a `QRY029`-fires-in-projection note.
- `docs/articles/analyzer-rules.md` now lists `QRY043` (row entity materializability) under Raw SQL Resolution and `QRY044` (missing `InterceptorsNamespaces`) under a new Project Setup section.
- `docs/articles/getting-started.md` and `docs/articles/context-definition.md` note that `Quarry.Generated` is auto-registered and point at `QRY044` for missing entries.
- Replaced anonymous-type `Select` projections with named tuples across `docs/`, `llm.md`, and package READMEs (14a367f).
- Added `llm-release.md` skill for preparing tagged releases (d2a6b1e). Repo-local LLM workflow; not user-facing.

---

## Stats

- 4 PRs merged since v0.3.0
- 3 new diagnostics: `QRY043`, `QRY044`, `QRY074`
- 1 retired diagnostic slot (`QRY073`, introduced + retired in v0.3.0 — remains intentionally skipped so lingering `#pragma warning disable QRY073` directives stay inert)
- 0 breaking changes

---

## Full Changelog

### Bug Fixes

- Fix Many<T>.Sum/Min/Max/Avg/Count silently broken in Select projection (#263)
- Support Sql.Raw<T> in Select projections (#256) (#262)
- Fix MigrationRunner bind-parameter mismatch on Npgsql 10 (GH-258) (#261)

### DX / Diagnostics

- Docs/DX: three friction points introducing Quarry (#259) (#260)

### Direct commits

- Replace anonymous-type Select projections in docs with named tuples (14a367f)
- Add llm-release.md skill for preparing tagged releases (d2a6b1e)
