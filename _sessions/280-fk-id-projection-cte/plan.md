# Plan: FK `.Id` projection (issue #280)

## Goal

Make `.Select(o => (..., UserKey: o.UserId.Id, ...))` work correctly across:
1. FromCte chains (post-CTE Select on placeholder path).
2. Non-CTE single-entity Select (semantic-model path on `IEntityAccessor<T>`).
3. Joined chains with `.Select((u, o) => (..., o.UserId.Id, ...))`.

Correct = SQL emits the FK column name (e.g. `"UserId"`), the generated reader
calls the right `Get<KeyType>(ordinal)`, and the projected tuple/DTO type
contains the key type (e.g. `int`) at that slot — not `EntityRef<…, …>`.

## Design summary

A `.Id` access on an `EntityRef<TEntity, TKey>` column is a **key-only
projection**. It reads the same database column as projecting the whole FK
column would (`o.UserId`) but emits only the raw key value — no
`new EntityRef<TEntity, TKey>(value)` wrap. The two cases must coexist:

| Expression       | SQL column | Reader emits                          | Tuple slot type     |
|------------------|------------|---------------------------------------|---------------------|
| `o.UserId`       | `UserId`   | `new EntityRef<User, int>(GetInt32)`  | `EntityRef<User,int>` |
| `o.UserId.Id`    | `UserId`   | `GetInt32(ordinal)`                   | `int`               |

Discovery encodes "this is a `.Id` access" via a new `IsRefKeyAccess` flag on
`ProjectedColumn`. Enrichment honors the flag: it looks up the FK column by
its property name (which discovery stores in `ColumnName`), copies the key
type from the registry-derived `ColumnInfo` (which already stores `ClrType`
as the key type, not `EntityRef`), and suppresses `IsForeignKey` so the
reader code generator does not emit the wrap.

Discovery has two relevant paths:
1. **Placeholder path** (CTE post-Select, joined Select): runs without
   `SemanticModel` access to entity types. Emits `ProjectedColumn` with empty
   `ClrType`/`ColumnName`; column metadata gets filled in later by
   `BuildProjection` against the registry.
2. **Semantic-model path** (`AnalyzeFromTypeSymbol`): runs with the entity's
   `ITypeSymbol` and a `columnLookup` built from its properties. Returns
   `ProjectedColumn` with types already resolved.

Both paths must set `IsRefKeyAccess=true` for `o.FK.Id`. The placeholder path
also needs to write the FK property name into `ColumnName` so the enrichment
pass can find the right entity column. The semantic-model path already has
the column in hand and can fully populate `ClrType`/`FullClrType`/
`ReaderMethodName` from the FK's type arguments.

A defensive secondary fix in `TryParseNavigationChain`: short-circuit when
`finalProp == "Id"` and the first hop is a known column in `columnLookup`.
This prevents the navigation-chain fallback from silently swallowing future
FK `.Id` patterns that miss the dedicated branch.

## Phases

### Phase 1 — Add `IsRefKeyAccess` to ProjectedColumn

Add a new `bool IsRefKeyAccess` property to `ProjectedColumn`
(`src/Quarry.Generator/Models/ProjectionInfo.cs`). Default `false`. Update the
constructor (with default), the equality member, and the hash combiner. Add
an XML doc comment that says the column projects only the FK key value
(`<TKey>`), not the wrapping `EntityRef<TEntity, TKey>`. No other code
changes in this phase — purely additive on the model.

**Tests:** No new tests in this phase. Existing 3364 tests must still pass.

### Phase 2 — Fix the placeholder path (CTE / joined)

In `ResolveJoinedColumnWithPlaceholder`
(`src/Quarry.Generator/Projection/ProjectionAnalyzer.cs:401`), the FK `.Id`
branch (line 444-464) currently emits:

```
new ProjectedColumn(
    propertyName: propertyName,
    columnName: "",                         // ← problem
    clrType: "",
    fullClrType: "",
    isNullable: false,
    ordinal: ordinal,
    tableAlias: info.Alias,
    isForeignKey: true)                      // ← wrong, causes EntityRef wrap
```

Change it to:

