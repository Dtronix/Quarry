# Plan: 332-crossdialect-join-row-order

## Approach

Convert the real-provider (`pg` / `my` / `ss`) assertions in the nine affected tests from positional
indexing to order-independent multiset equality via NUnit's `Is.EquivalentTo`. The SQLite side stays
positional throughout — its incidental insertion order is the deliberate reference shape the other
three dialects mirror, and keeping it positional preserves that signal.

No chain is touched. No `Prepare()` call, no `Select`, no `Where`, no expected-SQL string changes.
The `AssertDialects(...)` block — the actual point of these tests — is byte-identical after the
change. Because no chain changes, the SQL manifest goldens in `ManifestOutput/` should not
regenerate; a post-build `git status` check confirms this rather than assuming it.

### Why not `SortedByAsync`

The asserted order is `orders.OrderId` ascending, and `OrderId` is not projected. The only projected
discriminator, `Total`, runs *descending* within the Alice group (250.00 then 75.50), so
`.SortedByAsync(r => (r.UserName, r.Total))` compiles, reads as correct, and silently swaps rows
`[0]` and `[1]`. The three aggregate variants are worse — both Alice rows tie on the aggregate
column too (`OrderTotal` 325.50, `OrderCount` 2, `MaxAddrId` 2), so it breaks no tie either. This is
exactly the trap the file's `<remarks>` block warns about, and why the #314 sweep skipped these nine.

### What `Is.EquivalentTo` preserves

Every row and every value stays asserted; only the accidental ordering assumption is dropped.
`Is.EquivalentTo` is exact multiset equality — an extra row, a missing row, or a wrong value all
still fail. The existing `Has.Count.EqualTo(n)` line is kept above each one: strictly redundant, but
a count mismatch reports far more legibly than a multiset diff, and it mirrors the SQLite side.

### Three assertion shapes

The nine tests fall into three shapes, which is how the steps below are grouped.

**Plain positional tuple** (four tests) — the direct issue-suggested form:

```csharp
var pgResults = await pg.ExecuteFetchAllAsync();
Assert.That(pgResults, Has.Count.EqualTo(3));
Assert.That(pgResults, Is.EquivalentTo(new[]
{
    ("Alice", 250.00m),
    ("Alice",  75.50m),
    ("Bob",   150.00m),
}));
```

**Named-tuple accessor** (two tests) — these exist to prove named element access survives the join
boundary, so the accessors must still be exercised on the actual rows, not just appear in the
expected literal. Projecting through `.Select(...)` keeps that:

```csharp
Assert.That(pgResults.Select(r => (r.Name, r.Amount)), Is.EquivalentTo(new[]
{
    ("Alice", 250.00m),
    ("Alice",  75.50m),
    ("Bob",   150.00m),
}));
```

**Three-element aggregate tuple** (three tests) — same as the plain shape with a third element.

### Seed data the expected sets are derived from

From `QueryTestHarness.SeedData` (`QueryTestHarness.cs:604-621`):

```
users:       1 Alice (active), 2 Bob (active), 3 Charlie (inactive)
orders:      OrderId 1 → UserId 1, Total 250.00
             OrderId 2 → UserId 1, Total  75.50
             OrderId 3 → UserId 2, Total 150.00
order_items: 1 → OrderId 1 'Widget', 2 → OrderId 2 'Gadget', 3 → OrderId 3 'Widget'
```

One `order_item` per order, so the three-table join yields exactly three rows and its full
`(User, Amount, Product)` multiset is knowable.

### Scope note on `Join_ThreeTable_NamedTupleProjection`

This test currently asserts only `[0]` (`User == "Alice"`, `Amount == 250.00m`, `Product is not
null`) under `Has.Count.EqualTo(3)`. There is no order-independent rendering of "row `[0]` is Alice"
— `(Alice, 250.00, Widget)` is not the ascending minimum over any projected column (75.50 < 250.00,
"Gadget" < "Widget"). The conversion therefore asserts the full three-row multiset with real product
names, which is **strictly stronger** than what it replaces. `Product is not null` becomes an exact
value check, which is the point of the projection anyway.

## Steps

- [x] **1. Plain `(UserName, Total)` tests → `Is.EquivalentTo`**
  `Join_InnerJoin_OnClause`, `Join_WithWhere_OnLeftTable`,
  `Join_WithWhere_TwoCapturedParams_BooleanBetween_SequentialIndices`,
  `Where_BeforeJoin_GetsTableAliasQualification`.
  Convert the `pg`/`my`/`ss` blocks in each; leave the `lt` block positional. Note the two-captured-
  params test has only two rows (`("Alice", 250.00m)`, `("Alice", 75.50m)`) — Bob is filtered by
  `UserName == userName`.
  *Tests:* these are the tests. Verification = suite green, plus a deliberate temporary mutation of
  one expected value locally to confirm `Is.EquivalentTo` over `ValueTuple` actually compares
  elements rather than silently passing (no existing call site in the suite compares tuples).

- [x] **2. Named-tuple tests → accessor projection + `Is.EquivalentTo`**
  `Join_InnerJoin_NamedTupleProjection` (3 rows, `(Name, Amount)`),
  `Join_ThreeTable_NamedTupleProjection` (3 rows, `(User, Amount, Product)` — see scope note above).
  Keep the `// Verify named element access works across join boundaries` comment and make sure the
  projection lambda is what carries it. SQLite side stays positional.
  Depends on step 1 only for the tuple-equality confidence it establishes.

- [x] **3. Aggregate tests → `Is.EquivalentTo`**
  `Select_Joined_Many_Sum_OnLeftTable` (`325.50m`/`325.50m`/`150.00m`),
  `Select_Joined_Many_Count_OnLeftTable` (`2`/`2`/`1`),
  `Select_Joined_HasManyThrough_Max_OnLeftTable` (`2`/`2`/`1`).
  Preserve each test's existing explanatory comment about what the aggregate column holds.

- [x] **4. Docs**
  Rewrite the `CrossDialectJoinTests` `<remarks>` block: it currently declares these an open flake
  tracked in #332 and warns against fixing them. It should instead explain why this file uses
  `Is.EquivalentTo` where the rest of the suite uses `SortedByAsync` — the trap is still worth
  documenting so nobody "simplifies" it back to a sort later.
  Update `llm-testing.md:135`, which says `CrossDialectJoinTests` "has nine such tests, tracked in
  #332", to record them as resolved and point at the assertion-side remedy.
  *Verification:* `dotnet build` then `git status` on `src/Quarry.Tests/ManifestOutput` — must be
  clean, confirming no chain changed.
