# Implementation Plan: Inline Scalar Execution & EnsureConnectionOpenAsync Fast-Path

**Issue:** Dtronix/Quarry#94 — ~2x overhead on scalar aggregate execution path
**Branch:** `fix/94-scalar-aggregate-perf` (or continuation on `feature/docfx-scaffold`)
**Scope:** Generator changes + runtime changes

---

## Problem Summary

Scalar aggregate queries (COUNT, SUM, AVG via `ExecuteScalarAsync`) show ~2x overhead vs Raw ADO.NET (6.4 μs vs 2.8 μs for COUNT). The overhead comes from:

1. **Double async state machine** — `await EnsureConnectionOpenAsync()` + `await ExecuteScalarAsync()` creates two async state machines when Raw has one.
2. **Interface dispatch** — `ctx.EnsureConnectionOpenAsync`, `ctx.Connection`, `ctx.SlowQueryThreshold`, `ctx.DefaultTimeout` are all interface calls through `IQueryExecutionContext`.
3. **Method call overhead** — The interceptor delegates to `QueryExecutor.ExecuteCarrierScalarWithCommandAsync<TScalar>`, adding a method boundary and generic instantiation.
4. **Residual instrumentation checks** — Even with the instrumentation gating already merged, there are still null checks and property accesses in the hot path.

## Changes Overview

Two independent changes that compose together:

| Change | Layer | Impact |
|---|---|---|
| **A. EnsureConnectionOpenAsync synchronous fast-path** | Runtime (`QuarryContext`) | All execution paths benefit (~0.5-1 μs saved) |
| **B. Inline scalar execution into generated interceptor** | Generator (`CarrierEmitter`) | Scalar paths eliminate executor delegation (~1-2 μs saved) |

---

## Change A: EnsureConnectionOpenAsync Synchronous Fast-Path

### Rationale

`EnsureConnectionOpenAsync` is an `async Task` method. Even when the connection is already open (the common case), the caller pays for async state machine setup. Since the connection is opened once and reused for the lifetime of the context, every subsequent call is a no-op that still incurs async overhead.

### Current Implementation

**File:** `src/Quarry/Context/QuarryContext.cs:97-106`

```csharp
protected async Task EnsureConnectionOpenAsync(CancellationToken cancellationToken = default)
```

This is always `async`, always returns `Task`, and the caller always `await`s it. When the connection is already open, the method checks `_connection.State != ConnectionState.Open`, finds it open, and returns — but the async machinery is already allocated.

### Proposed Change

Add a synchronous fast-path that returns a completed task when the connection is already open. Two parts:

**Part 1 — Add a cached completed task and sync check to `QuarryContext`:**

```csharp
private Task EnsureConnectionOpenCoreAsync(CancellationToken cancellationToken)
```

The public/interface method becomes a non-async method that checks `_connection.State == ConnectionState.Open` and returns `Task.CompletedTask` immediately. Only when the connection is NOT open does it fall through to the actual async method that calls `_connection.OpenAsync()`.

This pattern (sync fast-path with async slow-path) is standard in high-performance .NET code. The JIT can inline the fast-path check, and `Task.CompletedTask` is a cached singleton — zero allocation.

**Part 2 — Update `IQueryExecutionContext` interface:**

The interface method signature stays the same (`Task EnsureConnectionOpenAsync(CancellationToken)`). The optimization is purely in the implementation. Callers that `await` a completed task pay near-zero cost because the runtime short-circuits completed tasks in `async` methods.

### Files Modified

| File | Change |
|---|---|
| `src/Quarry/Context/QuarryContext.cs` | Split `EnsureConnectionOpenAsync` into sync check + async slow-path |

### Key Detail

The `ValueTask` pattern would be even more optimal but would require changing the `IQueryExecutionContext` interface signature, which is a larger breaking change. `Task.CompletedTask` is sufficient and non-breaking.

---

## Change B: Inline Scalar Execution into Generated Interceptor

### Rationale

Currently, all `ExecuteScalarAsync` interceptors delegate to `QueryExecutor.ExecuteCarrierScalarWithCommandAsync<TScalar>()`. This adds:

- A generic method instantiation boundary
- Interface dispatch for `ctx.EnsureConnectionOpenAsync`, `ctx.Connection`, `ctx.SlowQueryThreshold`
- An extra method frame in the async state machine chain

For scalar queries, the executor method does very little beyond what the interceptor already sets up. Inlining eliminates the method call and allows the generator to emit tighter code with direct field access on the carrier instead of interface dispatch.

### Current Generated Code (ExecuteScalarAsync interceptor)

```csharp
public static Task<TScalar> ExecuteScalarAsync_XXX<TEntity, TResult, TScalar>(
    this IQueryBuilder<TEntity, TResult> builder,
    CancellationToken cancellationToken = default) where TEntity : class
{
    var __c = Unsafe.As<Chain_0>(builder);
    var __opId = LogsmithOutput.Logger != null ? OpId.Next() : 0;
    var sql = Chain_0._sql;
    if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)
        QueryLog.SqlGenerated(__opId, sql);
    var __cmd = __c.Ctx.Connection.CreateCommand();
    __cmd.CommandText = sql;
    __cmd.CommandTimeout = (int)(__c.Ctx!.DefaultTimeout).TotalSeconds;
    return QueryExecutor.ExecuteCarrierScalarWithCommandAsync<TScalar>(__opId, __c.Ctx, __cmd, cancellationToken);
}
```

