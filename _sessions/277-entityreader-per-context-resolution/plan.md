# Plan: 277-entityreader-per-context-resolution

## Direction (locked in DESIGN)

`[EntityReader]` resolution becomes per-context by default. For every interceptor the generator emits, the reader FQN is constructed as `<contextNamespace>.<readerSimpleName>` (where `<readerSimpleName>` is derived by string-slicing the existing schema-namespace FQN that already lives on `ProjectionInfo.CustomEntityReaderClass`). No Compilation lookup, no fallback to the schema-namespace FQN, no new analyzer diagnostics. Missing or mis-declared per-context readers surface as ordinary C# compile errors against the generated interceptor source. Single-context-same-namespace consumers see zero behavior change because `<contextNamespace>.<simpleName>` resolves to the schema-namespace FQN they used before.

The complementary work is Layer 1 — adding per-context partials and reader classes for `Pg` / `My` / `Ss` to `src/Quarry.Tests/Samples` so the test compilation has the per-context readers the generator now references.

The generator change happens in two codegen seams: `InterceptorCodeGenerator.CollectEntityReaderInstances` (used by `FileEmitter` to emit cached `private static readonly` reader fields) and `ReaderCodeGenerator.GenerateReaderDelegate` (used during chain enrichment to emit the `static (DbDataReader r) => _entityReader_X.Read(r)` delegate). Both already accept the schema-namespace FQN; both need to know the context namespace so they can rewrite the FQN consistently.

The interceptor's `IQueryBuilder<TIn, TOut>` projection output type is a third seam. The issue's diagnostic shows `IQueryBuilder<Pg.Product, Quarry.Tests.Samples.Product>` — input `TIn` is already per-context (`Pg.Product`), but output `TOut` is the schema-namespace `Product`. For identity projections (`Select(p => p)`) with `[EntityReader]` active, `TOut` needs to bind to the per-context entity to match `TIn`. That requires tracing where `ResultTypeName` is set for the chain/projection and ensuring it picks the per-context FQN — `chain.EntityTypeName` is already per-context, so the fix is mechanical once the responsible site is identified.

## Phasing

Phases land in dependency order. Layer 1 lands first so the per-context readers exist; the generator switch lands second so the emitted code references real types; tests for the generator and the deferred Phase 10 cross-dialect file land last.

### Phase 1 — Layer 1: per-context Product partials + per-context ProductReader classes

Add per-context partials of the `Product` entity exposing the same `DisplayLabel` extension that the global `Product` partial provides. Add per-context `ProductReader : EntityReader<Product>` classes (each `Product` resolving to the per-context entity in its enclosing namespace) with the same `Read` body as `Quarry.Tests.Samples.ProductReader`. Three pairs total: `Pg`, `My`, `Ss`.

File location options: append to `src/Quarry.Tests/Samples/ProductSchema.cs` or create a sibling `src/Quarry.Tests/Samples/PerContextProductReaders.cs`. Choose the sibling file for clarity — the existing file's role is "schema definition + global reader," and a separate file keeps the per-context readers easy to delete or audit.

Each per-context `Product` partial only adds the `DisplayLabel` extension (matching the global partial). Each per-context `ProductReader` mirrors the global `Read` body verbatim, but the `new Product { ... }` literal binds to the enclosing namespace's per-context type via C# name resolution.

At the end of this phase the per-context readers exist but are dead code — the generator still emits the schema-namespace FQN. Build succeeds; all 3340 tests still pass.

Tests for this phase: none added; the existing test surface validates that the new types compile and don't conflict.

Commit boundary: `Add per-context Pg/My/Ss Product partials and ProductReader classes (Layer 1)`.

### Phase 2 — Generator: emit per-context reader FQN at the two codegen seams

Add a `string? contextNamespace` parameter to:

- `InterceptorCodeGenerator.CollectEntityReaderInstances(IReadOnlyList<TranslatedCallSite>, HashSet<string>, IReadOnlyList<AssembledPlan>?, string? contextNamespace)`. For each FQN encountered (`site.ProjectionInfo.CustomEntityReaderClass` or `chain.ProjectionInfo.CustomEntityReaderClass`), compute the per-context FQN: `contextNamespace + "." + schemaFqn.Substring(schemaFqn.LastIndexOf('.') + 1)`. When `contextNamespace` is null/empty, fall through to the original FQN (covers the no-namespace test scaffolding case). Use that per-context FQN for both the field name and the cached field's type expression.

