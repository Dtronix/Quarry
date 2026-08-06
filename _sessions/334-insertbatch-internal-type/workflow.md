# Workflow: 334-insertbatch-internal-type

## Config
platform: github
base-branch: master

## State
phase: FINALIZE
status: active
issue: #334
pr: #340

## Problem Statement

`InsertBatch(...)` interceptors emit an unconditional call to
`Quarry.Internal.BatchInsertSqlBuilder.Build(...)`, but that type is `internal` to the `Quarry`
assembly. Any consumer outside Quarry's `InternalsVisibleTo` list therefore fails to compile with
`CS0122: 'BatchInsertSqlBuilder' is inaccessible due to its protection level` in the generated
`*.Interceptors.*.g.cs`.

Emission sites: `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs:518` (diagnostics terminal) and
`:559` (carrier NonQuery/Scalar terminal).

Masked in-repo because every project exercising `InsertBatch` (`Quarry.Tests`, `Quarry.Benchmarks`,
`Quarry.Sample.WebApp`) holds a friend grant. The only non-friend compilation in the repo is the
synthetic `CSharpCompilation` inside `Generation/InterceptorBindingGuardTests.cs`, added in #314,
where the two `InsertBatch` shapes are currently pinned as
`KnownBug_Issue334_BatchInsert_ReferencesInternalType`.

Issue #334 also asks for a recurrence guard: audit every type a generated interceptor can name and
assert accessibility.

### Baseline test status
Clean. No pre-existing failures.

- `dotnet test src/Quarry.Tests` — 3501 passed, 0 failed, 0 skipped (2 m 30 s)
- `dotnet test src/Quarry.Migration.Tests` — 201 passed, 0 failed, 0 skipped (6 s)

Pre-existing build warnings (not failures, out of scope): CS0219 `__colShift` assigned but never
used across generated `*.Interceptors.*.g.cs`, and NUnit2009 at
`src/Quarry.Tests/IR/PipelineModelEqualityTests.cs:331`.

## Decisions

### 2026-08-04 — Fix shape: promote `BatchInsertSqlBuilder` to public
Make the type `public` with `[EditorBrowsable(EditorBrowsableState.Never)]`, and promote
`MaxParameterCount` to `public` alongside it. Chosen over a public forwarder shim.

**Why:** this is exactly the existing convention for the emitted runtime surface — `OpId`,
`QueryExecutor`, `QueryLog` and `ParameterLog` are all `public` + `[EditorBrowsable(Never)]`. The
`Quarry.Internal` namespace already signals "not for consumers" without a second type name, and a
forwarder would add an indirection on every batch-insert execution plus a name to keep in sync.
`MaxParameterCount` goes public because the now-public `Build` documents it in its
`<exception>` cref and the 2100 ceiling is consumer-visible behaviour (documented in `llm.md:255`).

### 2026-08-04 — Recurrence guard: broaden the non-friend compilation matrix
Return the two `InsertBatch` shapes to `GenericTerminalShapes`, delete the #334 pin, and extend
`Generation/InterceptorBindingGuardTests.cs` to cover the emitter families the matrix never reaches
today (joins, set operations, raw SQL, collection/`IN`, conditional masks, `Prepare`/`ToDiagnostics`,
aggregates, window functions, CTEs). Add a dedicated accessibility assertion so a `CS0122` reads as
an accessibility break rather than as a generic "fixture does not compile".

**Why:** discovery-driven rather than convention-driven. A hand-maintained "these types must be
public" list was rejected because it rots — and because a blanket "everything in `Quarry.Internal`
is public" rule is provably wrong (`ScalarConverter` is a legitimately internal runtime-private
helper in that namespace). Since every in-repo project is a friend assembly, a synthetic non-friend
`CSharpCompilation` is the only mechanism that can observe this class of defect at all.

### 2026-08-04 — Fix the `QueryDiagnostics` constructor in this branch (scope expansion)
Make the constructor `public` + `[EditorBrowsable(Never)]` here rather than splitting it to a
follow-up issue.

**Why:** it is the same root cause as #334 (emitted code naming a surface consumers cannot reach),
and #334's own text asks for precisely this audit — "a second instance would fail the same way". The
broadened matrix surfaces it on every `ToDiagnostics` shape in steps 3/5/7 anyway, so splitting would
mean deliberately pinning a known-broken headline API out of the very guard being built in this PR.
All six sibling diagnostic types the same emitted code constructs are already public, so this is
restoring consistency, not widening the API surface by design. PR scope grows beyond the issue title;
called out explicitly in the PR body.

