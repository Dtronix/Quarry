# Plan: 213-lambda-with-overload

## Overview

Replace direct-argument `With<T>(IQueryBuilder<T>)` and set-operation (`Union(IQueryBuilder<T>)` etc.) APIs with lambda-form equivalents. Refactor the source generator discovery and analysis pipeline from flat SpanStart-based matching to a tree-based approach for inner chains. Eliminate inner chain carriers and interceptors entirely — the inner chain is purely compile-time.

### Key Concepts

**Direct capture model (no inner interceptors):** The inner chain is analyzed entirely at compile time. The source generator extracts the SQL and identifies captured parameters from the lambda body. At runtime, the `With()`/`Union()` interceptor accesses captured variables through the lambda delegate's display class (`innerBuilder.Target`) via the existing `UnsafeAccessor` pattern, and stores them at `ParameterOffset` in the outer carrier. No inner carrier class, no inner chain interceptors, no lambda invocation at runtime.

**Display class reuse:** The `DisplayClassEnricher` already resolves captured variables for any `Func<>` delegate parameter. For the lambda form, the enricher analyzes the outer lambda (`orders => orders.Where(o => o.Total > cutoff)`) and finds all captured variables (e.g., `cutoff`). The `With()` emitter generates `UnsafeAccessor` methods on the outer carrier to extract these values from `innerBuilder.Target`.

**Synthesized ChainRoot:** Lambda-form inner chains have no entity accessor method call (e.g., `db.Orders()`) to serve as the chain root. During analysis, `ChainAnalyzer` injects a synthetic `ChainRoot` for lambda inner chain groups that lack one, using the CTE/set-op site's entity type information.

**ChainId for lambda inner chains:** `ComputeChainId` is extended to use the lambda's `SpanStart` as the scope key when the chain root expression is a lambda parameter (`IParameterSymbol`), preventing collisions between multiple lambda bodies in the same statement.

