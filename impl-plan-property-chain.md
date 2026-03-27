# Implementation Plan: Property Chain Captured Variable Extraction

## Problem Statement

When a Where lambda captures a value through a property chain (e.g., `u => u.Email == input.Email` where `input` is a local variable of a class type), the generated interceptor crashes at runtime with `InvalidCastException`. The extraction code assumes the expression tree node at the navigation target is a closure field (`FieldInfo`), but for property chains it's a property accessor (`PropertyInfo`).

### Reproduction

```csharp
var input = new InputModel { Email = "test@example.com" };
db.Users().Where(u => u.Email == input.Email).ToDiagnostics(); // InvalidCastException
```

Failing test: `CrossDialectDiagnosticsTests.ToDiagnostics_WithPropertyChainCapturedParameter_ExtractsValue`

### Current Generated Code (Broken)

For `u => u.Email == input.Email`, the generator emits:

```csharp
var _n0 = Unsafe.As<BinaryExpression>(expr.Body).Right;          // → MemberExpression(.Email)
var _m0 = _n0 is UnaryExpression _u0 ? ... : Unsafe.As<MemberExpression>(_n0);
Chain_0.F0 ??= Unsafe.As<FieldInfo>(_m0.Member);                  // BUG: .Member is PropertyInfo
var p0 = Chain_0.F0.GetValue(Unsafe.As<ConstantExpression>(_m0.Expression!).Value);
```

`_m0.Member` is the `PropertyInfo` for `.Email`, not the `FieldInfo` for the closure field. The `Unsafe.As<FieldInfo>` reinterprets the reference, and `GetValue` returns garbage.

---

## Root Cause Analysis

### Expression Tree Structure

For `u => u.Email == input.Email`, the C# compiler produces:

- `Body` → `BinaryExpression` (Equal)
  - `Left` → `MemberExpression` (u.Email, lambda parameter column access)
  - `Right` → `MemberExpression` (.Email, `PropertyInfo`)
    - `Expression` → `MemberExpression` (closure field holding `input`, `FieldInfo`)
      - `Expression` → `ConstantExpression` (closure instance)

For a simple capture like `u => u.Email == name` (where `name` is a `string` local):

- `Right` → `MemberExpression` (closure field holding `name`, `FieldInfo`)
  - `Expression` → `ConstantExpression` (closure instance)

The single-hop case works because `Body.Right` is directly the `FieldInfo` member. The property chain case fails because `Body.Right` is a `PropertyInfo` member with the `FieldInfo` one level deeper.

### Where the Bug Lives

**File:** `src/Quarry.Generator/Generation/InterceptorCodeGenerator.Utilities.cs`
**Method:** `GenerateCachedExtraction` (line 430)
**Line 436:** `Unsafe.As<FieldInfo>({memberVar}.Member)` — hardcoded assumption that `Member` is always `FieldInfo`.

### Upstream: How the Expression Path is Computed

**File:** `src/Quarry.Generator/IR/SqlExprParser.cs`
**Method:** `ParseMemberAccess` (line 122)

When the parser encounters `input.Email`:
1. `memberAccess.Expression` is `IdentifierNameSyntax("input")` — not a lambda parameter
2. Line 137: Creates `CapturedValueExpr(variableName: "input", syntaxText: "input.Email", expressionPath: "Body.Right")`

The `ExpressionPath` is `"Body.Right"` which navigates to the top-level `.Email` `MemberExpression`. The parser does NOT distinguish between `name` (simple capture, one hop to `FieldInfo`) and `input.Email` (property chain, two hops).

For deeper chains like `input.Address.City`:
- Line 157-159: Recursively parses the inner `CapturedValueExpr` and wraps it, preserving the same `ExpressionPath`
- The `SyntaxText` becomes `"input.Address.City"` but `ExpressionPath` is still `"Body.Right"`

### Carrier Static Field Type

**File:** `src/Quarry.Generator/CodeGen/CarrierAnalyzer.cs` (line 137)

