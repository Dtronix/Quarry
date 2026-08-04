# Workflow: 332-crossdialect-join-row-order

## Config
platform: github
base-branch: master

## State
phase: FINALIZE
status: active
issue: #332
pr: #337

## Problem Statement

Nine tests in `src/Quarry.Tests/SqlOutput/CrossDialectJoinTests.cs` assert row values
**positionally** on the PostgreSQL, MySQL and SQL Server sides of a `users → orders` join that
carries no top-level `ORDER BY`. They pass only by accident of each planner's chosen access path.

The order encoded is `orders.OrderId` ascending (seed insertion order), but `OrderId` is not in the
projection, and the only projected discriminator (`Total`) runs *descending* within the Alice group.
So no client-side `SortedByAsync` key over projected columns reproduces the asserted sequence — this
is why the #314 row-order sweep deliberately skipped these nine.

Affected tests:

| Test | Shape |
|---|---|
| `Join_InnerJoin_OnClause` | `(UserName, Total)` × 3 rows |
| `Join_WithWhere_OnLeftTable` | `(UserName, Total)` × 3 rows |
| `Join_InnerJoin_NamedTupleProjection` | `(Name, Amount)` × 3 rows, per-element accessor asserts |
| `Join_ThreeTable_NamedTupleProjection` | `(User, Amount, Product)` × 3 rows, `[0]`-only asserts |
| `Join_WithWhere_TwoCapturedParams_BooleanBetween_SequentialIndices` | `(UserName, Total)` × 2 rows |
| `Where_BeforeJoin_GetsTableAliasQualification` | `(UserName, Total)` × 3 rows |
| `Select_Joined_Many_Sum_OnLeftTable` | `(UserName, Total, OrderTotal)` × 3 rows |
| `Select_Joined_Many_Count_OnLeftTable` | `(UserName, Total, OrderCount)` × 3 rows |
| `Select_Joined_HasManyThrough_Max_OnLeftTable` | `(UserName, Total, MaxAddrId)` × 3 rows |

The three aggregate variants tie on the aggregate column as well (both Alice rows share
`OrderTotal` 325.50 / `OrderCount` 2 / `MaxAddrId` 2), so the aggregate does not break the tie.

### Baseline
`dotnet test src/Quarry.Tests` on b03e246 (2026-08-04): **Failed: 0, Passed: 3501, Skipped: 0,
Total: 3501, Duration 2m 1s.** No pre-existing failures. Docker was available — nothing was
`Assert.Ignore`d, so the container-backed dialects really executed.

`dotnet test src/Quarry.Migration.Tests` on the same commit: **Failed: 0, Passed: 201, Skipped: 0.**
Untouched by this work (it changes only `Quarry.Tests` assertions and `llm-testing.md`), recorded
for completeness.

Build emits pre-existing warnings unrelated to this work: many `CS0219 __colShift assigned but
never used` in generated `MyDb.Interceptors.*.g.cs`, and one `NUnit2009` in
`IR/PipelineModelEqualityTests.cs:331`. Both predate the branch.

## Decisions

- **2026-08-04 — Remedy: assertion-side `Is.EquivalentTo`, not query-side `ORDER BY`.**
  The issue's preferred option. Every row and value stays asserted; only the accidental ordering
  assumption is dropped. Critically it leaves the pinned SQL untouched — the `AssertDialects(...)`
  block is the bulk and actual purpose of each of these tests, and the alternative would rewrite 36
  expected-SQL strings to pin an `ORDER BY` unrelated to what the tests are named for (plus the
  derived-table wrap risk noted in the issue).
- **2026-08-04 — SQLite side stays positional.** Per the issue and `llm-testing.md:119`, SQLite's
  incidental insertion order is the deliberate reference shape the other three dialects mirror.
  Converting it too would erase that signal. Only `pg`/`my`/`ss` assertions change.
- **2026-08-04 — Named-tuple tests keep exercising their accessors.**
  `Join_InnerJoin_NamedTupleProjection` and `Join_ThreeTable_NamedTupleProjection` exist to prove
  named element access survives the join boundary. Feeding `Is.EquivalentTo` a
  `.Select(r => (r.Name, r.Amount))` projection keeps `.Name`/`.Amount` exercised on the actual rows
  rather than letting the names appear only in the expected literal.
- **2026-08-04 — `Join_ThreeTable_NamedTupleProjection` gains coverage.** It asserts only `[0]`
  today, and no order-independent rendering of "row `[0]` is Alice" exists. The conversion asserts
  the full three-row multiset with real product names — strictly stronger than what it replaces,
  and a deliberate (small) scope increase over a pure ordering fix.
