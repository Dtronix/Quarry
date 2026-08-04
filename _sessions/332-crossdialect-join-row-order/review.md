# Review: 332-crossdialect-join-row-order

## Classifications

| ID | Class | Rec | Sev | Section | Finding | Action Taken |
|----|-------|-----|-----|---------|---------|--------------|
| F3 | A | A | M | Codebase Consistency | `<remarks>` claim "no total order over the projection exists at all" is false — the rows are pairwise distinct | Fixed. `<remarks>` rewritten: the trap is now stated precisely (`(r.UserName, r.Total)` IS a total order, but an ascending one that swaps rows [0]/[1]; sorting would require rewriting the expected sequence, which llm-testing.md forbids). Aggregate para reworded to 'adds no discriminator beyond Total'. The three inline comments reworded to match. |
| F1 | A | A | L | Plan Compliance | `plan.md` still specifies the inline array form; never amended for the hoisted `var expected` refactor | Fixed. plan.md gained an 'Amendment (during IMPLEMENT): hoist expected' section recording the switch, the Join_FiveTable_Select precedent, and the deliberate choice not to use a fixture-level static. |
| F4 | A | A | L | Codebase Consistency | Comment says positional asserts reached "only two" rows; they reached all three | Fixed. Comment rewritten to describe what the .Select projection actually does; the false 'only on the two' row count removed. |
| F6 | A | A | L | Codebase Consistency | Two new comments use `--` where the file uses `—` | Fixed. Both `--` occurrences replaced with em dashes. |
| F2 | B | B | L | Test Quality | `Join_ThreeTable_NamedTupleProjection` SQLite side still asserts only `[0]`, weaker than its three mirrors | Fixed. SQLite side now asserts all three rows positionally with exact product names, matching its mirrors' strength while staying positional as the reference dialect. |
| F5 | B | B | L | Codebase Consistency | `<remarks>` says "Several tests" without naming the three deliberately-exempt `SortedByAsync` sites | Fixed. `<remarks>` gained a closing paragraph naming Join_WithWhere_OnRightTable, Join_WithWhere_MultiParamAndBoolColumn_SequentialParamIndices and Join_WithWhere_CapturedParam_OnRightTable as correct-as-sorts, with the reason. |

Scope reviewed: `git diff origin/master...HEAD -- . ':(exclude)_sessions'` (2 files, +362/-97 excluding
session artifacts), against `plan.md`, the `## Decisions` / `## Working Notes` in `workflow.md`, the
full current `CrossDialectJoinTests.cs`, `llm-testing.md:117-136`, and `QueryTestHarness.SeedData`
(`QueryTestHarness.cs:600-670`).

## Plan Compliance

| ID | Finding | Sev | Why It Matters |
|----|---------|-----|----------------|
| F1 | `plan.md` "Three assertion shapes" still specifies the inline form `Is.EquivalentTo(new[] { … })` for all three shapes; every one of the nine conversions instead hoists a per-test `var expected = new[] { … }`. The refactor itself is **justified and correctly executed** — it matches the pre-existing local precedent `Join_FiveTable_Select` (`CrossDialectJoinTests.cs:1035-1051`, on `origin/master` already), and all nine hoists were verified: correct arity, correct element order relative to each dialect assert, one `expected` per test with no fixture-level static and no leakage between tests. But `plan.md` was never amended to match; only `workflow.md`'s Working Notes record the switch. | L | The plan is the spec of record for this session. Anyone reconciling `plan.md` against the diff (a later reviewer, or the PR description generator) reads a mismatch in all nine sites and has to go hunting in `workflow.md` to learn it was deliberate. |

No other divergence: all four plan steps are implemented as written; the only scope increase is the
one the plan explicitly sanctions (`Join_ThreeTable_NamedTupleProjection`, see F2); no chain, no
`Prepare()`, no `Select`/`Where`, and no `AssertDialects` expected-SQL string was touched; the SQLite
side stays positional in all nine as decided.

## Correctness

No concerns.

All nine expected sets were checked row-by-row against the seed (`users` 1 Alice/2 Bob/3 Charlie
inactive; `orders` (1→u1, 250.00), (2→u1, 75.50), (3→u2, 150.00); `order_items` (1→o1 'Widget'),
(2→o2 'Gadget'), (3→o3 'Widget'); `user_addresses` u1→{1,2}, u2→{1}):

