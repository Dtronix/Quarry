# Work Handoff: 258-fix-npgsql-parameter-naming-redux

## Key Components

- **PR #266** is open and CI-green on commit `2767e77` (rebased on `origin/master`). Covers the core GH-258 fix across Phases 1–8 plus all 9 REMEDIATE review findings. This PR is mergeable today as-is.
- **Phase 9** (this session's new scope) adds Pg execution parity to every `CrossDialect*Tests.cs` file: wherever Lite currently calls `await lt.ExecuteXxxAsync()` with assertions, mirror with `await pg.ExecuteXxxAsync()` and the SAME assertions. First file (`CrossDialectOrderByTests.cs`) is in progress; DDL bug surfaced mid-verification that requires a fix before mass-apply.

## Completions (This Session)

- INTAKE through REMEDIATE complete: probe, SqlFormatting fix, generator emission fix, harness upgrade, 4 integration tests, MigrationRunner DateTime fix, doc cleanup, 9 review findings addressed.
- PR #266 created, CI green (`gh run 24914189866` — 2m6s, ubuntu-latest).
- Phase 9 started: `CrossDialectOrderByTests.cs` mirrored on all 4 tests (Asc, Desc, ThenBy, Joined). 3 of 4 pass on Pg.

## Previous Session Completions

(None — first session.)

## Progress

- Phases 1–8 + REMEDIATE: **done and on PR #266**.
- Phase 9 file 1 of ~25: **code edits done**, one failure exposing a schema DDL bug that blocks proceeding.

## Current State

**Blocking bug found while verifying Phase 9:** `CrossDialectOrderByTests.OrderBy_Joined_RightTableColumn` fails on Pg with:

```
Quarry.QuarryQueryException : Error reading query results:
Reading as 'System.Decimal' is not supported for fields having DataTypeName 'double precision'
```

Root cause: in `PostgresTestContainer.CreateSchemaObjectsAsync` I ported SQLite `REAL` columns to PG `DOUBLE PRECISION`, but the Quarry schemas declare `Col<decimal>` (and `Col<Money>` → decimal via MoneyMapping). Npgsql refuses to read `double precision` as `System.Decimal`. The columns need to be `NUMERIC(18, 2)` in PG.

**Uncommitted WIP in working tree:**
- `_sessions/258-fix-npgsql-parameter-naming-redux/workflow.md` — phase flipped from REMEDIATE to IMPLEMENT for Phase 9
- `src/Quarry.Tests/Integration/PostgresTestContainer.cs` — doc comment updated to say NUMERIC(18,2), but the actual DDL still emits DOUBLE PRECISION (not yet changed)
- `src/Quarry.Tests/SqlOutput/CrossDialectOrderByTests.cs` — 4 Pg-mirror blocks added

### Exact DDL columns that need to change to NUMERIC(18, 2)

Per `grep "Col<decimal\|Col<Money" src/Quarry.Tests/Samples/`:

| Table         | Column            | Quarry schema type        | Current PG DDL      | Must be          |
|---------------|-------------------|---------------------------|---------------------|------------------|
| orders        | Total             | `Col<decimal>` Precision(18,2) | DOUBLE PRECISION | NUMERIC(18, 2) |
| order_items   | UnitPrice         | `Col<decimal>` Precision(18,2) | DOUBLE PRECISION | NUMERIC(18, 2) |
| order_items   | LineTotal         | `Col<decimal>`             | DOUBLE PRECISION | NUMERIC(18, 2) |
| accounts      | Balance           | `Col<Money>`→decimal       | DOUBLE PRECISION | NUMERIC(18, 2) |
| accounts      | credit_limit      | `Col<Money>`→decimal       | DOUBLE PRECISION | NUMERIC(18, 2) |
| products      | Price             | `Col<decimal>` Precision(18,2) | DOUBLE PRECISION | NUMERIC(18, 2) |
| products      | DiscountedPrice   | Computed<decimal>          | DOUBLE PRECISION (GENERATED ALWAYS) | NUMERIC(18, 2) (GENERATED ALWAYS) |

## Known Issues / Bugs

1. **DDL bug above** — must fix first, then re-run `CrossDialectOrderByTests` to confirm the joined decimal assertion works.
2. **DateTime materialization risk** — CreatedAt / LastLogin / OrderDate / ShipDate / ScheduledAt / CancelledAt / applied_at / started_at are all stored as TEXT in the PG harness schema but the entity property type is `DateTime` / `DateTime?`. Npgsql may refuse `text → DateTime` conversion when the generator emits a `reader.GetDateTime(i)` call. Not yet exercised by any Pg-execute test; will surface in Phase 9 file-by-file. Candidate fix: change the DDL to use PG `TIMESTAMP` for these columns (and port seed INSERTs to PG timestamp literal format — the current seeds use `'2024-06-01 00:00:00'` which PG accepts as TIMESTAMP directly).
3. **Potential row-order flakiness** — some existing tests may assert `results[0]` without an explicit `OrderBy`, relying on SQLite's insertion-order return. PG does not guarantee row order without ORDER BY. If/when a test fails this way, either add an `OrderBy` or loosen the assertion.
4. **`Col<bool> IsActive`** — PG schema uses INTEGER for IsActive. Quarry emits `IsActive = TRUE` / `FALSE` SQL on PG but binds ints for parameters. Should work, but untested end-to-end on real Npgsql. Watch in Phase 9.

## Dependencies / Blockers

- Fix the NUMERIC DDL bug before any more Phase 9 files can be verified — every test that reads a decimal column will hit the same error.
- Docker must be available locally (it is on this dev box: 29.1.2). CI (`ubuntu-latest`) has Docker by default.

## Architecture Decisions

- **Phase 9 strategy confirmed** by the user: "Mirror Lite exactly — same assertions on both" for every existing `await lt.ExecuteXxxAsync()` block. Start with smallest file first; pg-execute SQLite-specific features trigger per-test triage. No blanket skips.
- **NOT** attempting to switch MySQL (`My`) or SqlServer (`Ss`) off their mock connections — out of scope for #258, stays on `MockDbConnection`.
- **Decimal storage on PG** → NUMERIC(18, 2) to match Quarry's schema contract. Do not use DOUBLE PRECISION even though SQLite's REAL worked; Npgsql's strict typing requires exact DataTypeName compatibility with `GetDecimal(i)`.

## Open Questions

1. If DateTime columns need to change from TEXT to TIMESTAMP for Npgsql materialization, does this change the SQL Quarry generates on PG (ORDER BY, WHERE comparisons)? Need to verify against the existing CrossDialect*Tests `pg:` SQL assertions when the port lands. If they drift, the test file's `AssertDialects` calls would break.
2. Should `PostgresTestContainer` SQL-dialect-match the Quarry migration system's PG DDL? Currently hand-written; `DdlRenderer` could generate it from the schemas. Keeping it hand-written is fine for the test harness, but worth revisiting.

## Next Work (Priority Order)

### 1. Fix the NUMERIC DDL bug (unblocks everything)

In `src/Quarry.Tests/Integration/PostgresTestContainer.cs`, inside `CreateSchemaObjectsAsync`:

- `orders.Total` → change `DOUBLE PRECISION` → `NUMERIC(18, 2)`
- `order_items.UnitPrice`, `order_items.LineTotal` → `NUMERIC(18, 2)`
- `accounts.Balance`, `accounts.credit_limit` → `NUMERIC(18, 2)`
- `products.Price` → `NUMERIC(18, 2)`
- `products.DiscountedPrice` in the `GENERATED ALWAYS AS ("Price" * 0.9) STORED` clause → `NUMERIC(18, 2)`

After editing, run:
```
dotnet test src/Quarry.Tests --filter "FullyQualifiedName~CrossDialectOrderByTests" --nologo
```
Expect 4/4 passing, including `OrderBy_Joined_RightTableColumn` with the 75.50m / 150.00m / 250.00m decimal tuple assertions.

### 2. Commit the DDL fix + OrderByTests Pg mirror as "Phase 9 file 1 / bootstrap"

Commit message frame: "Add Pg execution mirror to CrossDialectOrderByTests + NUMERIC DDL fix". Session dir must be staged with the commit per the always-commit-sessions rule.

### 3. Run full suite once to see which other tests fail on Pg for preview purposes

```
dotnet test src/Quarry.Tests --nologo --no-build
```

(Remember — currently NO existing CrossDialect test has Pg execution, so the suite will still pass at 2996; the goal here is to confirm the DDL fix didn't break any of the existing Lite/Pg-diagnostic tests. If Pg-execute tests start failing after adding them in step 4, you have a clean baseline.)

### 4. Roll out Pg mirror to the remaining CrossDialect files, smallest to largest

Order (line counts):
1. ✅ `CrossDialectOrderByTests.cs` (130)
2. `CrossDialectSchemaTests.cs` (177)
3. `CrossDialectBatchInsertTests.cs` (190)
4. `CrossDialectHasManyThroughTests.cs` (192)
5. `CrossDialectInsertTests.cs` (192)
6. `CrossDialectDeleteTests.cs` (195)
7. `CrossDialectTypeMappingTests.cs` (199)
8. `CrossDialectAggregateTests.cs` (203)
9. `CrossDialectEnumTests.cs` (215)
10. `CrossDialectComplexTests.cs` (247)
11. `CrossDialectWhereTests.cs` (292)
12. `CrossDialectNavigationJoinTests.cs` (343)
13. `CrossDialectNullableValueTests.cs` (368)
14. `CrossDialectCteTests.cs` (420)
15. `CrossDialectMiscTests.cs` (507)
16. (and the rest: Join/Select/Subquery/Update/StringOp/Composition/SetOperation/WindowFunction/Diagnostics)

For each file, the mechanical pattern is:
```csharp
var results = await lt.ExecuteFetchAllAsync();
Assert.That(results, Has.Count.EqualTo(N));
// ...Lite-specific assertions...

var pgResults = await pg.ExecuteFetchAllAsync();
Assert.That(pgResults, Has.Count.EqualTo(N));
// ...SAME assertions, applied to pgResults...
```

Applied to every `await lt.ExecuteXxxAsync()` block including `ExecuteFetchFirstAsync`, `ExecuteFetchFirstOrDefaultAsync`, `ExecuteScalarAsync<T>()`, `ExecuteNonQueryAsync()`, `ToAsyncEnumerable()`.

**Delegatable to an agent** once the pattern is proven on 2–3 files. Keep classification of failures (fix / SQLite-only marker / follow-up issue) on the main context.

### 5. Triage failures as they surface

- **Decimal/double**: already fixed above.
- **DateTime read**: if `reader.GetDateTime()` on a TEXT column throws, migrate those columns to PG TIMESTAMP, re-verify existing `AssertDialects` calls still match (SQL text comparison may drift if the generator emits different CAST syntax for TIMESTAMP).
- **Row-order assumptions**: add `.OrderBy(...)` to the test chain or loosen the assertion.
- **SQLite-specific features**: mark the test `[Ignore("SQLite-specific behavior: <reason>")]` per user's explicit exclusion.

### 6. Once Phase 9 is complete, go to REMEDIATE-style commit + REVIEW pass + PR update

Either update PR #266 in place (likely — this is the same work stream) or land Phase 9 as a follow-up PR. Decide based on PR size at that point; if adding Pg mirrors grows PR #266 to >2000 additions, consider a follow-up. Otherwise piggyback.
