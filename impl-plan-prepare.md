# Implementation Plan: `.Prepare()` Multi-Terminal Support

## Overview

Add a `.Prepare()` terminal to all builder interfaces that returns a `PreparedQuery<TResult>`, allowing multiple terminal operations (`.ToDiagnostics()`, `.ExecuteFetchAllAsync()`, etc.) to be called on the same compiled query chain without rebuilding it.

`.Prepare()` sits in the terminal position — it can only be called where a terminal would be valid, after all clauses and projections are finalized. The returned `PreparedQuery<TResult>` exposes the same terminal methods the originating builder had.

## Core Design Principles

**Single-terminal collapse**: When the generator detects that only one terminal is called on a `PreparedQuery` variable, it must emit identical code to a direct terminal chain — no `PreparedQuery` allocation, no indirection, zero overhead. Leftover `.Prepare()` calls with a single terminal are free.

**Multi-terminal union emission**: When the generator detects N>1 terminals on a `PreparedQuery` variable, it emits a carrier covering only the observed terminals. If `.ToDiagnostics()` is never called, no diagnostics metadata is emitted. If no `Execute*` is called, no reader delegate is emitted.

**Escape analysis error**: If a `PreparedQuery` variable escapes the analyzable scope (returned from a method, stored in a field, passed as an argument), the generator emits a compile-time diagnostic error. This is not a fallback — it is a hard error. The developer must keep `.Prepare()` and its terminals in the same method body.

**Zero unused-terminal detection**: If `.Prepare()` is called but zero terminals are observed on the variable, the generator emits a diagnostic error (dead code).

## New Runtime Type

### `PreparedQuery<TResult>`

A single generic type used across all builder kinds. `TResult` is the row type for select queries, `int` for delete/update (`ExecuteNonQueryAsync` return), or `TKey` for insert scalar returns.

**Location**: `src/Quarry/Query/PreparedQuery.cs`

```csharp
public sealed class PreparedQuery<TResult>
```

**Terminal methods exposed** (superset — generator interceptors replace only the ones actually used):

```csharp
// Diagnostics
public QueryDiagnostics ToDiagnostics();
public string ToSql();

// Select terminals
public Task<List<TResult>> ExecuteFetchAllAsync(CancellationToken cancellationToken = default);
public Task<TResult> ExecuteFetchFirstAsync(CancellationToken cancellationToken = default);
public Task<TResult?> ExecuteFetchFirstOrDefaultAsync(CancellationToken cancellationToken = default);
public Task<TResult> ExecuteFetchSingleAsync(CancellationToken cancellationToken = default);
public Task<TScalar> ExecuteScalarAsync<TScalar>(CancellationToken cancellationToken = default);
public IAsyncEnumerable<TResult> ToAsyncEnumerable(CancellationToken cancellationToken = default);

// Modification terminals
public Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default);
```

The type holds an internal reference to the frozen `QueryState` (or carrier fields). The default method bodies throw `NotSupportedException` — they only exist so the compiler resolves the call sites. The generator intercepts and replaces them entirely.

## Interface Changes

### `.Prepare()` Method Addition

Add `.Prepare()` to every builder interface that has at least one terminal method. The return type is always `PreparedQuery<TResult>` with the appropriate `TResult` for that builder kind.

**Select builders** — `TResult` is the projected row type:

```csharp
// IQueryBuilder<T> — TResult = T (no projection)
PreparedQuery<T> Prepare();

// IQueryBuilder<TEntity, TResult>
PreparedQuery<TResult> Prepare();

// IJoinedQueryBuilder<T1, T2> — no projection, TResult = (T1, T2) or similar
PreparedQuery<(T1, T2)> Prepare();

// IJoinedQueryBuilder<T1, T2, TResult>
PreparedQuery<TResult> Prepare();

// IJoinedQueryBuilder3<T1, T2, T3>
PreparedQuery<(T1, T2, T3)> Prepare();

// IJoinedQueryBuilder3<T1, T2, T3, TResult>
PreparedQuery<TResult> Prepare();

// IJoinedQueryBuilder4<T1, T2, T3, T4>
PreparedQuery<(T1, T2, T3, T4)> Prepare();

// IJoinedQueryBuilder4<T1, T2, T3, T4, TResult>
PreparedQuery<TResult> Prepare();
```

