## Summary

- Closes #332

Nine tests in `CrossDialectJoinTests` asserted row values **positionally** on the PostgreSQL, MySQL
and SQL Server sides of a `users → orders` join carrying no top-level `ORDER BY`. They passed only
by accident of each planner's chosen access path — a statistics refresh, a parallel scan, or a hash
join picked instead of a nested loop could reorder the result set and turn them red with no code
change.

This converts the real-provider assertions to order-independent `Is.EquivalentTo`. Every row and
every value stays asserted; only the accidental ordering assumption is dropped.

## Reason for Change

The order these tests encoded was `orders.OrderId` ascending (seed insertion order), but `OrderId`
is not in the projection, so it cannot be a client-side sort key — and the only projected
discriminator, `Total`, runs *descending* within the Alice group. This is why the #314
`SortedByAsync` sweep, which now covers 118 sites, deliberately skipped these nine: a
plausible-looking `(r.UserName, r.Total)` key compiles, reads as correct, and silently swaps rows
`[0]` and `[1]`.

State the trap precisely, because the imprecise version is dangerous: `(UserName, Total)` **is** a
total order over these rows. It is just an *ascending* one, so it orders the two Alice rows opposite
to the asserted sequence. Sorting could only be made to work by rewriting the expected sequence to
fit the key — exactly what `llm-testing.md` forbids. The three navigation-aggregate variants offer
no escape either: both Alice rows tie on the aggregate column as well (`OrderTotal` 325.50,
`OrderCount` 2, `MaxAddrId` 2), so it contributes no discriminator beyond `Total`.

## Impact

**The pinned SQL is untouched.** No chain, no `Prepare()`, no `Select`/`Where`/`Join`, and no
expected-SQL string was modified on this branch — verified mechanically across all branch commits.
The `AssertDialects(...)` block is the bulk and the actual purpose of each of these tests, and it is
byte-identical after the change. `ManifestOutput/` goldens are unchanged, confirming no chain
regenerated.

The SQLite side stays positional throughout: its incidental insertion order is the deliberate
reference shape the other three dialects mirror, and keeping it positional preserves that signal.

Two files change: `src/Quarry.Tests/SqlOutput/CrossDialectJoinTests.cs` and `llm-testing.md`. No
production source, public API, analyzer, or generator is touched.

## Plan items implemented as specified

| Step | Tests |
|---|---|
| 1 — plain `(UserName, Total)` | `Join_InnerJoin_OnClause`, `Join_WithWhere_OnLeftTable`, `Join_WithWhere_TwoCapturedParams_BooleanBetween_SequentialIndices`, `Where_BeforeJoin_GetsTableAliasQualification` |
| 2 — named-tuple accessor | `Join_InnerJoin_NamedTupleProjection`, `Join_ThreeTable_NamedTupleProjection` |
| 3 — aggregate 3-tuple | `Select_Joined_Many_Sum_OnLeftTable`, `Select_Joined_Many_Count_OnLeftTable`, `Select_Joined_HasManyThrough_Max_OnLeftTable` |
| 4 — docs | `<remarks>` block rewritten; `llm-testing.md:135` updated |

The two named-tuple tests exist to prove named element access survives the join boundary, so their
accessors are projected through `.Select(r => (r.Name, r.Amount))` into `Is.EquivalentTo` rather
than letting the names appear only in the expected literal — this exercises them on every row.

## Deviations from plan implemented

**Hoisted `expected` arrays.** The plan specified the inline `Is.EquivalentTo(new[] { … })` form.
During step 4 it emerged that `Join_FiveTable_Select` — already on `master`, in this same file —
does exactly this conversion with a hoisted `var expected = new[] { … }` and cites #332 in its
comment. That is the established local pattern and the direct precedent for this work, so all nine
were reworked to match (one `expected` per test, net −46 lines). Deliberately not hoisted to a
fixture-level static, so expected values stay visible where they are read.

## Gaps in original plan implemented

- **`Join_ThreeTable_NamedTupleProjection` coverage.** It asserted only row `[0]` plus a
  `Product is not null` check. There is no order-independent way to say "row `[0]` is Alice/250.00"
  — that row is not the ascending minimum over any projected column (75.50 < 250.00,
  "Gadget" < "Widget"). All four dialects now pin the full three-row multiset with exact product
  names. Seeded `order_items` are one per order, so that set is fully determined. Strictly stronger
  than what it replaced.
- **`<remarks>` names the exempt sites.** Three tests in this file keep `SortedByAsync(r => r.UserName)`
  and are correct as-is — each returns two rows with distinct usernames, so the key is a total order
  that reproduces the asserted sequence. The block now names them, so a future row-order sweep does
  not convert them and lose genuine order coverage.

## Migration Steps

None. Test-only change.

## Performance Considerations

None. `Is.EquivalentTo` over three-element collections; no query, chain, or generated SQL changed.

## Security Considerations

None. No credential, connection-string, input-validation, or SQL-construction surface is touched.

## Breaking Changes

None — consumer-facing or internal. No production code, public API, or generated output changes.

---

### Verification

- Baseline before any change: **3501 passed / 0 failed** (`Quarry.Tests`), plus 201/201 in
  `Quarry.Migration.Tests`. Docker available, nothing `Assert.Ignore`d — the container-backed
  dialects really executed.
- After implementation and again after review remediation: **3501 passed / 0 failed**. No new or
  changed test count, as expected for an assertion-shape change.
- `Is.EquivalentTo` over `ValueTuple` was confirmed empirically rather than assumed: temporarily
  changing an expected `("Bob", 150.00m)` to `151.00m` failed with a precise
  `Missing (1)` / `Extra (1)` diff. The converted assertions compare elements structurally.

### Review

Six findings, all addressed in `07f445a` — 4 classified A, 2 classified B, none deferred or
dismissed. The one worth calling out: the first `<remarks>` rewrite claimed "no total order over the
projection exists at all" for the aggregate variants, which is **false** — the rows are pairwise
distinct. Since `llm-testing.md` now directs readers to that block as the canonical explanation, a
maintainer who checked the claim would either distrust the rest of the rationale or invert it ("a
total order does exist, so I may sort") and reintroduce the very bug the block exists to prevent.
Corrected to the accurate, narrower statement.
