## Summary
- Closes #277

## Reason for Change

`[EntityReader]` resolution previously baked the schema-namespace reader FQN into every emitted interceptor regardless of which `QuarryContext` the call site belonged to. When a schema was referenced by multiple contexts in different namespaces (e.g. `App.Pg.PgDb`, `App.My.MyDb`), the per-context interceptor emitted `IQueryBuilder<Pg.Product, GlobalProduct>` and routed materialization through the schema-namespace reader. The runtime's `Unsafe.As<>` casts papered over the static-type mismatch, but the chain shape didn't match the C# call site, so call sites that expected per-context entity semantics (extensions, partial extensions on `Pg.Product`, downstream type checks) silently broke. Phase 10 of the cross-dialect-test-coverage workflow surfaced this when its `CrossDialectEntityReaderTests` failed to compile against `PgDb.Products().Select(p => p)`.

## Impact

Per-context `[EntityReader]` resolution becomes the default. The generator emits the reader FQN as `<contextNamespace>.<readerSimpleName>` for every interceptor. When schema and context share a namespace (the common single-context case), the per-context FQN equals the schema-namespace FQN — preserving today's behavior with byte-identical emit. When schema and context live in different namespaces, the consumer must provide a per-context reader at that location; missing or mis-declared per-context readers surface as ordinary C# compile errors against the generated interceptor reference.

## Plan items implemented as specified

- **Phase 1** — per-context `Pg`/`My`/`Ss` `Product` partial extensions (`DisplayLabel`) plus per-context `ProductReader` classes mirroring the global reader's `Read` body, in `src/Quarry.Tests/Samples/PerContextProductReaders.cs`.
- **Phase 2** — `string? contextNamespace` parameter added to `InterceptorCodeGenerator.CollectEntityReaderInstances` and `ReaderCodeGenerator.GenerateReaderDelegate`, plus a new `InterceptorCodeGenerator.ResolvePerContextReaderFqn` utility that rewrites the schema-namespace FQN to `<contextNamespace>.<simpleName>`. Updated the two callers (`FileEmitter.cs:368`, `QuarryGenerator.cs:605`) to thread the context namespace.
- **Phase 3** — three generator integration tests in `GeneratorTests.cs` under "Per-Context EntityReader Resolution (#277)": multi-namespace per-context FQN rewrite, single-context-same-namespace preservation, and cached-field deduplication.
- **Phase 4** — converted `Integration/EntityReaderIntegrationTests.cs` (9 SQLite-only tests) to `SqlOutput/CrossDialectEntityReaderTests.cs` (8 cross-dialect tests across Lite/Pg/My/Ss). Closes the deferred Phase 10 from cross-dialect-test-coverage.
- **Phase 5** — manifest verification folded into the Phase 4 commit; deltas reflect the new cross-dialect Products() chains on Pg/My/Ss (+86 lines each) and the removed SQLite-only Integration entries (−1 on Lite).
- **Phase 6** — added an "EntityReader" prose section to `llm.md` documenting per-context resolution.

## Deviations from plan implemented

None. All six phases landed in the planned dependency order with no scope drift.

## Gaps in original plan implemented

None. The plan called for fixing the `IQueryBuilder<TIn, TOut>` `TOut` binding in addition to the reader FQN; investigation showed the projection analyzer already produces per-context `TOut` for identity projections (`chain.EntityTypeName` is per-context throughout), so the reader-FQN rewrite alone is sufficient. Verified end-to-end via canary test before commit; the per-context PgDb interceptor now emits `IQueryBuilder<Pg.Product, Pg.Product>` and `ExecuteCarrierWithCommandAsync<Pg.Product>` correctly.

## Migration Steps

For consumers using `[EntityReader]` with multiple `QuarryContext` subclasses in different namespaces: provide a per-context reader at `<contextNamespace>.<readerSimpleName>` inheriting `EntityReader<TPerContextEntity>`. Per-context partial extensions on the entity (e.g. `partial class Product { public string DisplayLabel { get; set; } = ""; }`) may also be needed if the schema-namespace partial defines members that the per-context entity needs to expose. Single-context consumers (where schema and context share a namespace) need no changes.

## Performance Considerations

None. The codegen change is a string rewrite at one seam; no additional symbol lookups, no Compilation references threaded through. Emitted interceptor file size is unchanged. Field deduplication is unaffected.

## Security Considerations

None. The `contextNamespace` parameter is sourced from generator-internal state (`ContextGroup.ContextNamespace`, derived from `INamedTypeSymbol`-resolved `[QuarryContext]`-annotated classes); never untrusted input. The FQN string concatenation is safe — both inputs are C#-identifier-validated by Roslyn before the generator sees them.

## Breaking Changes

- **Consumer-facing**: For consumers using `[EntityReader]` across multiple contexts in different namespaces, this surfaces a clear C# compile error against the missing per-context reader class. Previously the consumer code "compiled" but routed materialization through the schema-namespace reader with `Unsafe.As<>` papering over a static-type mismatch — i.e., the compile-error case was already producing latently broken behavior. Single-context-same-namespace consumers see no change. No analyzer rule change; no new diagnostics.
- **Internal**: `InterceptorCodeGenerator.CollectEntityReaderInstances` gained an optional `string? contextNamespace` parameter (default null preserves prior behavior); `ReaderCodeGenerator.GenerateReaderDelegate` gained the same optional parameter. New internal `InterceptorCodeGenerator.ResolvePerContextReaderFqn` helper. No public API change.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
