# Quarry Source Generator — LLM Reference

Compile-time SQL query generator for .NET. Analyzes C# lambda expressions, generates SQL + typed interceptors via Roslyn incremental source generation. Supports SQLite, PostgreSQL, MySQL, SQL Server.

## Usage (for helping users build with Quarry)

### Schema Definition
```csharp
public class UserSchema : Schema
{
    public static string Table => "users";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive => Default(true);
    public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);
    public Many<OrderSchema> Orders => HasMany<OrderSchema>(o => o.UserId);
}
```
Column types: `Col<T>`, `Key<T>` (PK), `Ref<TSchema, TKey>` (FK), `Many<T>` (1:N navigation).
Modifiers: `Identity()`, `Length(n)`, `Precision(p,s)`, `Default(v)`, `ClientGenerated()`, `Computed<T>()`, `IsSensitive()`.

### Context
```csharp
[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class AppDb : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}
```

### Query API
```csharp
var results = await db.Users()
    .Where(u => u.IsActive && u.CreatedAt > cutoff)
    .Select(u => (u.UserId, u.UserName))
    .OrderBy(u => u.UserName)
    .Limit(10)
    .Prepare()
    .ExecuteFetchAllAsync();
```
Terminals: `ExecuteFetchAllAsync`, `ExecuteFetchFirstAsync`, `ExecuteFetchFirstOrDefaultAsync`, `ExecuteFetchSingleAsync`, `ExecuteFetchSingleOrDefaultAsync`, `ExecuteScalarAsync<T>`, `ExecuteNonQueryAsync`, `ToAsyncEnumerable`, `ToDiagnostics`.

Subqueries via navigation: `.Where(u => u.Orders.Any(o => o.Total > 100))` generates `EXISTS(SELECT 1 ...)`.

Joins: `.Join<Order>((u, o) => u.UserId == o.UserId).Select(...)`.

DML: `.Delete().Where(...)`, `.Update().Set(u => u.Name, "val").Where(...)`, `.Insert(entity)`, `.BatchInsert(list, o => (o.Col1, o.Col2))`.

## Architecture Overview

### Pipeline Stages

Three parallel Roslyn incremental pipelines registered in `QuarryGenerator.Initialize()`:

**Pipeline A: Schema/Context** (design-time + build-time via RegisterSourceOutput)
```
Stage 1: Schema/Context    ContextParser + SchemaParser → ContextInfo[] + EntityInfo[]
                           EntityCodeGenerator → entity .g.cs files
                           ContextCodeGenerator → context partial .g.cs files
```

**Pipeline B: Interceptors** (build-time only via RegisterImplementationSourceOutput)
```
Stage 2: Discovery         UsageSiteDiscovery → RawCallSite[]
                           ── .Collect() barrier: all sites gathered ──
Stage 2.5: Enrichment      DisplayClassEnricher → enriched RawCallSite[] (display class names, captured variable types)
Stage 3a: Bind             CallSiteBinder → BindStageResult[] (BoundCallSite OR BindFailure)
                           Returns ImmutableArray (1:N for navigation joins). Failures branch to a
                           dedicated QRY900 report node; successes filter into Stage 3b.
Stage 3b: Translate        CallSiteTranslator → TranslatedCallSite (SQL expression bound, parameters extracted)
                           Returns single site. Errors → TranslatedCallSite.PipelineError field.
                           ── .Collect() barrier: all translated sites gathered ──
Stage 4: Chain Analysis     ChainAnalyzer → AnalyzedChain[] (groups by ChainId, classifies optimization tier)
Stage 5a: SQL Assembly      SqlAssembler → AssembledPlan[] (rendered SQL per conditional mask)
Stage 5b: Carrier Analysis  CarrierAnalyzer → CarrierPlan[] (eligibility gates, field layout, extraction plans)
Stage 5c: Post-analysis     BuildResultTypePatches: resolves unresolved tuple types from chain projections
                            PropagateChainUpdatedSites: replaces original sites with chain-enriched versions
                              (e.g., JoinedEntityTypeNames on post-join sites, patched ResultTypeName)
Stage 5d: File Grouping     GroupTranslatedIntoFiles → FileInterceptorGroup[] (keyed by context + source file)
Stage 5e: Emission          FileEmitter → interceptor .g.cs files
```
Stages 4-5d run inside PipelineOrchestrator.AnalyzeAndGroupTranslated() after all sites are collected.
Stage 5e runs per FileInterceptorGroup in RegisterImplementationSourceOutput.

**Pipeline C: Migrations** (build-time only)
```
Stage 1: Migration discovery → MigrationInfo[]
         MigrateAsyncCodeGenerator → MigrateAsync .g.cs
```

### Call Site Lifecycle

`RawCallSite` (syntax-only: location, interceptor kind, lambda expression, scope context)
→ `BoundCallSite` (adds: entity metadata, context class, SQL dialect, table names)
→ `TranslatedCallSite` (adds: translated SQL clause, parameter list, result types)

### SqlExpr Pipeline

```
SqlExprParser     C# lambda syntax → SqlExpr tree (unresolved)
SqlExprAnnotator  + SemanticModel → type annotations, enum constant folding to LiteralExpr
SqlExprBinder     + EntityInfo → ColumnRef resolved to quoted column names + table qualifiers
SqlExprClauseTranslator  CapturedValueExpr/literals → ParamSlotExpr + ParameterInfo list
SqlExprRenderer   SqlExpr tree → SQL string
```

Node types: ColumnRefExpr, ResolvedColumnExpr, ParamSlotExpr, LiteralExpr, CapturedValueExpr, BinaryOpExpr, UnaryOpExpr, FunctionCallExpr, InExpr, IsNullCheckExpr, LikeExpr, SubqueryExpr, RawCallExpr, SqlRawExpr.

### Carrier Pattern

Carrier classes (`Chain_0`, `Chain_1`, ...) are generated per-chain to avoid intermediate `QueryBuilder<T>` allocations. They hold query state (context, parameters, conditional mask) and implement all builder interfaces.

Flow: `CarrierAnalyzer.AnalyzeNew(AssembledPlan)` → `CarrierPlan` → `CarrierEmitter.EmitCarrierClass()`.

Interceptors cast the builder to the carrier via `Unsafe.As<Chain_N>()`, extract parameters, set mask bits, and dispatch SQL at the terminal.

### Conditional Clause Masking

Clauses inside `if`/`else`/ternary branches deeper than the execution terminal's nesting depth get assigned bit indices. Constants: `MaxConditionalBits = 8`, `MaxIfNestingDepth = 2`. Beyond either → QRY032.

