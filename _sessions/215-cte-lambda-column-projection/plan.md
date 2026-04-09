# Plan: 215-cte-lambda-column-projection

## Problem

Lambda-form `With<TEntity, TDto>` inner CTE selects all entity columns instead of only the DTO-projected subset. Root cause: lambda inner chain calls are non-analyzable (receiver is a lambda parameter), so the Select's ProjectionInfo is null, and the projection defaults to identity with all entity columns.

## Key Concepts

**Identity projection enrichment:** When no Select projection is available, ChainAnalyzer creates an identity projection and enriches it with all entity columns via `EnrichIdentityProjectionWithEntityColumns`. This produces explicit column names in the SQL rather than `SELECT *`.

**CTE DTO column metadata (`raw.CteColumns`):** The CTE definition site already resolves the DTO's public properties into `CteColumn` objects (property name, column name, CLR type). This metadata is available at the point where the lambda inner chain is processed but is currently only stored in `CteDef.Columns` for outer-query binding — never used to reduce the inner SELECT.

**Projection reduction:** Filter the enriched identity projection's `ProjectedColumn` list to only those whose `ColumnName` matches a `CteColumn.ColumnName` from the DTO metadata. Create a new `QueryPlan` with this reduced projection.

## Phase 1: Add projection reduction in ChainAnalyzer

**File:** `src/Quarry.Generator/Parsing/ChainAnalyzer.cs` (around line 725-748)

After `AnalyzeChainGroup` returns the lambda inner chain's plan, and before creating the `CteDef`:

1. Check if `lambdaColumns` (from `raw.CteColumns`) is non-empty AND the inner plan's projection is identity.
2. Build a `HashSet<string>` of DTO column names from `lambdaColumns`.
3. Filter `lambdaInnerAnalyzed.Plan.Projection.Columns` to only those whose `ColumnName` is in the set.
4. Create a new `SelectProjection` with the filtered columns (same Kind, ResultTypeName, CustomEntityReaderClass; `isIdentity: false`).
5. Create a new `QueryPlan` copying all fields from the original but with the reduced projection.
6. Use the reduced plan for the `CteDef.InnerPlan`.

The `lambdaInnerSql` (pre-rendered fallback) doesn't need updating because SqlAssembler always uses `cte.InnerPlan` when non-null.

**Tests:** Existing tests will initially fail (expected SQL has all columns). Fix in Phase 2.

## Phase 2: Update tests to expect reduced columns

**Files:**
- `src/Quarry.Tests/SqlOutput/LambdaCteTests.cs` — `LambdaCte_DedicatedDto` test (line ~152)
- `src/Quarry.Tests/SqlOutput/CrossDialectCteTests.cs` — `Cte_FromCte_DedicatedDto` test (line ~375)

For each test:
1. Update expected SQL strings to select only the 3 DTO columns (`OrderId`, `Total`, `Status`) instead of all 7 entity columns.
2. Remove the "known gap" / "projection not reduced" comments.
3. Verify all 4 dialect variants (SQLite, PostgreSQL, MySQL, SQL Server).

**Tests:** All tests should pass after this phase.