### Proposed Generated Code

```csharp
public static async Task<TScalar> ExecuteScalarAsync_XXX<TEntity, TResult, TScalar>(
    this IQueryBuilder<TEntity, TResult> builder,
    CancellationToken cancellationToken = default) where TEntity : class
{
    var __c = Unsafe.As<Chain_0>(builder);
    var __ctx = __c.Ctx!;

    // Inline connection check (direct field access, no interface dispatch)
    await __ctx.EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);

    var __opId = LogsmithOutput.Logger != null ? OpId.Next() : 0;
    var sql = Chain_0._sql;

    if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Debug, QueryLog.CategoryName) == true)
        QueryLog.SqlGenerated(__opId, sql);

    var __cmd = __ctx.Connection.CreateCommand();
    __cmd.CommandText = sql;
    __cmd.CommandTimeout = (int)(__ctx.DefaultTimeout).TotalSeconds;

    // [parameter binding emitted here for parameterized chains]

    var __result = await __cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

    // Inline instrumentation (gated)
    var __instrumented = LogsmithOutput.Logger != null || __ctx.SlowQueryThreshold.HasValue;
    if (__instrumented)
    {
        // Stopwatch + CheckSlowQuery + ScalarResult logging
        // Note: startTimestamp must be captured before ExecuteScalarAsync
        // See "Stopwatch placement" below
    }

    // Inline null check
    if (__result is null or DBNull)
    {
        if (default(TScalar) is null) return default!;
        throw new InvalidOperationException("Query returned null but expected a non-nullable value.");
    }

    return ScalarConverter.Convert<TScalar>(__result);
}
```

### Stopwatch Placement

The stopwatch must be started before `ExecuteScalarAsync` to measure the actual query time. This means the `instrumented` check needs to happen before the query too:

```csharp
var __instrumented = LogsmithOutput.Logger != null || __ctx.SlowQueryThreshold.HasValue;
var __ts = __instrumented ? Stopwatch.GetTimestamp() : 0;
var __result = await __cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
if (__instrumented) { /* elapsed calc, slow query, logging */ }
```

This is the same pattern already used in `ExecuteCarrierScalarWithCommandAsync` — the generator just emits it inline.

### Generator Changes

#### File: `src/Quarry.Generator/CodeGen/CarrierEmitter.cs`

**New method: `EmitInlineScalarTerminal`**

```csharp
internal static void EmitInlineScalarTerminal(
    StringBuilder sb, CarrierPlan carrier, AssembledPlan chain)
```

This method emits the full inline scalar execution body. It replaces the call to `EmitCarrierExecutionTerminal` for scalar interceptors.

Responsibilities:
1. Emit carrier cast (`Unsafe.As<Chain_N>(builder)`) and context extraction
2. Emit `EnsureConnectionOpenAsync` call
3. Emit conditional OpId generation
4. Emit SQL dispatch (single static field or mask-indexed array — reuse `EmitCarrierSqlDispatch`)
5. Emit SQL logging (guarded)
6. Emit command creation and parameter binding (reuse `EmitCarrierCommandBinding`)
7. Emit instrumentation gate + Stopwatch start
8. Emit `await __cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)`
9. Emit instrumentation body (elapsed, CheckSlowQuery, ScalarResult logging)
10. Emit null/DBNull check with appropriate default/throw
11. Emit `ScalarConverter.Convert<TScalar>(__result)` return

The method reuses existing helpers:
- `EmitCarrierSqlDispatch` for SQL variant lookup
- `EmitCarrierCommandBinding` for parameter binding
- `EmitInlineParameterLogging` for parameter trace logging

#### File: `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs`

**Modify `EmitReaderTerminal` (or add new path):**

At the point where `InterceptorKind.ExecuteScalar` is handled (around line 91), instead of:

```csharp
InterceptorKind.ExecuteScalar => "ExecuteCarrierScalarWithCommandAsync<TScalar>"
```

Route to the new inline emitter:

```csharp
if (site.Kind == InterceptorKind.ExecuteScalar)
{
    CarrierEmitter.EmitInlineScalarTerminal(sb, carrier, chain);
    return;
}
```

**Signature change:** The method must become `async Task<TScalar>` instead of returning `Task<TScalar>`. This means the method signature emission (lines 69-73) needs a conditional `async` keyword for the scalar inline path.

#### File: `src/Quarry.Generator/CodeGen/FileEmitter.cs`

No structural changes needed. The routing at line 586 already sends ExecuteScalar to `TerminalBodyEmitter.EmitReaderTerminal()`. The change is internal to how that method emits the body.

### Handling Parameterized Chains

For chains with parameters (e.g., `WHERE`-filtered aggregates), the generator already emits parameter binding via `EmitCarrierCommandBinding`. The inline scalar path reuses this identically — the parameter binding code goes between command creation and `ExecuteScalarAsync`.