- **2026-08-04 — Docs updated, `<remarks>` rewritten rather than deleted.** The block currently
  declares these an open flake and warns against fixing them. The underlying trap — that
  `(UserName, Total)` looks like a valid sort key and silently reorders — is still worth documenting
  so the file isn't "simplified" back to `SortedByAsync` later. `llm-testing.md:135` updated to
  match.

## Working Notes

- Seed data confirmed from `QueryTestHarness.SeedData` (`QueryTestHarness.cs:604-621`):
  users 1 Alice / 2 Bob / 3 Charlie; orders (1→u1, 250.00), (2→u1, 75.50), (3→u2, 150.00);
  order_items one per order — (1→o1 'Widget'), (2→o2 'Gadget'), (3→o3 'Widget'). The
  one-item-per-order shape means the three-table join yields exactly three rows, so its full
  `(User, Amount, Product)` multiset is knowable and assertable.
- The file carries a `<remarks>` block (lines 9–19) documenting the #332 flake and warning against a
  mechanical `SortedByAsync` fix. `llm-testing.md:135` also points at #332. Both are stale once fixed.
- **Correction to an earlier note in this file:** I first recorded that no existing `Is.EquivalentTo`
  call site compares tuples. That was wrong — it came from a truncated grep. `Join_FiveTable_Select`
  in *this same file* already does exactly what #332 asks for, including a `#332` reference in its
  comment: a hoisted `var expected = new[] { … }` of 4-tuples asserted with `Is.EquivalentTo`
  against all three real providers, with the SQLite side left positional. It is the established
  local pattern and the direct precedent for this work.
- Because of that precedent, the nine conversions were reworked to hoist a per-test
  `var expected = new[] { … }` instead of repeating the literal array three times per test. Same
  assertions, ~3× less text, and consistent with the neighbouring test. Do not hoist this to a
  fixture-level static — keeping it per-test keeps the expected values visible where they are read.
- **Confirmed empirically (step 1):** NUnit's `Is.EquivalentTo` *does* compare `ValueTuple` elements
  structurally. Temporarily changing an expected `("Bob", 150.00m)` to `("Bob", 151.00m)` failed
  with a precise diff — `Missing (1): < ("Bob", 151m) >` / `Extra (1): < ("Bob", 150m) >`. So the
  converted assertions have real teeth; they are not silently passing on reference equality.
  Also worth noting from that output: decimal scale is not part of the comparison (`150.00m`
  renders and compares as `150m`), which is what we want across four providers with different
  decimal type mappings.
- Three *other* sites in this file still index `pgResults[0]`/`[1]` positionally (around lines 161,
  556, 1047 post-change) and are **correct as-is** — each is preceded by
  `.SortedByAsync(r => r.UserName)` over a two-row result whose usernames are distinct (Alice, Bob),
  which is a genuine total order. Those were handled by the #314 sweep and are out of scope here.
  Worth knowing so a future sweep doesn't "finish the job" by converting them too.
- **The precise statement of the #332 trap matters, and my first `<remarks>` rewrite got it wrong.**
  I wrote that for the aggregate variants "no total order over the projection exists at all". False:
  the three rows are pairwise distinct (`Total` separates the two Alice rows), so
  `(UserName, Total)` *is* a total order. The real constraint is narrower — no *ascending* key
  reproduces the *originally asserted sequence* (250.00 before 75.50), and making a sort work would
  mean rewriting the expected sequence to fit the key, which `llm-testing.md` explicitly forbids.
  The aggregate column is not "worse still"; it simply adds no discriminator beyond `Total`.
  This matters because `llm-testing.md:135` now sends readers to that `<remarks>` as the canonical
  explanation — a maintainer who checks a false claim either distrusts the rest or inverts it
  ("a total order does exist, so I may sort") and reintroduces the bug. Caught as F3 in review.
- `Assert.That` throws on first failure rather than collecting, so only the first mutated dialect
  block reported. Not a concern for the real assertions, but it means a multi-dialect row-order
  regression would surface one dialect at a time.

## Suspend State

## Session Log

| Date | Phases | Summary |
|------|--------|---------|
| 2026-08-04 | INTAKE → DESIGN | Loaded issue #332, created worktree/branch, read all nine test sites and seed data. Baseline 3501/3501. |
| 2026-08-04 | PLAN → IMPLEMENT | All 4 steps committed (80895b3, 2abee88, ff9d8e2, 7428a1a). Full suite 3501/3501, matching baseline. Manifest goldens unchanged. |
| 2026-08-04 | → REVIEW | `origin/master` had not moved from b03e246, so no rebase was needed. Delegated analysis pass. |
| 2026-08-04 | REVIEW → REMEDIATE | 6 findings (1 M, 5 L), classified 4A/2B, none deferred or dismissed. All fixed in 07f445a; full suite 3501/3501. |
| 2026-08-04 | REMEDIATE | Pushed branch; opened PR #337. |