Static fields for FieldInfo caching are declared as `FieldInfo?`. This must change to `MemberInfo?` to accommodate both `FieldInfo` and `PropertyInfo`.

---

## Affected Components

| Component | File | Role |
|-----------|------|------|
| Expression path tracking | `IR/SqlExprParser.cs` | Sets `ExpressionPath` on `CapturedValueExpr` — currently identical for simple and chain captures |
| Parameter creation | `IR/SqlExprClauseTranslator.cs` | Copies `ExpressionPath` from `CapturedValueExpr` to `ParameterInfo` |
| Cached extraction emission | `Generation/InterceptorCodeGenerator.Utilities.cs` | Emits the runtime extraction code — assumes `FieldInfo`, the crash point |
| Carrier static fields | `CodeGen/CarrierAnalyzer.cs` | Declares static `FieldInfo?` fields — must widen to `MemberInfo?` |
| Carrier param bindings | `CodeGen/CarrierEmitter.cs` | Calls `GenerateCachedExtraction` for both non-joined and joined paths |

---

## Design: Runtime Extraction Algorithm

### Current Algorithm (Single-Hop)

Given a `MemberExpression` node at `Body.Right`:
1. Get `node.Member` → assumed `FieldInfo` (the closure field)
2. Get `node.Expression` → assumed `ConstantExpression` (the closure instance)
3. Call `fieldInfo.GetValue(closureInstance)` → the captured value

### Required Algorithm (Multi-Hop)

Given a `MemberExpression` node at `Body.Right`, the member chain may be N levels deep. The algorithm must walk from the outermost `MemberExpression` inward until it finds a `ConstantExpression`, then evaluate outward.

**Walk inward (find the root):**
1. Start at `Body.Right` (a `MemberExpression`)
2. If `node.Expression` is a `ConstantExpression` → this is a simple capture (current case)
3. If `node.Expression` is another `MemberExpression` → there's a property chain; continue walking

**Evaluate outward (extract the value):**
1. Start at the innermost `MemberExpression` whose `Expression` is a `ConstantExpression`
2. Extract the root object: `((ConstantExpression)node.Expression).Value`
3. Get the member value: if `Member` is `FieldInfo`, use `FieldInfo.GetValue(obj)`; if `PropertyInfo`, use `PropertyInfo.GetValue(obj)`
4. Walk outward through each `MemberExpression`, calling `GetValue` on each member with the previous result
5. The final result is the captured value

### Generated Code Pattern

For `input.Email` (2-hop chain):

```csharp
// Navigate to the outermost MemberExpression
var _n0 = Unsafe.As<BinaryExpression>(expr.Body).Right;
var _m0 = _n0 is UnaryExpression _u0 ? Unsafe.As<MemberExpression>(_u0.Operand) : Unsafe.As<MemberExpression>(_n0);

// Walk the chain and extract the value
var p0 = Quarry.Internal.ExpressionHelper.ExtractMemberChainValue(_m0);
```

The runtime helper method `ExtractMemberChainValue` handles the walk:

```csharp
internal static object? ExtractMemberChainValue(MemberExpression memberExpr)
```

### Why a Runtime Helper

1. **The chain depth is unknown at compile time.** The expression path `"Body.Right"` navigates to the outermost `MemberExpression`, but the depth of the member chain beneath it varies (1 hop for `name`, 2 for `input.Email`, 3 for `input.Address.City`).

2. **FieldInfo caching becomes complex for chains.** Each level in the chain has its own `MemberInfo`. Caching all of them in carrier statics would require N static fields per parameter and compile-time knowledge of the chain depth.

3. **Performance is acceptable.** The chain walk uses `MemberInfo.MemberType` check + `GetValue` per hop. For typical 1-3 hop chains, this is negligible compared to the DB round-trip. The first hop (closure field) could still be cached if profiling shows it matters.

---

## Implementation Phases

### Phase 1: Add `ExpressionHelper.ExtractMemberChainValue` Runtime Method

**File:** `src/Quarry/Internal/ExpressionHelper.cs` (new or existing)