**Delete builders** — `TResult` = `int`:

```csharp
// IDeleteBuilder<T>
PreparedQuery<int> Prepare();

// IExecutableDeleteBuilder<T>
PreparedQuery<int> Prepare();
```

**Update builders** — `TResult` = `int`:

```csharp
// IUpdateBuilder<T>
PreparedQuery<int> Prepare();

// IExecutableUpdateBuilder<T>
PreparedQuery<int> Prepare();
```

**Insert builders** — `TResult` = `int` (NonQuery) or entity-dependent:

```csharp
// IInsertBuilder<T>
PreparedQuery<int> Prepare();

// IExecutableBatchInsert<T>
PreparedQuery<int> Prepare();
```

### `QueryBuilder` Implementation

The concrete `QueryBuilder<T>` (and joined variants, delete/update builders) implement `.Prepare()` by snapshotting the current `QueryState` into a `PreparedQuery<TResult>`:

```csharp
public PreparedQuery<TResult> Prepare() => new(State);
```

This is the unintercepted fallback. The generator replaces this call site entirely.

## Source Generator Changes

### Stage 1: Discovery — `UsageSiteDiscovery`

**New `InterceptorKind` value**:

```csharp
internal enum InterceptorKind
{
    // ... existing values ...
    Prepare,
}
```

**Interceptable methods registration**: Add `"Prepare"` to the interceptable methods dictionary, mapped to `InterceptorKind.Prepare`. The builder kind detection logic maps it based on the receiver type, same as existing terminals.

**Chain ID computation**: When `ComputeChainId` encounters a `.Prepare()` call, it computes the chain ID normally (traces back through the builder chain). The `.Prepare()` site gets a chain ID like any terminal.

**Forward terminal scanning**: After discovering a `.Prepare()` call site, the discovery phase must also discover the terminal calls made on the `PreparedQuery` variable. This requires:

1. Identify the variable the `.Prepare()` result is assigned to (e.g., `var q = ...Prepare()`)
2. Find all invocations on that variable in the same method body
3. Register each as a call site with the **same chain ID** as the `.Prepare()` site
4. Tag each with a new flag indicating it's a "prepared terminal" — a terminal called on a `PreparedQuery` rather than directly on a builder

The forward scan is bounded: it only looks within the declaring method body, and the variable must not escape (see error conditions below).

### Stage 1: Discovery — `VariableTracer`

**New recognized type**: Add `PreparedQuery` to `IsBuilderTypeName`:

```csharp
internal static bool IsBuilderTypeName(string name)
{
    return name switch
    {
        // ... existing builder types ...
        "PreparedQuery" => true,
        _ => false,
    };
}
```

This allows `TraceToChainRoot` to trace through `PreparedQuery` variable assignments back to the originating builder chain.

### Stage 2: Binding — No Changes

`BoundCallSite` construction for `.Prepare()` follows the same pattern as other terminals — it inherits the context, dialect, entity, and table info from the chain root. The prepared terminals inherit the same binding.

### Stage 3: Translation — `ChainAnalyzer`

**Multi-terminal chain detection**: `ChainAnalyzer.Analyze` currently expects exactly one execution terminal per chain group. This must change to support 1+ terminals when a `.Prepare()` is present.

Algorithm change in `AnalyzeChainGroup`:

1. Collect all call sites in the chain group
2. If a `Prepare` kind site exists:
   a. Identify all prepared terminal sites (tagged during discovery)
   b. Count the distinct terminals: N
   c. If N == 0 → emit `QRY0XX` error (dead `.Prepare()`)
   d. If N == 1 → **collapse**: treat the single prepared terminal as if `.Prepare()` didn't exist. Remove the `Prepare` site from the chain. The single terminal becomes the chain's execution terminal. Proceed with existing single-terminal codegen.
   e. If N > 1 → **multi-terminal path**: record all N terminals on the `AnalyzedChain`. The `QueryPlan` is built once. The `AssembledPlan` must account for multiple terminal kinds.
