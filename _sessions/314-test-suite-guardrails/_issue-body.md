## Description

Nine tests in `CrossDialectJoinTests` assert row values **positionally** on PostgreSQL, MySQL and
SQL Server results from a query that carries no `ORDER BY`. They pass today only by accident of each
planner's chosen access path; a statistics refresh, a parallel scan, or a hash join being picked
instead of a nested loop can reorder the result set and turn them red with no code change.

This is the same defect class as the row-order sweep in #314, but these nine could **not** be fixed
by that sweep, which is why they are broken out here.

Every one of them projects `(u.UserName, o.Total)` (or a superset) from a `users → orders` join and
asserts:

```csharp
Assert.That(pgResults, Has.Count.EqualTo(3));
Assert.That(pgResults[0], Is.EqualTo(("Alice", 250.00m)));
Assert.That(pgResults[1], Is.EqualTo(("Alice",  75.50m)));
Assert.That(pgResults[2], Is.EqualTo(("Bob",   150.00m)));
```

The order encoded there is `orders.OrderId` ascending — the seed insertion order. But `OrderId` is
**not in the projection**, so it cannot be used as a client-side sort key, and the only discriminator
that *is* projected (`Total`) runs **descending** within the Alice group. Consequently:

- `.SortedByAsync(r => r.UserName)` is not a total order — the two Alice rows tie.
- `.SortedByAsync(r => (r.UserName, r.Total))` is total, but ascending `Total` swaps rows `[0]` and
  `[1]`, turning the tests red.

So no client-side sort over the projected columns can reproduce the asserted sequence. The fix has to
be query-side or assertion-side.

## Location

`src/Quarry.Tests/SqlOutput/CrossDialectJoinTests.cs`

| Test | Approx. line (pg / my / ss fetch) |
|---|---|
| `Join_InnerJoin_OnClause` | 39 / 45 / 51 |
| `Join_WithWhere_OnLeftTable` | 87 / 93 / 99 |
| `Join_InnerJoin_NamedTupleProjection` | 215 / 224 / 233 |
| `Join_ThreeTable_NamedTupleProjection` | 268 / 274 / 280 |
| `Join_WithWhere_TwoCapturedParams_BooleanBetween_SequentialIndices` | 561 / 566 / 571 |
| `Where_BeforeJoin_GetsTableAliasQualification` | 611 / 617 / 623 |
| `Select_Joined_Many_Sum_OnLeftTable` | 798 / 804 / 810 |
| `Select_Joined_Many_Count_OnLeftTable` | 847 / 853 / 859 |
| `Select_Joined_HasManyThrough_Max_OnLeftTable` | 902 / 908 / 914 |

Line numbers are as of the #314 branch.

`Join_ThreeTable_NamedTupleProjection` is the subtlest: it reads only `[0]`, so it looks convertible,
but that lone `[0]` sits under `Has.Count.EqualTo(3)` and the row it names — `(Alice, 250.00, "Widget")`
— is not the ascending minimum over any projected column (75.50 < 250.00, "Gadget" < "Widget").

The three aggregate variants (`Select_Joined_Many_Sum_OnLeftTable`,
`Select_Joined_Many_Count_OnLeftTable`, `Select_Joined_HasManyThrough_Max_OnLeftTable`) are worse
still: the two Alice rows tie on the aggregate column as well (`OrderTotal` 325.50 for both,
`OrderCount` 2 for both, `MaxAddrId` 2 for both), so the aggregate does not break the tie either.

## Diagnostics

Seed data (`QueryTestHarness.SeedData`) for the join:

```
users:   1 Alice, 2 Bob, 3 Charlie
orders:  OrderId 1 → UserId 1, Total 250.00
         OrderId 2 → UserId 1, Total  75.50
         OrderId 3 → UserId 2, Total 150.00
```

The join yields three rows. The asserted sequence is `OrderId` 1, 2, 3.

`RowOrderExtensions.SortedByAsync` (added in #314) is the standard remedy for this class of test and
now covers 118 sites across the suite; it simply cannot reach these nine.

## What Has Been Tried

- **Client-side sort via `SortedByAsync`** — ruled out above for all nine, by inspecting each test's
  projection against the seed data rather than by trial.
- **The #314 sweep deliberately skipped them** rather than converting them with a plausible-looking
  key, because `(r.UserName, r.Total)` compiles, reads as correct, and silently reorders the
  assertions.

## Gathered Information

- 50 `Is.EquivalentTo` call sites already exist in the test suite, so the order-independent assertion
  style has precedent here.
- `.OrderBy((u, o) => o.Total)` after a `Join<T>(...)` is supported and renders a top-level `ORDER BY`
  — see `CrossDialectDistinctOrderByTests.Distinct_OrderBy_NonProjectedColumn_WrapsAcrossAllDialects`.
  Note that ordering on a **non-projected** column combined with `Distinct()` makes the generator wrap
  the query in a derived table, which would materially change the SQL these tests pin.
- These tests' primary purpose is pinning join **SQL rendering** — the `AssertDialects(...)` block is
  the bulk of each test. Any fix that rewrites the expected SQL dilutes that.

## Suggested Approach

Preferred: **make the assertions order-independent**, leaving the pinned SQL untouched.

```csharp
Assert.That(pgResults, Is.EquivalentTo(new[]
{
    ("Alice", 250.00m),
    ("Alice",  75.50m),
    ("Bob",   150.00m),
}));
```

Every row and every value stays asserted; only the accidental ordering assumption is dropped. The
SQL-rendering pin — the actual point of these tests — is unaffected.

Alternative, if positional assertions are considered worth keeping: add
`.OrderBy((u, o) => o.OrderId)` to each chain and append the resulting `ORDER BY` to all four expected
SQL strings per test (36 strings total). This makes the queries genuinely deterministic, at the cost
of these tests pinning an `ORDER BY` clause unrelated to what they are named for, plus the
derived-table wrap risk noted above.

Either way the SQLite side should stay positional — its incidental insertion order is the deliberate
reference shape that the other three dialects mirror.
