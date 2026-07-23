## Description
Query chains that terminate directly on `IQueryBuilder<T>` (the entity-terminal fallback path — no explicit `.Select(...)` projection) generate an interceptor whose signature/arity does not match the call site. The compiler reports CS9177 (interceptor generic-arity mismatch) and the call is silently NOT intercepted — it falls through to the runtime path. In a carrier-only architecture an unintercepted call is a correctness hole, not a perf footnote.

The test suite works around this everywhere by appending explicit `.Select(...)` projections, and `Quarry.Tests.csproj` carries a blanket `<NoWarn>CS9177</NoWarn>` that also hides any NEW arity mismatch introduced later. Previously noted only as duplicated "tracked separately" comments with no tracking issue — split out of #314's finding 5.

## Location
- Workaround comments (duplicated): `src/Quarry.Tests/Integration/PostgresIntegrationTests.cs:45-48`, `src/Quarry.Tests/Integration/MySqlIntegrationTests.cs:63-66`, `src/Quarry.Tests/Integration/SqlServerIntegrationTests.cs:42-45`, plus shortened variants at the InsertBatch tests ("Explicit projection avoids the IQueryBuilder<T>-terminal overload mismatch").
- Blanket suppression: `src/Quarry.Tests/Quarry.Tests.csproj:13-14`.
- Interceptor arity handling: `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs` (arity commentary at ~362-372, 465-466), `src/Quarry.Generator/CodeGen/JoinBodyEmitter.cs:303`.

## Diagnostics
- Chains like `db.Addresses().Where(...).ExecuteFetchAllAsync()` (no `.Select`) trip the mismatch; adding `.Select(a => a.City)` routes to the `IQueryBuilder<T,TResult>` overload whose interceptor binds correctly.
- CS9177 = interceptor generic-arity mismatch: a generic terminal on a generic receiver needs the combined arity (e.g. `ExecuteScalarAsync<TKey>` on `IInsertBuilder<T>` needs `<T, TKey>`); the entity-terminal fallback emits an interceptor that doesn't satisfy this for the `IQueryBuilder<T>` overload set.
- No other project in the repo (including Samples using interceptors) needs the CS9177 suppression — only the test project exercises the fallback path.

## What Has Been Tried
Only avoidance: explicit projections at every affected call site plus the project-wide NoWarn.

## Gathered Information
- CS9144 (signature-type mismatch) is the related diagnostic family; see `src/Quarry.Generator/IR/CallSiteBinder.cs:93` and `DiagnosticDescriptors.cs:682`.
- #314 adds (a) a pinning test asserting the entity-terminal call is currently not intercepted — it fails when this is fixed — and (b) a CS9177 guard test asserting the exact expected set of mismatch sites so new regressions surface despite the NoWarn.

## Suggested Approach
Emit entity-terminal interceptors with the combined generic arity matching the `IQueryBuilder<T>` terminal overloads (mirroring the TerminalBodyEmitter approach used for `PreparedQuery<TResult>.ExecuteScalarAsync<TKey>`), then remove the explicit-projection workarounds and the blanket NoWarn. If some overload genuinely cannot be intercepted, disqualify it with an explicit QRY diagnostic instead of a silent fallback.