3. If no `Prepare` kind site exists → existing single-terminal logic, unchanged

**`AnalyzedChain` changes**: Add a field to hold the set of terminal kinds when multi-terminal:

```csharp
internal sealed class AnalyzedChain
{
    // ... existing fields ...
    public IReadOnlyList<TranslatedCallSite> PreparedTerminals { get; }
}
```

When `PreparedTerminals` has more than one entry, the assembly phase knows to emit a multi-terminal carrier.

### Stage 3: Translation — `AnalyzabilityChecker`

**Escape detection**: When a `.Prepare()` call site is found, the checker must verify:

1. The result is assigned to a local variable (`var q = ...Prepare()`)
2. The variable is not returned from the method
3. The variable is not passed as an argument to another method
4. The variable is not assigned to a field or property
5. The variable is not captured in a lambda or closure

If any of these conditions are violated, emit diagnostic error `QRY0XX` ("PreparedQuery must not escape the declaring method scope").

This reuses the same escape-detection patterns already applied to builder variables (`isPassedAsArgument`, `isCapturedInLambda`, `isAssignedFromNonQuarryMethod` flags on `RawCallSite`), extended to cover return statements and field assignments.

### Stage 4: Assembly — `PlanAssembler`

**Single-terminal (collapsed)**: No changes. The `.Prepare()` was elided in Stage 3. Assembly proceeds as if the chain ended with the single terminal directly.

**Multi-terminal**: The assembler builds one `AssembledPlan` that is the **union** of what each terminal needs:

