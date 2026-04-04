# Implementation Plan: nullable-byte-array-reader-fix

## Problem Summary
When the Quarry generator emits typed entity readers, nullable reference-type columns (e.g., `Col<byte[]?>`) produce bare `default()` without a type in the null branch, causing CS1031 compilation error in object initializer context.

**Root cause:** `ReaderCodeGenerator.GetReaderCall()` line 266 constructs nullable type by: `IsValueType ? $"{ClrType}?" : ClrType`. For reference types, this drops the `?` suffix entirely, resulting in untyped `default()` emission downstream.

## Solution
Emit `null` (instead of `default(untyped)`) for nullable reference types in the ternary expression. This is idiomatic C# and lets the compiler infer the type from context.

**Before:**
```csharp
Password = r.IsDBNull(3) ? default() : r.GetFieldValue<byte[]>(3),
```

**After:**
```csharp
Password = r.IsDBNull(3) ? null : r.GetFieldValue<byte[]>(3),
```

## Implementation Phases

### Phase 1: Fix ReaderCodeGenerator.GetReaderCall()
**File:** `src/Quarry.Generator/Projection/ReaderCodeGenerator.cs`

**Changes:**
1. Modify lines 262-268 (nullable reference type handling) to emit `null` instead of `default(nullableType)` when `IsValueType == false`
2. Keep nullable value-type path unchanged (continues to emit `default(DateTime?)` etc.)

**Code change:**
- Line 262-268: Change from ternary with `default()` to explicit `null` for reference types
- Pattern: Check `if (column.IsNullable && !column.IsValueType)` → emit `null`
- Value types continue with `default(type?)`

**Affected paths:**
- Entity readers (SelectProjection → GenerateEntityReader)
- DTO readers (SelectProjection → GenerateDtoReader) 
- Tuple readers (SelectProjection → GenerateTupleReader)
- Single-column readers (SelectProjection → GenerateSingleColumnReader)

**Tests to add/verify:**
- New test: `GenerateReaderDelegate_WithNullableByteArray_EmitsNullNotDefault()`
- Verify output contains `? null :` instead of `? default()`
- Add test for other nullable reference types (string?, custom class?)
- Ensure value-type behavior unchanged (DateTime? still uses `default(DateTime?)`)

### Phase 2: Run full test suite
**Verify:**
- All 2606 existing tests still pass
- No regression in other projection types (Entity, DTO, Tuple, Scalar)
- Cross-dialect SQL tests pass
- Integration tests pass

**Expected:**
- Tests that were checking for specific `default()` patterns may need assertion updates
- No changes to SQL output or logic, only reader delegate format

## Edge Cases

1. **Nested generics:** `List<byte[]>?` — should emit `null` (reference type)
2. **Qualified types:** `System.Byte[]?` — should emit `null`
3. **Value types:** `int?`, `DateTime?` — unchanged, still use `default(T?)`
4. **Custom types:** Unknown reference types → emit `null`
5. **Foreign keys:** Already have special handling at line 208, unchanged
6. **Enums:** Already have special handling at line 219, unchanged
7. **Custom type mappings:** Already have special handling at line 244, unchanged

## Risk Assessment

**Low risk:**
- Change is localized to one conditional branch in GetReaderCall()
- Emitting `null` for reference types is standard C# pattern
- All database readers (GetFieldValue, GetValue, etc.) return null for IsDBNull case anyway
- Compiler will correctly infer type from context

**Testing:**
- Cross-dialect tests cover all major projection types
- Entity reader tests exist and will validate new behavior
- No changes to SQL emission or query execution logic

## Rollback Plan
If issues arise:
1. Revert line 267 to emit `default({nullableType})` where `nullableType = column.ClrType + "?"` for reference types
2. Add explicit handling to ensure all reference type nullables have the `?` suffix