**Parent-child linking:** Inner chain invocations detect their parent via ancestor walking (extending `DetectCteInnerChain` to walk through `LambdaExpressionSyntax`). A stable key (the lambda's `SpanStart`) links inner chain groups to their parent CTE/set-op site during analysis.

---

## Phase 1: Add lambda API overloads (additive)

Add new lambda-form overloads alongside existing direct-argument overloads. No generator changes — new overloads exist but aren't intercepted yet. All existing tests remain green.

### 1a. Runtime API — CTE methods

**`src/Quarry/Context/QuarryContext.cs`:**
- Add `virtual QuarryContext With<TDto>(Func<IEntityAccessor<TDto>, IQueryBuilder<TDto>> innerBuilder) where TDto : class` alongside existing `With<TDto>(IQueryBuilder<TDto>)`
- Add `virtual QuarryContext With<TEntity, TDto>(Func<IEntityAccessor<TEntity>, IQueryBuilder<TEntity, TDto>> innerBuilder) where TEntity : class where TDto : class` alongside existing two-arg overload
- Both throw `NotSupportedException` with same guidance message as existing overloads

**`QuarryContext<TSelf>` overrides (same file, ~lines 636-656):**
- Add `override TSelf With<TDto>(Func<IEntityAccessor<TDto>, IQueryBuilder<TDto>> innerBuilder)`
- Add `override TSelf With<TEntity, TDto>(Func<IEntityAccessor<TEntity>, IQueryBuilder<TEntity, TDto>> innerBuilder)`

### 1b. Runtime API — Set operation methods

**`src/Quarry/Query/IQueryBuilder.cs`:**

On `IQueryBuilder<T>` — add 6 lambda set-op methods alongside existing:
```csharp
IQueryBuilder<T> Union(Func<IEntityAccessor<T>, IQueryBuilder<T>> other)
IQueryBuilder<T> UnionAll(Func<IEntityAccessor<T>, IQueryBuilder<T>> other)
IQueryBuilder<T> Intersect(Func<IEntityAccessor<T>, IQueryBuilder<T>> other)
IQueryBuilder<T> IntersectAll(Func<IEntityAccessor<T>, IQueryBuilder<T>> other)
IQueryBuilder<T> Except(Func<IEntityAccessor<T>, IQueryBuilder<T>> other)
IQueryBuilder<T> ExceptAll(Func<IEntityAccessor<T>, IQueryBuilder<T>> other)
```

On `IQueryBuilder<TEntity, TResult>` — add 12 lambda set-op methods:
- 6 same-entity: `Union(Func<IEntityAccessor<TEntity>, IQueryBuilder<TEntity, TResult>> other)` etc.
- 6 cross-entity: `Union<TOther>(Func<IEntityAccessor<TOther>, IQueryBuilder<TOther, TResult>> other) where TOther : class` etc.

All default-implemented to throw `InvalidOperationException` (same pattern as existing set-op defaults).

### 1c. Generated code

**`src/Quarry.Generator/Generation/ContextCodeGenerator.cs` (`GenerateCteMethods`):**
- Add emission of lambda-form `With<TDto>` and `With<TEntity, TDto>` overloads with `new` keyword, alongside existing overloads
- Lambda overloads return `{context.ClassName}` for chaining

### 1d. Verify

- Build succeeds with new overloads
- All existing tests pass unchanged

---

## Phase 2: Discovery — detect lambda-form inner chains

Extend the discovery pipeline to recognize lambda arguments to `With()` and set-op methods, and to detect invocations inside those lambda bodies as inner chain sites. Existing non-lambda detection remains intact. All existing tests remain green (new code paths not yet exercised by ChainAnalyzer).

### 2a. Extend inner chain detection

**`src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs`:**

Extend `DetectCteInnerChain` (lines 3736-3766) → `DetectInnerChain`:
- Walk ancestors as today, but ALSO detect `LambdaExpressionSyntax` → `ArgumentSyntax` → `ArgumentListSyntax` → `InvocationExpressionSyntax` with method name in `{"With", "Union", "UnionAll", "Intersect", "IntersectAll", "Except", "ExceptAll"}`.
- Semantically verify the parent is a Quarry method (existing `IsQuarryContextType` for With; builder type check for set ops).
- Return a result struct with:
  - `IsInnerChain: bool`
  - `Kind: Direct | Lambda` (Direct = old form, Lambda = new form)
  - `ArgSpanStart: int` (for Direct)
  - `LambdaSpanStart: int` (for Lambda)
- Keep existing Direct detection path unchanged for backward compatibility.

**ChainId suffix (lines 706-714):**
- When `DetectInnerChain` returns `Kind.Lambda`: append `:lambda-inner:{lambdaSpanStart}` to ChainId.
- Existing `:cte-inner:{argSpanStart}` path unchanged for Direct.

### 2b. Handle lambda arguments in CTE discovery

**`DiscoverCteSite` (lines 3570-3681):**
- After extracting method name and type arguments, check if the first argument is a `LambdaExpressionSyntax`.
- If lambda: extract `lambda.SpanStart`, store as `LambdaInnerSpanStart` (new field) on the RawCallSite. Set `EnrichmentLambda = lambda` so DisplayClassEnricher processes the outer lambda. Do NOT set `CteInnerArgSpanStart`.
- If not lambda: existing path (set `CteInnerArgSpanStart` as before).

### 2c. Handle lambda arguments in set-op discovery

**Set-op operand extraction (lines 766-787):**
- When detecting a set-op call's operand, check if the argument is a `LambdaExpressionSyntax`.
- If lambda: extract `lambda.SpanStart`, store as `LambdaInnerSpanStart`. Set `EnrichmentLambda = lambda`. Do NOT call `ExtractSetOperationOperandChainId` or record `OperandArgEndLine/Column`.
- If not lambda: existing path.

### 2d. ChainId for lambda parameter roots

**`ComputeChainId` (lines 1937-2042):**
- When `VariableTracer.TraceToChainRoot` fails (root is an `IdentifierNameSyntax` resolving to `IParameterSymbol`): walk ancestors to find the containing `LambdaExpressionSyntax` and use `lambda.SpanStart` as the scope key.
- Resulting ChainId: `file:lambdaSpanStart:parameterName` — unique per lambda body, distinct from outer chain.

### 2e. IR additions

**`src/Quarry.Generator/IR/RawCallSite.cs`:**
- Add `int? LambdaInnerSpanStart` property — set on CTE definition and set-op sites that have lambda arguments.
- Add `LambdaExpressionSyntax? EnrichmentLambda` (or reuse existing property if one exists) — the outer lambda for display class enrichment.

### 2f. Verify

- All existing tests pass unchanged
- New detection code exists but is not exercised by ChainAnalyzer yet

---

## Phase 3: ChainAnalyzer — tree-based inner chain analysis

Add a new analysis path in `ChainAnalyzer` for lambda-form inner chains. When a CTE definition or set-op site has `LambdaInnerSpanStart`, analyze its inner chain group on-demand via recursive call, and create the `CteDef`/`SetOperationPlan` from the result. Existing `:cte-inner:` and inline-operand-splitting paths remain intact. All existing tests remain green.

### 3a. Identify lambda inner chain groups

**`ChainAnalyzer.AnalyzeChains` (lines 117-208):**
- After grouping chains by ChainId, build additional lookup: `Dictionary<int, string> lambdaInnerChainIds` mapping `lambdaSpanStart → ChainId` for chains whose ChainId contains `:lambda-inner:`.
- These groups are excluded from both the old `:cte-inner:` pass AND the outer chain pass. They're analyzed on-demand only.

### 3b. Synthesized ChainRoot injection

When a lambda inner chain group is about to be analyzed and has no `ChainRoot` site:
- Create a synthetic `TranslatedCallSite` with `Kind = ChainRoot`.
- Entity type name comes from the parent CTE/set-op site's type arguments (the `TDto` or `T` from `With<TDto>` or `Union<T>`).
- Insert as the first site in the group.

### 3c. Recursive analysis for CTE definitions

**`AnalyzeChainGroup` CTE definition processing (~lines 660-743):**
- When a `CteDefinition` site has `LambdaInnerSpanStart`:
  1. Look up `lambdaInnerChainIds[site.LambdaInnerSpanStart]` to find inner ChainId.
  2. Look up `allChainGroups[innerChainId]` to find inner chain sites.
  3. Inject synthetic ChainRoot if needed (Phase 3b).
  4. Recursively call `AnalyzeChainGroup` on inner chain sites.
  5. Assemble inner chain to SQL via `SqlAssembler.Assemble`.
  6. Create `CteDef` with inner SQL, inner parameters, columns, parameterOffset — same downstream data as existing path.
- When a `CteDefinition` site has `CteInnerArgSpanStart` (old form): existing path unchanged.

### 3d. Recursive analysis for set operations

**`AnalyzeChainGroup` set-op processing (~lines 1019-1072):**
- When a set-op site has `LambdaInnerSpanStart`:
  1. Same lookup and recursive analysis as CTE (Phase 3c).
  2. Create `SetOperationPlan` with operand plan, parameterOffset, operandEntityTypeName.
- When a set-op site has `OperandChainId` (old form): existing path unchanged.

### 3e. Pass inner chain parameter info to downstream

The inner chain's `QueryPlan.Parameters` list provides each parameter's `CapturedFieldName` and `CapturedFieldType`. These are stored on the `CteDef`/`SetOperationPlan` as `InnerParameters` (already exists on `CteDef`; add to `SetOperationPlan` if needed).

### 3f. Verify

- All existing tests pass unchanged
- New analysis path exists but no tests exercise it yet (new tests come in Phase 5)

---

## Phase 4: Carrier analysis and emission — direct capture

Implement the direct capture pattern: the `With()`/`Union()` interceptor extracts captured variables from the outer lambda's display class and stores them at `ParameterOffset` in the outer carrier. No inner carrier class. No inner chain interceptors.

### 4a. Display class enrichment for With()/Union() sites

**`src/Quarry.Generator/Parsing/DisplayClassEnricher.cs`:**
- The `EnrichmentLambda` set in Phase 2b/2c is processed by the existing enrichment pipeline.
- For the With()/Union() call site, the enricher analyzes the outer lambda body and finds all captured variables (locals and parameters from the enclosing scope).
- Sets `DisplayClassName`, `CapturedVariableTypes`, `CaptureKind` on the With()/Union() RawCallSite.
- **No changes to enricher code needed** — it already processes any site with an `EnrichmentLambda`.

### 4b. Carrier analysis: inner chain params as outer carrier extractors

**`src/Quarry.Generator/CodeGen/CarrierAnalyzer.cs`:**
- When building extraction plans for the outer carrier, include entries for inner chain parameters:
  - For each `CteDef` (lambda form), iterate `InnerParameters`.
  - For each inner parameter that has `IsCaptured = true` and `CapturedFieldName`:
    - Create a `CapturedVariableExtractor` targeting the `innerBuilder` (or `other`) delegate parameter.
    - The extractor's display class is the With()/Union() site's `DisplayClassName`.
    - The field name is the inner parameter's `CapturedFieldName`.
    - The variable type is the inner parameter's `CapturedFieldType` (or resolved from `CapturedVariableTypes`).
  - Store in the outer carrier's extraction plan, keyed to the With()/Union() site's `UniqueId`.
- Same pattern for `SetOperationPlan` lambda-form inner parameters.

### 4c. Carrier emission: UnsafeAccessor methods

**`src/Quarry.Generator/CodeGen/CarrierEmitter.cs`:**
- The outer carrier class gets `UnsafeAccessor` methods for inner chain captured variables (same `[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "cutoff")]` pattern already used for clause parameters).
- **No changes to emitter code needed** — it already emits UnsafeAccessors for all extractors in the carrier's extraction plan.

### 4d. CTE emission: direct capture instead of inner carrier copy

**`src/Quarry.Generator/CodeGen/TransitionBodyEmitter.cs` (`EmitCteDefinition`):**
- When the CTE definition site has `LambdaInnerSpanStart` (lambda form):
  - Do NOT cast `innerBuilder` to an inner carrier.
  - Do NOT copy P-fields from inner to outer.
  - Instead, emit extraction code:
    ```csharp
    var __target = innerBuilder.Target!;
    var cutoff = {carrier.ClassName}.__ExtractVar_cutoff_N(__target);
    __c.P{offset} = cutoff;
    ```
  - This uses the same `EmitExtractionLocalsAndBindParams` pattern already used by clause interceptors.
- When the CTE definition has `CteInnerArgSpanStart` (old form): existing copy path unchanged.
- Interceptor method signature: parameter type is `Func<IEntityAccessor<TDto>, IQueryBuilder<TDto>>` (use `LambdaInnerSpanStart` presence to determine which type).

### 4e. Set-op emission: direct capture

**`src/Quarry.Generator/CodeGen/SetOperationBodyEmitter.cs` (`EmitSetOperation`):**
- Same direct capture pattern as CTE (Phase 4d).
- When the set-op site has `LambdaInnerSpanStart`:
  - Emit extraction from `other.Target!` instead of copying from operand carrier.
  - Interceptor method signature: `Func<IEntityAccessor<T>, IQueryBuilder<T>>`.
- Old form: existing copy path unchanged.

### 4f. Skip inner carrier and interceptor generation for lambda inner chains

**`src/Quarry.Generator/CodeGen/InterceptorCodeGenerator.cs`:**
- When a chain is identified as a lambda inner chain (was analyzed via the lambda path in Phase 3):
  - Do NOT generate a carrier class for the inner chain.
  - Do NOT generate interceptor methods for the inner chain's call sites.
  - The inner chain is purely compile-time — its SQL is embedded in the outer WITH/set-op clause, and its parameters are captured directly by the outer interceptor.

### 4g. Verify

- All existing tests pass unchanged
- New emission paths exist but no tests exercise them yet

---

## Phase 5: CTE lambda tests

Add comprehensive tests for the CTE lambda form. This is the first end-to-end validation of Phases 1-4 for CTEs.

### 5a. SQL output tests

**New test file `src/Quarry.Tests/SqlOutput/LambdaCteTests.cs`:**
- Single CTE with lambda, no captured params:
  `db.With<OrderDto>(orders => orders.Select(o => new OrderDto { ... })).FromCte<OrderDto>()...`
- Single CTE with captured parameter:
  `db.With<Order>(orders => orders.Where(o => o.Total > cutoff)).FromCte<Order>()...`
- Multi-CTE chain with lambdas:
  `db.With<A>(a => ...).With<B>(b => ...).FromCte<A>()...`
- CTE with entity accessor chaining (QuarryContext<TSelf>):
  `cte.With<Order>(orders => orders.Where(...)).Users().Join<Order>(...).Select(...)`
- CTE with projection (With<TEntity, TDto>):
  `db.With<Order, SummaryDto>(orders => orders.Select(o => new SummaryDto {...})).FromCte<SummaryDto>()...`
- Verify SQL output matches existing non-lambda tests (same SQL, same parameter bindings)

### 5b. Captured parameter tests

- Lambda capturing local variable
- Lambda capturing method parameter
- Lambda capturing multiple variables
- Lambda capturing no variables (const-inlined inner chain)
- Lambda with static field capture

### 5c. Diagnostic tests

**`src/Quarry.Tests/Generation/CarrierGenerationTests.cs`:**
- QRY080 equivalent: lambda body that isn't a valid chain (e.g., empty lambda, lambda returning variable)
- QRY081: `FromCte<T>()` without preceding `With<T>()` (should still work)
- QRY082: Duplicate CTE names with lambda form

### 5d. Verify

- All existing tests pass
- All new lambda CTE tests pass
- SQL output verified against dialect expectations

---

## Phase 6: Set operation lambda tests

Add comprehensive tests for set operation lambda form. Validates Phase 2-4 for set operations.

### 6a. SQL output tests

**New test file `src/Quarry.Tests/SqlOutput/LambdaSetOperationTests.cs`:**
- Same-entity union with lambda:
  `db.Users().Union(users => users.Where(u => u.IsActive)).Select(...)`
- Same-entity with all 6 ops: Union, UnionAll, Intersect, IntersectAll, Except, ExceptAll
- Cross-entity union with lambda:
  `db.Users().Select(u => (u.UserId, u.UserName)).Union<Product>(products => products.Select(p => (p.ProductId, p.ProductName)))`
- Set op with captured parameters in lambda
- Chained set ops: `.Union(a => ...).Except(b => ...)`
- Post-set-op clauses: `.Union(a => ...).OrderBy(...).Limit(...)`
- Verify SQL output matches existing non-lambda tests

### 6b. Captured parameter tests

- Lambda capturing local variable in set op operand
- Lambda capturing multiple variables
- Cross-entity with captured parameters

### 6c. Verify

- All existing tests pass
- All new lambda set-op tests pass

---

## Phase 7: Remove old API forms and migrate tests

Remove the non-lambda `With<T>(IQueryBuilder<T>)` and set-op `Union(IQueryBuilder<T>)` overloads. Migrate all existing tests to lambda form. Remove all old detection/matching/splitting machinery.

### 7a. Remove old API overloads

**`src/Quarry/Context/QuarryContext.cs`:**
- Remove `With<TDto>(IQueryBuilder<TDto> innerQuery)` and `With<TEntity, TDto>(IQueryBuilder<TEntity, TDto> innerQuery)` from base class and `QuarryContext<TSelf>`

**`src/Quarry/Query/IQueryBuilder.cs`:**
- Remove all 18 non-lambda set-op methods (6 on `IQueryBuilder<T>`, 12 on `IQueryBuilder<TEntity, TResult>`)

**`src/Quarry.Generator/Generation/ContextCodeGenerator.cs`:**
- Remove emission of old `With<TDto>(IQueryBuilder<TDto>)` generated methods

### 7b. Remove old discovery machinery

**`src/Quarry.Generator/Parsing/UsageSiteDiscovery.cs`:**
- Remove `Direct` branch from `DetectInnerChain` (the old ancestor-walks-to-ArgumentSyntax path)
- Remove `:cte-inner:` ChainId suffix handling
- Remove `ExtractSetOperationOperandChainId` method
- Remove `OperandArgEndLine/Column` recording from set-op discovery

**`src/Quarry.Generator/IR/RawCallSite.cs`:**
- Remove `CteInnerArgSpanStart`, `IsCteInnerChain`
- Remove `OperandChainId`, `OperandArgEndLine`, `OperandArgEndColumn`

### 7c. Remove old analysis machinery

**`src/Quarry.Generator/Parsing/ChainAnalyzer.cs`:**
- Remove `:cte-inner:` inner/outer split in `AnalyzeChains`
- Remove `cteInnerResults` dictionary and SpanStart-based lookup
- Remove `CteInnerArgSpanStart` lookup path in CTE definition processing
- Remove inline operand splitting (the `OperandArgEndLine/Column` boundary detection and operand site extraction)
- Remove `AnalyzeOperandChain` method if fully superseded
- All inner chains now go through the lambda/tree path

### 7d. Remove old emission paths

**`src/Quarry.Generator/CodeGen/TransitionBodyEmitter.cs`:**
- Remove old `CteInnerArgSpanStart` branch — only lambda-form emission remains
- Remove inner carrier cast and P-field copy loop

**`src/Quarry.Generator/CodeGen/SetOperationBodyEmitter.cs`:**
- Remove old operand carrier cast and P-field copy — only lambda-form emission remains

### 7e. Rename lambda-specific identifiers to generic names

- `LambdaInnerSpanStart` → `InnerChainSpanStart`
- `:lambda-inner:` → `:inner:`
- `IsLambdaInnerChain` → `IsInnerChain`
- `DetectInnerChain` return type simplified (remove `Kind` field, `Direct` variant)

### 7f. Migrate all existing tests to lambda form

Mechanically replace in all CTE test files:
```csharp
// Old:  db.With<Order>(db.Orders().Where(o => o.Total > cutoff))
// New:  db.With<Order>(orders => orders.Where(o => o.Total > cutoff))
```

Mechanically replace in all set-op test files:
```csharp
// Old:  .Union(db.Users().Where(u => u.IsActive))
// New:  .Union(users => users.Where(u => u.IsActive))

// Old (cross-entity):  .Union<Product>(db.Products().Select(p => ...))
// New:                  .Union<Product>(products => products.Select(p => ...))
```

Update diagnostic tests (QRY080, QRY081, QRY082, QRY083) to use lambda form.

### 7g. Verify

- Full test suite passes (3021+ tests)
- No references remain to old API surface (`IQueryBuilder<T> innerQuery` on With, `IQueryBuilder<T> other` on set ops)
- No references remain to removed IR fields
- No references remain to removed analysis methods

---

## Dependency Graph

```
Phase 1 (API overloads)
   │
   v
Phase 2 (Discovery pipeline)
   │
   v
Phase 3 (ChainAnalyzer tree-based analysis)
   │
   v
Phase 4 (Carrier + Emission direct capture)
   │
   v
Phase 5 (CTE lambda tests)        Phase 6 (Set-op lambda tests)
   │                                │
   └──────────┬─────────────────────┘
              v
Phase 7 (Remove old, migrate, clean up)
```

Phases 5 and 6 can run in parallel (independent test suites). All other phases are sequential. Each phase is independently committable with all tests green.
