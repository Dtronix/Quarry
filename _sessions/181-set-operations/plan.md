# Implementation Plan: Set Operations (#181)

## Overview

Add UNION, UNION ALL, INTERSECT, INTERSECT ALL, EXCEPT, EXCEPT ALL to the chain API. Both same-entity and cross-entity unions are supported. Post-union re-chaining (WHERE, ORDER BY, LIMIT, etc.) is fully supported, with automatic subquery wrapping when WHERE/HAVING/GroupBy are applied to a set operation result.

## Key Concepts

**SetOperatorKind**: An enum representing the six set operation types: `Union`, `UnionAll`, `Intersect`, `IntersectAll`, `Except`, `ExceptAll`.

**SetOperationPlan**: A new IR class that pairs a `SetOperatorKind` with the right-hand operand's `QueryPlan`. The main QueryPlan holds a list of these, enabling chained set operations (`q1.Union(q2).Except(q3)`).

**Operand Chain Linking**: The right-hand operand of a set operation is a separate query chain (its own ChainId, discovered independently). The set operation call site stores the operand's ChainId so ChainAnalyzer can link them. Operand chains have no terminal — they're "consumed" by the set operation and don't get standalone carriers.

**Subquery Wrapping**: When post-union WHERE/HAVING/GroupBy is used, the SQL assembler wraps the entire set operation in a derived table (`SELECT * FROM (...) AS __set`), then applies the clauses on the outer query. ORDER BY and LIMIT/OFFSET apply directly to the set operation result without subquery wrapping.

**Parameter Remapping**: Parameters are globally indexed across all operand chains. Left chain params get indices 0..N-1, right chain params get N..N+M-1, post-union clause params get N+M...

## Phase 1: Foundation Types & Runtime API

Add the core types and API surface. No pipeline changes yet — set operation methods are defined but not intercepted.

**IR Types** (`src/Quarry.Generator/IR/`):

- Create `SetOperationPlan` class in `QueryPlan.cs`:
  ```csharp
  internal sealed class SetOperationPlan : IEquatable<SetOperationPlan>
  {
      public SetOperatorKind Kind { get; }
      public QueryPlan Operand { get; }
      public int ParameterOffset { get; }  // Global index offset for operand params
  }
  ```

- Create `SetOperatorKind` enum in `QueryPlan.cs`:
  ```csharp
  internal enum SetOperatorKind { Union, UnionAll, Intersect, IntersectAll, Except, ExceptAll }
  ```

- Extend `QueryPlan` constructor and properties with `IReadOnlyList<SetOperationPlan> SetOperations`. Add to `Equals()` and `GetHashCode()`.

**Model Types** (`src/Quarry.Generator/Models/`):

- Add to `InterceptorKind`: `Union`, `UnionAll`, `Intersect`, `IntersectAll`, `Except`, `ExceptAll`
- Add to `ClauseRole` in `OptimizationTier.cs`: `SetOperation`
- Add `RawCallSite.OperandChainId` (string?) — the ChainId of the consumed operand chain

**Runtime API** (`src/Quarry/Query/IQueryBuilder.cs`):

- Add to `IQueryBuilder<T>`: `Union(IQueryBuilder<T>)`, `UnionAll(IQueryBuilder<T>)`, `Intersect(IQueryBuilder<T>)`, `IntersectAll(IQueryBuilder<T>)`, `Except(IQueryBuilder<T>)`, `ExceptAll(IQueryBuilder<T>)` — all return `IQueryBuilder<T>`, all have default throw implementations.

- Add to `IQueryBuilder<TEntity, TResult>`: same six methods with `IQueryBuilder<TEntity, TResult>` parameter + cross-entity overloads `Union<TOther>(IQueryBuilder<TOther, TResult> other) where TOther : class`, etc.

**InterceptorRouter** (`src/Quarry.Generator/CodeGen/InterceptorRouter.cs`):

- Route Union/UnionAll/Intersect/IntersectAll/Except/ExceptAll → new `EmitterCategory.SetOperation` (or reuse `Clause` with a sub-check).

**Tests**: Verify all existing 2779 tests still pass. No new tests yet.

## Phase 2: Discovery & Chain Linking

Teach the source generator to recognize set operation calls and link operand chains.

**UsageSiteDiscovery.cs** (`src/Quarry.Generator/Parsing/`):

