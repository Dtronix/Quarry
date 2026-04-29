# Plan: 274-ss-window-function-int

## Summary

Fix `InvalidCastException` raised by `SqlDataReader.GetInt32` when the generator emits a window-function projection of CLR type `int` against `Microsoft.Data.SqlClient`. The dedicated ranking/numbering window functions (`ROW_NUMBER`, `RANK`, `DENSE_RANK`, `NTILE`) return `BIGINT` from SQL Server; the reader's `GetInt32` does not auto-narrow and throws.

The fix wraps such expressions with `CAST(... AS INT)` in the rendered SQL **only on `SqlDialect.SqlServer`**. Other dialects emit the same SQL as today. The C# reader (`r.GetInt32(ordinal)`) is unchanged; the cast happens server-side and the value comes back as `INT`.

## Key concepts

**ProjectedColumn carries the SQL expression and the reader metadata.** The `SqlExpression` field on `ProjectedColumn` already holds the canonical SQL fragment for window-function and aggregate projections (e.g., `ROW_NUMBER() OVER (...)`). At final SQL assembly (`ReaderCodeGenerator.GenerateColumnList`), each column's expression is passed through `SqlFormatting.QuoteSqlExpression(expr, dialect)` — the dialect-aware step. This is the natural seam to inject `CAST(... AS INT)`.

**The discriminator is a new flag, not text matching.** Adding a `RequiresSqlServerIntCast` flag to `ProjectedColumn` lets us mark window-function int projections at the source and keep the dialect logic localized. Text-matching `OVER (` in the SQL would be brittle (subqueries, future operators).

**The flag is set at four call sites in `ProjectionAnalyzer`.** All projection construction sites that build a `ProjectedColumn` from `GetAggregateInfo` / `GetJoinedAggregateInfo` results check `HasOverClauseLambda(invocation) && clrType == "int"` to decide. The helpers themselves are unchanged.

**Scope.** All window-function emit sites where `ClrType == "int"`:
- Dedicated functions: `ROW_NUMBER`, `RANK`, `DENSE_RANK`, `NTILE` (always int).
- Aggregate-OVER overloads: `COUNT(*)`, `COUNT(col)` (always int); `SUM`/`AVG`/`MIN`/`MAX` when `ResolveAggregateClrType` returns `"int"` (i.e., projecting from an int column).
- Excluded: `LAG`, `LEAD`, `FirstValue`, `LastValue` — they inherit the source column type and don't produce BIGINT-narrow.

**Functional vs defensive coverage.** Of the included functions, only `ROW_NUMBER`/`RANK`/`DENSE_RANK`/`NTILE` actually return `BIGINT` on SQL Server. `COUNT`/`SUM`/`AVG`/`MIN`/`MAX` already return `INT` (or the input type). The wrap on those is a defensive type-contract no-op — accepted to keep the rule uniform.

## Phases

### Phase 1: Add `RequiresSqlServerIntCast` flag to `ProjectedColumn`

Pure model change. No emit changes. No call-site changes. Default `false`.

**Files:** `src/Quarry.Generator/Models/ProjectionInfo.cs`

**Changes:**
- Constructor: add `bool requiresSqlServerIntCast = false` parameter (last position to avoid breaking positional callers — all callers use named arguments today).
- Property: `public bool RequiresSqlServerIntCast { get; init; }`.
- `Equals`: include the flag.
- `GetHashCode`: include the flag.
- XML doc on the property explaining intent: "True for window-function projections whose CLR type is `int`. When `dialect == SqlDialect.SqlServer`, the column-list emitter wraps `SqlExpression` with `CAST(... AS INT)` because SQL Server's `ROW_NUMBER`/`RANK`/`DENSE_RANK`/`NTILE` return `BIGINT` and `Microsoft.Data.SqlClient.SqlDataReader.GetInt32` does not auto-narrow."

**Tests added:** none in this phase (pure DTO change). `dotnet build` is the gate.

**Commit:** `feat(generator): add RequiresSqlServerIntCast flag on ProjectedColumn (#274)`

### Phase 2: Apply CAST wrap in `ReaderCodeGenerator.GenerateColumnList`

Wire the flag into emit. No upstream sets it yet, so user-visible behavior doesn't change after this phase — but the mechanism is testable in isolation.

**Files:** `src/Quarry.Generator/Projection/ReaderCodeGenerator.cs`

**Change at `GenerateColumnList` (line 22):**

```csharp
if (!string.IsNullOrEmpty(column.SqlExpression))
{
    var rendered = SqlFormatting.QuoteSqlExpression(column.SqlExpression, dialect);
    if (column.RequiresSqlServerIntCast && dialect == SqlDialect.SqlServer)
        rendered = $"CAST({rendered} AS INT)";
    sb.Append(rendered);
    if (!string.IsNullOrEmpty(column.Alias))
    {
        sb.Append(" AS ");
        sb.Append(QuoteIdentifier(column.Alias!, dialect));
    }
}
```

The cast wraps the *post-quoting* rendered expression so the inner identifier quoting is preserved.

**Mirror the same change in `GenerateColumnNamesArray` (line 321)** if it path-emits the SQL expression for the same column kind. (Verify during implementation — if it does, apply the wrap; if not, no change.)

**Tests added:** unit-level test in `Quarry.Tests` that constructs a `ProjectedColumn` with `RequiresSqlServerIntCast = true` and a sample `SqlExpression`, then calls `GenerateColumnList` for each dialect:
- `SqlDialect.SqlServer` → output contains `CAST([...] AS INT)`.
- `SqlDialect.PostgreSQL`, `MySQL`, `SQLite` → output unchanged from today.
- Same assertion with the flag `false` → no wrap on any dialect.

