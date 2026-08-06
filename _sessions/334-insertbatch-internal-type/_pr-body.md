## Summary

- Closes #334

`InsertBatch(...)` did not compile in any project outside this repository. Its generated interceptor
calls `Quarry.Internal.BatchInsertSqlBuilder.Build(...)`, and that type was `internal`, so consumers
got `error CS0122: 'BatchInsertSqlBuilder' is inaccessible due to its protection level` in the
generated `*.Interceptors.*.g.cs`.

Issue #334 also asked for a recurrence guard: *"a second instance would fail the same way."* There
was one, and it was larger — see below.

## Reason for Change

A generated interceptor is compiled into the **consumer's** assembly, so every Quarry type, method
and constructor it names is part of the public API contract whether or not it is documented as such.
Nothing enforced that.

The reason it went unnoticed is structural: all seven `InternalsVisibleTo` grants in
`src/Quarry/Quarry.csproj` cover every project in the solution — `Quarry.Tests`, `Quarry.Benchmarks`,
`Quarry.Sample.WebApp`, `Quarry.Sample.Aot`. **No in-repo build has ever compiled generated
interceptors the way a consumer does.** The synthetic non-friend `CSharpCompilation` inside
`Generation/InterceptorBindingGuardTests.cs` (added by #314) is the only thing in the repository that
can observe this class of defect at all.

## Impact

Two consumer-facing fixes, both of which made a documented feature unusable from NuGet:

| Defect | Symptom for a consumer | Fix |
|---|---|---|
| `Quarry.Internal.BatchInsertSqlBuilder` was `internal` | `InsertBatch(...)` — `CS0122` | type is now `public` |
| `QueryDiagnostics`' only constructor was `internal` | **`ToDiagnostics()` on any chain shape** — `CS1729` | constructor is now `public` |

The second was found by this PR's own guard on the first `ToDiagnostics` shape added to the matrix,
and is the wider of the two: `llm.md` documents `ToDiagnostics()` as available on every builder type
and "the primary tool for asserting generated SQL in tests". Three emitter sites construct it
(`TerminalEmitHelpers.cs:615` — the general path every non-batch chain uses,
`CarrierEmitter.cs:1095`, `TerminalBodyEmitter.cs:519`).

It surfaced as `CS1729`, not `CS0122`, which is worth knowing: when a type's only constructor is
`internal` it is not an overload candidate at all outside a friend assembly, so the compiler reports
the arity rather than the protection level.

Fixing it here rather than deferring was an explicit decision (recorded in `workflow.md`): splitting
it out would have meant pinning a known-broken headline API out of the very guard being built.

## Plan items implemented as specified

- **Promote `BatchInsertSqlBuilder`** to `public` + `[EditorBrowsable(Never)]`, matching the existing
  convention for the emitted surface (`OpId`, `QueryExecutor`, `QueryLog`, `ParameterLog`). No
  generator change — the emitted name was already correct, just unreachable.
- **Unpin #334** — the two `InsertBatch` shapes return to the clean-binding matrix and
  `KnownBug_Issue334_BatchInsert_ReferencesInternalType` is deleted.
- **A dedicated accessibility assertion**, checked before the catch-all so a regression reads as
  "the emitter named a type consumers cannot reach" rather than "the fixture does not compile".
- **Broaden the matrix** from 16 shapes on single-table chains to 33, across every emitter family:
  joins (inner/left), aggregates, correlated `EXISTS` subqueries, set operations, collection `IN`
  (both the `IReadOnlyList` and `IEnumerable<T>` arms), conditional masks, `Prepare()`/`ToDiagnostics`,
  window functions, CTEs and raw SQL.
- **Document the invariant** in `llm-testing.md` and `src/Quarry.Generator/llm.md`.

## Deviations from plan implemented

- **CTEs need no `QuarryContext<TSelf>`.** The plan budgeted a separate context source and a refactor
  of `Run` on that premise. `llm.md` scopes the generic base to *typed post-`With` accessors*;
  `FromCte<T>()` works on the plain non-generic `QuarryContext`. Both were dropped as unnecessary.
- **`RawSqlNonQueryAsync` is never intercepted** — only `RawSqlAsync` and `RawSqlScalarAsync` have an
  `InterceptorKind`. It emits nothing into the consumer's assembly, so there is no surface to guard
  and no shape for it. (`llm.md` lists all three together, which makes the asymmetry easy to miss.)
- **Raw-SQL interceptors are emitted into `Quarry.Generated`**, not the context's namespace, so the
  fixture's `InterceptorsNamespaces` needed it. This matches an ordinary consumer more closely, not
  less — Quarry's shipped build targets (`src/Quarry/build/Quarry.targets`) register exactly it.
- The window shape ships as `Sql.RowNumber` rather than the planned `Sql.Rank` with
  `PartitionBy`/descending; those two clauses remain unexercised.

## Gaps in original plan implemented

- **`Shape_StillReachesItsEmitter`.** Compiling clean and emitting *an* interceptor does not prove a
  shape reached the emitter it was added for. This earned itself on its first run: the collection
  shape as first written emitted **none** of the collection helpers while passing the binding matrix
  green. `EveryShape_HasAnEmissionExpectation` enforces that every shape declares what it must emit,
  so the coverage cannot silently rot.
- **`AccessibilityGuard_DetectsAnInaccessibleType`.** The matrix is meaningful only if this
  compilation genuinely lacks friend access; if that quietly stopped being true every shape would
  keep passing while guarding nothing. The probe uses `ScalarConverter`, which is internal *by design*
  and stays that way. The guard was also verified by hand — temporarily reverting
  `BatchInsertSqlBuilder` to `internal` and watching both shapes fail with the new message.
- **Narrowed `CS1729` classification.** Classified as accessibility only when the quoted name is a
  type Quarry declares, so a genuine emitter arity bug — a defect class this matrix exists to
  catch — is still reported as one. Covered by tests including the negative case.
- **Multi-terminal shapes.** `Prepared_MultiTerminal`'s `ToDiagnostics()` half was compiled but never
  probed; `Shape.AdditionalTerminals` fixes that.
- **`Insert(...).ToDiagnostics()`** — the third `QueryDiagnostics` construction site — had no shape.
- **Argument validation** on both newly public entry points (see Security).

## Migration Steps

None. All changes are strictly widening; no consumer action required. Consumers who previously could
not compile `InsertBatch` or `ToDiagnostics()` will find they now do.

## Performance Considerations

None. No change to any emitted code path or runtime algorithm — the emitters were already producing
the correct calls. `MaxParameterCount` moves from `const` to `static readonly`, replacing a compile-time
inline with a static field read on a path that already builds a SQL string.

## Security Considerations

Widening visibility grants consumers no capability they lacked — anything reachable through
`BatchInsertSqlBuilder` is reachable through `RawSqlAsync` already. Both newly public entry points
now validate their arguments rather than assuming a generated caller: `Build` null-checks
`sqlPrefix`, rejects non-positive `entityCount`/`columnsPerRow`, and computes the parameter product
in 64-bit so it cannot wrap negative past the `MaxParameterCount` ceiling; the `QueryDiagnostics`
constructor null-checks its three required arguments.

## Breaking Changes

- **Consumer-facing** — none. Every visibility change is additive: no removals, no signature changes,
  no behavioural change to any existing member.
- **Internal** — `MaxParameterCount` is `public static readonly` rather than `public const`,
  deliberately: a public `const` is inlined into consumer assemblies at their compile time, which
  would freeze a value its own documentation calls "a conservative default". The `QueryDiagnostics`
  constructor is now frozen public API; its `<remarks>` states it is not supported and warns against
  binding to the signature, since new diagnostics fields are appended as optional parameters.

## Testing

`Quarry.Tests` 3561 passed / 0 failed; `Quarry.Migration.Tests` 201 passed / 0 failed. Baseline
before this branch was 3501 / 201, both green. Manifest goldens unchanged throughout.
