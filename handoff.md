# Work Handoff

## Key Components
- `src/Quarry/Query/PreparedQuery.cs` — `PreparedQuery<TResult>` runtime type (all methods throw; replaced by generator interceptors)
- `src/Quarry/Query/IQueryBuilder.cs`, `IJoinedQueryBuilder.cs`, `Modification/IModificationBuilder.cs` — `.Prepare()` on all builder interfaces
- `src/Quarry/Query/QueryBuilder.cs`, `JoinedQueryBuilder.cs`, `Modification/DeleteBuilder.cs`, `UpdateBuilder.cs`, `InsertBuilder.cs` — concrete `.Prepare()` implementations
- `src/Quarry/Internal/CarrierBase.cs`, `JoinedCarrierBase*.cs`, `ModificationCarrierBase.cs` — explicit interface Prepare() on all carrier bases
- `src/Quarry.Generator/Models/InterceptorKind.cs` — `Prepare`, `ToSql` enum values
- `src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs` — PreparedQuery discovery, `isPreparedTerminal` flag, `DetectPreparedQueryEscape()`, `IsPreparedQueryTerminal()`
- `src/Quarry.Generator/Parsing/VariableTracer.cs` — `PreparedQuery` in `IsBuilderTypeName`
- `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` — multi-terminal chain detection, single-terminal collapse, QRY035/QRY036 diagnostic emission
- `src/Quarry.Generator/IR/RawCallSite.cs` — `IsPreparedTerminal`, `PreparedQueryEscapeReason` fields
- `src/Quarry.Generator/IR/TranslatedCallSite.cs` — `IsPreparedTerminal` accessor
- `src/Quarry.Generator/IR/AssembledPlan.cs`, `SqlAssembler.cs` — `PreparedTerminals`/`PrepareSite` passthrough
- `src/Quarry.Generator/IR/PipelineOrchestrator.cs` — passes diagnostics list to ChainAnalyzer
- `src/Quarry.Generator/CodeGen/FileEmitter.cs` — Prepare/ToSql dispatch, carrier lookup registration for prepared terminals
- `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs` — `EmitPrepareInterceptor`, `EmitToSqlTerminal`, prepared terminal signatures
- `src/Quarry.Generator/CodeGen/InterceptorRouter.cs` — Prepare, ToSql categorization
- `src/Quarry.Generator/DiagnosticDescriptors.cs` — QRY035 (escapes scope), QRY036 (no terminals)
- `src/Quarry.Generator/QuarryGenerator.cs` — QRY035/QRY036 in deferred descriptors dictionary
- `src/Quarry.Tests/SqlOutput/PrepareTests.cs` — single-terminal and multi-terminal SQL output tests
- `src/Quarry.Tests/Integration/PrepareIntegrationTests.cs` — SQLite execution tests for Prepare
- `src/Quarry.Tests/UsageSiteDiscoveryTests.cs` — escape detection unit tests, QRY036 negative test
- `src/Quarry.Tests/VariableTracerTests.cs` — PreparedQuery type recognition tests

## Completions (This Session)
- QRY035 diagnostic wired: escape detection via `DetectPreparedQueryEscape()` in UsageSiteDiscovery, stored on `RawCallSite.PreparedQueryEscapeReason`, emitted in ChainAnalyzer
- QRY036 diagnostic wired: no-terminals detection in `ChainAnalyzer.AnalyzeChainGroup()`, emitted via `DiagnosticInfo`
- Both diagnostics registered in `s_deferredDescriptors` for file-level emission routing
- Multi-terminal emission: works end-to-end using `Unsafe.As` zero-overhead approach (same as single-terminal collapse — `.Prepare()` casts builder to `PreparedQuery`, each terminal casts back)
- `InterceptorKind.ToSql` added: full interceptor pipeline (discovery, routing, emission via `EmitToSqlTerminal`)
- Prepared terminals registered in carrier lookup for carrier-optimized chain support
- 8 integration tests: single-terminal execution + multi-terminal (ToDiagnostics+Execute, ToSql+Execute) for select, delete, update
- 7 escape detection unit tests: return, argument, lambda capture, field assignment, not-escaped, fluent chain
- 4 multi-terminal SQL output tests: select, delete, update, cross-dialect