**Cascade model** (#307): `UsageSiteDiscovery.DetectNestingContext` identifies each site's innermost *cascade* — one whole `if/else-if/else` statement chain or ternary — structurally via syntax ancestry, not condition text. `NestingContext` carries `GroupKey` (cascade head span position, `"if:N"`/`"t:N"`), `ArmIndex`, `ArmCount`, `HasFinalElse`. `NestingDepth` counts **cascades** crossed to the method body, so a flat `else if` chain of any arm count is depth 1; depth 2 means a cascade nested inside a cascade arm. Sites inside a condition expression pass through (they run during arm dispatch, before any arm is taken). A ternary is a 2-arm cascade with a final else — including the `q = flag ? q.Where(...) : q` reassignment shape.

**Bit assignment** (ChainAnalyzer): For each clause site, compute `relativeDepth = clause.NestingDepth - terminal.NestingDepth`. If `relativeDepth <= 0`, the clause is unconditional (same scope as terminal). If `relativeDepth > MaxIfNestingDepth`, the chain is RuntimeBuild. Otherwise, assign a `BitIndex` (0-7). Limit/Offset/Distinct sites get bits like any clause (their bits live on `PaginationPlan.LimitBitIndex`/`OffsetBitIndex`/`QueryPlan.DistinctBitIndex`); WithTimeout is explicitly skipped — the carrier `Timeout` field is `TimeSpan?` with a `DefaultTimeout` fallback, so it is conditional-correct without a bit. Each `ConditionalTerm` records the site's `SiteUniqueId`; all site→bit correlation downstream (`AssembledPlan.GetClauseEntries`, emitters) matches by ID, never positionally.

**Mask enumeration** (ChainAnalyzer.EnumerateMaskCombinations): Per cascade, at runtime exactly one arm executes (or none). Options = each represented arm's OR-of-bits — all of an arm's bits set together — plus 0 when the cascade lacks a final else, has arms without chain sites, or is itself conditionally entered (relative depth > 1: a nested cascade can be skipped entirely, so a final else does not guarantee an arm). The 0 option enumerates first so the base variant leads diagnostics/manifest output. Masks are the cross-product of one option per cascade. A 1-arm `if` reproduces the classic independent bit ({0, b}); a single-clause `if/else` reproduces the classic exclusive pair ({b0, b1}). Nested cascades enumerate independently (a reachable superset).

**Unanalyzable positions** (fail-loud QRY032 demotions): a chain site inside a NON-head arm's condition expression (`else if ((q = q.Where(...)) != null)`) executes only when earlier conditions failed but belongs to no arm — `NestingContext.UnanalyzablePositionKey` marks it and ChainAnalyzer demotes unless the terminal shares the exact position. A clause at the terminal's own depth but in a DIFFERENT arm of the terminal's cascade never executes on any path reaching that terminal — demoted rather than baked in unconditionally.

**Reachability validator** (ChainAnalyzer.ValidateMaskEnumeration, defense in depth): after enumeration, brute-force all `2^totalBits` masks against per-cascade constraints (intersection with a cascade's bits must be empty — allowed only when the cascade can take no represented arm — or exactly one arm's complete bit set) and demote the chain to RuntimeBuild (QRY032) if any reachable mask has no variant. Deliberately a separate walk from EnumerateMaskCombinations so one bug cannot hide in both; it should never fire through the public pipeline.

**SQL rendering** (SqlAssembler): For each mask, evaluate which terms are active (`BitIndex == null` or bit set in mask), then render the full SQL statement. Parameter indices are globally stable — skipped conditional terms still occupy their parameter slots to keep `@p0, @p1, ...` aligned. LIMIT/OFFSET/DISTINCT render only in variants whose mask includes their bit (`AppendPagination`, DISTINCT keyword sites, and `NeedsDistinctOrderByWrap` are all mask-gated); the batch prefix/middle/suffix decomposition is disabled (`canBatch = false`) when pagination or distinct is conditional. Offset-without-LIMIT (chain-level or a mask-gated limit-inactive variant) renders the dialect's no-limit idiom via `SqlFormatting.NoLimitIdiom` — SQLite `LIMIT -1`, MySQL `LIMIT 18446744073709551615`; PostgreSQL accepts bare `OFFSET`.

**Code generation** (CarrierEmitter): Single variant → `static readonly string _sql`. Multiple variants → `static readonly string[] _sql` indexed by mask value (gaps filled with `null!`). Carrier accumulates a `byte` mask field via `Mask |= (1 << bitIndex)` as conditional clause interceptors execute — including `EmitPagination`/`EmitDistinct` for conditional Limit/Offset/Distinct. Pagination carrier fields bind only when their bit is active. Terminal dispatches via direct array index guarded by `Quarry.Internal.ThrowHelper.UnenumeratedMask` (bounds + null check → actionable `InvalidOperationException` instead of an NRE/provider error if a mask ever escapes enumeration): `_sql[__c.Mask]`.

### MySQL Positional Bind Order

MySqlConnector binds the Nth `?` in SQL text to the Nth `cmd.Parameters.Add()`; the other three dialects carry slot identity in the placeholder itself (`@pN` by name, `$N` by index). Renderers may emit placeholders out of chain order — e.g. the DISTINCT+ORDER BY wrap hoists ORDER BY exprs textually before WHERE — so MySQL needs the bind sequence to follow SQL-text order, not `GlobalIndex` order (#303).

Mechanism (correct by construction for any renderer — no per-renderer ordering obligation):
1. **Marker emission** — when `SqlDialectConfig.EmitMySqlBindMarkers` is set (only by `SqlAssembler.Assemble`, MySQL only), placeholders render as `{__Q{globalIndex}__}` (`MySqlBindMarkers`) instead of bare `?`. Render paths outside variant assembly (diagnostics fragments, runtime column arrays, wrap-detection comparison renders) stay marker-free. Pagination markers use the plan's true slots (`PaginationPlan.LimitParamIndex`/`OffsetParamIndex`, allocated last by ChainAnalyzer) — the running render index lags the slot when projection params exist.
2. **Rewrite + extraction** — `PipelineOrchestrator.RewriteMySqlBindMarkers` runs inside `AnalyzeAndGroupTranslated`, before file grouping, so both output actions (interceptor emission and the manifest) consume final SQL and incremental equality compares post-processed plans. One pass per variant rewrites markers to `?` (or `{__COL_P{n}__}` for carrier-eligible collection params — this replaced the Nth-`?` substitution and its literal-`?` miscount hazard), records the text-order slot sequence, and validates it against the mask's expected active set (marker-free variants are not exempt — a variant with active params and zero markers means a render surface missed marker emission). Per-variant sequences merge into one chain ranking via topological sort over pairwise order constraints (`TryMergeTextOrders`), smallest-slot-first among unconstrained slots (GlobalIndex tiebreak for mutually exclusive branch groups); a cycle = contradictory orders across variants. Do NOT merge incrementally with placement guesses — mask enumeration feeds singleton variants (`[0]`, `[1]`) before the combined one (`[0,1]`), and an anchor-insertion merge falsely reports a contradiction on that family. Extraction/validation failure ⇒ **QRY048 warning** + identity fallback (pre-#303 behavior). Stored as `AssembledPlan.MySqlBindOrder` (null = identity; excluded from equality — derived from `SqlVariants`).

   QRY048 is a *deferred* diagnostic (emitted as `DiagnosticInfo` from the orchestrator, reported later at emission). Every deferred diagnostic ID MUST be registered in `QuarryGenerator.s_deferredDescriptors` so the real descriptor (severity, message format) is used. Unregistered IDs are no longer silently dropped (#311): `ReportDeferredDiagnostic` reports a QRY900 naming the unregistered ID instead — this trap shipped three separate times (QRY048 in #304, then QRY900 and QRY063 found in #311). `MySqlBindOrderGenerationTests.MarkerShapedStringLiteral_MySQL_SurfacesQRY048_AsWarning` and `DeferredDiagnosticRegistryTests` guard the registrations.
3. **Reordered binding** — `CarrierEmitter.EmitCarrierCommandBinding` iterates the ranking when present; identity emits byte-identical code to before. `ParameterName` needs no handling: on MySQL every name path emits the constant `"?"` — MySqlConnector binds purely by position and ignores names against bare `?` placeholders. Pagination slots are verified to rank last and keep their bind-after-loop position. Insert/batch-insert bind in column order by construction and never carry markers; Patch SET stays runtime-assembled and binds first (SET precedes WHERE textually).

Parameter logging / `ToDiagnostics` lists remain in `GlobalIndex` order — a cosmetic divergence from text order on reordered MySQL chains.

### Error Propagation & QRY900

Every error channel is a value in the incremental pipeline (#311) — nothing error-bearing lives in `[ThreadStatic]` state, so errors survive incremental caching, thread switches between pipeline nodes, and cancellation:

| Stage | Return Type | Error Channel | Rationale |
|-------|-------------|---------------|-----------|
| 3a Bind | `ImmutableArray<BindStageResult>` | `BindStageResult.Failure` (`BindFailure`: file/line/column/message) | An exception produces no `BoundCallSite` to attach an error to, so the failure IS the stage output. Successes filter to Stage 3b; failures branch to a dedicated `Collect()` + report node. Equality includes the failure fields for cache invalidation. |
| 3b Translate | `TranslatedCallSite` | `TranslatedCallSite.PipelineError` field | Scalar return allows natural error field. Equality includes PipelineError for incremental cache invalidation on error state changes. |
| 4–5d Chain analysis | (inside orchestrator) | Deferred `DiagnosticInfo` with `InternalError.Id` added to the diagnostics list | ChainAnalyzer catch handlers route analysis exceptions through the same deferred channel as ordinary diagnostics; QRY900 is registered in `s_deferredDescriptors`. Deferred diagnostics reach the user via file groups; ones whose file has no group are collected into a synthetic site-less "OrphanDiagnostics" group by `GroupTranslatedIntoFiles` so they still report. Bind failures (which can zero out a file's sites) use the dedicated node above. |

**QRY900 source paths**:
1. `site.PipelineError != null` on TranslatedCallSite → Translate-stage exceptions (reported in `EmitFileInterceptors()`)
2. `BindStageResult.Failure` → Bind-stage exceptions (dedicated `RegisterImplementationSourceOutput` in `QuarryGenerator.Initialize`, independent of file groups)
3. Deferred `DiagnosticInfo(InternalError.Id, …)` → ChainAnalyzer/SqlExprBinder exceptions (reported with the group's deferred diagnostics)
4. Exception catch in `EmitFileInterceptorsNewPipeline()` → Emission-stage exceptions
5. `ReportDeferredDiagnostic` miss path → a deferred diagnostic whose ID is missing from `s_deferredDescriptors` is reported as QRY900 naming the ID (never silently dropped)

### Caching Boundaries

| Stage | Granularity | Invalidation Blast Radius |
|-------|-------------|---------------------------|
| 2-2.5 | Per-site (individual transforms) | One changed call site re-enriches only that site |
| 3a-3b | Per-site (Select/SelectMany) | One changed site re-binds/re-translates only that site |
| 4-5d | **All sites** (`.Collect()` barrier) | One new/changed TranslatedCallSite triggers re-analysis of ALL chains for ALL contexts |
| 5e | Per FileInterceptorGroup | FileInterceptorGroup equality gates per-file code generation |

**EntityRegistry as cross-pipeline bridge**: Built from all `ContextInfo` objects (Pipeline A output). Passed via `.Combine(entityRegistry)` into Pipeline B stages 2.5, 3a, 3b, and 4. Consequence: changing a Schema class invalidates all call site binding for entities in that schema.

**Tracking names**: the load-bearing pipeline nodes are labelled via `WithTrackingName` so tests can read per-node run reasons out of `GeneratorRunResult.TrackedSteps`. Constants live in `TrackingNames.cs` — reference them typed, never as string literals:

| Constant | Node |
|---|---|
| `ContextDeclarations` | Per-context `[QuarryContext]` discovery (Pipeline A root) |
| `EntityRegistry` | Collected-context registry barrier feeding all interceptor stages |
| `RawCallSites` | Stage 2 — raw call-site discovery |
| `EnrichedCallSites` | Stage 2.5 — batch display-class enrichment |
| `BindResults` | Stage 3 — per-site bind (success or `BindFailure`) |
| `TranslatedCallSites` | Stage 4 — per-site translation |
| `PerFileGroups` | Stage 5 — collected analysis grouped per source file |

Two Roslyn semantics matter when asserting on these (`IncrementalCachingTests`):

- **An absent stage is itself a cached signal.** A named node that is wholesale-skipped because its inputs were untouched records *no* steps at all — so "missing from `TrackedSteps`" means cached, not broken.
- **Reference equality short-circuits the model comparison.** Re-running a driver against the same `CSharpCompilation` instance never invokes model `.Equals`. To actually exercise the equality implementations, build the second compilation from **freshly parsed trees of identical text** (`CSharpSyntaxTree.ParseText`), which is what the compiler server does on a warm rebuild.

### Chain Disqualification

Chains that cannot be statically analyzed receive `OptimizationTier.RuntimeBuild` → QRY032 compile error. Disqualifiers (from `ChainAnalyzer.CheckDisqualifiers`):

| Disqualifier | Example |
|-------------|---------|
| Forked query chain | `var q = db.T().Where(...); q.Select(A).Execute(); q.Select(B).Execute();` |
| Chain variable captured in lambda | `var q = db.T(); items.Select(x => q.Where(...))` |
| Chain variable passed to non-Quarry method | `var q = db.T(); SomeMethod(q);` |
| Chain variable assigned from non-Quarry method | `var q = GetQuery();` |
| Chain crosses loop boundary | Some clauses inside loop, terminal outside (or vice versa) |
| Conditional nesting depth > 2 | Conditional clause in a cascade nested 3 cascade levels below the terminal (flat `else if` chains are ONE level) |
| Conditional bits > 8 | More than 8 conditional clause sites across all cascades |
| Reachable mask without a variant | Validator backstop (`ValidateMaskEnumeration`) — should never fire; indicates an enumeration bug |
| Clause captures across >1 closure scope | `var minId = …; foreach (var name in names) … .Where(u => u.UserName == name && u.UserId > minId)` — see "Display Class Prediction"; split into separate `.Where(...)` clauses |

### Display Class Prediction

The generator predicts compiler-generated closure class names to emit `[UnsafeAccessor]` methods for captured variable extraction without reflection.

**Algorithm** (DisplayClassEnricher + DisplayClassNameResolver):
1. Group all RawCallSites by enclosing method, walked up past local functions **and lambdas** — `GetEnclosingSymbol` returns the `MethodKind.AnonymousFunction` symbol for a chain written inside a lambda, and failing to unwrap it made `ComputeMethodOrdinal` return -1 and skip the site entirely (issue #333)
2. Compute `methodOrdinal` = index of method in `containingType.GetMembers()` (linear scan)
3. Analyze closures: pre-order traversal of lambda/local-function descendants, assign ordinals to scopes with captures
4. Final name: `"{FullyQualifiedType}+<>c__DisplayClass{methodOrdinal}_{closureOrdinal}"`
5. Classify capture kind (ClosureCapture vs FieldCapture) via `dataFlow.CapturedInside`

**What counts as a scope** — `FindDeclaringScope`. Ground truth below is dumped from emitted IL; every row was a prediction bug before #333.

| Source shape | Emitted display classes |
|---|---|
| lambda param + its own body-block local | `_0 { p, bodyLocal }` — **one** class; a parameter shares with its owner's body |
| local-function param + its body local | `_0 { lfParam, lfLocal }` — one class |
| method local / lambda param / inner-lambda local | `_0 { methodLocal }`, `_1 { p, … }`, `_2 { innerLocal, … }` |
| `foreach` var + its body-block local | `_0 { name }`, `_1 { body, … }` — **two**; the loop variable owns a scope separate from the body |
| `for` decl var + body local | `_0 { i }`, `_1 { body, … }` — same |
| `using` decl var + body local | `_0 { d }`, `_1 { body, … }` — same |
| `switch` section locals | `_0 { a, b }` — one class for the whole section |
| `catch (E ex)` + its body local | `_0 { ex }`, `_1 { bodyLocal, … }` — the catch variable owns a scope |

So: a scope is a `BlockSyntax`; a **parameter** resolves to its owner's body block (not the enclosing block); and `foreach`/`for`/`using`/`switch`-section/`catch` **declarations** own a scope distinct from both the enclosing block and their own body — see `IsOwnScopeStatement`.

**Known unhandled form:** a `switch`-*expression* arm variable (`o switch { string s => … }`) also owns a display class, and its field is name-mangled to `<s>5__2` rather than `s` — so both the ordinal and the accessor's field name would be wrong. It resolves to the enclosing block today and is not detected; tracked separately. Adding a form to `IsOwnScopeStatement` is only safe once the emitted field NAME has been checked too, not just the scope. Resolving any of these to the enclosing block merges two scopes and shifts every later ordinal — invisible until a method has two capture scopes, since ordinal 0 is otherwise correct by accident.

**Instance fields mixed with locals.** With only a field captured, the delegate `Target` IS the containing instance and the field is read straight off it (`FieldCapture`). Add a captured local and the compiler interposes a display class holding the local plus `<>4__this`; the field then lives on the instance behind that back-reference. The emitter detects this (a captured name absent from `CapturedVariableTypes`, which holds exactly the locals/parameters) and emits an `<>4__this` accessor returning `ref TContaining`. That hop is expressible precisely because `<>4__this`'s type is the user's own class and needs no `[return: UnsafeAccessorType]`.

**Multi-scope captures are rejected, not emitted.** A clause capturing locals from two or more distinct scopes is disqualified (`ChainAnalyzer.CheckDisqualifiers` → QRY032) with a message naming the shape and the workaround. The outer scope is reachable only via the compiler's `CS$<>8__locals` link field, whose type is another display class — and a field accessor must return byref while a byref return cannot name an inaccessible type ([dotnet/runtime#119664](https://github.com/dotnet/runtime/issues/119664), open/`Future`, deliberately excluded as not memory safe). The `Unsafe.As` shadow-overlay alternative was rejected: it is UB ([discussion #111049](https://github.com/dotnet/runtime/discussions/111049) — display classes hold reference fields, so they are non-blittable and get `Auto` layout with no guaranteed offsets), and its failure mode is silently swapped values rather than an exception.

The scope count deliberately ignores variables **declared inside the clause lambda**: a nested subquery lambda (`u => u.Orders.Any(o => …)`) contributes its own parameters to `CapturedInside`, and counting those made the guard reject working nested-subquery and set-operation chains.

**Compiler assumptions** (undocumented implementation details, not guaranteed contracts):
- `GetMembers()` returns members in declaration order (all members count: backing fields, properties, accessors, methods)
- Display class naming follows `<>c__DisplayClass{M}_{C}` pattern
- Closure ordinals assigned in pre-order source traversal order
- Partial classes contribute members in compilation unit order
- The scope→display-class mapping in the table above

**Closure ordinals depend on `<Optimize>`, not just on source.** `ClosureConversion.Analysis` calls `MergeEnvironments()` only when `OptimizationLevel == Release` (gated by the MSBuild `<Optimize>` property — *not* the configuration name, `DebugType`, or `DebugSymbols`). A merged-away environment never consumes an ordinal, so **every later `closureOrdinal` shifts down by one**. Verified on one SDK and one compiler build:

```
Debug:    _0 [a]       _1 [b, CS$<>8__locals1]   _2 [c]
Release:  _0 [a, b]                              _1 [c]
```

`dotnet test` defaults to Debug; CI runs `-c Release`. This is issue #344, and it is the mechanism behind "passes locally, fails in CI". Two consequences worth internalising:

- **The multi-scope guard cannot catch it.** The mispredicted clause is an ordinary single-scope capture; it is an *unrelated* lambda elsewhere in the same method that causes the merge. The generator cannot guard on a lambda it never inspects.
- **Compiler *version* was NOT the variable** in that instance. Roslyn 4.11 / 4.14 / 5.0 agreed on all 25 shapes tested; the `<Optimize>` axis changed 7 of them. Pinning an SDK would not have helped.
- **But versions do change it over time.** [roslyn#82430](https://github.com/dotnet/roslyn/issues/82430) (Feb-Mar 2026) defers display-class allocation for async local functions — `IntroduceFrame` skips frame creation for eligible environments, so later ordinals renumber. Same file, same `<Optimize>` gate. Treat the numbering as a moving target.

**No Roslyn API can replace the prediction.** Display classes are synthesized during `Emit`, after generators finish; every closure type in `Microsoft.CodeAnalysis.CSharp` is `NotPublic`; and for a captured local every shipped `SymbolDisplayFormat` — `FullyQualifiedFormat` included — returns just the bare name, with a null documentation-comment id. The two upstream issues often cited here (roslyn#11565, #55651) are the *opposite* direction (mangled name → original) and do not represent a refusal of this direction. See `_research-roslyn-closures.md` and `_research-symbol-to-name.md`. The practical consequence: the prediction can only be **verified**, not eliminated.

The shapes tabulated above were verified under both settings.

A prediction that is merely *wrong* still compiles and then throws `MissingFieldException` (bad field name), `InvalidCastException` (bad display class) or `TypeLoadException` (no such display class) on first execution — so codegen tests alone cannot validate this area. `Generation/LambdaCaptureExecutionTests` exists for exactly that reason.

**Supplemental compilation**: `DisplayClassEnricher.BuildSupplementalCompilation` adds generated entity classes and context partial classes to the compilation before creating semantic models. This lets Roslyn resolve all generated types natively — no manual error-type fallbacks needed. Variables flowing from generated methods (e.g., `db.Equipments().ExecuteFetchAllAsync()`) resolve to their correct types automatically. When `TypeKind.Error` persists (e.g., types from other generators), the fallback is `"object"`.

### Subquery & Aggregate Support

**Navigation subquery methods** (recognized by `SqlExprParser.IsSubqueryMethod`):

| Pattern | SQL | Notes |
|---------|-----|-------|
| `nav.Any()` | `EXISTS (SELECT 1 FROM t WHERE correlation)` | Parameterless |
| `nav.Any(x => pred)` | `EXISTS (SELECT 1 FROM t WHERE correlation AND pred)` | With predicate |
| `!nav.Any(...)` | `NOT EXISTS (...)` | Negation supported |
| `nav.All(x => pred)` | `NOT EXISTS (SELECT 1 FROM t WHERE correlation AND NOT pred)` | Predicate required |
| `nav.Count()` | `(SELECT COUNT(*) FROM t WHERE correlation)` | Scalar subquery |
| `nav.Count(x => pred)` | `(SELECT COUNT(*) FROM t WHERE correlation AND pred)` | With predicate |

Navigation aggregates (v0.3.0): `.Sum(selector)`, `.Min(selector)`, `.Max(selector)`, `.Avg(selector)` / `.Average(selector)` follow the same correlated-subquery pattern. Still not supported on navigation: `.FirstOrDefault()`, `.Exists()`.

**Sql.* aggregate functions** (work in any expression context — Select, Where, Having):

| Function | SQL |
|----------|-----|
| `Sql.Count()` | `COUNT(*)` |
| `Sql.Count(expr)` | `COUNT(expr)` |
| `Sql.Sum(expr)` | `SUM(expr)` |
| `Sql.Avg(expr)` | `AVG(expr)` |
| `Sql.Min(expr)` | `MIN(expr)` |
| `Sql.Max(expr)` | `MAX(expr)` |

Subquery aliases are generated as `sq0`, `sq1`, etc. Correlation is always `inner.FK = outer.PK` (automatic from navigation metadata). Nested subqueries are supported (e.g., `u.Orders.Any(o => o.Items.Any(i => ...))`).

### Window Functions (Select projections)

`Sql.*` window variants use a fluent `IOverClause` lambda:

| Function | SQL |
|----------|-----|
| `Sql.RowNumber(over => …)` | `ROW_NUMBER() OVER (…)` |
| `Sql.Rank(over => …)` | `RANK() OVER (…)` |
| `Sql.DenseRank(over => …)` | `DENSE_RANK() OVER (…)` |
| `Sql.Ntile(n, over => …)` | `NTILE(n) OVER (…)` |
| `Sql.Lag(col, offset, default, over => …)` | `LAG(col, offset, default) OVER (…)` |
| `Sql.Lead(col, offset, default, over => …)` | `LEAD(col, offset, default) OVER (…)` |
| `Sql.FirstValue(col, over => …)` | `FIRST_VALUE(col) OVER (…)` |
| `Sql.LastValue(col, over => …)` | `LAST_VALUE(col) OVER (…)` |
| `Sql.{Sum,Count,Avg,Min,Max}(col, over => …)` | aggregate + OVER |

`IOverClause` fluent methods: `PartitionBy`, `OrderBy`, `OrderByDescending`. Frame specs (ROWS/RANGE) not yet supported. Non-column args (offsets, defaults, Ntile buckets) are parameterized at compile time (C# suffixes stripped: `0m` → `0`). Aggregate/window column identifiers emit backticks on MySQL and brackets on SQL Server (not double quotes).

### CTEs and Set Operations

**`.With<TDto>(lambda)` / `.With<TEntity,TDto>(lambda)` + `.FromCte<TDto>()`:** compile to standard `WITH name AS (SELECT …)` across all four dialects. Multi-CTE chains supported (`.With<A>(…).With<B>(…)`). Per-CTE parameter-space isolation prevents `@p{n}` collisions. `QRY080` / `QRY081` / `QRY082` diagnostics cover unanalyzable inner, missing `With`, and duplicate names. Typed post-`With` accessor chains require `QuarryContext<TSelf>`.

**`Union/UnionAll/Intersect/IntersectAll/Except/ExceptAll`** on `IQueryBuilder<T>` / `IQueryBuilder<TEntity,TResult>`. Post-set-op `Where`/`GroupBy`/`Having` auto-wraps the set expression as a subquery. Cross-entity set operations are supported (`Users.Select(…).Union(Products.Select(…))`). `QRY070`/`QRY071` for dialect-unsupported variants (SQLite has no INTERSECT ALL/EXCEPT ALL); `QRY072` for projection column-count/type mismatch. Parameter indexing through set-op operands goes through `AnalyzeOperandChain`, which merges projection parameters to avoid cross-operand collisions.

### Navigation Joins and 6-Table Explicit Joins

`One<T>` with `HasOne<T>()` emits a reverse-side nullable nav; `HasManyThrough<TTarget, TJunction, TSelf>(junctionNav, targetNav)` emits many-to-many skip nav with an implicit junction→target JOIN. All type parameters are Schema classes, not generated entity types. Schema-level diagnostics: QRY060–065. The `NavigationAccessExpr` node threads through parse → bind → translate → assemble → emit; `KnownDotNetMembers` excludes `.ToString()` / `.Equals()` etc. from being parsed as nav access. Implicit joins from nav lambdas are deduplicated against explicit joins.

Explicit joins support 2–6 tables via T4-generated `IJoinedQueryBuilder5/6` and `JoinedCarrierBase5/6`. New join kinds: `CrossJoin<T>()` (no condition), `FullOuterJoin<T>(condition)`. **Join-aware nullable readers:** the projection analyzer inspects join-side nullability and wraps reader column reads on LEFT/RIGHT/FULL OUTER nullable sides with `IsDBNull` guards. Declared tuple types unchanged; only generated reader code is affected.

### Partial Updates via Patch

Two `Update().Set(...)` overloads accept a generated per-entity `Patch` struct so the column set can vary at runtime — the missing case in the existing `Set(entity)` and `Set(u => u.X = v)` forms, both of which lock the column set at the call site.

**Generation.** `EntityCodeGenerator.GeneratePatchStruct` emits a nested `public struct Patch : Quarry.IPatchFor<TEntity>` inside every entity with 1–64 updatable columns (identity + computed excluded; FKs / enums / custom-mapped types included with their entity-side types — `EntityRef<T, TKey>`, the enum type, the `Mapped<T>` user type). Each property setter ORs a constant bit into a hidden `internal ulong __mask` field. Entities exceeding 64 updatable columns raise **QRY045** at generation time and self-suppress Patch emission. The `_Mask_{Col}` constants are emitted alongside the properties for downstream code (no current external consumers beyond the carrier binder).

**Discovery.** Patch classification in `UsageSiteDiscovery` is *syntax-only*, not semantic. The SyntaxProvider's `SemanticModel` runs on the pre-generator compilation, so the generated `Entity.Patch` struct isn't visible and Roslyn would otherwise bind `Set(somePatch)` to the SetPoco DIM. Instead `IsPatchConstructionExpression` recognizes `new X.Patch { … }` / `default(X.Patch)`, `IsPatchVariableReference` walks the enclosing `MemberDeclarationSyntax` to verify a local's declarator initializer matches that shape, and the `ref` modifier on a single lambda parameter discriminates `Set(PatchAction<TPatch>)`. Unsupported shapes (factory returns, ternaries) fall through to UpdateSetPoco and surface as CS9144 at the user's call site — actionable.

**IR & SQL.** `PatchInfo` mirrors `InsertInfo` and lives on `BoundCallSite`. ChainAnalyzer emits a single sentinel `SetTerm` with a `PatchSetPlaceholderExpr` value (renders as literal `{__PATCH_SET__}`) and zero per-column `QueryParameter`s — the column set is runtime-determined, so compile-time can't allocate parameter slots. `SqlAssembler.RenderUpdateSql` emits `UPDATE "users"{__PATCH_SET__} WHERE …` (no space before the token); the runtime emitter owns the entire ` SET … ` clause including its leading space.

**Runtime SET assembly.** `TerminalEmitHelpers.ParseSqlSegments` recognizes the `{__PATCH_SET__}` token as a `PatchSet` segment. `EmitInlineSqlBuilder`'s PatchSet case emits an empty-mask guard (`throw new InvalidOperationException` when `__c.PatchMask == 0UL`), then walks a per-chain fragment table `(ulong Bit, string Prefix)[] _PatchFragments`, appending each active fragment's prefix + dialect-correct placeholder and bumping a local `__setShift` counter. WHERE-side scalar parameters reference `(idx + __colShift + __setShift)` so their placeholder names shift past the runtime-bound SET params; for MySQL only bind order matters.

**Carrier wiring.** Chains with any Patch site get two extra carrier fields — `Patch Patch` and `ulong PatchMask` — plus the per-chain `_PatchFragments` table and a static `_BindPatchParams(DbCommand, in Patch, ulong mask, int startIdx)` method with unrolled per-column if-blocks (FK `.Id` extraction, enum cast, custom-mapper `ToDb` + `ConfigureParameter`, bool → int conversion where the dialect requires). Because `_BindPatchParams` lives on the file-scoped `Chain_N` carrier class, it can't see the interceptor class's private `_mapper_X` fields; `EmitPatchSupport` therefore emits a per-carrier mirror field for each unique mapper FQN referenced by the chain's Patch columns. The `Set(Patch)` interceptor body is `__c.Patch = patch; __c.PatchMask = patch.__mask;` — the lambda form invokes `action(ref __c.Patch)` then mirrors the mask.

**Tier.** Patch chains are always `OptimizationTier.PrebuiltDispatch` *for the chain shape* but always `Opaque` at the SQL level — there's no prebuilt variant set, every execute rebuilds the SET clause. Users who want a prebuilt SQL string for a fixed column set should stay on `Set(new User { … })` (untouched `UpdateSetPoco`).

### SQL Manifest Emission

Gated by MSBuild property `QuarrySqlManifestPath`. `ManifestEmitter` runs after Stage 6 and writes per-dialect markdown files (one per dialect present in the compilation). `WriteIfChanged` compares against on-disk content to suppress no-op writes. Output includes every chain's SQL, parameter table (including LIMIT/OFFSET parameters), bitmask-labeled conditional variants (`Variant[0b0001]`), and per-file summary. Write failures surface as `QRY040` warnings.

### Supplemental Compilation (v0.3.0)

The discovery stage builds a supplemental compilation containing Pipeline-A outputs (entity classes, context accessors) before creating semantic models for Pipeline-B. This replaces ~700 lines of prior error-type fallback heuristics (`TryResolveErrorType`, `TryQualifyErrorTypeFromUsings`). Remaining unresolvable types still fall back to `"object"` under the strict/lenient `IsUnresolvedTypeName` split. `EntityRegistry.Equals`/`GetHashCode` include `_allContexts` — this was a latent incremental-caching bug that could leave stale cross-context views.

### Shared SQL Parser

`Quarry.Shared/Sql/Parser/` (tokenizer, recursive-descent parser, AST, walker) is `#if QUARRY_GENERATOR`-gated — consumed by the generator, excluded from the runtime assembly. Powers: RawSqlAsync compile-time column resolution, QRY042 convertibility detection, and the `Quarry.Migration` converters.

### Carrier Dedup

Structurally-identical carrier classes are merged at emission time. Carrier class numbering (`Chain_N`) may have gaps and is not a stable contract. Dedup checks `CarrierPlan` equality (fields, parameters, extraction plans, SQL variants). Diagnostics still reference the canonical carrier name.

### Incremental SQL Mask Rendering

For chains with N conditional terms (up to 8 bits = 256 variants), shared prefix/suffix is rendered once and variant-specific middle segments are assembled via `StringBuilder.Append` rather than re-rendering from scratch per mask. Applies to SELECT and DELETE multi-mask chains.

### Generated Output Files

- `{Namespace}.{Entity}.g.cs` — Entity class (FK as `EntityRef<T,K>`, nav as `NavigationList<T>`, nested `Patch` struct).
- `{Context}.g.cs` — Context partial: constructors, accessor properties, Insert/Update/Delete methods, MigrateAsync (self-contained: uses `SqlDialect.{Dialect}` enum directly, no instance field dependency).
- `{Context}.Interceptors.g.cs` — `file static` class with `[InterceptsLocation]` methods + carrier classes (each with `_sql` field).

## File Map

### Entry Point
| File | Purpose |
|------|---------|
| `QuarryGenerator.cs` | IIncrementalGenerator. Registers 3 pipelines: schema/context, interceptors, migrations. Stages 2-5 orchestration. |
| `DiagnosticDescriptors.cs` | Central registry of all QRY diagnostic descriptors (QRY001–QRY055, QRY900) with severity, title, and message format. |

### Parsing (Stage 1-2.5) — `Parsing/`
| File | Purpose |
|------|---------|
| `UsageSiteDiscovery.cs` | Stage 1. Discovers Quarry call sites → RawCallSite. Symbol resolution, ChainId computation, scope detection. |
| `SchemaParser.cs` | Parses Schema classes → EntityInfo (columns, navigations, indexes, naming). |
| `ContextParser.cs` | Parses [QuarryContext] classes → ContextInfo (dialect, entities, mappings). |
| `ChainAnalyzer.cs` | Stage 4. Groups sites by ChainId → QueryPlan. Conditional classification, projection building, parameter enrichment. |
| `AnalyzabilityChecker.cs` | Per-site analyzability gate. Checks receiver is a fluent chain (not parameter/variable), lambda is present, traces up to 2 hops in variable chains. Sets IsAnalyzable + NonAnalyzableReason on RawCallSite. |
| `DisplayClassEnricher.cs` | Stage 2.5. Batch closure analysis per method. Predicts display class names, collects captured variable types. |
| `DisplayClassNameResolver.cs` | Display class name prediction utilities. Method ordinals, closure ordinals, captured variable types. |
| `VariableTracer.cs` | Variable declaration tracing. Builder type checks, fluent chain root walking. |
| `NamingConventions.cs` | Property → column name conversion (snake_case, camelCase, etc). |

### IR (Intermediate Representation) — `IR/`
| File | Purpose |
|------|---------|
| `RawCallSite.cs` | Discovery-time model. ~50 properties: location, kind, expression, scope flags. |
| `BoundCallSite.cs` | Wraps RawCallSite + resolved entity metadata, context, dialect. |
| `TranslatedCallSite.cs` | Wraps BoundCallSite + translated clause, parameters, result types. PipelineError field. |
| `CallSiteBinder.cs` | Stage 3. Resolves entity refs from EntityRegistry → BoundCallSite. |
| `CallSiteTranslator.cs` | Stage 3. Runs SqlExpr pipeline (parse→annotate→bind→extract→render) → TranslatedCallSite. |
| `SqlExpr.cs` | Base SqlExpr class. |
| `SqlExprNodes.cs` | All SqlExpr node types (ColumnRef, Literal, BinaryOp, CapturedValue, Subquery, etc). |
| `SqlExprParser.cs` | C# expression syntax → SqlExpr tree. No SemanticModel. |
| `SqlExprAnnotator.cs` | Type annotation + constant folding (enums → LiteralExpr). |
| `SqlExprBinder.cs` | Column resolution (ColumnRef → ResolvedColumn with quoted names). |
| `SqlExprClauseTranslator.cs` | Parameter extraction. Unified for standard + subquery modes. |
| `SqlExprRenderer.cs` | SqlExpr → SQL string. Dialect-specific quoting. |
| `SqlAssembler.cs` | QueryPlan → AssembledPlan. Renders SQL per conditional mask. INSERT RETURNING/OUTPUT. |
| `QueryPlan.cs` | Dialect-agnostic query structure: terms, joins, projection, pagination, parameters. |
| `AssembledPlan.cs` | QueryPlan + rendered SQL variants + reader delegate code + execution metadata. |
| `EntityRegistry.cs` | Multi-key entity index (by type, name, accessor name). Built from all contexts. |
| `EntityRef.cs` | Lightweight entity reference (avoids Location/indices). |
| `PipelineOrchestrator.cs` | Stage 5. Chains: diagnostics → ChainAnalyzer → SqlAssembler → CarrierAnalyzer → file grouping. |
| `BindStageResult.cs` | Stage 3a output: BoundCallSite OR BindFailure. Failures reported as QRY900 by a dedicated output node. |
| `FileOutputGroup.cs` | Legacy output container (superseded by FileInterceptorGroup). |
| `TraceCapture.cs` | [ThreadStatic] trace accumulator for .Trace() chains. Produced AND consumed within one AnalyzeAndGroupTranslated call (captured onto AssembledPlan.TraceLines, cleared in finally) — never crosses a node/thread boundary. |

### Code Generation — `CodeGen/`
| File | Purpose |
|------|---------|
| `CarrierAnalyzer.cs` | Analyzes AssembledPlan → CarrierPlan. Eligibility gates, field/parameter computation, extraction plans. |
| `CarrierPlan.cs` | Carrier plan model: fields, parameters, mask, extraction plans, interfaces. |
| `CarrierParameter.cs` | Extended carrier parameter with global index, field name/type, extraction/binding code, type mapping, collection/sensitivity flags. |
| `CarrierEmitter.cs` | Emits carrier class + carrier-path method bodies (clause binding, terminal execution). |
| `InterceptorRouter.cs` | Routes InterceptorKind → EmitterCategory (Clause, Terminal, Join, Transition, RawSql). |
| `FileEmitter.cs` | Per-file orchestrator. Pass 1: carrier classes. Pass 2: interceptor methods via dispatcher. |
| `ClauseBodyEmitter.cs` | Emits Where/OrderBy/GroupBy/Having/Set/Select clause bodies. |
| `JoinBodyEmitter.cs` | Emits Join/LeftJoin/RightJoin + joined clause bodies. |
| `TerminalBodyEmitter.cs` | Emits execution terminals (FetchAll, FetchFirst, Insert, BatchInsert, NonQuery, Diagnostics, Prepare). |
| `TerminalEmitHelpers.cs` | Shared: ResolveSiteParams, parameter locals, collection expansion, diagnostic arrays, return type/executor resolution. |
| `TransitionBodyEmitter.cs` | Emits Delete/Update/Insert transitions, ChainRoot, Pagination, Distinct, WithTimeout. |
| `RawSqlBodyEmitter.cs` | Emits RawSqlAsync/RawSqlScalarAsync (bypasses query builder). |

### Entity/Context Generation — `Generation/`
| File | Purpose |
|------|---------|
| `EntityCodeGenerator.cs` | Generates entity classes from EntityInfo (properties, types, defaults). |
| `ContextCodeGenerator.cs` | Generates context partial (constructors, query builder properties). |
| `InterceptorCodeGenerator.cs` | Delegates to FileEmitter. Collects cached extractor fields. |
| `InterceptorCodeGenerator.Utilities.cs` | Helpers: GetColumnValueExpression, IsBrokenTupleType, SanitizeTupleResultType. |
| `MigrateAsyncCodeGenerator.cs` | Generates MigrateAsync method from migration metadata. |

### Projection — `Projection/`
| File | Purpose |
|------|---------|
| `ProjectionAnalyzer.cs` | Analyzes Select() lambdas → ProjectionInfo (kind, columns, reader method). |
| `ReaderCodeGenerator.cs` | Generates column list SQL + typed reader delegates (entity, DTO, tuple, scalar). |

### Translation — `Translation/`
| File | Purpose |
|------|---------|
| `ParameterInfo.cs` | Parameter extracted from SQL expressions: index, name, CLR type, value expression, collection flag, capture metadata. |
| `SqlLikeHelpers.cs` | LIKE expression helpers: `EscapeLikeMetaChars()`, `FormatLikeWithParameter()`. Dialect-aware concatenation. |

### Utilities — `Utilities/`
| File | Purpose |
|------|---------|
| `TypeClassification.cs` | Central type classification: IsValueType, GetReaderMethod, NeedsSignCast, IsUnresolvedTypeName/IsUnresolvedResultType, BuildTupleTypeName, SplitTupleElements. |
| `SymbolDisplayCache.cs` | Caches ITypeSymbol.ToDisplayString() results via ConditionalWeakTable. |
| `FileHasher.cs` | Converts file paths into sanitized tags for generated file names and C# identifiers. |

### Models — `Models/`
All pipeline models implement `IEquatable<T>` for incremental caching.

| File | Type(s) | Purpose |
|------|---------|---------|
| `InterceptorKind.cs` | `enum InterceptorKind` | 40+ enum values for all interceptor categories. |
| `ColumnInfo.cs` | `class ColumnInfo` | Column from schema: property name, column name, CLR type, modifiers. |
| `ContextInfo.cs` | `class ContextInfo` | Discovered QuarryContext: configuration, dialect, entity mappings. |
| `EntityInfo.cs` | `class EntityInfo` | Discovered entity: name, table name, columns, navigations, indexes. |
| `EntityMapping.cs` | `class EntityMapping` | Maps context property name → EntityInfo. |
| `NavigationInfo.cs` | `class NavigationInfo` | One-to-many navigation (Many<T>): property name, related entity, FK. |
| `IndexInfo.cs` | `class IndexInfo` | Index: columns with sort directions, uniqueness, type (BTree/Hash), filter, includes. |
| `ProjectionInfo.cs` | `class ProjectionInfo` | Analyzed Select() lambda: kind, result type, columns, reader method. |
| `ExecutionInfo.cs` | `class ExecutionInfo` | Execution context for terminals: SQL, parameters, reader. |
| `InsertInfo.cs` | `class InsertInfo` | Insert operation metadata: columns, identity column, RETURNING clause. |
| `ClauseExtractionPlan.cs` | `class ClauseExtractionPlan` | Groups per-variable extractors for a single clause. |
| `CapturedVariableExtractor.cs` | `class CapturedVariableExtractor` | Per-variable [UnsafeAccessor] extractor: method name, variable name/type, display class, capture kind. |
| `CarrierField.cs` | `enum FieldRole`, `class CarrierField` | FieldRole (ExecutionContext, Parameter, Collection, ClauseMask, Limit, Offset, Timeout, Entity). CarrierField describes a field on the generated carrier class. |
| `SetActionAssignment.cs` | `class SetActionAssignment` | Single assignment from `Set(Action<T>)` lambda: column SQL, value type, inlined value. |
| `FileInterceptorGroup.cs` | `class FileInterceptorGroup` | Groups all interceptor data for a (context, source file) pair. Output of PipelineOrchestrator. |
| `OptimizationTier.cs` | `enum OptimizationTier`, `enum ClauseRole` | PrebuiltDispatch vs RuntimeBuild. ClauseRole tracks clause position. |
| `QueryKind.cs` | `enum QueryKind` | Query routing: Select, Delete, Update, Insert, BatchInsert. |
| `ClauseKind.cs` | `enum ClauseKind` | Clause types: Where, OrderBy, GroupBy, Having, Set. |
| `RawSqlTypeInfo.cs` | `class RawSqlTypeInfo` | Resolved result type T for RawSqlAsync<T>/RawSqlScalarAsync<T>. |
| `DiagnosticInfo.cs` | `class DiagnosticInfo` | Deferred diagnostic: ID, location, message args. Carried through pipeline for reporting in emission. The ID must be registered in `QuarryGenerator.s_deferredDescriptors`; an unregistered ID is reported as a QRY900 naming the ID (#311) instead of the intended diagnostic. |
| `DiagnosticLocation.cs` | `struct DiagnosticLocation` | Structural source location (file, line, column, span). Replaces Roslyn Location for IEquatable. |
| `MigrationInfo.cs` | `class MigrationInfo` | Migration class metadata: version, name, flags (HasDestructiveSteps, HasBackup, etc). |
| `SnapshotInfo.cs` | `class SnapshotInfo` | [MigrationSnapshot] metadata: version, name, schema hash. |
| `EquatableArray.cs` | `struct EquatableArray<T>` | ImmutableArray wrapper with element-wise equality for incremental caching. |
| `EquatableDictionary.cs` | `struct EquatableDictionary<K,V>` | ImmutableDictionary wrapper with key-value equality for incremental caching. |
| `EqualityHelpers.cs` | `static class EqualityHelpers` | SequenceEqual, HashSequence, NullableSequenceEqual, DictionaryEqual utilities. |
| `HashCodePolyfill.cs` | `struct HashCode` | System.HashCode polyfill for netstandard2.0 compatibility. |

## InterceptorKind Categories

| Category | Kinds |
|----------|-------|
| Clause | Select, Where, OrderBy, ThenBy, GroupBy, Having, Set, DeleteWhere, UpdateWhere, UpdateSetAction, UpdateSetPoco, UpdateSetPatch, UpdateSetPatchAction |
| Terminal | ExecuteFetchAll, ExecuteFetchFirst, ExecuteFetchFirstOrDefault, ExecuteFetchSingle, ExecuteFetchSingleOrDefault, ExecuteScalar, ExecuteNonQuery, ToAsyncEnumerable, ToDiagnostics, Prepare |
| Insert Terminal | InsertExecuteNonQuery, InsertExecuteScalar, InsertToDiagnostics |
| Batch Insert | BatchInsertExecuteNonQuery, BatchInsertExecuteScalar, BatchInsertToDiagnostics, BatchInsertColumnSelector, BatchInsertValues |
| Join | Join, LeftJoin, RightJoin |
| Transition | ChainRoot, DeleteTransition, UpdateTransition, InsertTransition, AllTransition |
| Modifier | Limit, Offset, Distinct, WithTimeout |
| Raw SQL | RawSqlAsync, RawSqlScalarAsync |
| Debug | Trace |

## Diagnostics (QRY Codes)

| Code | Severity | Meaning |
|------|----------|---------|
| QRY001 | Error | Query not fully analyzable (non-analyzable receiver/lambda — site gets no interceptor, call would throw at runtime) |
| QRY002 | Error | Missing Table property on schema |
| QRY003 | Error | Invalid column type / no TypeMapping |
| QRY006 | Error | Unsupported Where operation |
| QRY008 | Warning | Potential SQL injection |
| QRY009 | Error | GroupBy required for aggregate |
| QRY011 | Error | Select required before execution |
| QRY014 | Error | Anonymous type projection not supported |
| QRY015 | Warning | Ambiguous context resolution |
| QRY019 | Error | Clause not translatable (clause interceptor skipped, call would throw at runtime) |
| QRY020 | Error | All() requires predicate |
| QRY029 | Error | Sql.Raw placeholder mismatch |
| QRY031 | Error | Unresolvable RawSqlAsync\<T\> generic type parameter |
| QRY032 | Error | Chain not analyzable |
| QRY033 | Error | Forked query chain |
| QRY034 | Warning | .Trace() requires QUARRY_TRACE define |
| QRY035 | Error | PreparedQuery escapes scope |
| QRY036 | Error | Prepared query with no terminals |
| QRY040 | Warning | SQL manifest write failure |
| QRY041 | Warning | RawSqlAsync column expression without alias (falls back to runtime ordinal discovery) |
| QRY042 | Info | RawSqlAsync convertible to chain query (code fix available) |
| QRY043 | Error | Row entity type not materializable (no parameterless ctor, init-only property, abstract class, or interface) |
| QRY044 | Warning | `[QuarryContext]` namespace missing from `<InterceptorsNamespaces>` |
| QRY045 | Error | Entity has more than 64 updatable columns; cannot generate `Patch` struct (single-`ulong` mask cap) |
| QRY046 | Warning | `Set(...)` argument is not a recognized Patch construction shape (descriptor reserved; detection wiring is a future enhancement — see workflow.md) |
| QRY047 | Warning | Entity has a column named `Patch`; nested struct auto-renamed to `_Patch` (or more underscores) to avoid CS0102 — reference as `Entity._Patch`. If all candidates collide, Patch struct emission is suppressed for the entity. |
| QRY048 | Warning | MySQL bind-order extraction failed for a chain; parameter binding falls back to chain order, which may not match the SQL text's `?` positions (see "MySQL Positional Bind Order") |
| QRY050-055 | Mixed | Migration diagnostics |
| QRY060 | Error | No FK column for `One<T>` navigation |
| QRY061 | Error | Ambiguous FK for `One<T>` navigation |
| QRY062 | Error | `HasOne` references invalid column |
| QRY063 | Error | Navigation target entity not found |
| QRY064 | Error | `HasManyThrough` invalid junction navigation |
| QRY065 | Error | `HasManyThrough` invalid target navigation |
| QRY070 | Warning | `IntersectAll` not supported on this dialect |
| QRY071 | Warning | `ExceptAll` not supported on this dialect |
| QRY072 | Error | Set operation projection mismatch |
| QRY074 | Error | Navigation aggregate in `Select` projection unresolved |
| QRY080 | Error | CTE inner query not analyzable |
| QRY081 | Error | `FromCte` without matching `With` |
| QRY082 | Error | Duplicate CTE name in chain |
| QRY900 | Error | Internal generator error (pipeline exception) |

QRY073 was introduced then retired in v0.3.0 when cross-entity set operations became supported; `#pragma warning disable QRY073` directives should be removed. The ID is intentionally skipped so those pragmas remain inert.

## Key Design Decisions

1. **Incremental caching**: All pipeline models implement `IEquatable<T>`. Equality on TranslatedCallSite includes PipelineError to detect error state changes.
2. **No cross-node ThreadStatic state** (#311): every error and trace channel is either a pipeline value (`BindStageResult`, `TranslatedCallSite.PipelineError`, deferred `DiagnosticInfo`, `AssembledPlan.TraceLines`) or ThreadStatic state produced and consumed within a single transform call (TraceCapture inside the orchestrator, ProjectionAnalyzer's Sql.Raw error list). Roslyn does not guarantee which thread runs which node, so state must never rely on surviving a node boundary. Test hooks (`ChainAnalyzer.TestCapturedChains`, `CallSiteBinder.TestThrowOnMethodName`) are the only cross-call ThreadStatics.
3. **Display class prediction**: Generator predicts compiler-generated closure class names to emit [UnsafeAccessor] methods for captured variable extraction without reflection.
4. **Supplemental compilation**: DisplayClassEnricher builds a supplemental compilation containing generated entity/context source before creating semantic models. This eliminates TypeKind.Error for generated types; remaining error types fall back to "object".
5. **IsUnresolvedTypeName strict/lenient split**: Strict treats "object" as unresolved (chain analysis). Lenient allows "object" (projection analysis where it is a valid placeholder via fallbackToObject).
6. **Enum constant folding**: SqlExprAnnotator folds enum member accesses to LiteralExpr before parameter extraction. CapturedValueExpr reaching the translator are always genuine runtime captures.
7. **Conditional mask limit**: Max 8 conditional bits (256 SQL variants) and max nesting depth 2 (counted in cascades — a whole `if/else-if/else` chain or ternary is one level). Beyond either limit → QRY032 compile error.
8. **RuntimeBuild is a compile-error path, not a runtime fallback**: There is no runtime query builder. When ChainAnalyzer classifies a chain as `OptimizationTier.RuntimeBuild` (forked chain, excessive conditional depth, unanalyzable projection, disqualified chain), no SQL is rendered, no carrier is generated, and QRY032 is reported as a compile error directing the user to restructure. `CarrierAnalyzer` immediately marks RuntimeBuild chains as `Ineligible`; `SqlAssembler` produces empty SQL variants.

## Project Boundaries

| Project | Target | Role |
|---------|--------|------|
| `Quarry.Generator` | netstandard2.0 | Roslyn source generator. Compile-time analysis and code generation. |
| `Quarry` | net10.0 | Runtime library. QuarryContext, IEntityAccessor<T>, QueryBuilder<T>, execution, type mappings. |
| `Quarry.Shared` | shared projitems | Shared code compiled into Generator, Runtime, and Tool. Contains Migration/ (schema models + builders, diffing, snapshot codegen), Scaffold/ (database introspection for 4 dialects), and Sql/ (dialect enum, formatting). Generator excludes only Scaffold/. The migration model types (Migration/Models + Migration/Builders) are single-sourced: `QUARRY_RUNTIME` (defined by Quarry.csproj) gates them into the public `Quarry.Migration` namespace, while Generator (internal) and Tool (public) compile them as `Quarry.Shared.Migration` — never edit one namespace's copy, there is only one file (#313). |

## Testing

- Cross-dialect SQL output tests in `Quarry.Tests/SqlOutput/CrossDialect*.cs` — primary regression gate
- `QueryTestHarness` seeds SQLite with known data, runs queries, asserts SQL + result values across all 4 dialects
- `TypeClassificationTests` — unit tests for type classification utilities
- `DisplayClassEnricherTests` — closure analysis and type resolution tests
- `DateTimeOffsetIntegrationTests` — GetFieldValue round-trip tests
- `IncrementalCachingTests` — per-stage run reasons via the tracking names above; also pins the two #310 defects
- `InterceptorBindingGuardTests` — compiles a matrix of chain shapes in isolated compilations and asserts no CS8785/CS9144/CS9177 plus that the terminal's interceptor was actually emitted. Do **not** rely on a synthetic compilation to surface a terminal receiver-arity mismatch: the same shape that is a hard CS9144 in the full test project raises nothing in isolation, so assert on the emitted interceptor text instead
- `IR/PipelineModelEqualityTests` — negative equality + hash consistency for `EntityRegistry`, `AssembledPlan`, `CarrierPlan`, `FileInterceptorGroup`. These implement `IEquatable<T>` by hand and are what gates incremental caching, so a dropped field comparison silently degrades every downstream stage