```
new ProjectedColumn(
    propertyName: propertyName,
    columnName: refPropertyName,             // FK property name, e.g. "UserId"
    clrType: "",
    fullClrType: "",
    isNullable: false,
    ordinal: ordinal,
    tableAlias: info.Alias,
    isRefKeyAccess: true)                    // new flag — replaces isForeignKey
```

`refPropertyName` is the name from `nestedAccess.Name.Identifier.Text`. The
existing enrichment dual-lookup (`PropertyName` → `ColumnName` fallback) at
`ChainAnalyzer.cs:2178-2185` finds the FK column by name. Phase 4 adds the
enrichment branch that consumes `IsRefKeyAccess`.

**Tests:** No new tests in this phase. Build still expected to fail compilation
of generated interceptor for the bug repro until Phase 4 lands. Run existing
tests — none touch FK `.Id` placeholder path, so all 3364 should still pass.

### Phase 3 — Fix the semantic-model path (non-CTE)

In `AnalyzeProjectedExpression`
(`src/Quarry.Generator/Projection/ProjectionAnalyzer.cs:1821`), the FK `.Id`
branch (line 1889-1909) currently:
- Requires `refColumn.Kind == ColumnKind.ForeignKey`.
- Returns the column with `clrType: refColumn.ClrType` — but
  `BuildColumnInfoFromTypeSymbol` sets `ClrType="EntityRef"` (because
  `GetSimpleTypeName` returns `type.Name` for non-special types). That string
  is meaningless for the reader.

Rewrite the branch to:
1. Drop the `Kind == ColumnKind.ForeignKey` requirement entirely. Detect the
   FK structurally from the property's *type symbol* via
   `semanticModel.GetTypeInfo(nestedAccess).Type` — accept any
   `INamedTypeSymbol` named `EntityRef` with two type arguments. This makes
   the branch resilient to FK-detection misses in
   `BuildColumnInfoFromTypeSymbol` (which the issue reproducer already shows
   happens in some cases).
2. Extract the key type from `entityRefType.TypeArguments[1]`. Use
   `GetSimpleTypeName` for `ClrType` and `ToDisplayString()` for
   `FullClrType`.
3. Resolve `ReaderMethodName`/`IsValueType` via
   `ColumnInfo.GetTypeMetadata(keyType)` (matching the pattern at line 1875).
4. Set `IsRefKeyAccess: true`. Do NOT set `IsForeignKey: true`.
5. Use `refColumn.ColumnName` if `columnLookup` has the FK column (correct
   column-naming), otherwise fall back to `refPropertyName` (the property
   name as the column name — same convention as the existing fallback at
   line 1879).

This path emits a fully resolved `ProjectedColumn` — no enrichment needed.

**Tests:** No new tests in this phase. Build the project; the non-CTE bug
reproducer should now compile and produce correct SQL/reader. Existing
tests must still pass.

### Phase 4 — Enrichment honors `IsRefKeyAccess`

In `BuildProjection`
(`src/Quarry.Generator/Parsing/ChainAnalyzer.cs:1974`), the enrichment loop
at line 2087-2217 currently sets
`IsForeignKey = entityCol.Kind == ColumnKind.ForeignKey` whenever it finds an
entity column match. Add a guard:

When the source `ProjectedColumn` has `IsRefKeyAccess=true`, the enrichment
must:
- Set `ColumnName = entityCol.ColumnName` (correct per-naming-convention name).
- Set `ClrType = entityCol.ClrType` (already `int`/`long`/etc. — the key type
  from `SchemaParser`).
- Set `FullClrType = entityCol.FullClrType`.
- Set `IsValueType = entityCol.IsValueType`.
- Set `ReaderMethodName = entityCol.DbReaderMethodName ?? entityCol.ReaderMethodName`.
- Set `IsNullable = entityCol.IsNullable`.
- Force `IsForeignKey = false` and `ForeignKeyEntityName = null` — overrides
  the default behavior on FK columns.
- Preserve `IsRefKeyAccess = true`.

The simplest way to express this: branch on `col.IsRefKeyAccess` *before* the
generic enrichment block at line 2156-2206. If set, do the lookup, copy the
fields above, and `continue` past the standard FK-enriching code.

