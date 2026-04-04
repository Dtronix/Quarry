# Workflow: nullable-byte-array-reader-fix
## Config
platform: github
remote: https://github.com/DJGosnell/Quarry
base-branch: master
## State
phase: DESIGN
status: active
issue: discussion
pr:
session: 1
phases-total:
phases-complete: 0
## Problem Statement
When the Quarry generator emits a typed entity reader (from `.Select(p => p).ExecuteFetchFirstOrDefaultAsync()`), nullable `byte[]?` columns produce `default()` for the null branch instead of `default(byte[]?)`. The untyped `default()` is invalid in an object initializer expression context where the compiler cannot infer the target type, causing CS1031 compilation error.

**Reproduction:**
- Schema with `Col<byte[]?> Password { get; }` nullable byte array column
- Query: `.Select(p => p).ExecuteFetchFirstOrDefaultAsync()`
- Generated code emits: `Password = r.IsDBNull(3) ? default() : r.GetFieldValue<byte[]>(3),`
- Expected: `Password = r.IsDBNull(3) ? default(byte[]?) : r.GetFieldValue<byte[]>(3),`

**Context:**
- Commit 5219f85 fixed byte[] reader from `r.GetValue(i)` to `r.GetFieldValue<byte[]>(i)` but did not update null-handling default expression.
- String? columns correctly emit `default(string)` with explicit type.

**Baseline Test Status:** All 2606 tests passing (61 Analyzers + 2545 Core).

## Decisions

**2026-04-04: Root cause identified**
- Bug is in `ReaderCodeGenerator.GetReaderCall()` lines 262-268
- For nullable reference types like `byte[]?`, when `IsValueType = false`, the nullable type is not marked with `?`
- Example: emits `default(byte[])` instead of a properly typed default
- User type inference in object initializer context requires an explicitly typed expression

**2026-04-04: Fix approach approved**
- Emit `null` for nullable reference types instead of `default(untyped)`
- Simpler and more idiomatic C# than `default(byte[]?)`
- Apply uniformly to all nullable reference types, not just `byte[]`
- This aligns with the existing pattern at line 231 which already emits sign-cast types with `?`

phase: REVIEW
status: active
phases-total: 1
phases-complete: 1

## Suspend State

## Session Log
| # | Phase Start | Phase End | Summary |
|---|------------|-----------|---------|
| 1 | INTAKE | DESIGN | Created branch, ran baseline tests, confirmed all green |
| 1 | DESIGN | PLAN | Clarified root cause, chose null emission approach, wrote plan |
| 1 | PLAN | IMPLEMENT | Phase 1: Fixed GetReaderCall() for nullable ref types, added 3 tests, all 2548 tests pass |