### 2026-08-04 — Do not add `CS1729` to `AccessibilityDiagnosticIds`
Keep the accessibility filter to genuine accessibility IDs; extend the catch-all assertion's message
instead to say that a `CS1729` naming a Quarry type may mean an internal constructor.

**Why:** `CS1729` normally means a real emitter arity bug — one of the defect classes this matrix
exists to catch. Folding it into the accessibility filter would mislabel those. The catch-all already
fails the build, so nothing is missed; only the diagnosis hint was missing.

## Working Notes

### 2026-08-04 — Accessibility audit of the emitted runtime surface (pre-DESIGN)

Swept every type a generated interceptor can name, against every `internal` type declared in
`src/Quarry` + `src/Quarry.Shared` (102 candidates).

Emitted-code references to Quarry runtime types (fully qualified or via the emitted
`using Quarry; using Quarry.Internal; using Quarry.Logging;` block):

| Emitted reference | Declared | Accessibility |
|---|---|---|
| `Quarry.Internal.BatchInsertSqlBuilder.Build` | `Internal/BatchInsertSqlBuilder.cs:10` | **internal** ❌ |
| `Quarry.Internal.ThrowHelper.UnenumeratedMask` | `Internal/ThrowHelper.cs:9` | public |
| `Quarry.Internal.CollectionHelper.Materialize` | `Internal/CollectionHelper.cs:10` | public |
| `Quarry.Internal.CollectionSqlCache` | `Internal/CollectionSqlCache.cs:8` | public |
| `Quarry.Internal.ParameterNames.AtP` / `.Dollar` | `Internal/ParameterNames.cs:9` | public |
| `QueryExecutor.*` (unqualified) | `Internal/QueryExecutor.cs:16` | public + `[EditorBrowsable(Never)]` |
| `OpId.Next` (unqualified) | `Internal/OpId.cs:10` | public + `[EditorBrowsable(Never)]` |
| `QueryLog.*` | `Logging/QueryLog.cs:10` | public + `[EditorBrowsable(Never)]` |
| `ParameterLog.*` | `Logging/ParameterLog.cs:10` | public + `[EditorBrowsable(Never)]` |
| `LogsmithOutput.Logger` | Logsmith-generated into `Quarry` | public (proven — non-friend `Insert_*` shapes compile today) |
| `SqlDialect.*` | `Quarry.Shared/Sql/SqlDialect.cs:11` | public (the `internal` sibling at :4 is `#if QUARRY_GENERATOR`) |

**`BatchInsertSqlBuilder` is the only internal type named by emitted code.** No second instance in
`TerminalBodyEmitter`, `JoinBodyEmitter`, `CarrierEmitter`, `SetOperationBodyEmitter`,
`RawSqlBodyEmitter`, `ClauseBodyEmitter` or `TransitionBodyEmitter`.

Near-misses that are **not** defects — do not "fix" these:

- `Quarry.Shared.Sql.SqlFormatting` (internal) appears at `CarrierEmitter.cs:1645` only inside a
  `<see cref="..."/>` XML doc comment on a generator-side method. Not emitted.
- `Quarry.Internal.ScalarConverter` is internal and stays internal: it is called only from
  `QueryExecutor.cs:250` inside the runtime assembly, never named by emitted code. This is why a
  blanket "every type in `Quarry.Internal` must be public" convention test would be wrong — the
  namespace holds both the emitted surface and runtime-private helpers.
- `RawSqlBodyEmitter.GenerateScalarConverter` is a generator-side method name, unrelated to the
  `ScalarConverter` runtime type; it emits inline `Convert.ToXxx(v)` expressions, no type reference.
- `SqlExpr` / `SqlDialect` hits in the sweep are generator-internal IR types with the same simple
  name as something in the emitted text. Match on the fully-qualified name, not the simple name.

**Existing convention for the emitted surface:** `public` + `[EditorBrowsable(EditorBrowsableState.Never)]`
(`OpId`, `QueryExecutor`, `QueryLog`, `ParameterLog`). Not applied consistently —
`CollectionHelper`, `CollectionSqlCache`, `ParameterNames`, `ThrowHelper` are public without the
attribute.

**Every in-repo project is a friend of `Quarry`** (`src/Quarry/Quarry.csproj:19-25`:
`Quarry.Tests`, `Quarry.IntegrationTests`, `Quarry.Benchmarks`, `Quarry.Generator`, `Quarry.Tool`,
`Quarry.Sample.WebApp`, `Quarry.Sample.Aot`). No in-repo build models an ordinary consumer, so a
non-friend `CSharpCompilation` is the only mechanism available for a recurrence guard.

### 2026-08-04 — Step 1 notes