- `ReaderCodeGenerator.GenerateReaderDelegate(ProjectionInfo, string entityTypeName, string? contextNamespace)`. Same FQN-rewrite rule applied at line 71 (where `GetEntityReaderFieldName(projection.CustomEntityReaderClass)` is called).

Update the two callers:

- `FileEmitter.cs:365` — pass `_contextNamespace` into `CollectEntityReaderInstances`.
- `QuarryGenerator.cs:605` — pass `group.ContextNamespace` into `GenerateReaderDelegate`.

Trace and fix the `IQueryBuilder<TIn, TOut>` `TOut` issue. Read `CarrierEmitter.cs:188` and trace `chain.ResultTypeName` / `chain.ExecutionSite.ResultTypeName` back to where they're set for an identity projection with `[EntityReader]`. The fix is to bind `TOut` to the per-context entity FQN (which is already available on `chain.EntityTypeName` or equivalent) when the projection is identity over an `[EntityReader]`-active entity. The `ExecuteCarrierWithCommandAsync<T>` `T` for terminal emission needs the same treatment — find the corresponding emit site in `TerminalBodyEmitter` / `CarrierEmitter`.

Add no new IR fields. Do not thread `Compilation`. Do not emit any new diagnostics.

Tests for this phase: re-run the full suite. The expected outcome is all 3340 tests still passing. The `obj/GeneratedFiles/.../*.Interceptors.*.g.cs` for `PgDb`/`MyDb`/`SsDb` files now reference `_entityReader_Pg_ProductReader` / `_My_ProductReader` / `_Ss_ProductReader` instead of the schema-namespace one. The `IQueryBuilder<Pg.Product, Pg.Product>` chain shape is now consistent.

Commit boundary: `Generator: emit per-context EntityReader FQN and projection TOut for [EntityReader] schemas`.

### Phase 3 — Generator integration tests for per-context emission

Add tests to `src/Quarry.Tests/GeneratorTests.cs` (or a sibling file `EntityReaderResolutionTests.cs` if the existing file is large) that lock in the per-context emission behavior. Tests use the existing `PgDb`/`MyDb`/`SsDb` + `ProductSchema` fixture per the DESIGN decision.

Three test cases:

1. **Per-context emission, multi-context chain.** Compile real test source containing `PgDb.Products().Select(p => p).ExecuteFetchAllAsync()` (and the same against `MyDb` / `SsDb`). Inspect the emitted interceptor file's source string and assert it contains the per-context reader field `_entityReader_Quarry_Tests_Samples_Pg_ProductReader` and the chain shape `IQueryBuilder<Pg.Product, Pg.Product>`.

2. **Schema-namespace context preservation.** Same against `TestDbContext` (in `Quarry.Tests.Samples`). Assert it emits `_entityReader_Quarry_Tests_Samples_ProductReader` (unchanged behavior — schema namespace == context namespace).

3. **Cached-field deduplication.** Compile a chain that uses `ProductSchema` from multiple `PgDb` call sites in the same file; assert the emitted file contains exactly one cached `_entityReader_Quarry_Tests_Samples_Pg_ProductReader` field.

Use the existing `RunGeneratorWithDiagnostics` helper or whatever pipeline-test scaffolding the QRY070/QRY071 tests landed in cross-dialect-test-coverage Phase 3 use; mirror that pattern.

Commit boundary: `Add generator integration tests for per-context [EntityReader] emission`.

### Phase 4 — Phase 10 conversion: CrossDialectEntityReaderTests

Convert `src/Quarry.Tests/Integration/EntityReaderIntegrationTests.cs` to `src/Quarry.Tests/SqlOutput/CrossDialectEntityReaderTests.cs` using the verbatim 4-dialect pattern from cross-dialect-test-coverage Phases 4–9. Each existing SQLite-only test becomes a cross-dialect test that runs `Prepare()` + `AssertDialects(...)` + `ExecuteFetchAllAsync()` against `Lite` / `Pg` / `My` / `Ss`.