- `Join_InnerJoin_OnClause` (61), `Join_WithWhere_OnLeftTable` (110), `Join_InnerJoin_NamedTupleProjection` (241), `Where_BeforeJoin_GetsTableAliasQualification` (637) — 3 rows `(Alice,250.00) (Alice,75.50) (Bob,150.00)`; the `IsActive` filters drop only Charlie, who has no orders anyway. ✓
- `Join_ThreeTable_NamedTupleProjection` (290) — `(Alice,250.00,Widget) (Alice,75.50,Gadget) (Bob,150.00,Widget)`; one `order_item` per order so the multiset is fully determined. ✓
- `Join_WithWhere_TwoCapturedParams_BooleanBetween_SequentialIndices` (584) — 2 rows, Bob correctly excluded by `UserName == "Alice"`, both Alice totals > 50. ✓
- `Select_Joined_Many_Sum_OnLeftTable` (827) `325.50/325.50/150.00`, `Select_Joined_Many_Count_OnLeftTable` (878) `2/2/1`, `Select_Joined_HasManyThrough_Max_OnLeftTable` (935) `2/2/1` (Alice→addr{1,2} max 2, Bob→addr{1} max 1). ✓

Every `Has.Count.EqualTo(n)` agrees with its `expected.Length` (3/3, 3/3, 3/3, 3/3, 2/2, 3/3, 3/3,
3/3, 3/3). No row was dropped, no value altered in transit, and no `expected` array is shared across
tests. Reusing one array across the three dialects within a test is sound: all four dialects run the
same seed and the same projection, so any legitimate per-dialect difference would be a bug. The only
assertion strength lost is the ordering assumption itself; `Join_ThreeTable_NamedTupleProjection`'s
real-provider sides got strictly stronger (`Product is not null` → exact `"Widget"`/`"Gadget"`).

## Security

No concerns. Test-only change: no credential, connection-string, input-validation, or SQL-construction
surface is touched, and the two edited files are a test fixture and a contributor doc.

## Test Quality

| ID | Finding | Sev | Why It Matters |
|----|---------|-----|----------------|
| F2 | `Join_ThreeTable_NamedTupleProjection` now has an inverted-strength asymmetry: the pg/my/ss sides assert the full 3-row `(User, Amount, Product)` multiset with exact product names (`CrossDialectJoinTests.cs:297-307`), while the SQLite side still asserts only `results[0].User`, `results[0].Amount` and `results[0].Product is not null` (`:280-284`). The fixture `<remarks>` (`:27-28`) states SQLite "is the deliberate reference shape the other three dialects mirror" — here the reference pins strictly less than its mirrors. Concrete failure: a SQLite-only materialization regression that puts the wrong column in `Product` (or swaps rows 1/2 so `results[0]` becomes `Gadget`) is caught by all three real providers and passes on SQLite, which is the one dialect that runs without Docker. Fix is one line per row: `Assert.That(results[0], Is.EqualTo(("Alice", 250.00m, "Widget")))` and the same for `[1]`/`[2]`. | L | The plan's sanctioned coverage increase was applied to only three of the four dialects, leaving the reference dialect as the weakest assertion in the test. |

Other test-quality checks came back clean:

- `Is.EquivalentTo` is NUnit's `CollectionEquivalentConstraint` — **exact multiset equality**, not
  subset: an extra row, a missing row, a duplicated row or a wrong element all fail. The retained
  `Has.Count.EqualTo(n)` line above each one is redundant but improves the failure message, and it
  independently pins cardinality. The empirical mutation check recorded in `workflow.md` (changing
  `("Bob", 150.00m)` → `151.00m` produced a precise Missing/Extra diff) confirms `ValueTuple`
  elements really are compared structurally rather than by reference.
- No test is strictly weaker than before except in the ordering dimension, which is the declared
  point of the change. Decimal-scale insensitivity (`150.00m` ≡ `150m`) is unchanged from the old
  `Is.EqualTo` form and is desirable across four providers with different decimal mappings.
- The SQLite-positional / real-provider-order-independent split is applied consistently in all nine.
- The named-tuple tests keep exercising their accessors on real rows via `.Select(r => (r.Name, r.Amount))`
  rather than letting the names appear only in the expected literal (`:250`, `:299`), as decided.

## Codebase Consistency