If `ReaderCodeGenerator` / `ProjectedColumn` are `internal`, use `InternalsVisibleTo`. Check first.

**Commit:** `feat(generator): emit CAST(... AS INT) on Ss when column flagged (#274)`

### Phase 3: Set the flag at window-function projection sites

Source of truth: `ProjectionAnalyzer.cs`. Four `ProjectedColumn` construction sites consume `GetAggregateInfo` / `GetJoinedAggregateInfo`:

1. **Line 813** — `AnalyzeJoinedInvocation` (single-column joined aggregate): `Value` column.
2. **Line 971** — `ResolveJoinedAggregate` (joined tuple/DTO element).
3. **Line 1774** — `AnalyzeInvocation` (single-column non-joined aggregate).
4. **Line 1957** — `ResolveProjectedExpression`-style site for non-joined tuple/DTO element.

**Pattern at each site:**

```csharp
var aggregateClrType = clrType ?? "int";
var requiresSqlServerIntCast =
    HasOverClauseLambda(invocation) && aggregateClrType == "int";

return new ProjectedColumn(
    ...
    requiresSqlServerIntCast: requiresSqlServerIntCast);
```

`HasOverClauseLambda(invocation)` (already defined at line 2956) returns true when the last argument is a lambda — true for `Sql.RowNumber(over => ...)`, `Sql.Sum(col, over => ...)`, etc., and false for plain `Sql.Sum(col)` / `Sql.Count()`. This cleanly gates window-vs-non-window aggregates.

**Tests added:**
- A window-function projection test (single-entity) verifies the rendered `Ss` SQL contains `CAST(ROW_NUMBER() OVER (...) AS INT)` and the rendered Pg/My/Lite SQL does *not* contain `CAST`.
- A joined window-function projection test verifies the same.
- A non-window aggregate test (e.g., `Sql.Sum(o.Total)` without OVER) verifies *no* CAST is emitted on Ss.
- A `LAG` window function test verifies no CAST (excluded scope).

These are SQL-string assertions, not execution tests — they don't require a database.

**Commit:** `feat(generator): wrap int-typed window projections with CAST(... AS INT) on Ss (#274)`

### Phase 4: Update existing SQL-string assertions and regenerate the Ss manifest

Now the existing cross-dialect tests' Ss expected strings need updating (they previously asserted the un-wrapped emit). The Ss manifest snapshot also needs regeneration.

**Files affected:**
- `src/Quarry.Tests/SqlOutput/CrossDialectWindowFunctionTests.cs` — update `ss:` parameter on every `AssertDialects` call where the emit is a window function with int return type.
- `src/Quarry.Tests/SqlOutput/CrossDialectSetOperationTests.cs` — same for the two NTILE-based UNION ALL tests.
- `src/Quarry.Tests/ManifestOutput/quarry-manifest.sqlserver.md` — regenerate via the manifest update mechanism (verify the build target / script during implementation).

**Approach:**
- Run `dotnet test` first; collect every Ss-string-assertion failure for window-function projections.
- Update each `ss:` expected string to add `CAST(... AS INT)` around the affected expression.
- Other dialect strings (sqlite/pg/mysql) do not change.
- Regenerate the Ss manifest. Inspect the diff — only window-function-int sites should change.
- Confirm zero diff in `quarry-manifest.{postgresql,mysql,sqlite}.md`.

**Tests:** all dialect-string assertions and manifest-snapshot tests pass.

**Commit:** `test: update Ss SQL assertions and regenerate Ss manifest for CAST(... AS INT) wrap (#274)`

### Phase 5: Un-skip the nine `Ss.ExecuteFetchAllAsync` paths

The fix's purpose is to make these execute on Ss. With the cast in place, run them and verify.

**Files affected:**
- `src/Quarry.Tests/SqlOutput/CrossDialectWindowFunctionTests.cs` — seven sites (lines 54, 113, 433, 895, 934, 973, 1010).
- `src/Quarry.Tests/SqlOutput/CrossDialectSetOperationTests.cs` — two sites (lines 1300, 1346).

**Approach for each site:**
- Remove the `// ss execution skipped — see #274 …` comment.
- Add `var ssResults = await ss.ExecuteFetchAllAsync();` followed by `Assert.That(...)` mirroring the Pg/My assertions for the same test.

**Tests:** all nine tests now exercise SQL Server. Testcontainers MsSql is required (Docker present per environment check).

**Commit:** `test: un-skip Ss execution for nine window-function tests after #274 fix`

### Dependencies

- Phase 1 → Phase 2 (Phase 2 reads `RequiresSqlServerIntCast` from the model).
- Phase 2 → Phase 3 (Phase 3 sets the flag; without Phase 2, setting it does nothing).
- Phase 3 → Phase 4 (Phase 4's existing assertions only diverge once Phase 3 makes them diverge).
- Phase 4 → Phase 5 (Phase 5 needs the manifest in sync, otherwise unrelated test noise).

Each phase is independently committable and leaves the test suite green. Phase 4 and Phase 5 are bundled around the change of expectations.

### Risks / open items

- **Manifest regeneration tooling.** Need to confirm during implementation whether the manifests are regenerated by a `dotnet test` snapshot updater (Verify-style), a separate target, or manual edit. If Verify-style, accepting the new snapshot is enough; if manual, edit is straightforward but requires the tool that produced the original.
- **Testcontainers cold start.** First MsSql container pull/start can be slow (~minutes). Phase 5 may have a long first run.
- **`InternalsVisibleTo` for Phase 2 unit test.** If `ReaderCodeGenerator` is `internal`, may need to add the test assembly to `InternalsVisibleTo` if not already there.