- If any terminal is `ToDiagnostics` → include clause diagnostic metadata in the carrier
- If any terminal is an `Execute*` variant → include the reader delegate and parameter hydration
- If any terminal is `ToSql` → the SQL string is always available (it's needed by all paths anyway)

The `AssembledPlan` gains a field tracking which terminal kinds are present:

```csharp
internal sealed class AssembledPlan
{
    // ... existing fields ...
    public IReadOnlySet<InterceptorKind>? PreparedTerminalKinds { get; }
}
```

When `PreparedTerminalKinds` is null or empty, it's a standard single-terminal plan (existing behavior). When populated, the emitter knows to generate the multi-terminal carrier.

### Stage 5: Emission — `InterceptorEmitter`

**Single-terminal (collapsed)**: No changes. Identical emitted code.

**Multi-terminal**: The emitter generates:

1. **An interceptor for the `.Prepare()` call site** that constructs a `PreparedQuery<TResult>` populated with the frozen carrier fields (pre-built SQL string(s), parameter slots, clause mask, reader delegate if needed, diagnostics metadata if needed). This is the only place the `PreparedQuery` is allocated.

2. **An interceptor for each prepared terminal call site** that reads from the `PreparedQuery<TResult>` instance and dispatches:
   - `.ToDiagnostics()` interceptor → reads SQL, parameters, clause metadata from the prepared instance, constructs `QueryDiagnostics`
   - `.ExecuteFetchAllAsync()` interceptor → reads SQL, hydrates parameters, executes via connection, applies reader delegate
   - `.ToSql()` interceptor → returns the pre-built SQL string from the prepared instance

Each prepared terminal interceptor is a thin accessor — the heavy lifting (SQL assembly, reader codegen) was done once at the `.Prepare()` interceptor site.

**Interceptor signature for `.Prepare()`**:

```csharp
internal static PreparedQuery<TResult> Prepare_interceptor(
    IQueryBuilder<TEntity, TResult> builder)
```

**Interceptor signatures for prepared terminals** (one per observed terminal):

```csharp
internal static QueryDiagnostics ToDiagnostics_prepared_interceptor(
    PreparedQuery<TResult> prepared)

internal static Task<List<TResult>> ExecuteFetchAllAsync_prepared_interceptor(
    PreparedQuery<TResult> prepared,
    CancellationToken cancellationToken)
```

The emitter distinguishes "prepared terminal" interceptors from "direct terminal" interceptors by the receiver type (`PreparedQuery<TResult>` vs `IQueryBuilder<...>`).

## New Diagnostic IDs

| ID | Title | Severity | Condition |
|---|---|---|---|
| QRY035 | PreparedQuery escapes scope | Error | `PreparedQuery` variable is returned, passed as argument, captured in lambda, or assigned to field |
| QRY036 | PreparedQuery has no terminals | Error | `.Prepare()` called but no terminal methods invoked on the resulting variable |

These follow the existing diagnostic numbering sequence (QRY033 is the current highest non-migration diagnostic).

## Implementation Order

### Phase 1: Runtime Type and Interface Changes

1. Create `PreparedQuery<TResult>` type in `src/Quarry/Query/PreparedQuery.cs`
2. Add `.Prepare()` method to all builder interfaces:
   - `IQueryBuilder<T>`
   - `IQueryBuilder<TEntity, TResult>`
   - `IJoinedQueryBuilder<T1, T2>`
   - `IJoinedQueryBuilder<T1, T2, TResult>`
   - `IJoinedQueryBuilder3<T1, T2, T3>`
   - `IJoinedQueryBuilder3<T1, T2, T3, TResult>`
   - `IJoinedQueryBuilder4<T1, T2, T3, T4>`
   - `IJoinedQueryBuilder4<T1, T2, T3, T4, TResult>`
   - `IDeleteBuilder<T>`
   - `IExecutableDeleteBuilder<T>`
   - `IUpdateBuilder<T>`
   - `IExecutableUpdateBuilder<T>`
   - `IInsertBuilder<T>`
   - `IExecutableBatchInsert<T>`
3. Implement `.Prepare()` on all concrete builder classes (snapshot `QueryState`)

### Phase 2: Generator Discovery

4. Add `InterceptorKind.Prepare` to the enum
5. Register `"Prepare"` in the interceptable methods dictionary in `UsageSiteDiscovery`
6. Add `"PreparedQuery"` to `VariableTracer.IsBuilderTypeName`
7. Implement forward terminal scanning in `UsageSiteDiscovery`: when a `Prepare` site is discovered, scan the method body for terminal invocations on the assigned variable, register them with the same chain ID
8. Add escape detection for `PreparedQuery` variables in `AnalyzabilityChecker`
9. Add `QRY035` and `QRY036` diagnostic descriptors

### Phase 3: Chain Analysis and Assembly

10. Modify `ChainAnalyzer.AnalyzeChainGroup` to handle multi-terminal chains:
    - Detect `Prepare` site in chain group
    - Count prepared terminals
    - Collapse single-terminal case (elide `Prepare`)
    - Record multi-terminal set on `AnalyzedChain`
11. Add `PreparedTerminals` field to `AnalyzedChain`
12. Modify `PlanAssembler` to compute the union carrier for multi-terminal plans
13. Add `PreparedTerminalKinds` field to `AssembledPlan`

### Phase 4: Emission

14. Emit `.Prepare()` interceptor that constructs `PreparedQuery<TResult>` with frozen carrier fields
15. Emit prepared terminal interceptors that read from `PreparedQuery<TResult>` and dispatch to the appropriate execution path
16. Ensure single-terminal collapsed case emits identical code to existing direct-terminal chains (verify via test comparison)

### Phase 5: Testing

17. Unit tests for `VariableTracer` with `PreparedQuery` type recognition
18. Unit tests for forward terminal scanning in discovery
19. Chain analysis tests: single-terminal collapse, multi-terminal detection
20. SQL output tests: verify collapsed `.Prepare()` + single terminal produces identical SQL to direct terminal
21. SQL output tests: verify multi-terminal `.Prepare()` produces correct SQL and diagnostics
22. Diagnostic tests: `QRY035` (escape detection), `QRY036` (no terminals)
23. Integration tests: `.Prepare()` with `.ToDiagnostics()` + `.ExecuteFetchAllAsync()` on same chain
24. Integration tests: `.Prepare()` across all builder kinds (select, joined select, delete, update, insert, batch insert)