Check if `ExpressionHelper` already exists:

```csharp
internal static class ExpressionHelper
```

Add a new static method:

```csharp
internal static object? ExtractMemberChainValue(MemberExpression memberExpr)
```

**Algorithm:**
1. Walk inward from `memberExpr` collecting `MemberExpression` nodes into a stack/list
2. The innermost `MemberExpression` has `Expression` as `ConstantExpression` — extract the root object
3. Walk outward through the stack, calling `GetMemberValue(member, obj)` at each level
4. `GetMemberValue` checks `member.MemberType`: `MemberTypes.Field` → `((FieldInfo)member).GetValue(obj)`; `MemberTypes.Property` → `((PropertyInfo)member).GetValue(obj)`
5. Return the final value

This method handles ALL depths: 1-hop (simple capture), 2-hop (property chain), N-hop (nested property chain). The existing single-hop case becomes a degenerate case of the chain walk.

### Phase 2: Modify `GenerateCachedExtraction` Emission

**File:** `src/Quarry.Generator/Generation/InterceptorCodeGenerator.Utilities.cs`
**Method:** `GenerateCachedExtraction` (line 430)

Replace the current FieldInfo-specific extraction:

```csharp
// BEFORE (broken for property chains):
sb.AppendLine($"        {field.FieldName} ??= Unsafe.As<FieldInfo>({memberVar}.Member);");
sb.AppendLine($"        var p{field.ParameterIndex} = {field.FieldName}.GetValue(Unsafe.As<ConstantExpression>({memberVar}.Expression!).Value);");
```

With a call to the runtime helper:

```csharp
// AFTER (works for all chain depths):
sb.AppendLine($"        var p{field.ParameterIndex} = Quarry.Internal.ExpressionHelper.ExtractMemberChainValue({memberVar});");
```

This change:
- Removes the `FieldInfo` assumption entirely
- Delegates to the runtime helper which handles both `FieldInfo` and `PropertyInfo`
- Works for all chain depths without compile-time knowledge

### Phase 3: Remove Carrier Static `FieldInfo` Fields for Captured Parameters

**File:** `src/Quarry.Generator/CodeGen/CarrierAnalyzer.cs` (line 136-137)

The `F{N}` static fields of type `FieldInfo?` were used to cache the `FieldInfo` for repeated extraction. With the runtime helper approach, these fields are no longer used by the new extraction code.

**Decision point:** Remove `NeedsFieldInfoCache` fields entirely, or keep them for backward compatibility with any remaining code paths that use them.

**Recommended approach:** Check all references to `NeedsFieldInfoCache` and the `F{N}` fields. If `GenerateCachedExtraction` was the only consumer, remove them. If other code paths use them, keep them but stop emitting them for the new path.

**Files to check for `NeedsFieldInfoCache` / `F{N}` references:**
- `CarrierAnalyzer.cs` — declares fields
- `CarrierEmitter.cs` — may reference `F{N}` in non-carrier path
- `InterceptorCodeGenerator.cs` — the non-carrier extraction path

### Phase 4: Verify Non-Carrier Path Compatibility

**File:** `src/Quarry.Generator/CodeGen/CarrierEmitter.cs` (lines 399-410)

The non-carrier extraction path (`EmitCarrierClauseBody`) also calls `GenerateCachedExtraction`. After Phase 2, this path will automatically use the new helper. Verify that:

1. The `expr` parameter name is available in the interceptor method signature (it should be — Where interceptors take `Expression<Func<T, bool>> expr`)
2. The `MemberExpression` variable from `GenerateInlineNavigation` is correctly passed

### Phase 5: Update Test and Add Additional Test Cases

**File:** `src/Quarry.Tests/SqlOutput/CrossDialectDiagnosticsTests.cs`

The existing failing test should now pass:
- `ToDiagnostics_WithPropertyChainCapturedParameter_ExtractsValue` — `input.Email` single property chain