- Add method name → InterceptorKind mappings: "Union" → `InterceptorKind.Union`, etc.
- When discovering a set operation call site:
  - Extract the argument expression (the right-hand operand builder)
  - Trace the argument expression back to its chain root to compute the operand's ChainId
  - Store on `RawCallSite.OperandChainId`
- The operand chain is discovered normally (it has its own db.Table().Where(...)... chain), but it has no terminal. Mark it so ChainAnalyzer doesn't skip it.

**RawCallSite.cs** (`src/Quarry.Generator/IR/`):

- Add `OperandChainId` property (string?) to store the linked operand chain reference.
- Add to `Equals()` and `GetHashCode()`.

**CallSiteBinder.cs**: No changes needed — set operation sites bind normally (entity resolution from the main chain's entity).

**CallSiteTranslator.cs**: Set operation sites have no expression to translate (the operand is a separate chain, not a lambda). Return null clause, similar to CrossJoin/Limit/Offset.

**Tests**: Verify discovery recognizes set operations. All existing tests pass.

## Phase 3: Chain Analysis & SQL Assembly

Build composite QueryPlans and render set operation SQL.

**ChainAnalyzer.cs** (`src/Quarry.Generator/Parsing/`):

- Before the main analysis loop, collect all `OperandChainId` values from set operation sites across all chains.
- Analyze operand chains (those without terminals but referenced as operands):
  - Build their QueryPlans as if they were standalone SELECT queries
  - Store in an `operandPlans` dictionary keyed by ChainId
- In `AnalyzeChainGroup()`, when processing set operation clause sites:
  - Look up the operand's QueryPlan from the dictionary
  - Create `SetOperationPlan(kind, operandPlan, paramOffset)`
  - Remap operand parameters to global indices (offset by the left chain's parameter count)
  - Add to the main plan's `SetOperations` list
- For multiple chained set operations, process them in order, accumulating parameter offsets.

**SqlAssembler.cs** (`src/Quarry.Generator/IR/`):

- Extend `RenderSelectSql()`:
  - After rendering the left-hand SELECT, check if `plan.SetOperations` is non-empty
  - For each SetOperationPlan: render the operator keyword, then render the operand's SELECT SQL
  - If post-union WHERE/HAVING/GroupBy exists AND set operations exist: wrap the entire set operation block in `SELECT * FROM (...) AS __set`, then append WHERE/HAVING/GroupBy on the outer query
  - ORDER BY and LIMIT/OFFSET: append directly after the set operation block (no subquery needed)

- Set operator keywords:
  ```
  Union → "UNION", UnionAll → "UNION ALL",
  Intersect → "INTERSECT", IntersectAll → "INTERSECT ALL",
  Except → "EXCEPT", ExceptAll → "EXCEPT ALL"
  ```

- Subquery wrapping logic:
  ```
  IF (plan has SetOperations AND plan has post-union WHERE/HAVING/GroupBy):
    SELECT * FROM (
      SELECT ... FROM left_table [WHERE ...] [GROUP BY ...] [HAVING ...]
      UNION [ALL]
      SELECT ... FROM right_table [WHERE ...]
    ) AS __set
    WHERE __set.col ...
    GROUP BY ...
    HAVING ...
    ORDER BY ...
    LIMIT/OFFSET
  ELSE IF (plan has SetOperations):
    SELECT ... FROM left_table [WHERE ...]
    UNION [ALL]
    SELECT ... FROM right_table [WHERE ...]
    ORDER BY ...
    LIMIT/OFFSET
  ELSE:
    (existing behavior)
  ```

  Important: WHERE terms on the individual operands (before the set operation) are part of those operands' SQL. Post-union WHERE terms are applied after the set operation. The ChainAnalyzer must distinguish between pre-union and post-union clauses based on source location relative to the set operation call site.

**Tests**: Add `CrossDialectSetOperationTests.cs` with:
- Basic UNION across all 4 dialects
- UNION ALL, INTERSECT, EXCEPT
- UNION with ORDER BY and LIMIT
- Multiple chained set operations
- Tests run against SQLite for execution, mock connections for SQL verification

## Phase 4: Code Generation

Generate carrier classes and interceptor methods for set operation chains.

**CarrierAnalyzer.cs** (`src/Quarry.Generator/CodeGen/`):

- Extend carrier plan building to handle set operation chains:
  - Carrier holds parameters from ALL operand chains
  - Parameter fields: P0..PN-1 (left), PN..PN+M-1 (right), etc.

**CarrierEmitter.cs** (`src/Quarry.Generator/CodeGen/`):

- Emit carrier class that implements `IQueryBuilder<T>` (or `IQueryBuilder<TEntity, TResult>`)
- The carrier's SQL static fields include the full set operation SQL
- Set operation interceptor methods: capture the operand carrier reference, copy its parameters into the main carrier's fields, return `this`

**ClauseBodyEmitter.cs** or new **SetOperationBodyEmitter.cs**:

- Emit the interceptor method body for Union/UnionAll/etc.:
  - Cast the argument to the operand's carrier type
  - Copy operand parameters into the main carrier's parameter fields
  - Set a flag or bitmask bit indicating the set operation is active
  - Return `Unsafe.As<CarrierType>(this)` to continue the chain

**TerminalBodyEmitter.cs**: No changes needed if the carrier SQL already includes the set operation — the terminal just executes the prebuilt SQL as usual.

**Tests**:
- CarrierGenerationTests: verify carrier class generation for set operation chains
- End-to-end execution: verify correct results from SQLite with real data
- Verify parameter binding works across operand chains

## Phase 5: Cross-Entity Support & Post-Union WHERE

Full cross-entity union support and subquery wrapping for post-union filtering.

**IQueryBuilder<TEntity, TResult>** cross-entity methods:

- Add generic overloads:
  ```csharp
  IQueryBuilder<TEntity, TResult> Union<TOther>(IQueryBuilder<TOther, TResult> other) where TOther : class
  // ... same for UnionAll, Intersect, IntersectAll, Except, ExceptAll
  ```

**Discovery adjustments**:

- For cross-entity set operations, the operand chain has a different entity type
- Discovery extracts the operand's entity type from the generic argument
- Store on RawCallSite for downstream processing

**Chain Analysis adjustments**:

- When building composite QueryPlans for cross-entity unions:
  - The operand's QueryPlan has a different PrimaryTable
  - Validate projection compatibility (same column count)
  - Post-union WHERE/ORDER BY resolve columns against the LEFT entity type's column mapping

**Post-union WHERE (subquery wrapping)**:

- ChainAnalyzer distinguishes pre-union vs post-union clause sites by their source location relative to the set operation site
- Pre-union WHERE terms go into the left operand's WhereTerms
- Post-union WHERE terms are stored separately (new field or marker)
- SqlAssembler applies subquery wrapping when post-union WHERE exists

**Tests**:
- Cross-entity UNION with compatible Select projections
- Post-union WHERE filtering across all dialects
- Post-union WHERE + ORDER BY + LIMIT combination
- Post-union GroupBy + Having

## Phase 6: Diagnostics & Edge Cases

Compile-time diagnostics for unsupported combinations and edge cases.

**New diagnostics**:

- **QRY041**: `IntersectAllNotSupported` — INTERSECT ALL not supported by SQLite/SQL Server
- **QRY042**: `ExceptAllNotSupported` — EXCEPT ALL not supported by SQLite/SQL Server  
- **QRY043**: `SetOperationProjectionMismatch` — operands have different column counts
- **QRY044**: `PostUnionColumnNotInProjection` — post-union WHERE references a column not in the projection

**Dialect support matrix**:
| Operator | SQLite | PostgreSQL | MySQL 8.0.31+ | SQL Server |
|----------|--------|------------|---------------|------------|
| UNION | Yes | Yes | Yes | Yes |
| UNION ALL | Yes | Yes | Yes | Yes |
| INTERSECT | Yes | Yes | Yes | Yes |
| INTERSECT ALL | No | Yes | No | No |
| EXCEPT | Yes | Yes | Yes | Yes |
| EXCEPT ALL | No | Yes | No | No |

**Tests**: Analyzer tests verifying diagnostics fire for unsupported dialect combinations.

## Dependencies Between Phases

- Phase 1 is independent (foundation)
- Phase 2 depends on Phase 1 (uses new types)
- Phase 3 depends on Phase 2 (needs discovery to feed chain analysis)
- Phase 4 depends on Phase 3 (needs assembled plans for code gen)
- Phase 5 depends on Phase 4 (builds on working same-entity support)
- Phase 6 depends on Phase 5 (diagnostics need the full pipeline working)
