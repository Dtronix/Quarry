# Review: 277-entityreader-per-context-resolution

Review pass against the 5-commit branch (871fb73 → b10edb9) cross-referenced against `plan.md`'s 6-phase plan and `workflow.md`'s Decisions table. Each section either reports findings as a `Finding | Severity | Why It Matters` table or "No concerns."

## Plan Compliance

No concerns.

All 6 phases landed in the planned dependency order: Layer 1 sample-side partials/readers (871fb73) → generator codegen change (2f357eb) → generator integration tests (d3b33df) → cross-dialect test conversion + manifest deltas (991f85b) → documentation (b10edb9). Phase 5 manifest verification was folded into the Phase 4 commit because the manifest deltas are direct consequences of the test conversion — `workflow.md` records this fold explicitly.

All four DESIGN decisions were honored: per-context resolution is the default with no fallback; QRY027 was preserved unchanged; no IR `ReaderClassSimpleName` slot was added; no `Compilation` threading or new diagnostics were introduced. The simple name is derived from the existing FQN at the codegen seam, which is exactly what was prescribed.

## Correctness

No concerns.

`ResolvePerContextReaderFqn` (`InterceptorCodeGenerator.cs:224`) handles all edge cases: empty/null `contextNamespace` returns the original FQN unchanged (covers test scaffolding without a namespace); a reader FQN with no dots returns its own simple name (`lastDot >= 0` guards the substring); the simple-name slice is correct (`Substring(lastDot + 1)`).

The two callers pass semantically-equivalent values: `FileEmitter._contextNamespace` is the file emitter's record of the same context that `QuarryGenerator.cs:605` passes via `group.ContextNamespace`. Both originate from the same `ContextGroup` upstream, so they cannot diverge.

Phase 1's per-context `Read` bodies (`PerContextProductReaders.cs`) are verbatim copies of the global reader for `Pg`, `My`, `Ss` — no divergence in column ordering, null handling, or `DisplayLabel` formatting that could produce inconsistent cross-dialect results.

## Security

No concerns.

The `contextNamespace` parameter is sourced from generator-internal state (`ContextGroup.ContextNamespace`, derived from `INamedTypeSymbol`-resolved `[QuarryContext]`-annotated classes during compilation), never from untrusted input. The FQN string concatenation is safe — both inputs are C#-identifier-validated by Roslyn before the generator sees them.

## Test Quality

No concerns.

The three generator pipeline tests in `GeneratorTests.cs` cover the meaningful behaviors:
1. `Generator_PerContextEntityReader_RewritesFqnPerContext` — the multi-namespace happy path, schema in `App.Schemas` referenced by `App.Pg.PgDb` and `App.My.MyDb`, asserts each interceptor file emits its own per-context reader FQN and explicitly does NOT reference the schema-namespace one.
2. `Generator_PerContextEntityReader_PreservesSchemaNamespaceWhenContextShares` — the boundary case (schema and context in the same namespace) where the per-context FQN equals the schema-namespace FQN; locks in the "zero behavior change for single-context consumers" guarantee.
3. `Generator_PerContextEntityReader_DeduplicatesCachedField` — uses a regex match on `private static readonly App.Pg.ProductReader _entityReader_App_Pg_ProductReader = new();` to assert exactly one declaration, even though the field is referenced multiple times across three call sites.

The eight cross-dialect tests in `CrossDialectEntityReaderTests.cs` exercise: identity projection runs the custom reader (proven by `DisplayLabel` populated on Lite/Pg/My/Ss); NULL handling on a column that the reader explicitly handles; multi-row materialization; tuple and single-column projections explicitly do NOT route through the custom reader; `ExecuteFetchFirst` and `ExecuteFetchFirstOrDefault` variants; and an Insert+Select round-trip on each dialect to verify per-context reader behavior on materialization of freshly-inserted rows. Each test runs against four dialects, so the effective execution count is 32 logical tests.

## Codebase Consistency

No concerns.

`ResolvePerContextReaderFqn` follows existing `InterceptorCodeGenerator` utility-method conventions (`internal static`, brief XML docs explaining the rule, dialect-agnostic). The `string? contextNamespace` parameter naming and default-null pattern matches existing codegen seams (e.g., `GenerateInterceptorsFile`'s `string? contextNamespace`). The new tests use the same `RunGeneratorWithDiagnostics` / `RunGenerator` helpers that existing `GeneratorTests.cs` tests use; the cross-dialect test follows the verbatim 4-dialect pattern from existing `CrossDialectSelectTests.cs` / `CrossDialectWhereTests.cs`. No utility duplication.

## Integration / Breaking Changes

No concerns.

The behavior change is a strict improvement, not a regression:

- **Single-context-same-namespace** (schema and context share a namespace, e.g., `TestDbContext` in `Quarry.Tests.Samples` with `ProductSchema` in `Quarry.Tests.Samples`): per-context FQN computes to the schema-namespace FQN, so emitted code is byte-identical to the previous behavior.
- **Multi-context-different-namespace** (schema in one namespace, contexts in others): previously emitted invalid `IQueryBuilder<PerContextEntity, GlobalEntity>` chains with `Unsafe.As<>` papering over the static-type mismatch. Now emits correct `IQueryBuilder<PerContextEntity, PerContextEntity>` chains backed by per-context readers. Consumers without per-context readers will see a clear C# compile error against the missing class — which is more useful than the silent type-divergence they had before.

No new dependencies; no API surface changes; no migrations needed. The `llm.md` doc update is supplementary and accurately describes the new behavior.

## Issues Created

None — no findings of any severity required follow-up work.