Add additional tests:
- **Nested property chain:** `input.Address.City` (3-hop) — requires a helper class with nested object property
- **Mixed captures:** `u => u.Email == input.Email && u.UserId == id` — one property chain, one simple capture in the same Where
- **Simple capture still works:** Verify the existing `ToDiagnostics_WithCapturedParameter_ParametersContainValue` test (uses `name` simple capture) continues to pass — regression check

### Phase 6: Integration Verification

1. Run all 1864+ tests
2. Build sample webapp — verify `Login.cshtml.cs` with `Input.Email` compiles and works
3. Build sample webapp — verify `Register.cshtml.cs` with `Input.Email` compiles and works

---

## Edge Cases

### Enum Property Chains

For `input.Role` where `Role` is an enum, the C# compiler wraps the `MemberExpression` in a `UnaryExpression(Convert)`. The existing `GenerateInlineNavigation` already handles this with the `is UnaryExpression` check (line 422). The runtime helper receives the unwrapped `MemberExpression`, so enum chains work without special handling.

### Nullable Property Chains

For `input.OptionalDate` where `OptionalDate` is `DateTime?`, the member chain is the same as non-nullable. `PropertyInfo.GetValue` returns the boxed `DateTime?` value. No special handling needed.

### `this` Property Chains (PageModel/Controller)

For Razor Page expressions like `u => u.Email == Input.Email` where `Input` is a property on `this` (the PageModel), the expression tree has an additional level:

- `Body.Right` → `MemberExpression` (.Email, `PropertyInfo`)
  - `Expression` → `MemberExpression` (.Input, `PropertyInfo`)
    - `Expression` → `MemberExpression` (closure field holding `this`, `FieldInfo`)
      - `Expression` → `ConstantExpression` (closure instance)

This is a 3-hop chain. The runtime helper handles it identically to other chains.

### Static Property Access

For `u => u.Status == StatusCodes.Active` where `StatusCodes.Active` is a static property, the expression tree has no closure — it's a static `MemberExpression` with `Expression == null`. The runtime helper must handle this case: when `memberExpr.Expression == null`, use `MemberInfo.GetValue(null)` (static access).

### Existing Simple Captures (Regression Safety)

For `u => u.Email == name` where `name` is a `string` local:
- `Body.Right` → `MemberExpression` (closure field, `FieldInfo`)
  - `Expression` → `ConstantExpression` (closure instance)

The chain walk finds `ConstantExpression` at depth 1 and extracts via `FieldInfo.GetValue`. This is functionally identical to the current code, just using the generic `MemberInfo` dispatch instead of the hardcoded `FieldInfo` cast.

---

## Performance Considerations

The `ExtractMemberChainValue` runtime helper replaces the cached `FieldInfo.GetValue` pattern with an uncached `MemberInfo` chain walk. Impact:

- **Single-hop captures:** One `MemberInfo` type check + `GetValue` call. Previously: one cached `FieldInfo.GetValue` call. Marginal overhead from the type check.
- **Multi-hop chains (new):** N type checks + N `GetValue` calls. This is new functionality that didn't work before, so there's no regression.
- **Hot path concern:** Where clause interceptors run on every query execution. However, `GetValue` via reflection is dwarfed by the DB round-trip. The cached FieldInfo optimization saved ~50ns per call — negligible.

If profiling later shows this matters, the caching can be re-added using `MemberInfo?[]` static fields (one per chain hop) and a separate code path for simple vs chain captures. This is an optimization, not a correctness concern.

---

## Files Modified Summary

| File | Change |
|------|--------|
| `src/Quarry/Internal/ExpressionHelper.cs` | Add `ExtractMemberChainValue` static method |
| `src/Quarry.Generator/Generation/InterceptorCodeGenerator.Utilities.cs` | Change `GenerateCachedExtraction` to emit helper call instead of inline FieldInfo extraction |
| `src/Quarry.Generator/CodeGen/CarrierAnalyzer.cs` | Evaluate whether to keep or remove `FieldInfo?` static field declarations |
| `src/Quarry.Tests/SqlOutput/CrossDialectDiagnosticsTests.cs` | Add nested property chain and mixed capture tests |
