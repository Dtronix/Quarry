## Description

On non-MySQL dialects, parameterized pagination placeholders (`LIMIT $N` / `LIMIT @pN`) are numbered from the renderer's clause-level running parameter index, which does not advance past projection parameters. A chain that combines parameterized projection arguments (e.g. window-function args: `Sql.Lag(col, offset, default, ...)`, `Sql.Ntile(buckets, ...)`) with a parameterized `Limit`/`Offset` therefore emits a pagination placeholder whose number collides with (or under-counts) a projection parameter's placeholder.

Concrete PostgreSQL example — `Where(o => o.Total > threshold).Select(o => (o.Total, Sql.Lag(o.Total, lagOffset, lagDefault, over => ...))).Limit(take)`:

- Chain slots: `threshold`=P0, `lagOffset`=P1, `lagDefault`=P2, `take`=P3 (pagination allocated last).
- Rendered text: projection `{@N}` placeholders resolve to absolute `$2`/`$3`, WHERE renders `$1`, but `LIMIT` renders `$2` (running index = 1 after WHERE) — colliding with `lagOffset`'s `$2`.
- Npgsql positional mode indexes the Bind frame: `LIMIT $2` receives the **lagOffset value**, silently returning the wrong number of rows.

On SQLite/SqlServer the same shape emits `LIMIT @p1` while the bound parameter is named `@p3` — a name-lookup failure rather than silent wrongness, but still broken.

## Location

- `src/Quarry.Generator/IR/SqlAssembler.cs` — `AppendPagination` (running `paramIndex` used for `limitIdx`/`offsetIdx` on non-marker dialects)
- `src/Quarry.Generator/IR/SqlAssembler.cs` — end of `RenderSelectSql`: the `totalPlanParams = Math.Max(...)` note documents that the running index excludes projection params
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — pagination slots allocated last via `paramGlobalIndex++` (`PaginationPlan.LimitParamIndex`/`OffsetParamIndex` carry the true global slots)

## Diagnostics

Found during the #303 review (finding #7): the MySQL bind-order marker pass initially inherited the running index for pagination markers, and its slot-set validation flagged the mismatch. The MySQL side was fixed in the #303 PR by sourcing marker indices from `PaginationPlan.LimitParamIndex`/`OffsetParamIndex`; the non-MySQL placeholder numbering was left untouched there because it changes rendered SQL text on three dialects and deserves its own change + test sweep.

## What Has Been Tried

Nothing yet for non-MySQL. The MySQL-side equivalent (true-slot sourcing) is implemented and covered by `MySqlIntegrationTests.WindowFunctionParamsWithParameterizedLimit_OnMySQL_PreservesBindingAlignment`.

## Gathered Information

- Projection `{@N}` placeholders are resolved with absolute global indices (`QuoteSqlExpression`, offset 0 in the flat path), so they are already correct.
- All clause-level renderers use the running index consistently; only pagination can land *after* projection params in slot space while the running index has not counted them.
- `CarrierEmitter` binds pagination params at slot `ChainParameters.Count` (+1), i.e. the true slots — only the rendered placeholder number is wrong.

## Suggested Approach

In `AppendPagination`, use `pag.LimitParamIndex!.Value` / `pag.OffsetParamIndex!.Value` (true global slots) for placeholder formatting on **all** dialects — exactly what the MySQL marker path now does — instead of the running index. Update cross-dialect SQL-output expectations and add execution tests for window-param + parameterized `Limit`/`Offset` chains on PostgreSQL (silent-wrongness case) and SQLite/SqlServer (name-mismatch case).