Read the existing file first to enumerate tests; group them into regions matching the cross-dialect convention (e.g., "Identity projection — runtime materialization (4-dialect execution)", "Tuple projection (4-dialect execution)", etc.). For each test that asserts `DisplayLabel`, the assertion now passes on every dialect because each context's interceptor materializes through its own per-context `ProductReader`.

Delete `src/Quarry.Tests/Integration/EntityReaderIntegrationTests.cs` once the cross-dialect file is in place and tests are green.

Tests for this phase: the converted tests themselves. Expected outcome: all tests pass against all 4 dialects, validating end-to-end that the per-context fix is correct in real interceptor execution. Test count delta depends on the original file's coverage; record the delta in the commit.

Commit boundary: `Convert EntityReaderIntegrationTests to 4-dialect CrossDialectEntityReaderTests; delete original`.

### Phase 5 — Manifest verification

Re-build and inspect the generated `quarry-manifest.{Lite,Pg,My,Ss}.md` files. The manifest emitter records which entities/contexts use custom `[EntityReader]` resolution. With per-context resolution active, the manifests for `Pg`/`My`/`Ss` may now record `Pg.ProductReader` etc. instead of `Quarry.Tests.Samples.ProductReader`. Verify any deltas are correct (the per-context FQN is what we want recorded). Update any golden manifest snapshots referenced by tests if they exist.

Tests for this phase: any manifest snapshot tests that check FQNs directly. If no manifest tests exist that touch this area, this phase is verification-only.

Commit boundary: `Verify quarry-manifest changes for per-context EntityReader resolution` (only if manifests changed).

### Phase 6 — Documentation

Find the `[EntityReader]` doc page (likely under `docs/articles/` or in `llm.md`) and add a short note: when a schema annotated with `[EntityReader]` is referenced by `QuarryContext` subclasses in different namespaces, each context expects a per-context reader class at `<contextNamespace>.<readerSimpleName>` inheriting `EntityReader<TPerContextEntity>`. Single-context-same-namespace consumers see no change. Mention the C# compile-error path for missing/mis-declared per-context readers.

Tests for this phase: none.

Commit boundary: `Document per-context EntityReader resolution requirement`.

## Dependencies

- Phase 2 depends on Phase 1 (otherwise the generator emits references to types that don't exist).
- Phase 3 depends on Phase 2 (the test assertions match Phase 2's emission output).
- Phase 4 depends on Phase 2 (the cross-dialect test compilation requires the per-context emission).
- Phase 5 depends on Phase 2 (manifest content shifts with the new emission).
- Phase 6 is independent and can land last.

Phases 3, 4, 5, 6 can technically reorder among themselves — the chosen order is "lock generator behavior with focused tests, then prove end-to-end with the cross-dialect conversion, then verify side artifacts."

## Out of scope

- Renaming `CustomEntityReaderClass` field names across the IR. The existing field name still accurately describes the field's contents (the schema-namespace FQN of the user's `[EntityReader]` declaration); only the consumption sites in codegen change behavior.
- Adding `Compilation` threading or pre-collected name sets. Not needed under the per-context-default direction.
- New analyzer diagnostics. Not needed; the C# compiler handles missing/mis-declared cases.
- Removing or relaxing QRY027. It still validates the schema-level declaration against the schema-namespace entity, which remains a useful pre-build error for the common case.
- Auto-generation of per-context reader stubs by Quarry. Considered and rejected during DESIGN: the user's `Read` body is opaque to the generator, and synthesizing it would require either deep semantic-model rewriting (heavy and fragile) or `Unsafe.As<>`-based delegation (the same root-cause divergence the fix is removing).

## Verification at each step

After every phase commit, run `dotnet test src/Quarry.Tests` (or full solution) and confirm:

- No regressions in the 3340-test baseline (Phases 1, 2 — count unchanged).
- New tests pass (Phase 3 — count increases by the number of pipeline tests added).
- All cross-dialect tests pass (Phase 4 — count adjusted by Phase 10 conversion delta).
- Build is clean (no new warnings introduced).