## Previous Session Completions
- Phase 1: `PreparedQuery<TResult>` type + `.Prepare()` on all builder interfaces and concrete classes
- Phase 2: Generator discovery — InterceptorKind.Prepare, IsPreparedTerminal flag, PreparedQuery type tracing, QRY035/QRY036 descriptors
- Phase 3: ChainAnalyzer multi-terminal detection, single-terminal collapse, PreparedTerminals on AnalyzedChain/AssembledPlan
- Phase 4: Emission — `EmitPrepareInterceptor` (Unsafe.As zero-overhead cast), prepared terminal signatures with `PreparedQuery<TResult>` receiver
- Phase 5: Tests (7 new tests: 5 SQL output, 2 VariableTracer) + fixes for type resolution in collapsed Prepare chains
- (Base branch) Compiler-sourced query diagnostics (#60) — expanded QueryDiagnostics, ClauseDiagnostic metadata, IsEnum/IsSensitive fix

## Progress
- Single-terminal collapse: fully working end-to-end across select, delete, update (verified with cross-dialect and integration tests)
- Multi-terminal: fully working for select, delete, update chains (verified with SQL output and SQLite integration tests)
- QRY035 (escape detection): wired and tested — detects return, argument passing, lambda capture, field assignment
- QRY036 (no terminals): wired and tested via negative test
- Insert/batch insert Prepare: **blocked** by type resolution bug
- Join chain Prepare: **blocked** by type resolution bug

## Current State

### Multi-Terminal Emission (DONE)
The `Unsafe.As` approach works for both single and multi-terminal because:
1. `.Prepare()` interceptor: `Unsafe.As<PreparedQuery<TResult>>(builder)` — zero allocation
2. Each prepared terminal interceptor: `Unsafe.As<ConcreteBuilder>(builder)` — casts back, accesses fields normally
3. Builder state (SQL dispatch table, parameters, connection) is immutable after `.Prepare()`

### Insert/BatchInsert Prepare (BLOCKED)
**What was tried**: Added Prepare tests for `IInsertBuilder<T>` and `IExecutableBatchInsert<T>`.
**Why it failed**: `ClassifyBuilderKind()` maps `InsertBuilder` to `BuilderKind.Query` (fallback), so `EmitPrepareInterceptor` emits `PreparedQuery<User>` instead of `PreparedQuery<int>`. Also, carrier emission references `.Entity` / `.BatchEntities` fields that don't exist on the generated carrier.
**What's left**: Add `BuilderKind.Insert` and `BuilderKind.ExecutableInsert` to the enum, handle them in `EmitPrepareInterceptor` and in prepared terminal receiver type resolution. Carrier field access also needs fixing.

### Join Chain Prepare (BLOCKED)
**What was tried**: Added Prepare tests for `IJoinedQueryBuilder<T1, T2, TResult>` chains.
**Why it failed**: `EmitPrepareInterceptor` emits `IJoinedQueryBuilder<T1, T2>` (2 type args) instead of `IJoinedQueryBuilder<T1, T2, TResult>` (3 type args) for projected joins. This causes CS0452 because `IJoinedQueryBuilder<T1, T2>` has a `class` constraint on T2 that tuple projections violate.
**What's left**: Handle `BuilderKind.JoinedQuery` in `EmitPrepareInterceptor` — when `site.ResultTypeName != null`, emit the 3-type-arg variant.

## Known Issues / Bugs
- **Insert Prepare type resolution**: `PreparedQuery<User>` emitted instead of `PreparedQuery<int>` for insert builders. `ClassifyBuilderKind` doesn't distinguish insert builders from query builders.
- **Join Prepare type resolution**: Wrong receiver arity for projected join chains in `EmitPrepareInterceptor`.
- **Anonymous type projections**: Don't work with `.Prepare()` (e.g., `Select(u => new { u.Name }).Prepare()`). Produces unresolvable type names. Named tuples or concrete types work fine. Same limitation exists for some direct chain interceptors.
- **ExecuteScalarAsync<TScalar>** on `PreparedQuery`: Generic type parameter `TScalar` emitted as literal in generated code instead of being resolved. Causes CS0246.
- **ExecuteFetchFirstOrDefaultAsync** on `PreparedQuery` with value-type tuples: Signature mismatch (CS9144) due to nullable return type `Task<T?>` vs `Task<T>`.
- **ResolvePreparedQueryOriginBuilderType**: May fail for deeply nested chains — traces max 2 hops.

## Dependencies / Blockers
- No external blockers

## Architecture Decisions
- **Unsafe.As for all paths**: Both single-terminal and multi-terminal use `Unsafe.As` to cast the builder to/from `PreparedQuery<TResult>`. No real `PreparedQuery` allocation ever occurs. The type only exists for compiler resolution. This works because terminal interceptors are read-only against the builder's state.
- **IsPreparedTerminal flag on RawCallSite**: Prepared terminals keep their original InterceptorKind (e.g., `ToDiagnostics`) and are distinguished by the boolean flag. This avoids doubling all terminal-handling code paths.
- **PreparedQueryEscapeReason on RawCallSite**: Escape detection is syntactic-only (no SemanticModel), runs during discovery, and the reason string is carried through to chain analysis for diagnostic emission.
- **ToSql as separate InterceptorKind**: Rather than piggybacking on ToDiagnostics, ToSql has its own `InterceptorKind.ToSql` and emitter (`EmitToSqlTerminal`). Simpler generated code — just dispatch table + return SQL string.
- **Diagnostics via DiagnosticInfo pipeline**: QRY035/QRY036 are collected during chain analysis into the existing `List<DiagnosticInfo>`, routed through `FileInterceptorGroup`, and reported in `EmitFileInterceptors`. No special handling needed.

## Open Questions
- Should multi-terminal emission use a separate carrier class or extend `PreparedQuery<TResult>` with internal fields? (Answered: neither — `Unsafe.As` works for all cases)
- How should `.Prepare()` interact with `.Trace()`?
- Should `ExecuteScalarAsync<TScalar>` be supported on `PreparedQuery`? Requires resolving generic type parameters in interceptor emission.

## Next Work (Priority Order)
1. **Fix insert Prepare type resolution**: Add `BuilderKind.Insert` / `BuilderKind.ExecutableInsert`, handle in `EmitPrepareInterceptor` to emit `PreparedQuery<int>`, fix carrier field access for insert terminals
2. **Fix join Prepare type resolution**: Handle `BuilderKind.JoinedQuery` in `EmitPrepareInterceptor` — emit correct type arg count based on whether projection exists
3. **Fix ExecuteScalarAsync<TScalar> on PreparedQuery**: Resolve generic type parameter in prepared terminal interceptor emission
4. **Fix ExecuteFetchFirstOrDefaultAsync on PreparedQuery**: Handle nullable return type for value-type results
5. **Trace interaction**: Determine how `.Trace()` should work with `.Prepare()` chains