- `Build`'s public signature is safe: its `SqlDialect` parameter resolves to `Quarry.SqlDialect`
  (public), not the `#if QUARRY_GENERATOR` internal sibling. The internal `SqlFormatting` it calls is
  only used in the method *body*, which is legal from a public method — no CS0051.
- Fixture test count is coincidentally unchanged at 36: the 2 deleted `KnownBug_Issue334` cases are
  exactly offset by the same 2 shapes rejoining `AllShapes`. Do not read "36 before, 36 after" as
  "nothing happened".
- `dotnet test` emits pre-existing `NU1903` high-severity package advisories for
  `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 and `System.Security.Cryptography.Xml` 9.0.0. Present on
  `master`, unrelated to #334, out of scope here.
- Manifest goldens do not move (confirmed `git diff --stat -- src/Quarry.Tests/ManifestOutput` empty).
  The CRLF/LF warnings git prints on that path are a line-ending notice, not a content diff.

### 2026-08-04 — Step 2 notes

**The guard was verified end-to-end, not just asserted.** Temporarily reverted
`BatchInsertSqlBuilder` to `internal`, re-ran the fixture, and confirmed both `BatchInsert_*` shapes
fail with the new accessibility message naming `CS0122 'BatchInsertSqlBuilder'` — then restored via
`git checkout HEAD -- src/Quarry/Internal/BatchInsertSqlBuilder.cs`. Worth repeating if the assertion
is ever refactored; a guard nobody has watched fail is not known to work.

- Extracted `CompileNonFriend(IEnumerable<string>)` out of `Run` so the negative-control test shares
  the *exact* references and assembly name as the matrix. Using a separately built compilation would
  have let the two drift, and the control would stop attesting anything about the matrix.
- The diagnostic-ID set lives in one `AccessibilityDiagnosticIds` field consumed by both the
  assertion and the control, for the same reason.
- `ScalarConverter` is the negative control precisely because it must *stay* internal. If a future
  change makes it public, `AccessibilityGuard_DetectsAnInaccessibleType` fails loudly rather than
  silently going vacuous — pick a different still-internal type at that point, do not delete the test.

### 2026-08-04 — Step 3 found a second, larger instance: `ToDiagnostics()` is broken for all consumers

Adding the `BatchInsert_ToDiagnostics` shape immediately failed — but **not** with `CS0122`:

```
error CS1729: 'QueryDiagnostics' does not contain a constructor that takes 5 arguments
```

`QueryDiagnostics`'s only constructor (`src/Quarry/Query/QueryDiagnostics.cs:12`) is `internal`.
When no *accessible* overload exists the compiler reports `CS1729`, not `CS0122` — the internal
constructor is simply never a candidate. Same root cause as #334 (emitted code naming a
non-consumer-accessible surface), different manifestation.

**Blast radius is far wider than batch insert.** Probed a plain chain
(`db.Users().Where(...).Select(...).ToDiagnostics()`) in the non-friend compilation:

```
error CS1729: 'QueryDiagnostics' does not contain a constructor that takes 23 arguments
```

Three emission sites, all constructing the internal ctor:
`TerminalEmitHelpers.cs:615` (the general `ToDiagnostics()` path for every chain),
`CarrierEmitter.cs:1095`, and `TerminalBodyEmitter.cs:519` (batch insert). So `ToDiagnostics()` —
documented at `llm.md:299` as available on every builder type and "the primary tool for asserting
generated SQL in tests" — **does not compile for any consumer outside the friend list, on any chain
shape.**

Strong signal this is an oversight rather than intent: every sibling type the same emitted code
constructs already has a **public** constructor — `DiagnosticParameter` (:153),
`ClauseDiagnostic` (:202), `SqlVariantDiagnostic` (:245), `ProjectionColumnDiagnostic` (:261),
`JoinDiagnostic` (:298), `CollectionSqlCache` (:15). `QueryDiagnostics` is the lone holdout.

**Correction to my earlier audit:** the pre-DESIGN sweep was *type*-level only and therefore could
not have found this. Member-level re-audit of everything emitted code names — `ThrowHelper.UnenumeratedMask`,
`CollectionHelper.Materialize`, `ParameterNames.AtP`/`.Dollar`, `OpId.Next`, `QueryExecutor.*`,
`QueryLog.*`, `ParameterLog.*`, and all six diagnostic-type constructors above — found **no other**
internal member. The `QueryDiagnostics` constructor is the only one.

**Guard gap this exposes:** `AccessibilityDiagnosticIds` does not include `CS1729`, so the dedicated
accessibility assertion did not fire — the catch-all "fixture does not compile cleanly" caught it
instead. `CS1729` is ordinarily a genuine arity bug, so adding it to the accessibility filter
outright would mislabel real defects. Whatever the scope decision, the filter needs revisiting.

### 2026-08-04 — Step 5: a compiling shape can still guard nothing

`AssertBindsCleanly` only proves a shape compiles and that *some* interceptor was emitted for its
terminal. It cannot tell whether the shape reached the emitter path it was added for. Added
`Shape_StillReachesItsRuntimeHelper` (a shape-name → expected-emitted-text table) and it caught a
false-positive **immediately**, on its first run:

- `CollectionContains_FetchAll` as originally written — `var ids = new[] { 1, 2, 3 };` with an
  entity terminal — emitted **none** of `CollectionHelper.Materialize`, `CollectionSqlCache` or
  `ParameterNames.*`, yet passed the binding matrix green. Rewriting it as
  `var ids = new List<int> { … }` + `.Select(...)` (the form `SqlOutput/CollectionParameterCollisionTests.cs:25`
  uses) fixed two of the three.
- `CollectionHelper.Materialize` still did not appear. Cause is real and worth knowing:
  `CarrierEmitter.cs:1243/1252` emits `Materialize` **only** when `p.IsEnumerableCollection` — a
  `List<int>`/array is already `IReadOnlyList<T>` and is used directly. Only a collection *typed*
  `IEnumerable<T>` takes that arm. Added `CollectionEnumerableContains_FetchAll` for it; both arms
  are now guarded.

Take-away for anyone extending the matrix: **add the expectation entry at the same time as the
shape.** Without it there is no signal distinguishing "this shape guards the helper" from "this
shape happens to compile", and the latter looks identical in a green run.

### 2026-08-04 — Step 6: two facts about raw SQL that are not obvious from the source

1. **Raw-SQL interceptors are emitted into `Quarry.Generated`, not the context's namespace.** Chain
   interceptors go to the context namespace, so the fixture's `InterceptorsNamespaces` feature only
   listed `TestApp;TestApp.Sub` and the first raw-SQL shape failed with `CS9137` ("the 'interceptors'
   feature is not enabled in this namespace"). Added `Quarry.Generated` to the list. This is not
   loosening the fixture — real consumers get exactly that namespace registered automatically by the
   build targets Quarry ships (`src/Quarry/build/**`, wired at `Quarry.csproj:32-36`), so the fixture
   now matches an ordinary consumer's project more closely than before.

2. **`RawSqlNonQueryAsync` is never intercepted.** Only `RawSqlAsync` and `RawSqlScalarAsync` have an
   `InterceptorKind` (`InterceptorRouter.cs:74-75`, `FileEmitter.cs:875-886`); `RawSqlNonQueryAsync`
   is a plain public method on `QuarryContext` (`Context/QuarryContext.cs:230`). It emits nothing
   into the consumer's assembly, so there is no emitted surface to guard and the shape was dropped.
   `llm.md:279-289` lists all three together under "Raw SQL", which makes it easy to assume all three
   are generated — they are not.

### 2026-08-04 — Step 7: CTEs do not need `QuarryContext<TSelf>`

The plan budgeted a separate `CteDbContext` source and a refactor of `Run` (replacing its
`crossContext` bool with an extra-sources list) on the premise that CTEs require
`QuarryContext<TSelf>`. That premise was wrong, and both were dropped.

`llm.md:196` says `QuarryContext<TSelf>` is needed "for typed post-`With` accessors" — that is
narrower than "for CTEs". `FromCte<T>()` works fine on the plain non-generic `QuarryContext`:
`Quarry.Tests`' own `TestDbContext` derives from it (`Samples/TestDbContext.cs:10`) and
`SqlOutput/CrossDialectCteTests.cs:20` drives CTEs through exactly that. The shape went onto the
existing shared context unchanged.

Worth remembering generally: the fixture's `Run` still takes a `crossContext` bool. If a future shape
genuinely needs different sources, that is the point to generalize it — not before.

## Suspend State

## Session Log
| Date | Phases | Summary |
|------|--------|---------|
| 2026-08-04 | INTAKE, DESIGN | Worktree created from #334; emitted-surface accessibility audit completed. |
| 2026-08-04 | PLAN, IMPLEMENT | 8 plan steps committed. Step 3 uncovered a second, larger instance (internal `QueryDiagnostics` ctor breaking `ToDiagnostics()` for all consumers); fixed here by user decision. |
| 2026-08-06 | REVIEW, REMEDIATE | Rebased onto `origin/master` (#337). 13 findings, all classified A/B by user (C→A) and fixed. PR #340 opened; CI green. |