| ID | Finding | Sev | Why It Matters |
|----|---------|-----|----------------|
| F3 | The rewritten `<remarks>` claims (`CrossDialectJoinTests.cs:19-22`): "The three navigation-aggregate variants are worse still: both Alice rows tie on the aggregate column too … so **no total order over the projection exists at all**." That is false. The three projected rows are pairwise distinct — e.g. `("Alice",250.00,2)`, `("Alice",75.50,2)`, `("Bob",150.00,1)` — so lexicographic ordering over the projection *is* a total order, and `.SortedByAsync(r => (r.UserName, r.Total))` would in fact be deterministic here. The accurate statement is narrower: no *ascending* key reproduces the **originally asserted sequence** (250.00 before 75.50), and using a valid key would require rewriting the expected sequence — which `llm-testing.md:135` forbids. By the same token the aggregate variants are not "worse still": `Total` already distinguishes the two Alice rows in every one of the nine, so the aggregate column adds nothing either way. The same overstatement is echoed in three inline comments — `:825-826`, `:877`, `:934` ("the aggregate breaks no tie either") — which imply a tie that `Total` has already broken. | M | This block is the load-bearing justification for a `<remarks>` that instructs future maintainers "do not 'simplify' them back to a sort", and `llm-testing.md:135` now explicitly redirects readers to it ("Its `<remarks>` block explains…"). A maintainer who checks the claim finds it demonstrably false and has no reason to trust the rest of the rationale — or inverts it ("a total order does exist here, so I may sort") and silently reorders the assertions the doc was written to protect. |
| F4 | The new comment at `CrossDialectJoinTests.cs:239-240` in `Join_InnerJoin_NamedTupleProjection` reads "…projected through `.Select` so it is exercised on every row rather than only on **the two** the positional asserts used to reach." The pg/my/ss asserts it replaced reached all **three** rows (`[0]`, `[1]`, `[2]` — six asserts per dialect on `origin/master`). The sentence appears to have been written for `Join_ThreeTable_NamedTupleProjection`, the test that really did assert only `[0]`, and landed on the wrong one. It is also anchored to the `var expected` array rather than to the `.Select(...)` projection it describes. | L | Anyone diffing this test against master finds the comment contradicted by the removed lines, which undercuts the credibility of the surrounding rationale; and it obscures where the genuine coverage increase actually happened. |
| F5 | The `<remarks>` opens with "**Several** tests here assert their pg/my/ss rows with `Is.EquivalentTo`" and never enumerates them (the previous block named "Nine tests"). Meanwhile three tests in the same file deliberately keep `SortedByAsync` — `Join_WithWhere_OnRightTable` (`:154-167`), `Join_WithWhere_MultiParamAndBoolColumn_SequentialParamIndices` (`:533-546`) and `Join_WithWhere_CapturedParam_OnRightTable` (`:986-999`) — each correct because it sorts a 2-row result on distinct usernames. That reasoning exists only in `workflow.md`'s Working Notes, which is not a durable repo artifact. | L | The `<remarks>` says "do not 'simplify' them back to a sort" without saying which "them" is; a future row-order sweep can just as easily read it as licence to convert the three `SortedByAsync` sites to `Is.EquivalentTo` (losing genuine order coverage) as to leave them alone. One sentence naming the exception class would close it. |
| F6 | Two of the new comments use an ASCII double-hyphen where the file (and the `Join_FiveTable_Select` precedent it is modelled on) uses an em dash: `CrossDialectJoinTests.cs:286` ("…Alice/250.00" **--** that row is not the…") and `:826` ("**--** the real-provider sides have to be order-independent."). Compare `:1032-1034`, `:800`, `:1116`, and the other seven new comments, all of which use "—". | L | Cosmetic, but this change's whole justification for the hoist refactor was matching the local precedent; the two outliers make the new comments visibly non-uniform in a file that is otherwise consistent. |

Consistency checks that came back clean: the hoisted variable is named `expected` in all nine,
matching `Join_FiveTable_Select`; it is declared after the SQLite positional asserts and before the
first real-provider block, same as the precedent; each explanatory comment sits immediately above
the array as in the precedent; and each aggregate test's pre-existing comment about what the
aggregate column holds (`:818`, `:870`, `:910`) was preserved as the plan required.

## Integration / Breaking Changes

No concerns.

The branch touches exactly two files — `src/Quarry.Tests/SqlOutput/CrossDialectJoinTests.cs` and
`llm-testing.md`. No production source, no public API, no analyzer, no generator, and no file under
`src/Quarry.Tests/ManifestOutput` is modified; `git status` is clean apart from `workflow.md`,
corroborating the plan's "no chain changed, so no golden regenerates" verification. A repo-wide grep
for `#332` outside `obj/` and `_sessions/` returns exactly three live references, all consistent
after the edits:

- `llm-testing.md:135` — rewritten from "has nine such tests, tracked in #332" to the resolved form; accurate.
- `CrossDialectJoinTests.cs:25` — the new `<remarks>` "Resolved in #332…"; accurate apart from F3.
- `CrossDialectJoinTests.cs:1034` — the pre-existing `Join_FiveTable_Select` comment "(see the fixture `<remarks>` and #332)"; still resolves correctly, since the `<remarks>` block still exists and still covers that test's shape.

No stale reference to the old "known flake / do not fix" framing survives anywhere in the repo. The
`<see cref="RowOrderExtensions.SortedByAsync{T, TKey}"/>` in the rewritten `<remarks>` still binds
(the type is referenced and the method is still used three times in this same file), so the XML doc
build is unaffected.
