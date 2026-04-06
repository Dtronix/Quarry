# Review: #181

## Plan Compliance

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| QRY043 (SetOperationProjectionMismatch) and QRY044 (PostUnionColumnNotInProjection) from plan Phase 6 are not implemented. | Medium | Missing compile-time safety net -- users get no diagnostic when operand projections differ in column count or when post-union WHERE references columns not in the projection. |
| New diagnostic `IntersectAllNotSupported` reuses ID `"QRY041"` (`DiagnosticDescriptors.cs:717`), which already belongs to `RawSqlUnresolvableColumn` (`DiagnosticDescriptors.cs:546`). Two `DiagnosticDescriptor` fields with the same ID will cause ambiguous diagnostic reporting. `ExceptAllNotSupported` uses `"QRY042"` which is free, but it should also be renumbered to a fresh pair. | High | The Roslyn analyzer infrastructure identifies diagnostics by ID string. Two descriptors sharing `"QRY041"` means suppression, filtering, and IDE display will conflate a warning about unaliased RawSql columns with an error about unsupported INTERSECT ALL. |
| Cross-entity `Union<TOther>()` overloads defined in `IQueryBuilder<TEntity, TResult>` (`IQueryBuilder.cs:282-363`) are not wired in discovery or code generation. workflow.md Session 2 notes this is deferred. | Low | API surface advertises capability that will throw `InvalidOperationException` at runtime. Acceptable if documented. |
| `handoff.md` is stale -- still says "7 tests currently failing" and lists `AnalyzeOperandChain` as unused. Both are resolved. | Low | Misleading for anyone resuming work from the handoff document. |

## Correctness

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| **Operand parameter placeholders use wrong indices.** `SqlAssembler.cs:281` calls `RenderSelectSql(setOp.Operand, 0, dialect)` which internally starts `paramIndex` at 0. The operand's parameters render as `@p0`, `@p1`, etc. instead of `@p{offset}`, `@p{offset+1}`, etc. Manifest proof at `quarry-manifest.sqlite.md:3083`: `WHERE "UserId" >= @p0 UNION ... WHERE "UserId" <= @p0` -- both use `@p0` despite being different parameters (`@p0` = `int`, `@p1` = `int` listed on lines 3088-3089). | Critical | For PostgreSQL (`$1`, `$2`), both the left and right operand parameters would bind to `$1`, producing wrong results or errors. For SQLite/SQL Server (`@p0`), the runtime carrier may still bind correctly by field position, but the SQL text is semantically incorrect and will break with any driver that resolves by name. The test passes for SQLite only because the test harness binds parameters positionally. |
| `QueryPlan.Equals()` (`QueryPlan.cs:95-121`) includes `PostUnionWhereTerms` but omits `PostUnionGroupByExprs` and `PostUnionHavingExprs`. | Medium | Two plans differing only in post-union GROUP BY or HAVING would compare as equal, causing incorrect SQL caching or plan deduplication by the source generator's consolidation pass. |
| `GetSetOperatorKeyword` (`SqlAssembler.cs:636-648`) returns `"UNION"` for the default case (`_ => "UNION"`) instead of throwing. `MapToSetOperatorKind` in `ChainAnalyzer.cs` correctly throws for unknown values, creating an inconsistency. | Low | Silent fallback to UNION for an unexpected enum value would mask bugs rather than failing fast. |
| Post-union GROUP BY rendering (`SqlAssembler.cs:320-324`) does not advance `paramIndex` after each expression. If a GROUP BY expression contained a parameterized sub-expression, subsequent expressions and the HAVING clause would receive incorrect param offsets. | Low | GROUP BY typically references column names, not parameters, so impact is unlikely in practice. But it breaks the invariant established by every other clause rendering loop in the assembler. |

## Security

No concerns. Set operations use the same parameterized query infrastructure as existing clauses. No user input is interpolated into SQL strings.

## Test Quality

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| No tests verify the SQL text of parameterized set operations. `Union_WithCapturedVariable_Parameters` (`CrossDialectSetOperationTests.cs:289`) only checks execution results, not the generated SQL. The manifest reveals the `@p0`/`@p0` collision in the rendered SQL (see Correctness). | High | The critical parameter-index bug is hidden because the test only asserts row counts, not SQL correctness. A SQL assertion would have caught the duplicate `@p0`. |
| No tests for QRY041/QRY042 diagnostics (INTERSECT ALL / EXCEPT ALL on non-PostgreSQL). | Medium | Diagnostic behavior is unverified. Also, given the QRY041 ID collision, tests would reveal the conflict. |
| No tests for post-union GroupBy or Having SQL output. | Medium | Subquery wrapping for these clauses was added in the remediation but has no dedicated test. |
| No negative tests for cross-entity set operations (verifying graceful failure). | Low | A user calling `Union<TOther>()` would get an opaque `InvalidOperationException` with no diagnostic guidance. |
| No test validates parameter binding correctness across dialects for parameterized set operations (the test only runs against SQLite). | Medium | PostgreSQL's `$N`-indexed parameter placeholders would expose the index collision, but no PostgreSQL dialect SQL assertion exists for the parameterized case. |

## Codebase Consistency

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `AnalyzeOperandChain` (~215 lines in `ChainAnalyzer.cs:2100-2314`) duplicates significant logic from `AnalyzeChainGroup` (parameter remapping, projection building, entity column enrichment). | Medium | Changes to how projections are built or parameters are remapped in `AnalyzeChainGroup` would need to be mirrored. Consider extracting shared helpers. |
| `SetOperationBodyEmitter`, `InterceptorRouter`, and `EmitterCategory.SetOperation` follow existing patterns consistently. | -- | Good. |
| `QueryPlanReferenceComparer` in `FileEmitter.cs` uses `RuntimeHelpers.GetHashCode` for reference equality, which is correct and matches how the codebase handles identity-based lookups. | -- | Good. |

## Integration / Breaking Changes

| Finding | Severity | Why It Matters |
|---------|----------|----------------|
| `IQueryBuilder<T>` and `IQueryBuilder<TEntity, TResult>` gain 6 and 12 new default interface methods respectively. These are additive with default-throw implementations, matching the existing pattern (e.g., `LeftJoin`). | Low | Non-breaking. Any user implementing these interfaces who upgrades will see new methods with safe defaults. |
| `QueryPlan`, `RawCallSite`, and `AssembledPlan` constructors gain new optional parameters. All existing call sites pass them as null/default. | -- | Non-breaking. |
| `ClauseRole.SetOperation` added to enum. No switch statements on `ClauseRole` appear to be missing the new case. | -- | Non-breaking. |
| Manifest output counts increase by +16 discovered per dialect, consistent across all 4 dialect manifests. | -- | Pipeline handles new chains without disrupting existing chains. |
