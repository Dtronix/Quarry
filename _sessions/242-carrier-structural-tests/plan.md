# Plan: 242-carrier-structural-tests

## Overview

Add four structural unit tests to `CarrierGenerationTests.cs` that assert on the shape of generated carrier code. Each test generates code for a specific query chain configuration and validates that the codegen efficiency improvements (dead code removal, readonly fields, ParameterNames caching, reader field extraction) produce the expected output.

All tests follow the existing pattern: compose a source string with `SharedSchema`, run `CreateCompilation` + `RunGenerator`, extract the interceptors file, and assert `Does.Contain` / `Does.Not.Contain` on the generated code string.

## Phase 1: All four structural tests (single commit)

These tests are small, independent string assertions with no shared setup beyond what already exists. They belong in a single commit.

### Test 1: `CarrierGeneration_ParameterlessClause_OmitsCarrierCast`

Tests the dead code removal optimization from Phase 2 of the carrier-codegen-efficiency work. When a clause body has no parameters to bind and no mask bit to set, the `var __c = Unsafe.As<ClassName>(builder)` line should be omitted from the clause interceptor.

**Chain:** `db.Users().Select(u => u).ExecuteFetchAllAsync()` — Select has no captured params and no conditional mask bit.

**Assertions:**
- `Does.Not.Contain("var __c = Unsafe.As")` — the carrier cast is dead code in parameterless clauses
- `Does.Contain("return Unsafe.As<")` — the return line is always emitted (interface crossing)

**Why this chain:** The existing `CarrierGeneration_SimpleNoParams` test uses the same chain but only asserts on carrier class structure (interfaces, remark), not on clause body shape. This test specifically validates the clause interceptor body optimization.

### Test 2: `CarrierGeneration_CollectionParam_HasReadonlySqlCache`

Tests the readonly `_sqlCache` field from Phase 3. When a chain has collection parameters (e.g., `ids.Contains(u.UserId)`), the carrier class emits a `static readonly CollectionSqlCache?[]` field.

**Chain:** `db.Users().Where(u => ids.Contains(u.UserId)).Select(u => u).ExecuteFetchAllAsync()` with `IReadOnlyList<int> ids` parameter.

**Assertions:**
- `Does.Contain("static readonly Quarry.Internal.CollectionSqlCache?[] _sqlCache")` — confirms both `readonly` modifier and correct type
- `Does.Not.Contain("static Quarry.Internal.CollectionSqlCache?[] _sqlCache")` — would match if `readonly` were accidentally dropped (belt-and-suspenders; the first assertion makes this redundant, but it's cheap)

**Why this chain:** Reuses the same chain shape as the existing `CarrierGeneration_CollectionParam_EmitsNullBangInitializer` test but asserts on a different aspect (field declaration shape vs. initializer).

### Test 3: `CarrierGeneration_BatchInsert_UsesParameterNameCache`

Tests the ParameterNames.AtP optimization from Phase 4. Batch insert terminals should use the pre-allocated parameter name cache instead of string concatenation.

**Chain:** `db.Users().InsertBatch(u => (u.UserName, u.IsActive)).Values(users).ExecuteNonQueryAsync()` with a local `users` array.

**Assertions:**
- `Does.Contain("ParameterNames.AtP(")` — uses the cached lookup method
- `Does.Not.Contain("\"@p\" + __paramIdx")` — no string concatenation fallback

**Why this chain:** Reuses the same chain shape as `CarrierGeneration_BatchInsertTerminal_CachesCtxAndLogger` but asserts on parameter name generation rather than ctx/logger caching.

### Test 4: `CarrierGeneration_SelfContainedReader_EmitsReaderField`

Tests the reader field extraction from Phase 5. When the reader delegate is self-contained (no references to interceptor-class fields like `_entityReader_*` or `_mapper_*`), the carrier class should emit a `static readonly` `_reader` field.

**Chain:** `db.Users().Select(u => u).ExecuteFetchAllAsync()` — simple select produces a self-contained reader delegate.

**Assertions:**
- `Does.Contain("internal static readonly System.Func<System.Data.Common.DbDataReader,")` — the _reader field declaration with correct type prefix
- `Does.Contain("> _reader =")` — the field name and assignment
- `Does.Contain("Chain_0._reader")` — the terminal references the carrier's static field rather than inlining the lambda

**Why this chain:** The simple `Select(u => u)` guarantees a self-contained reader (no custom type mappings, no custom entity reader class). The terminal should reference `Chain_0._reader` instead of duplicating the lambda.

### Test placement

All four tests will be added at the end of the existing test class, before the closing `}`, following the existing naming convention `CarrierGeneration_*`.