**Tests:** This phase makes the bug reproducer compile and execute correctly.
Add a new test file `src/Quarry.Tests/SqlOutput/FkKeyProjectionTests.cs` with:
1. `FromCte_FkKeyProjection_TupleProjection` — `Lite/Pg/My/Ss.With<Order>(...).FromCte<Order>().Select(o => (o.OrderId, UserKey: o.UserId.Id, o.Total))` — assert SQL across dialects, assert runtime values.
2. `Single_FkKeyProjection_TupleProjection` — `Lite/Pg/My/Ss.Orders().Select(o => (o.OrderId, UserKey: o.UserId.Id, o.Total))` — assert SQL across dialects, assert runtime values.
3. `Joined_FkKeyProjection_TupleProjection` — `Lite/Pg/My/Ss.Users().Join<Order>(...).Select((u, o) => (u.UserName, UserKey: o.UserId.Id, o.Total))` — assert SQL across dialects, assert runtime values.
4. `FkKeyProjection_SqlOnlyAssertion` — `.ToDiagnostics()` assertions for fast regression: `projectionColumns[1].ClrType == "int"`, `projectionColumns[1].ColumnName == "UserId"`. Single-dialect (sqlite) is enough.

### Phase 5 — Update existing wide-tuple test

Per the issue's own suggestion: extend
`CrossDialectWideTupleTests.Tuple_PostCteWideProjection` to use
`UserKey: o.UserId.Id` for one of the elements (replace the
`Echo: o.OrderId` workaround). Update the SQL assertions and the runtime
value assertions for `UserKey`. The existing seed data has Order 1 owned by
Alice (UserId=1) and Order 3 owned by Bob (UserId=2); assertions match those.

**Tests:** The modified `Tuple_PostCteWideProjection` and the new
`FkKeyProjectionTests` must all pass across all four dialects.

### Phase 6 — Defensive `TryParseNavigationChain` short-circuit

In `TryParseNavigationChain`
(`src/Quarry.Generator/Projection/ProjectionAnalyzer.cs:3676`), add a guard:
when `finalProp == "Id"` and `hops.Count == 1` and the single hop is a key in
the supplied `columnLookup`, return `null`. Pass `columnLookup` as a new
parameter (it's already in scope at the single call site at line 1912).

This is defensive: without Phase 3's fix, this guard alone would *also* fix
the non-CTE case (by forcing the `(?)r.GetValue` path to fail rather than
silently emit broken code). With Phase 3 in place, the guard is unreachable
for the FK case, but it protects against future regressions where someone
adds another `.Id`-shaped pattern that misses the FK branch.

**Tests:** No new tests in this phase — existing tests must still pass.
Coverage from Phases 4 and 5 already exercises the affected paths.

### Phase 7 — Cleanup & full validation

Delete `src/Quarry.Tests/_Bug280Repro.cs`. Run the full solution test suite
(`dotnet test Quarry.sln`). Confirm all 3364 baseline tests + the ~5 new
tests pass on all four dialects.

## Dependencies

- Phase 1 must land before Phases 2 and 3 (they reference the new flag).
- Phase 4 must land before runtime validation of Phases 2 — the placeholder
  path produces incomplete columns until enrichment honors the flag.
- Phase 3 (semantic-model path) is independent of Phase 4 — that path emits
  fully resolved columns with no enrichment hop.
- Phases 5 and 6 are independent of each other and can land in either order.
- Phase 7 must be last.

## Out of scope

- Changing `BuildColumnInfoFromTypeSymbol`'s FK detection. Phase 3 makes the
  non-CTE branch resilient to FK-detection misses regardless of whether the
  detection itself is fixed. A separate hardening pass on
  `BuildColumnInfoFromTypeSymbol` (per the issue's layer 1) is deferred —
  it's not on the critical path and may be unnecessary once Phase 3 lands.
- Nullable FK key access (`o.UserId?.Id`). The issue and the schema do not
  exercise nullable FKs; if needed it can be a follow-up.
- Other navigation chains (`o.User.UserName`). Untouched by this fix.
