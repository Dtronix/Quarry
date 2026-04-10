# Plan: carrier-codegen-efficiency

## Overview

Five implementation phases, each independently committable. All changes are to the source generator (`Quarry.Generator`), except the batch insert fix which also relies on the existing `ParameterNames` runtime class. Two tracking issues will be created at the end for deferred items.

---

## Phase 1: Fix SQL Server boolean negation bug

**File:** `src/Quarry.Generator/IR/SqlExprBinder.cs`

**Problem:** `!u.IsActive` renders as `NOT ([IsActive])` which is invalid for SQL Server `bit` columns. The binder passes `inBooleanContext = false` when recursing into the `UnaryOpExpr` operand (line 141), so the boolean column doesn't get the `col = 1` wrapping treatment.

**Fix:** In the `UnaryOpExpr` case (lines 139-145), detect `Not` on a boolean column and emit `col = 0`/`col = FALSE` instead:

```csharp
case UnaryOpExpr unary:
{
    // NOT on a boolean column → emit "col = 0" / "col = FALSE" directly
    if (unary.Operator == SqlUnaryOperator.Not)
    {
        var operand = BindExpr(unary.Operand, ctx, true); // bind with boolean context
        if (operand is SqlRawExpr rawBool)
        {
            // The bool-context binding produced "col = 1"/"col = TRUE" — negate it
            var negated = NegateBoolean(rawBool.SqlText, ctx.Dialect);
            if (negated != null)
                return new SqlRawExpr(negated);
        }
        // Non-boolean operand: fall through to standard NOT(...) wrapping
        if (ReferenceEquals(operand, unary.Operand))
            return unary;
        return new UnaryOpExpr(unary.Operator, operand);
    }
    
    var op = BindExpr(unary.Operand, ctx, false);
    if (ReferenceEquals(op, unary.Operand))
        return unary;
    return new UnaryOpExpr(unary.Operator, op);
}
```

Add helper method:
```csharp
private static string? NegateBoolean(string boolExpr, SqlDialect dialect)
{
    var trueLit = FormatBoolean(true, dialect);
    var falseLit = FormatBoolean(false, dialect);
    // "col = TRUE" → "col = FALSE", "[col] = 1" → "[col] = 0"
    if (boolExpr.EndsWith($" = {trueLit}"))
        return boolExpr[..^trueLit.Length] + falseLit;
    return null;
}
```

**Tests:** Update all expected SQL in tests that assert `NOT (...)` for boolean columns. The output should change to `col = 0`/`col = FALSE` across all dialects (not just SQL Server — this is a normalization improvement for all).

---

## Phase 2: Remove dead `var __c` cast in parameterless clause interceptors

**File:** `src/Quarry.Generator/CodeGen/CarrierEmitter.cs`

**Problem:** Line 268 unconditionally emits `var __c = Unsafe.As<Chain_N>(builder);` even when `__c` is never used (no params to bind, no mask bit to set).

**Fix:** Wrap the emission in a condition:

```csharp
bool needsCarrierRef = (clauseParams != null && clauseParams.Count > 0) || clauseBit.HasValue;
if (needsCarrierRef)
    sb.AppendLine($"        var __c = Unsafe.As<{carrier.ClassName}>(builder);");
```

The rest of the method body only accesses `__c` when `clauseParams.Count > 0` (line 278) or `clauseBit.HasValue` (line 327), so this is safe.

**Tests:** Regenerate and verify all tests still pass. No semantic change — purely dead code elimination.

---

## Phase 3: Make `_sqlCache` field readonly

**File:** `src/Quarry.Generator/CodeGen/CarrierEmitter.cs`

**Problem:** Line 1191 emits `internal static Quarry.Internal.CollectionSqlCache?[] _sqlCache = ...` without `readonly`. The array reference never changes (only elements do), so `readonly` is valid and helps JIT hoist the base address.

**Fix:** Change line 1191 from:
```csharp
sb.AppendLine($"    internal static Quarry.Internal.CollectionSqlCache?[] _sqlCache = ...");
```
to:
```csharp
sb.AppendLine($"    internal static readonly Quarry.Internal.CollectionSqlCache?[] _sqlCache = ...");
```

**Tests:** All existing tests pass. No behavioral change.

---

## Phase 4: Integrate ParameterNames caching for batch insert terminals

**File:** `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs`

**Problem:** Line 576 emits `__p.ParameterName = "@p" + __paramIdx;` which allocates a string per parameter per row at runtime. The existing `ParameterNames.AtP(int)` method returns cached strings for indices 0-255.

**Fix:** Replace line 576 with dialect-aware cached lookup:

```csharp
// Note: DbParameter.ParameterName always uses @p format (even for PG/MySQL)
// because ADO.NET uses named parameters internally regardless of SQL placeholder format.
sb.AppendLine($"                __p.ParameterName = Quarry.Internal.ParameterNames.AtP(__paramIdx);");
```

Key insight: `DbParameter.ParameterName` always uses `@p{N}` format regardless of dialect (confirmed from `SqlFormatting.GetParameterName` which always returns `@p{N}`). The SQL placeholder format ($1, ?) is separate from the ADO.NET parameter name.

**Tests:** All existing tests pass. Runtime behavior identical but eliminates up to 256 string allocations per batch insert (beyond 256 falls back to concatenation gracefully).

---

## Phase 5: Extract reader delegate to static carrier field

**Files:** `src/Quarry.Generator/CodeGen/CarrierEmitter.cs`, `src/Quarry.Generator/CodeGen/TerminalBodyEmitter.cs`

**Problem:** The reader lambda (`static (DbDataReader r) => (...)`) is emitted inline at each terminal call site. When a chain has multiple terminals (e.g., prepared + non-prepared, or FetchAll + FetchFirst on same chain), identical lambdas are duplicated. Moving to a carrier field eliminates duplicates and removes the per-callsite lazy-init check.

**Approach:**
1. In `EmitCarrierClass` (CarrierEmitter.cs line 337-387): After emitting instance fields and before UnsafeAccessor methods, if `chain.ReaderDelegateCode != null`, emit a static reader field. We need the result type — pass it as a parameter or derive from chain.

2. The reader field declaration:
   ```csharp
   internal static readonly System.Func<System.Data.Common.DbDataReader, {ResultType}> _reader = {ReaderDelegateCode};
   ```

3. In `EmitCarrierExecutionTerminal` (line 747): Instead of passing the inline reader expression, pass `{carrier.ClassName}._reader`.

4. Update `EmitBatchInsertCarrierTerminal` similarly if it uses readers (it doesn't — batch insert uses NonQuery/Scalar, so no reader needed).

**Dependency:** Need access to `chain.ResultTypeName` or the full return type at carrier emission time. This is already available on `AssembledPlan`.

**Tests:** All existing tests pass. Generated code is structurally different (field reference vs inline lambda) but semantically identical.

---

## Tracking Issues (after implementation)

1. **Carrier deduplication** — merge structurally identical carriers with union interfaces in FileEmitter.cs
2. **Incremental SQL mask rendering** — split SqlAssembler.RenderSelectSql into shared prefix + per-mask suffix

---

## Test Strategy

After each phase, run the full test suite (`dotnet test src/Quarry.Tests/`). Since these changes modify the source generator, the tests exercise the generated output through SQL assertion and integration tests. Some phases (1) will require updating expected SQL strings in test assertions.
