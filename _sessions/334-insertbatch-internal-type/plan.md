# Plan: 334-insertbatch-internal-type

## Key concepts

**The emitted runtime surface is public API.** A generated interceptor lands in the *consumer's*
assembly. Every Quarry type it names must therefore be `public`, exactly as if a consumer had
hand-written the call. Nothing in the build enforces this today.

**Why the repo is blind to it.** All seven friend grants in `src/Quarry/Quarry.csproj:19-25` cover
every project in the solution, so no in-repo build ever compiles generated interceptors as an
ordinary consumer would. The single non-friend compilation in the repo is the synthetic
`CSharpCompilation.Create("InterceptorBindingGuardAssembly", …)` inside
`Generation/InterceptorBindingGuardTests.cs` (added by #314). Broadening that fixture's shape matrix
is the only way to get coverage — a `NoWarn`-proof, project-independent consumer simulation.

**`Quarry.Internal` is not a uniform accessibility zone.** It holds two different things: the
emitted surface (`BatchInsertSqlBuilder`, `ThrowHelper`, `CollectionHelper`, `CollectionSqlCache`,
`ParameterNames`, `QueryExecutor`, `OpId` — must be public) and runtime-private helpers
(`ScalarConverter`, called only from `QueryExecutor.cs:250` — legitimately internal). So the
invariant cannot be expressed as a namespace convention; it has to be discovered by compiling.

**Two emission sites, only one currently reachable by a test.**
`TerminalBodyEmitter.cs:559` is the carrier terminal (`ExecuteNonQueryAsync` / `ExecuteScalarAsync`)
— that is what the existing pinned shapes hit. `TerminalBodyEmitter.cs:518` is the *diagnostics*
terminal (`InsertBatch(...).Values(...).ToDiagnostics()`), which no shape in the matrix reaches. Both
hard-code `Quarry.Internal.BatchInsertSqlBuilder.Build(...)`. Step 3 closes that gap.

## Step dependencies

Step 1 is self-contained and must land first — the #334 pin goes red the instant the type becomes
public, so the promotion and the pin removal are one atomic commit. Step 2 must precede steps 3–7
because it introduces the assertion those shapes rely on for a legible failure message. Steps 4–7
each add shapes and are independent of one another; 4 must precede 5 only because 5's set-operation
and aggregate shapes reuse the `Order` entity that 4 introduces. Step 8 is documentation and lands
last.

---

## Steps

- [x] **1. Promote `BatchInsertSqlBuilder` to public and unpin #334**

  `src/Quarry/Internal/BatchInsertSqlBuilder.cs`: change `internal static class` → `public static
  class`, add `[EditorBrowsable(EditorBrowsableState.Never)]` (with `using System.ComponentModel;`)
  to match `OpId.cs:9` / `QueryExecutor.cs:15`, and change `internal const int MaxParameterCount`
  → `public const int`. Refresh the class `<summary>` to state that it is called from generated
  interceptor code in consumer assemblies and is not part of the supported API despite being public.

  No generator change: `TerminalBodyEmitter.cs:518` and `:559` already emit the correct fully
  qualified name — the type just was not reachable.

  *Tests:* in `Generation/InterceptorBindingGuardTests.cs`, delete
  `KnownBug_Issue334_BatchInsert_ReferencesInternalType`, delete the `BatchInsertShapes` array and
  the `BatchInsertOnlyShapes` source, and move both shapes into `GenericTerminalShapes` so they
  rejoin `AllShapes`. Update the `<summary>`/`<remarks>` on `BatchInsertShapes`' former holdout note.
  This must be the same commit as the promotion — the pin asserts the *buggy* behaviour and fails the
  moment the type is public.

- [x] **2. Add a dedicated accessibility assertion to the guard, and prove it is not vacuous**

  In `AssertBindsCleanly`, insert an accessibility check *before* the existing "fixture does not
  compile cleanly" catch-all, so the specific cause wins the failure message:

  ```csharp
  // Generated interceptors land in the *consumer's* assembly, so every Quarry type they
  // name must be public. CS0122 here means the emitter referenced a type that is only
  // reachable from a friend assembly — invisible to every in-repo build (#334).
  var inaccessible = diagnostics
      .Where(d => d.Id is "CS0122" or "CS0050" or "CS0051" or "CS0053" or "CS0060")
      .ToList();
  Assert.That(inaccessible, Is.Empty, () =>
      $"Generated interceptor for '{shape.Name}' references a type that is not accessible " +
      $"outside Quarry's InternalsVisibleTo list, so this chain does not compile for any " +
      $"ordinary consumer: {Describe(inaccessible)}");
  ```

  `CS0122` is the real case; `CS0050`/`CS0051`/`CS0053`/`CS0060` (inconsistent accessibility on
  return type / parameter / property / base type) are cheap to include and cover the same class of
  defect surfacing through an emitted member signature rather than a call.

  *Tests:* add `AccessibilityGuard_DetectsAnInaccessibleType` — compile a hand-written source in the
  same non-friend `CSharpCompilation` that calls `Quarry.Internal.ScalarConverter.Convert<int>(...)`
  (a type deliberately still internal after step 1) and assert the filter above reports it. Without
  this, the whole broadened matrix could pass green because the fixture accidentally *has* friend
  access, and nobody would know.

- [x] **3. Cover the `ToDiagnostics` terminals — and fix the internal `QueryDiagnostics` constructor**

  *Expanded during implementation.* Adding the batch shape revealed that
  `QueryDiagnostics`'s only constructor (`src/Quarry/Query/QueryDiagnostics.cs:12`) is `internal`, so
  **`ToDiagnostics()` fails to compile for every consumer on every chain shape**, not just batch
  insert — three emission sites (`TerminalEmitHelpers.cs:615` general, `CarrierEmitter.cs:1095`,
  `TerminalBodyEmitter.cs:519`). Same root cause as #334; reported as `CS1729` rather than `CS0122`
  because an inaccessible constructor with no accessible overload is not a candidate at all.

  Make the constructor `public` + `[EditorBrowsable(EditorBrowsableState.Never)]`, matching the six
  sibling diagnostic types the same emitted code already constructs with public constructors.

  Extend the catch-all "fixture does not compile cleanly" message: a `CS1729` naming a Quarry type
  may mean an internal constructor, not an emitter arity bug. `CS1729` deliberately stays **out** of
  `AccessibilityDiagnosticIds` — it normally signals a genuine arity defect and mislabelling those
  would blunt the matrix.

  Add both shapes: `BatchInsert_ToDiagnostics` (covers `TerminalBodyEmitter.cs:519`, the site the
  original #334 pin never reached) and `Projected_ToDiagnostics` (covers the general
  `TerminalEmitHelpers.cs:615` path that every non-batch chain uses).

  *Tests:* the two shapes flow through `Shape_BindsWithoutInterceptorMismatch`. Verify the emitted
  marker comment is `Intercepts ToDiagnostics() call at` (`FileEmitter.cs:702` formats it from
  `site.MethodName`).

<details><summary>Original step 3 text</summary>

- [ ] **3. Cover the second `BatchInsertSqlBuilder` emission site (`TerminalBodyEmitter.cs:518`)**

  Add a `BatchInsert_ToDiagnostics` shape to `GenericTerminalShapes`:

  ```csharp
  new("BatchInsert_ToDiagnostics", "ToDiagnostics",
      @"var rows = new[] { new User { UserName = ""a"", IsActive = true } };
      var d = db.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(rows).ToDiagnostics();
      _ = d.Sql;")
  ```

  This is the emission site the original pin never reached. Verify the emitted marker comment is
  `Intercepts ToDiagnostics() call at` before relying on the existing interceptor-emitted probe
  (`FileEmitter.cs:702` formats it from `site.MethodName`).

  *Tests:* the shape itself is the test — it flows through `Shape_BindsWithoutInterceptorMismatch`.

</details>

- [ ] **4. Introduce a second entity and add join / navigation / aggregate shapes**

  Extend `SharedSource` with an `OrderSchema` (`Key<int> OrderId => Identity()`, a
  `Ref<User> UserId`-style FK matching this repo's `EntityRef` convention, `Col<decimal> Total`,
  `Col<string> Status`) plus `public partial IEntityAccessor<Order> Orders();` on `TestDbContext`.
  Read `src/Quarry.Tests/Samples/OrderSchema.cs` first and mirror its actual FK declaration syntax —
  do not guess the `Ref`/`Key` surface.

  New `JoinShapes` array, wired into `AllShapes`:
  - `Join_Select_FetchAll` — `db.Users().Join<Order>((u, o) => …).Select((u, o) => (u.UserName, o.Total)).ExecuteFetchAllAsync()` (`JoinBodyEmitter`)
  - `LeftJoin_Select_FetchAll` — nullable-side reader emission
  - `GroupBy_Having_FetchAll` — `db.Orders().GroupBy(o => o.Status).Having(o => Sql.Count() > 5).Select(o => (o.Status, Sql.Count())).ExecuteFetchAllAsync()`
  - `NavigationSubquery_Exists_FetchAll` — `db.Users().Where(u => u.Orders.Any(o => o.Total > 100)).ExecuteFetchAllAsync()`

  *Tests:* each shape runs through `Shape_BindsWithoutInterceptorMismatch`. Adding `Orders()` to the
  shared context changes generated output for every existing shape too — confirm the pre-existing
  15 shapes stay green, which is itself the regression signal.

- [ ] **5. Add set-operation, collection-`IN`, conditional-mask, prepared and window shapes**

  New `RuntimeHelperShapes` array — these are the shapes that reach the *other* `Quarry.Internal`
  helpers, which is precisely what makes them worth guarding:
  - `Union_FetchAll` — `db.Users().Select(u => u.UserName).Union(db.Orders().Select(o => o.Status)).ExecuteFetchAllAsync()` (`SetOperationBodyEmitter`)
  - `CollectionContains_FetchAll` — `var ids = new[] { 1, 2, 3 }; db.Users().Where(u => ids.Contains(u.UserId)).ExecuteFetchAllAsync()` → `CollectionHelper.Materialize`, `CollectionSqlCache`, `ParameterNames.Dollar`/`AtP`
  - `ConditionalMask_FetchAll` — `var q = db.Users().Select(u => u); if (flag) q = q.Where(u => u.IsActive); await q.ExecuteFetchAllAsync();` → `ThrowHelper.UnenumeratedMask`
  - `Prepared_MultiTerminal` — `.Prepare()` then both `ToDiagnostics()` and `ExecuteFetchAllAsync()`
  - `Window_Rank_FetchAll` — `Sql.Rank(over => over.PartitionBy(...).OrderByDescending(...))` in a projection

  The `flag` for the conditional shape must be a method parameter or local, not a field — chain
  analysis needs it in scope (`llm.md:207-218`). Keep the whole chain at one scope level;
  `llm-testing.md:256` documents that co-locating a terminal with a conditional clause collapses the
  mask to a single variant.

  *Tests:* as above, via `Shape_BindsWithoutInterceptorMismatch`.

- [ ] **6. Add raw-SQL shapes**

  `RawSqlBodyEmitter` is a wholly separate emission path with its own reader-strategy branches
  (static lambda for literal SQL vs. `file struct IRowReader<T>` fallback), and it is currently
  untouched by any non-friend compilation:
  - `RawSql_FetchAll` — `await foreach (var u in db.RawSqlAsync<User>("SELECT * FROM users")) { _ = u; }`
  - `RawSql_Scalar` — `await db.RawSqlScalarAsync<int>("SELECT COUNT(*) FROM users");`
  - `RawSql_NonQuery` — `await db.RawSqlNonQueryAsync("DELETE FROM users WHERE \"UserId\" = @p0", 1);`

  Confirm the generated `User` entity satisfies the QRY043 materializability rule (concrete class,
  parameterless ctor, `get; set;` properties) before assuming `RawSqlAsync<User>` is legal here; if
  not, declare a small DTO in `SharedSource` instead.

  *Tests:* as above. Expect `site.MethodName` to be `RawSqlAsync` / `RawSqlScalarAsync` /
  `RawSqlNonQueryAsync` for the interceptor-emitted probe — verify rather than assume.

- [ ] **7. Add CTE shapes on a dedicated `QuarryContext<TSelf>` context**

  CTEs require `QuarryContext<TSelf>` for the typed post-`With` accessors (`llm.md:196`), which the
  shared `TestDbContext : QuarryContext` does not provide. Rather than change the base type of the
  context every other shape depends on, add a `CteContextSource` declaring
  `public partial class CteDbContext : QuarryContext<CteDbContext>` over the same schemas, and
  generalize `Run` so a shape can request extra source files. Replace the current
  `crossContext: bool` parameter with an explicit extra-sources list (the cross-namespace case
  becomes one caller of that list) so the fixture does not accumulate parallel booleans.

  - `Cte_FromCte_FetchAll` — `db.With<User, ActiveUser>(users => users.Where(u => u.IsActive).Select(u => new ActiveUser(u.UserId, u.UserName))).FromCte<ActiveUser>().ExecuteFetchAllAsync()`

  *Tests:* the shape, plus confirm the refactor of `Run` leaves
  `Shape_CrossNamespaceContext_BindsWithoutInterceptorMismatch` behaviourally identical (same
  `SubContextSource`, same context type, same assertions).

- [ ] **8. Document the invariant**

  - `src/Quarry/Internal/BatchInsertSqlBuilder.cs` — already covered by step 1's summary.
  - `llm-testing.md` — extend the `InterceptorBindingGuardTests` note near line 229 with the
    accessibility role: the fixture is the only non-friend compilation in the repo, so it is also
    the only thing that can catch an emitted reference to an internal type; add a shape there when
    adding an emitter path.
  - `src/Quarry.Generator/llm.md:542` — update the fixture's bullet to cover CS0122 alongside
    CS8785/CS9144/CS9177, and state the rule for emitter authors: **generated code may name only
    `public` Quarry types**; `Quarry.Internal` deliberately mixes the public emitted surface with
    internal runtime-private helpers (`ScalarConverter`), so the namespace is not a licence.

  *Tests:* none — documentation only. Full suite re-run before commit.

## Verification

Full `dotnet test src/Quarry.Tests` and `dotnet test src/Quarry.Migration.Tests` after every step;
baseline is 3501 / 201 all green. Manifest goldens are not expected to move — the fixture holds
synthetic source strings, not real chains, so nothing new flows through the SQL manifest pipeline.
If `git diff -- src/Quarry.Tests/ManifestOutput` is non-empty after a build, stop and investigate
before committing (CI enforces this at `.github/workflows/ci.yml`).