For conditional chains (multiple SQL variants), `EmitCarrierSqlDispatch` already handles mask-based lookup. The inline scalar path reuses this too.

### Handling Conditional Chains

Chains with conditional branches (`SqlVariants.Count > 1`) use mask-indexed SQL dispatch. This works identically in the inline path — `EmitCarrierSqlDispatch` emits `var sql = Chain_N._sql[__c.Mask]` and the rest of the inline body proceeds the same way.

### Error Handling

The current `ExecuteCarrierScalarWithCommandAsync` has a `catch` block that wraps exceptions in `QuarryQueryException`. The inline path must emit the same try/catch structure:

```csharp
try
{
    // ... execute + convert ...
}
catch (InvalidOperationException) { throw; }
catch (Exception ex) when (ex is not OperationCanceledException)
{
    if (LogsmithOutput.Logger?.IsEnabled(LogLevel.Error, QueryLog.CategoryName) == true)
        QueryLog.QueryFailed(__opId, ex);
    throw new QuarryQueryException($"Error executing scalar query: {ex.Message}", __cmd.CommandText, ex);
}
```

### Required Using Directives

The generated interceptor file already includes all needed usings (`System.Diagnostics` is needed for `Stopwatch`). Verify that `System.Diagnostics` is in the generated file's using list. If not, add it to the file-level usings emitted by `FileEmitter`.

**File:** `src/Quarry.Generator/CodeGen/FileEmitter.cs` — check the usings block (typically near the top of `EmitFileHeader` or equivalent).

---

## Testing Strategy

### Unit Tests (Generator)

**File:** `src/Quarry.Tests/Generation/CarrierGenerationTests.cs`

Add test cases that verify the generated interceptor code for scalar chains contains inlined execution rather than a `QueryExecutor.ExecuteCarrierScalarWithCommandAsync` delegation:

1. **Zero-parameter scalar** (COUNT) — verify inline body with no parameter binding
2. **Parameterized scalar** (COUNT with WHERE) — verify inline body with parameter binding
3. **Conditional scalar** (COUNT with conditional WHERE) — verify inline body with mask dispatch

Assert that the generated code:
- Does NOT contain `QueryExecutor.ExecuteCarrierScalarWithCommandAsync`
- DOES contain `await __cmd.ExecuteScalarAsync`
- DOES contain `ScalarConverter.Convert<TScalar>`
- DOES contain the try/catch error handling

### Integration Tests (Runtime)

**File:** `src/Quarry.Tests/ScalarConverterTests.cs` (existing)

Existing tests cover `ScalarConverter`. Add integration tests that exercise the full inline path:

1. COUNT returning int
2. SUM returning decimal
3. AVG returning decimal
4. Scalar returning null for nullable types
5. Scalar throwing for non-nullable when null

### Benchmark Verification

Re-run `AggregateBenchmarks` after both changes and compare against the baseline numbers:

| Method | Before | Target |
|---|---|---|
| Quarry_Count | 6.4 μs (2.27x) | ~3.0-3.5 μs (~1.1-1.2x) |
| Quarry_Sum | 13.7 μs (1.66x) | ~9-10 μs (~1.1-1.2x) |
| Quarry_Avg | 13.8 μs (1.70x) | ~9-10 μs (~1.1-1.2x) |

---

## Implementation Order

1. **Change A first** — `EnsureConnectionOpenAsync` sync fast-path. This is a runtime-only change, easy to verify independently, and benefits all execution paths.
2. **Change B second** — Inline scalar emitter. This is the generator change that depends on Change A being in place (the inlined code calls `EnsureConnectionOpenAsync` which should already have the fast-path).
3. **Run benchmarks** to verify combined impact.
4. **Run full test suite** to ensure no regressions.

---

## Files Modified Summary

| File | Change Type | Description |
|---|---|---|
| `src/Quarry/Context/QuarryContext.cs` | Runtime | Sync fast-path for `EnsureConnectionOpenAsync` |
| `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` | Generator | New `EmitInlineScalarTerminal` method |
| `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs` | Generator | Route ExecuteScalar to inline emitter, conditional `async` signature |
| `src/Quarry.Generator/CodeGen/FileEmitter.cs` | Generator | Ensure `System.Diagnostics` using is emitted (if missing) |
| `src/Quarry.Tests/Generation/CarrierGenerationTests.cs` | Test | Verify inline scalar code generation |
| `src/Quarry.Tests/ScalarConverterTests.cs` | Test | Integration tests for inline scalar path |

---

## Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Generated code size increase (inline vs delegation) | Scalar interceptors are few per project; size increase is negligible |
| Duplicated error handling logic between inline and executor | Extract shared constants/patterns; the executor method remains for non-carrier paths (PreparedQuery, RawSql) |
| `EnsureConnectionOpenAsync` fast-path hides connection state bugs | The slow-path still calls `OpenAsync` — behavior is identical, just faster for the common case |
| Conditional chains with many variants + parameters produce large inline bodies | Same code that `EmitCarrierExecutionTerminal` already produces — no new complexity |
